namespace DocumentReprocessor.Configuration;

/// <summary>
/// Root application settings loaded from appsettings.json.
/// </summary>
public sealed class AppSettings
{
    public ApiSettings Api { get; set; } = new();
    public PathSettings Paths { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
}

/// <summary>
/// API endpoint and authentication settings.
/// </summary>
public sealed class ApiSettings
{
    /// <summary>
    /// Full URL of the SendDIAN ENR PDF endpoint.
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// Bearer token used for authorization. Update this value when the token expires.
    /// </summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>
    /// HTTP Content-Type for the request body. Use text/plain to match Postman raw Text mode.
    /// </summary>
    public string ContentType { get; set; } = "text/plain";

    /// <summary>
    /// When true, wraps the .txt content as a JSON string (e.g. "line1\\nline2") before sending.
    /// Useful for ASP.NET endpoints that bind [FromBody] string with application/json.
    /// </summary>
    public bool WrapBodyAsJsonString { get; set; }
}

/// <summary>
/// Configurable folder paths used by the reprocessor.
/// </summary>
public sealed class PathSettings
{
    /// <summary>
    /// Folder that contains pending .txt documents to send.
    /// </summary>
    public string InputFolder { get; set; } = string.Empty;

    /// <summary>
    /// Folder where successfully sent documents are moved.
    /// </summary>
    public string ProcessedFolder { get; set; } = string.Empty;

    /// <summary>
    /// Folder where JSON processing logs are written.
    /// </summary>
    public string LogFolder { get; set; } = string.Empty;

    /// <summary>
    /// Folder where the Excel run report is written. Falls back to LogFolder when empty.
    /// </summary>
    public string ReportFolder { get; set; } = string.Empty;
}

/// <summary>
/// Parallel processing and retry settings.
/// </summary>
public sealed class ProcessingSettings
{
    /// <summary>
    /// Maximum number of documents sent at the same time.
    /// Use 1 for sequential processing. Default 20 (suited for ~1000 requests/minute capacity).
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 20;

    /// <summary>
    /// Extra attempts after the first failure for transient errors (429, 503, timeouts).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds before a retry. Actual delay grows with attempt number.
    /// </summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 3000;

    /// <summary>
    /// HttpClient timeout in seconds for each API call.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 45;
}
