namespace DocumentReprocessor.Configuration;

/// <summary>
/// Root application settings loaded from appsettings.json.
/// </summary>
public sealed class AppSettings
{
    public ApiSettings Api { get; set; } = new();
    public PathSettings Paths { get; set; } = new();
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
