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
    public List<string> Errors { get; set; } = new();
}

public class UploadService
{
    private readonly CsvParserService _csvParser;
    private readonly ITradeRepository _tradeRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IUploadLogRepository _uploadLogRepository;

    public UploadService(
        CsvParserService csvParser,
        ITradeRepository tradeRepository,
        IStrategyRepository strategyRepository,
        IUploadLogRepository uploadLogRepository)
    {
        _csvParser = csvParser;
        _tradeRepository = tradeRepository;
        _strategyRepository = strategyRepository;
        _uploadLogRepository = uploadLogRepository;
    }

    public async Task<UploadResultDto> ProcessUploadAsync(Stream fileStream, string fileName, int userId)
    {
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

        // Persist valid trades
        foreach (var record in parseResult.ValidRecords)
        {
            var strategy = await ResolveStrategyAsync(record.Strategy);

            var trade = new Trade
            {
                UserId = userId,
                UploadLogId = uploadLog.Id,
                StrategyId = strategy?.Id,
                TradeDate = DateOnly.Parse(record.TradeDate),
                EntryTime = TimeOnly.TryParse(record.EntryTime, out var t) ? t : TimeOnly.MinValue,
                Instrument = record.Instrument,
                Direction = Enum.Parse<TradeDirection>(record.Direction!, ignoreCase: true),
                EntryPrice = record.EntryPrice,
                ExitPrice = record.ExitPrice,
                RiskAmount = record.RiskAmount,
                PnL = record.PnL,
                RR = record.RR,
                Session = record.Session,
                DayOfWeek = record.DayOfWeek,
                TradeDuration = record.TradeDuration,
                IsManualOverride = record.IsManualOverride,
                DeviationNotes = record.DeviationNotes,
                Notes = record.Notes,
                CreatedAt = DateTime.UtcNow
            };

            await _tradeRepository.AddAsync(trade);
        }

        await _tradeRepository.SaveChangesAsync();

        // Update the upload log with final counts
        uploadLog.TotalRows = parseResult.ValidRecords.Count + parseResult.Errors.Count;
        uploadLog.ParsedRows = parseResult.ValidRecords.Count;
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
            Status = l.Status.ToString()
        });
    }

    private async Task<Strategy?> ResolveStrategyAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var strategy = await _strategyRepository.GetByNameAsync(name);
        if (strategy is null)
        {
            strategy = new Strategy { Name = name, CreatedAt = DateTime.UtcNow };
            await _strategyRepository.AddAsync(strategy);
            await _strategyRepository.SaveChangesAsync();
        }
        return strategy;
    }

    private static UploadResultDto BuildResult(UploadLog log, List<string> errors) => new()
    {
        UploadLogId = log.Id,
        FileName = log.FileName,
        TotalRows = log.TotalRows,
        ParsedRows = log.ParsedRows,
        FailedRows = log.FailedRows,
        Status = log.Status.ToString(),
        Errors = errors
    };
}
