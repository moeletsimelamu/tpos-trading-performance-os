using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class CsvParseResult
{
    public List<TradeCsvRecord> ValidRecords { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class CsvParserService
{
    public CsvParseResult Parse(Stream fileStream)
    {
        var result = new CsvParseResult();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,       // don't throw on missing optional columns
            HeaderValidated = null,          // don't throw on unrecognised headers
            TrimOptions = TrimOptions.Trim,  // handle accidental whitespace in CSV
        };

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, config);

        var rowNumber = 1;

        while (csv.Read())
        {
            rowNumber++;
            try
            {
                var record = csv.GetRecord<TradeCsvRecord>();
                if (record is null) continue;
                record.RowNumber = rowNumber;

                var validationError = ValidateRecord(record, rowNumber);
                if (validationError is not null)
                    result.Errors.Add(validationError);
                else
                    result.ValidRecords.Add(record);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        return result;
    }

    private static string? ValidateRecord(TradeCsvRecord record, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(record.Instrument))
            return $"Row {rowNumber}: Instrument is required.";

        if (!DateOnly.TryParse(record.TradeDate, out _))
            return $"Row {rowNumber}: Invalid TradeDate '{record.TradeDate}'.";

        if (record.Direction?.ToLower() is not "long" and not "short")
            return $"Row {rowNumber}: Direction must be 'Long' or 'Short'.";

        if (record.EntryPrice <= 0)
            return $"Row {rowNumber}: EntryPrice must be greater than zero.";

        if (record.ExitPrice <= 0)
            return $"Row {rowNumber}: ExitPrice must be greater than zero.";

        if (record.Strategy?.Trim().Length > Strategy.MaxNameLength)
            return $"Row {rowNumber}: Strategy must be at most {Strategy.MaxNameLength} characters.";

        return null;
    }
}
