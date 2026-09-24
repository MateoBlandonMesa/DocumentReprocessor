using System.Text.Json.Serialization;

namespace DocumentReprocessor.Models;

/// <summary>
/// Represents a single document send attempt written to the JSON log.
/// </summary>
public sealed class DocumentLogEntry
{
    /// <summary>
    /// Local date and time when the request was completed.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Name of the processed .txt file.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Document number extracted from the ENR field FAD05.
    /// </summary>
    [JsonPropertyName("documentNumber")]
    public string? DocumentNumber { get; set; }

    /// <summary>
    /// Company NIT extracted from the ENR field FAJ21.
    /// </summary>
    [JsonPropertyName("companyNit")]
    public string? CompanyNit { get; set; }

    /// <summary>
    /// Document date extracted from the ENR field FAD09.
    /// </summary>
    [JsonPropertyName("documentDate")]
    public string? DocumentDate { get; set; }

    /// <summary>
    /// Document time extracted from the ENR field FAD10.
    /// </summary>
    [JsonPropertyName("documentTime")]
    public string? DocumentTime { get; set; }

    /// <summary>
    /// Full path of the source file before it was moved.
    /// </summary>
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Destination path after a successful send, if the file was moved.
    /// </summary>
    [JsonPropertyName("processedPath")]
    public string? ProcessedPath { get; set; }

    /// <summary>
    /// Content-Type header that was sent with the request.
    /// </summary>
    [JsonPropertyName("requestContentType")]
    public string? RequestContentType { get; set; }

    /// <summary>
    /// HTTP status code returned by the transport layer.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    /// <summary>
    /// StatusCode value from the API JSON body (business status).
    /// </summary>
    [JsonPropertyName("apiStatusCode")]
    public string? ApiStatusCode { get; set; }

    /// <summary>
    /// StatusMessage value from the API JSON body.
    /// </summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// StatusDescription value from the API JSON body.
    /// </summary>
    [JsonPropertyName("statusDescription")]
    public string? StatusDescription { get; set; }

    /// <summary>
    /// Combined human-readable summary of the API status code and messages.
    /// </summary>
    [JsonPropertyName("responseMessage")]
    public string? ResponseMessage { get; set; }

    /// <summary>
    /// TrackId returned by the API, when present.
    /// </summary>
    [JsonPropertyName("trackId")]
    public string? TrackId { get; set; }

    /// <summary>
    /// Uuid / CUFE returned by the API, when present.
    /// </summary>
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>
    /// Indicates whether the HTTP call completed with a success status code.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Raw response body returned by the API.
    /// </summary>
    [JsonPropertyName("responseBody")]
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Error message when the send operation fails at transport or local level.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
