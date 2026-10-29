using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// Trade journal entries: create (as draft, open or closed), edit, review, delete. A status is only saved
// once the fields it needs are there (Trade.MissingFor); nothing is filled in with placeholder values.
public class TradeService
{
    private const string IdempotencyIndex = "IX_Trades_UserId_IdempotencyKey";

    private readonly ITradeRepository _tradeRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly TradeJournal _journal;
    private readonly TradeChecklists _checklists;
    private readonly AttachmentService _attachments;
    private readonly IAttachmentRepository _attachmentRecords;
    private readonly TradeHistory _history;
    private readonly TimeProvider _clock;

    public TradeService(ITradeRepository tradeRepository, IStrategyRepository strategyRepository, TradeJournal journal,
        TradeChecklists checklists, AttachmentService attachments, IAttachmentRepository attachmentRecords, TradeHistory history,
        TimeProvider clock)
    {
        _history = history;
        _tradeRepository = tradeRepository;
        _strategyRepository = strategyRepository;
        _journal = journal;
        _checklists = checklists;
        _attachments = attachments;
        _attachmentRecords = attachmentRecords;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<TradeResponseDto>> GetTradesByUserAsync(int userId, TradeStatus? status = null)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId)).Where(t => status is null || t.Status == status).ToList();
        // One query for every checklist, so the trade log can show rule adherence per row
        var checklists = trades.Count == 0 ? [] : await _strategyRepository.GetChecklistsAsync(trades.Select(t => t.Id).ToList());
        return trades.Select(t =>
        {
            var dto = MapToResponseDto(t);
            dto.RuleAdherence = Adherence(checklists.FirstOrDefault(c => c.TradeId == t.Id && c.StrategyId == t.StrategyId));
            return dto;
        }).ToList();
    }

    public async Task<TradeResponseDto?> GetTradeByIdAsync(int tradeId, int userId)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return null;
        return await DetailAsync(trade, userId);
    }

    // A retry with the same Idempotency-Key returns the trade it already created instead of saving a copy
    public async Task<TradeResult> CreateTradeAsync(CreateTradeDto dto, int userId, string? idempotencyKey = null)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is not null && await _tradeRepository.GetByIdempotencyKeyAsync(userId, key) is { } previous)
            return new TradeResult(MapToResponseDto(previous), Replayed: true);

        if (dto.Journal is not null) await _journal.ValidateAsync(userId, dto.Journal);
        await _checklists.ValidateNewAsync(userId, dto.StrategyName, dto.Checklist);
        var trade = new Trade { UserId = userId, CreatedAt = Now, IdempotencyKey = key };
        await ApplyAsync(trade, dto);
        trade.SetStatus(ParseStatus(dto.Status) ?? TradeStatus.Closed);
        _history.Record(trade, TradeRevisionKind.Created, [], Now);

        try
        {
            await _tradeRepository.AddAsync(trade);
            await _tradeRepository.SaveChangesAsync();
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == IdempotencyIndex && key is not null)
        {
            var winner = await _tradeRepository.GetByIdempotencyKeyAsync(userId, key)
                ?? throw new ConflictException("The trade could not be saved. Please retry.");
            return new TradeResult(MapToResponseDto(winner), Replayed: true);
        }

        if (dto.Journal is not null)
        {
            await _journal.RecordAsync(trade.Id, userId, dto.Journal, Now);
            await _tradeRepository.SaveChangesAsync();
        }
        if (dto.Checklist is { Count: > 0 })
        {
            await _checklists.RecordAsync(trade, dto.Checklist, Now);
            await _checklists.SaveAsync();
        }
        return new TradeResult(await DetailAsync(trade, userId), Replayed: false);
    }

    public async Task<TradeResponseDto?> UpdateTradeAsync(int tradeId, int userId, UpdateTradeDto dto)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return null;

        // Every changed field, journal answer and checklist answer is recorded in the same save as the change
        var before = TradeValues.Capture(trade);
        await ApplyAsync(trade, dto);
        trade.SetStatus(ParseStatus(dto.Status) ?? trade.Status);
        trade.UpdatedAt = Now;
        var journalChanges = dto.Journal is null ? [] : await _journal.RecordAsync(trade.Id, userId, dto.Journal, Now);
        var checklistChanges = await _checklists.RecordAsync(trade, dto.Checklist, Now);
        _history.Record(trade, TradeRevisionKind.Edited,
            [.. TradeValues.Diff(before, TradeValues.Capture(trade)), .. journalChanges, .. checklistChanges], Now);

        await _tradeRepository.UpdateAsync(trade);
        await _checklists.SaveAsync();
        return await DetailAsync(trade, userId);
    }

    public async Task<TradeResponseDto?> ReviewTradeAsync(int tradeId, int userId, ReviewTradeDto dto)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return null;

        // A template's required questions must be answered before the entry counts as reviewed
        var missing = await _journal.MissingRequiredAsync(trade.Id, userId);
        if (missing.Count > 0)
            throw new DomainException($"Answer the required journal questions first: {string.Join("; ", missing)}.");

        var before = TradeValues.Capture(trade);
        trade.MarkReviewed(dto.ReviewNotes, Now);
        _history.Record(trade, TradeRevisionKind.Reviewed, TradeValues.Diff(before, TradeValues.Capture(trade)), Now);
        await _tradeRepository.UpdateAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        return await DetailAsync(trade, userId);
    }

    public async Task<bool> DeleteTradeAsync(int tradeId, int userId)
    {
        var trade = await _tradeRepository.GetForUserAsync(tradeId, userId);
        if (trade is null) return false;

        // The records go with the trade (cascade); the files are removed once that is saved
        var files = (await _attachmentRecords.ListAsync(trade.Id)).Select(a => a.StorageKey).ToList();
        await _tradeRepository.DeleteAsync(trade);
        await _tradeRepository.SaveChangesAsync();
        _attachments.DeleteFiles(files);
        return true;
    }

    // --- Private helpers ---

    // One trade with its journal answers and checklist
    private async Task<TradeResponseDto> DetailAsync(Trade trade, int userId)
    {
        var dto = MapToResponseDto(trade);
        dto.Journal = await _journal.GetAsync(trade.Id, userId);
        dto.Checklist = await _checklists.GetAsync(trade);
        dto.RuleAdherence = dto.Checklist is null ? null : !dto.Checklist.IsComplete ? "Unfinished" : dto.Checklist.IsCompliant == true ? "Followed" : "Broke";
        dto.Attachments = await _attachments.ListAsync(trade.Id);
        return dto;
    }

    private static string? Adherence(TradeChecklist? checklist) =>
        checklist is null ? null : !checklist.IsComplete ? "Unfinished" : checklist.IsCompliant == true ? "Followed" : "Broke";

    // Copies what was entered. Calculated values (R, duration) are cleared first so they are recalculated
    // from the new figures rather than kept stale.
    private async Task ApplyAsync(Trade trade, CreateTradeDto dto)
    {
        var strategy = string.IsNullOrWhiteSpace(dto.StrategyName) ? null : await _strategyRepository.GetOrCreateAsync(trade.UserId, dto.StrategyName, Now);
        trade.StrategyId = strategy?.Id;
        trade.Strategy = strategy;
        trade.TradeDate = dto.TradeDate;
        trade.Instrument = dto.Instrument.Trim();
        trade.TradingAccount = Blank(dto.TradingAccount);
        trade.Direction = dto.Direction is null ? null : Enum.Parse<TradeDirection>(dto.Direction, ignoreCase: true);
        trade.EntryTime = dto.EntryTime;
        trade.EntryPrice = dto.EntryPrice;
        trade.StopLoss = dto.StopLoss;
        trade.TakeProfit = dto.TakeProfit;
        trade.PositionSize = dto.PositionSize;
        trade.RiskAmount = dto.RiskAmount;
        trade.ExitDate = dto.ExitDate;
        trade.ExitTime = dto.ExitTime;
        trade.ExitPrice = dto.ExitPrice;
        trade.PnL = dto.PnL;
        trade.Fees = dto.Fees;
        trade.RR = dto.RR;
        trade.Currency = dto.Currency is null ? null : Currency.FromCode(dto.Currency).Code;
        trade.Session = dto.Session.Trim();
        trade.DayOfWeek = dto.DayOfWeek;
        trade.TradeDuration = dto.TradeDuration;
        trade.IsManualOverride = dto.IsManualOverride;
        trade.DeviationNotes = Blank(dto.DeviationNotes);
        trade.Notes = Blank(dto.Notes);
    }

    private static TradeStatus? ParseStatus(string? status) =>
        status is null ? null : Enum.Parse<TradeStatus>(status, ignoreCase: true);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TradeResponseDto MapToResponseDto(Trade trade) => new()
    {
        Id = trade.Id,
        Status = trade.Status.ToString(),
        IsImported = trade.IsImported,
        TradeDate = trade.TradeDate,
        Instrument = trade.Instrument,
        TradingAccount = trade.TradingAccount,
        Direction = trade.Direction?.ToString(),
        EntryTime = trade.EntryTime,
        EntryPrice = trade.EntryPrice,
        StopLoss = trade.StopLoss,
        TakeProfit = trade.TakeProfit,
        PositionSize = trade.PositionSize,
        RiskAmount = trade.RiskAmount,
        ExitDate = trade.ExitDate,
        ExitTime = trade.ExitTime,
        ExitPrice = trade.ExitPrice,
        PnL = trade.PnL,
        Fees = trade.Fees,
        RR = trade.RR,
        Currency = trade.Currency,
        StrategyName = trade.Strategy?.Name,
        Session = trade.Session,
        DayOfWeek = trade.DayOfWeek,
        TradeDuration = trade.TradeDuration,
        IsManualOverride = trade.IsManualOverride,
        DeviationNotes = trade.DeviationNotes,
        Notes = trade.Notes,
        ReviewNotes = trade.ReviewNotes,
        ReviewedAt = trade.ReviewedAt,
        CreatedAt = trade.CreatedAt,
        UpdatedAt = trade.UpdatedAt
    };
}
