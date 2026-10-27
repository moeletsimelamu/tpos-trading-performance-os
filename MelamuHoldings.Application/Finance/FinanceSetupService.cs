using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// Funds and financial accounts: the structure money is recorded against. Balances are never set here.
public class FinanceSetupService
{
    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public FinanceSetupService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<FundDto>> GetFundsAsync() =>
        (await FinanceReadModel.LoadAsync(_repository)).Funds();

    public async Task<FundDto> CreateFundAsync(CreateFundRequest request, int userId)
    {
        if (await _repository.FundNameExistsAsync(request.Name))
            throw new ConflictException($"A fund named '{request.Name.Trim()}' already exists.");

        var fund = new Fund(request.Name, request.Description, Now);
        await SaveWithAuditAsync(() => _repository.Add(fund), () => AuditLog.Event(
            Now, userId, "fund.created", nameof(Fund), fund.Id, $"Created fund '{fund.Name}'.",
            new { fund.Name, fund.Description }), "IX_Funds_Name", $"A fund named '{fund.Name}' already exists.");

        return (await FinanceReadModel.LoadAsync(_repository)).Fund(fund);
    }

    public async Task<FundDto> UpdateFundAsync(int id, UpdateFundRequest request, int userId)
    {
        var fund = await _repository.GetFundAsync(id) ?? throw new NotFoundException($"Fund {id} does not exist.");
        if (await _repository.FundNameExistsAsync(request.Name, excludingId: id))
            throw new ConflictException($"A fund named '{request.Name.Trim()}' already exists.");

        var before = new { fund.Name, fund.Description };
        fund.Update(request.Name, request.Description);
        await SaveWithAuditAsync(() => { }, () => AuditLog.Event(
            Now, userId, "fund.updated", nameof(Fund), fund.Id, $"Updated fund '{fund.Name}'.",
            new { before, after = new { fund.Name, fund.Description } }), "IX_Funds_Name", $"A fund named '{fund.Name}' already exists.");

        return (await FinanceReadModel.LoadAsync(_repository)).Fund(fund);
    }

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync() =>
        (await FinanceReadModel.LoadAsync(_repository)).Accounts();

    public async Task<AccountDto> GetAccountAsync(int id)
    {
        var account = await _repository.GetAccountAsync(id) ?? throw new NotFoundException($"Account {id} does not exist.");
        return (await FinanceReadModel.LoadAsync(_repository)).Account(account);
    }

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, int userId)
    {
        if (await _repository.AccountNameExistsAsync(request.Name))
            throw new ConflictException($"An account named '{request.Name.Trim()}' already exists.");

        var account = new FinancialAccount(
            request.Name, request.Type!.Value, Currency.FromCode(request.Currency), request.Ownership!.Value,
            request.Institution, request.Notes, Now);
        await SaveWithAuditAsync(() => _repository.Add(account), () => AuditLog.Event(
            Now, userId, "account.created", nameof(FinancialAccount), account.Id,
            $"Created {account.Currency} {account.Type.ToString().ToLowerInvariant()} account '{account.Name}'.",
            new { account.Name, account.Type, Currency = account.Currency.Code, account.Ownership, account.Institution }),
            "IX_FinancialAccounts_Name", $"An account named '{account.Name}' already exists.");

        return (await FinanceReadModel.LoadAsync(_repository)).Account(account);
    }

    public async Task<AccountDto> UpdateAccountAsync(int id, UpdateAccountRequest request, int userId)
    {
        var account = await _repository.GetAccountAsync(id) ?? throw new NotFoundException($"Account {id} does not exist.");
        if (await _repository.AccountNameExistsAsync(request.Name, excludingId: id))
            throw new ConflictException($"An account named '{request.Name.Trim()}' already exists.");

        var before = new { account.Name, account.Ownership, account.Institution, account.Notes };
        account.UpdateDetails(request.Name, request.Ownership!.Value, request.Institution, request.Notes);
        await SaveWithAuditAsync(() => { }, () => AuditLog.Event(
            Now, userId, "account.updated", nameof(FinancialAccount), account.Id, $"Updated account '{account.Name}'.",
            new { before, after = new { account.Name, account.Ownership, account.Institution, account.Notes } }),
            "IX_FinancialAccounts_Name", $"An account named '{account.Name}' already exists.");

        return (await FinanceReadModel.LoadAsync(_repository)).Account(account);
    }

    public async Task<AccountDto> CloseAccountAsync(int id, int userId)
    {
        var account = await _repository.GetAccountAsync(id) ?? throw new NotFoundException($"Account {id} does not exist.");
        var balance = Money.Of(await _repository.GetAccountBalanceAsync(id), account.Currency);

        account.Close(balance, Now);
        await SaveWithAuditAsync(() => { }, () => AuditLog.Event(
            Now, userId, "account.closed", nameof(FinancialAccount), account.Id, $"Closed account '{account.Name}'."));

        return (await FinanceReadModel.LoadAsync(_repository)).Account(account);
    }

    // Saves the change, then the audit event that references its generated id, atomically
    private async Task SaveWithAuditAsync(Action change, Func<AuditEvent> audit, string? uniqueIndex = null, string? conflictMessage = null)
    {
        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                change();
                await _repository.SaveChangesAsync();
                _repository.Add(audit());
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == uniqueIndex)
        {
            throw new ConflictException(conflictMessage!);
        }
    }
}
