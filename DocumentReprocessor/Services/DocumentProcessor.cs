using System.Collections.Concurrent;
using DocumentReprocessor.Configuration;
using DocumentReprocessor.Models;

namespace DocumentReprocessor.Services;

/// <summary>
/// Orchestrates reading .txt files, sending them to the API (optionally in parallel),
/// logging results, and moving processed files.
/// </summary>
public sealed class DocumentProcessor
{
    private readonly PathSettings _paths;
    private readonly ProcessingSettings _processing;
    private readonly DianApiClient _apiClient;
    private readonly JsonFileLogger _logger;
    private readonly ExcelRunReportWriter _excelReportWriter;
    private readonly object _processedPathLock = new();

    public DocumentProcessor(
        PathSettings paths,
        ProcessingSettings processing,
        DianApiClient apiClient,
        JsonFileLogger logger,
        ExcelRunReportWriter excelReportWriter)
    {
        _paths = paths;
        _processing = processing;
        _apiClient = apiClient;
        _logger = logger;
        _excelReportWriter = excelReportWriter;
    }

    /// <summary>
    /// Processes every .txt file found in the configured input folder and writes an Excel run report.
    /// </summary>
    public async Task<ProcessingRunResult> ProcessAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureFoldersExist();

        var runStartedAt = DateTime.Now;
        var runEntries = new ConcurrentBag<DocumentLogEntry>();

