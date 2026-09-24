using System.Text.Json;
using DocumentReprocessor.Models;

namespace DocumentReprocessor.Services;

/// <summary>
/// Writes one JSON log file per processed document.
/// </summary>
public sealed class JsonFileLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _logFolder;

    public JsonFileLogger(string logFolder)
    {
        _logFolder = logFolder;
    }

    /// <summary>
    /// Ensures the log folder exists and writes the entry as a JSON file.
    /// File name pattern: {timestamp}_{nit}_{documentNumber}.json
    /// </summary>
    public async Task WriteAsync(DocumentLogEntry entry, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_logFolder);

        var timestamp = entry.Timestamp.ToString("yyyyMMdd_HHmmss");
        var nitPart = SanitizeForFileName(entry.CompanyNit) ?? "SIN_NIT";
        var documentPart = SanitizeForFileName(entry.DocumentNumber)
            ?? SanitizeForFileName(Path.GetFileNameWithoutExtension(entry.FileName))
            ?? "unknown";

        var logFileName = $"{timestamp}_{nitPart}_{documentPart}.json";
        var logPath = Path.Combine(_logFolder, logFileName);

        await using var stream = File.Create(logPath);
        await JsonSerializer.SerializeAsync(stream, entry, SerializerOptions, cancellationToken);
    }

    private static string? SanitizeForFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized.Trim();
    }
}
