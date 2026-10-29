using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// The single path every ledger transaction takes, whichever use case records it: idempotency check, build,
// then one database transaction that saves the ledger transaction, any records that depend on its id
// (a payout receipt, an allocation settlement) and the audit event.
public class LedgerPosting
{
    public const string IdempotencyIndex = "IX_LedgerTransactions_IdempotencyKey";
    public const string ReversalIndex = "IX_LedgerTransactions_ReversesTransactionId";

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public LedgerPosting(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    // Returns the saved transaction, or the earlier one recorded with the same key (Replayed = true).
    // `afterSave` runs inside the database transaction once the ledger transaction has its id.
    public async Task<(LedgerTransaction Transaction, bool Replayed)> PostAsync(
        object request, string? idempotencyKey, int userId,
        Func<RecordingInfo, Task<LedgerTransaction>> build,
        Func<LedgerTransaction, Task>? afterSave = null)
    {
        var requestHash = Idempotency.Hash(request);
        var key = Idempotency.Key(idempotencyKey);

        if (await _repository.GetTransactionByIdempotencyKeyAsync(key) is { } previous)
            return (Replay(previous, requestHash), true);

        var now = _clock.GetUtcNow().UtcDateTime;
        LedgerTransaction transaction;
        try
        {
            transaction = await build(new RecordingInfo(userId, now, key, requestHash));
        }
        catch (Exception ex) when (ex is DomainException or ConflictException)
        {
            // A concurrent retry with the same key may have committed between the key check above and the state
            // check that just failed (e.g. "this payout is already received"). The retry gets that result, not an error.
            if (await _repository.GetTransactionByIdempotencyKeyAsync(key) is { } winner)
                return (Replay(winner, requestHash), true);
            throw;
        }

        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                _repository.Add(transaction);
                await _repository.SaveChangesAsync();
                _repository.Add(AuditLog.Event(now, userId, "transaction.recorded", nameof(LedgerTransaction), transaction.Id,
                    Summarise(transaction), new
                    {
                        Type = transaction.Type.ToString(), Date = transaction.TransactionDate, transaction.Description,
                        transaction.ReversesTransactionId, transaction.ExchangeRate, transaction.QuotedRate,
                        Entries = transaction.Entries.Select(e => new
                        {
                            e.FinancialAccountId, e.FundId, e.Amount, Currency = e.Currency.Code, Role = e.Role.ToString()
                        })
                    }));
                if (afterSave is not null) await afterSave(transaction);
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == IdempotencyIndex)
        {
            // A concurrent retry with the same key won the race: answer with its transaction
            var winner = await _repository.GetTransactionByIdempotencyKeyAsync(key)
                ?? throw new ConflictException("The request could not be recorded. Please retry.");
            return (Replay(winner, requestHash), true);
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == ReversalIndex)
        {
            // A same-key retry of a reversal breaks both unique indexes and SQL Server may name either
            if (await _repository.GetTransactionByIdempotencyKeyAsync(key) is { } winner)
                return (Replay(winner, requestHash), true);
            throw new ConflictException($"Transaction {transaction.ReversesTransactionId} has already been reversed.");
        }

        return (transaction, false);
    }

    private static LedgerTransaction Replay(LedgerTransaction previous, string requestHash) =>
        previous.RequestHash == requestHash ? previous : throw new ConflictException(Idempotency.KeyReusedMessage);

    private static string Summarise(LedgerTransaction t) =>
        $"Recorded {t.Type} dated {t.TransactionDate:yyyy-MM-dd} ({string.Join(", ", t.Entries.Select(e => e.Money))}): {t.Description}";
}