        var files = Directory.GetFiles(_paths.InputFolder, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"No .txt files found in: {_paths.InputFolder}");
            return new ProcessingRunResult(0, 0, null);
        }

        var maxParallelism = Math.Max(1, _processing.MaxDegreeOfParallelism);
        Console.WriteLine($"Found {files.Length} file(s) to process.");
        Console.WriteLine($"MaxDegreeOfParallelism: {maxParallelism}");

        var successCount = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (filePath, ct) =>
        {
            // One failure must not cancel the rest of the batch.
            var entry = await ProcessFileAsync(filePath, ct);
            runEntries.Add(entry);

            if (entry.Success)
            {
                Interlocked.Increment(ref successCount);
            }
        });

        // Stable order for the Excel report (processing order is non-deterministic when parallel).
        var orderedEntries = runEntries
            .OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Timestamp)
            .ToList();

        string? reportPath = null;
        try
        {
            reportPath = _excelReportWriter.Write(orderedEntries, runStartedAt);
            if (reportPath is not null)
            {
                Console.WriteLine($"Excel report: {reportPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write Excel report: {ex.Message}");
        }

        return new ProcessingRunResult(files.Length, successCount, reportPath);
    }

    private async Task<DocumentLogEntry> ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        var entry = new DocumentLogEntry
        {
            Timestamp = DateTime.Now,
            FileName = fileName,
            SourcePath = filePath
        };

        WriteTrace(entry, $"Processing: {fileName}");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

            entry.DocumentNumber = EnrDocumentParser.ExtractDocumentNumber(content);
            entry.CompanyNit = EnrDocumentParser.ExtractCompanyNit(content);
            entry.DocumentDate = EnrDocumentParser.ExtractDocumentDate(content);
            entry.DocumentTime = EnrDocumentParser.ExtractDocumentTime(content);

            WriteTrace(entry, $"FAD05={entry.DocumentNumber ?? "n/a"} | FAJ21={entry.CompanyNit ?? "n/a"} | FAD09={entry.DocumentDate ?? "n/a"} | FAD10={entry.DocumentTime ?? "n/a"}");

            if (string.IsNullOrWhiteSpace(content))
            {
                entry.Success = false;
                entry.ErrorMessage = "File is empty.";
                await _logger.WriteAsync(entry, cancellationToken);
                WriteTrace(entry, "Skipped (empty file).");
                return entry;
            }

            var result = await SendWithRetryAsync(entry, content, cancellationToken);
            var apiResponse = DianResponseParser.Parse(result.ResponseBody);

            entry.RequestContentType = result.RequestContentType;
            entry.StatusCode = result.StatusCode;
            entry.ResponseBody = result.ResponseBody;
            entry.ApiStatusCode = apiResponse.ApiStatusCode;
            entry.StatusMessage = apiResponse.StatusMessage;
            entry.StatusDescription = apiResponse.StatusDescription;
            entry.ResponseMessage = apiResponse.FullMessage;
            entry.TrackId = apiResponse.TrackId;
            entry.Uuid = apiResponse.Uuid;
            entry.Success = result.IsSuccessStatusCode;
            entry.Timestamp = DateTime.Now;

            if (string.IsNullOrWhiteSpace(entry.DocumentNumber)
                && !string.IsNullOrWhiteSpace(apiResponse.DocumentNumber))
            {
                entry.DocumentNumber = apiResponse.DocumentNumber;
            }

            if (!result.IsSuccessStatusCode)
            {
                entry.ErrorMessage = string.IsNullOrWhiteSpace(entry.ResponseMessage)
                    ? $"API returned HTTP {result.StatusCode}."
                    : $"API returned HTTP {result.StatusCode}. {entry.ResponseMessage}";

                await _logger.WriteAsync(entry, cancellationToken);
                WriteTrace(entry, $"Failed HTTP {result.StatusCode}. {entry.ResponseMessage ?? entry.ResponseBody ?? string.Empty}");
                return entry;
            }

            var destinationPath = GetUniqueDestinationPath(fileName);
            File.Move(filePath, destinationPath);
            entry.ProcessedPath = destinationPath;

            await _logger.WriteAsync(entry, cancellationToken);
            WriteTrace(entry, $"Sent OK. {entry.ResponseMessage ?? "(no StatusMessage)"}");
            WriteTrace(entry, $"Moved to: {destinationPath}");
            return entry;
        }
        catch (Exception ex)
        {
            entry.Success = false;
            entry.ErrorMessage = ex.Message;
            entry.Timestamp = DateTime.Now;

            try
            {
                await _logger.WriteAsync(entry, cancellationToken);
            }
            catch (Exception logEx)
            {
                WriteTrace(entry, $"Failed to write log: {logEx.Message}");
            }

            WriteTrace(entry, $"Error: {ex.Message}");
            return entry;
        }
    }

    /// <summary>
    /// Sends the document and retries transient failures (429/503/408/timeouts/network).
    /// </summary>
    private async Task<ApiSendResult> SendWithRetryAsync(
        DocumentLogEntry entry,
        string content,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _processing.MaxRetryAttempts + 1);
        var baseDelayMs = Math.Max(0, _processing.RetryBaseDelayMilliseconds);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await _apiClient.SendDocumentAsync(content, cancellationToken);

                if (result.IsSuccessStatusCode || !IsTransientStatusCode(result.StatusCode) || attempt >= maxAttempts)
                {
                    return result;
                }

                var delay = TimeSpan.FromMilliseconds(baseDelayMs * attempt);
                WriteTrace(entry, $"Transient HTTP {result.StatusCode}. Retry {attempt}/{maxAttempts - 1} in {delay.TotalSeconds:0.#}s.");
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt < maxAttempts)
            {
                lastException = ex;
                var delay = TimeSpan.FromMilliseconds(baseDelayMs * attempt);
                WriteTrace(entry, $"Transient error: {ex.Message}. Retry {attempt}/{maxAttempts - 1} in {delay.TotalSeconds:0.#}s.");
                await Task.Delay(delay, cancellationToken);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        // Should not reach here; keep compiler happy.
        return await _apiClient.SendDocumentAsync(content, cancellationToken);
    }

    private static bool IsTransientStatusCode(int statusCode)
        => statusCode is 408 or 429 or 502 or 503 or 504;

    private static bool IsTransientException(Exception ex)
        => ex is HttpRequestException
            or TaskCanceledException
            or TimeoutException
            || ex.InnerException is TimeoutException or TaskCanceledException;

    private static void WriteTrace(DocumentLogEntry entry, string message)
    {
        var key = !string.IsNullOrWhiteSpace(entry.DocumentNumber)
            ? entry.DocumentNumber
            : entry.FileName;

        Console.WriteLine($"[{key}] {message}");
    }

    private void EnsureFoldersExist()
    {
        if (string.IsNullOrWhiteSpace(_paths.InputFolder))
        {
            throw new InvalidOperationException("Paths:InputFolder is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_paths.ProcessedFolder))
        {
            throw new InvalidOperationException("Paths:ProcessedFolder is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_paths.LogFolder))
        {
            throw new InvalidOperationException("Paths:LogFolder is not configured.");
        }

        if (!Directory.Exists(_paths.InputFolder))
        {
            throw new DirectoryNotFoundException($"Input folder does not exist: {_paths.InputFolder}");
        }

        Directory.CreateDirectory(_paths.ProcessedFolder);
        Directory.CreateDirectory(_paths.LogFolder);

        var reportFolder = ResolveReportFolder();
        Directory.CreateDirectory(reportFolder);
    }

    private string ResolveReportFolder()
        => string.IsNullOrWhiteSpace(_paths.ReportFolder) ? _paths.LogFolder : _paths.ReportFolder;

    /// <summary>
    /// Builds a destination path that always keeps prior versions.
    /// Thread-safe so parallel workers never overwrite each other.
    /// </summary>
    private string GetUniqueDestinationPath(string fileName)
    {
        lock (_processedPathLock)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var destinationPath = Path.Combine(
                _paths.ProcessedFolder,
                $"{nameWithoutExtension}_{timestamp}{extension}");

            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }

            var counter = 1;
            do
            {
                destinationPath = Path.Combine(
                    _paths.ProcessedFolder,
                    $"{nameWithoutExtension}_{timestamp}_{counter}{extension}");
                counter++;
            }
            while (File.Exists(destinationPath));

            return destinationPath;
        }
    }
}

/// <summary>
/// Summary of a processing run.
/// </summary>
public sealed record ProcessingRunResult(int TotalFiles, int SuccessCount, string? ExcelReportPath);
