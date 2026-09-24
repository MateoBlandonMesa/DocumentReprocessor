using DocumentReprocessor.Configuration;
using DocumentReprocessor.Models;

namespace DocumentReprocessor.Services;

/// <summary>
/// Orchestrates reading .txt files, sending them to the API, logging results, and moving processed files.
/// </summary>
public sealed class DocumentProcessor
{
    private readonly PathSettings _paths;
    private readonly DianApiClient _apiClient;
    private readonly JsonFileLogger _logger;
    private readonly ExcelRunReportWriter _excelReportWriter;

    public DocumentProcessor(
        PathSettings paths,
        DianApiClient apiClient,
        JsonFileLogger logger,
        ExcelRunReportWriter excelReportWriter)
    {
        _paths = paths;
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
        var runEntries = new List<DocumentLogEntry>();

        var files = Directory.GetFiles(_paths.InputFolder, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"No .txt files found in: {_paths.InputFolder}");
            return new ProcessingRunResult(0, 0, null);
        }

        Console.WriteLine($"Found {files.Length} file(s) to process.");

        var successCount = 0;

        foreach (var filePath in files)
        {
            var entry = await ProcessFileAsync(filePath, cancellationToken);
            runEntries.Add(entry);

            if (entry.Success)
            {
                successCount++;
            }
        }

        string? reportPath = null;
        try
        {
            reportPath = _excelReportWriter.Write(runEntries, runStartedAt);
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

        Console.WriteLine($"Processing: {fileName}");

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);

            entry.DocumentNumber = EnrDocumentParser.ExtractDocumentNumber(content);
            entry.CompanyNit = EnrDocumentParser.ExtractCompanyNit(content);
            entry.DocumentDate = EnrDocumentParser.ExtractDocumentDate(content);
            entry.DocumentTime = EnrDocumentParser.ExtractDocumentTime(content);
            Console.WriteLine($"  DocumentNumber (FAD05): {entry.DocumentNumber ?? "(not found)"}");
            Console.WriteLine($"  CompanyNit (FAJ21): {entry.CompanyNit ?? "(not found)"}");
            Console.WriteLine($"  DocumentDate (FAD09): {entry.DocumentDate ?? "(not found)"}");
            Console.WriteLine($"  DocumentTime (FAD10): {entry.DocumentTime ?? "(not found)"}");

            if (string.IsNullOrWhiteSpace(content))
            {
                entry.Success = false;
                entry.ErrorMessage = "File is empty.";
                await _logger.WriteAsync(entry, cancellationToken);
                Console.WriteLine($"  Skipped (empty): {fileName}");
                return entry;
            }

            var result = await _apiClient.SendDocumentAsync(content, cancellationToken);
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

            // Prefer document number from API when the ENR field was missing.
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
                Console.WriteLine($"  Failed: HTTP {result.StatusCode} | NIT={entry.CompanyNit ?? "n/a"} | FAD05={entry.DocumentNumber ?? "n/a"}");
                Console.WriteLine($"  Response message: {entry.ResponseMessage ?? entry.ResponseBody ?? "(empty)"}");
                return entry;
            }

            var destinationPath = GetUniqueDestinationPath(fileName);
            File.Move(filePath, destinationPath);
            entry.ProcessedPath = destinationPath;

            await _logger.WriteAsync(entry, cancellationToken);
            Console.WriteLine($"  Sent NIT={entry.CompanyNit ?? "n/a"} | FAD05={entry.DocumentNumber ?? "n/a"}");
            Console.WriteLine($"  Response message: {entry.ResponseMessage ?? "(no StatusMessage in body)"}");
            Console.WriteLine($"  Moved to: {destinationPath}");
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
                Console.WriteLine($"  Failed to write log: {logEx.Message}");
            }

            Console.WriteLine($"  Error: {ex.Message}");
            return entry;
        }
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
    /// Every successful send is stored as {name}_{yyyyMMdd_HHmmss}.txt so reprocessing never overwrites.
    /// </summary>
    private string GetUniqueDestinationPath(string fileName)
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

        // Same-second collision: append a counter to preserve every version.
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

/// <summary>
/// Summary of a processing run.
/// </summary>
public sealed record ProcessingRunResult(int TotalFiles, int SuccessCount, string? ExcelReportPath);
