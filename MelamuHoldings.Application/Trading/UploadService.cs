using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class UploadResultDto
{
    public int UploadLogId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ParsedRows { get; set; }
    public int FailedRows { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DuplicateRows { get; set; }
    public int MatchedRows { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class UploadService
{
    private readonly CsvParserService _csvParser;
    private readonly ITradeRepository _tradeRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IUploadLogRepository _uploadLogRepository;
    private readonly IImportMatchRepository _matchRepository;
    private readonly IPropAccountRepository _propAccounts;

    public UploadService(
        CsvParserService csvParser,
        ITradeRepository tradeRepository,
        IStrategyRepository strategyRepository,
        IUploadLogRepository uploadLogRepository,
        IImportMatchRepository matchRepository,
        IPropAccountRepository propAccounts)
    {
        _csvParser = csvParser;
        _tradeRepository = tradeRepository;
        _strategyRepository = strategyRepository;
        _uploadLogRepository = uploadLogRepository;
        _matchRepository = matchRepository;
        _propAccounts = propAccounts;
    }

    // A CSV has no account column, so a whole file can be put on one prop firm account
    public async Task<UploadResultDto> ProcessUploadAsync(Stream fileStream, string fileName, int userId, int? propAccountId = null)
    {
        PropFirmAccount? account = null;
        if (propAccountId is { } id)
        {
            account = await _propAccounts.GetForUserAsync(id, userId) ?? throw new DomainException($"Prop firm account {id} is not one of yours.");
            if (account.Status == PropAccountStatus.Closed) throw new DomainException($"{account.Nickname} is closed. Reopen it to add trades to it.");
        }

        // Create an upload log entry immediately — status starts as Pending
        var uploadLog = new UploadLog
        {
            UserId = userId,
            FileName = fileName,
            Status = UploadStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };

        await _uploadLogRepository.AddAsync(uploadLog);
        await _uploadLogRepository.SaveChangesAsync();

        // The log above stays so a failure can be seen. Everything below is one unit: if any of it fails, no trade,
        // match or new strategy from this file remains, and the same file can simply be uploaded again.
        try
        {
            return await _uploadLogRepository.InTransactionAsync(() => ImportAsync(fileStream, uploadLog, userId, account));
        }
        catch
        {
            await _uploadLogRepository.MarkFailedAsync(uploadLog.Id);
            throw;
        }
    }

    private async Task<UploadResultDto> ImportAsync(Stream fileStream, UploadLog uploadLog, int userId, PropFirmAccount? account)
    {
        // Parse the CSV
        var parseResult = _csvParser.Parse(fileStream);

        if (parseResult.ValidRecords.Count == 0)
        {
            uploadLog.Status = UploadStatus.Failed;
            uploadLog.TotalRows = parseResult.Errors.Count;
            uploadLog.FailedRows = parseResult.Errors.Count;
            await _uploadLogRepository.UpdateAsync(uploadLog);
            await _uploadLogRepository.SaveChangesAsync();

            return BuildResult(uploadLog, parseResult.Errors);
        }

        // A row already imported (or already decided on, or seen earlier in this file) is skipped. A row matching a trade journaled by hand is
        // held back as a match for the trader to link, import separately or leave out; it never duplicates the trade.
        var existing = (await _tradeRepository.GetByUserIdAsync(userId)).ToList();
        var decided = await _matchRepository.GetForUserAsync(userId);
        var linkedTrades = decided.Where(m => m.Status == ImportMatchStatus.Linked && m.TradeId is not null).Select(m => m.TradeId!.Value).ToHashSet();
        var imported = 0;

        foreach (var record in parseResult.ValidRecords)
        {
            var row = ImportReconciliationService.ToRow(record);
            if (existing.Any(row.Duplicates) || decided.Any(m => m.Values with { RowNumber = row.RowNumber } == row))
            {
                uploadLog.DuplicateRows++;
                continue;
            }

            if (existing.Where(t => row.Matches(t) && !linkedTrades.Contains(t.Id)).OrderBy(t => t.Id).FirstOrDefault() is { } manual)
            {
                var match = new ImportMatch(userId, uploadLog.Id, manual.Id, row, DateTime.UtcNow);
                _matchRepository.Add(match);
                decided.Add(match);
                uploadLog.MatchedRows++;
                continue;
            }

            var strategy = string.IsNullOrWhiteSpace(row.Strategy) ? null
                : await _strategyRepository.GetOrCreateAsync(userId, row.Strategy, DateTime.UtcNow);
            var trade = ImportReconciliationService.ToTrade(row, userId, uploadLog.Id, strategy?.Id, DateTime.UtcNow);
            trade.LinkTo(account);
            await _tradeRepository.AddAsync(trade);
            existing.Add(trade);
            imported++;
        }

        await _tradeRepository.SaveChangesAsync();

        // Update the upload log with final counts
        uploadLog.TotalRows = parseResult.ValidRecords.Count + parseResult.Errors.Count;
        uploadLog.ParsedRows = imported;
        uploadLog.FailedRows = parseResult.Errors.Count;
        uploadLog.Status = parseResult.Errors.Count == 0
            ? UploadStatus.Completed
            : UploadStatus.Completed; // partial success still completes

        await _uploadLogRepository.UpdateAsync(uploadLog);
        await _uploadLogRepository.SaveChangesAsync();

        return BuildResult(uploadLog, parseResult.Errors);
    }

    public async Task<IEnumerable<UploadResultDto>> GetUploadHistoryAsync(int userId)
    {
        var logs = await _uploadLogRepository.GetByUserIdAsync(userId);
        return logs.Select(l => new UploadResultDto
        {
            UploadLogId = l.Id,
            FileName = l.FileName,
            TotalRows = l.TotalRows,
            ParsedRows = l.ParsedRows,
            FailedRows = l.FailedRows,
            DuplicateRows = l.DuplicateRows,
            MatchedRows = l.MatchedRows,
            Status = l.Status.ToString()
        });
    }

    private static UploadResultDto BuildResult(UploadLog log, List<string> errors) => new()
    {
        UploadLogId = log.Id,
        FileName = log.FileName,
        TotalRows = log.TotalRows,
        ParsedRows = log.ParsedRows,
        FailedRows = log.FailedRows,
        DuplicateRows = log.DuplicateRows,
        MatchedRows = log.MatchedRows,
        Status = log.Status.ToString(),
        Errors = errors
    };
}
