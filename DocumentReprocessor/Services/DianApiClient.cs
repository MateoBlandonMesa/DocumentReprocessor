using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DocumentReprocessor.Configuration;

namespace DocumentReprocessor.Services;

/// <summary>
/// HTTP client responsible for posting document content to the SendDIAN endpoint.
/// Mirrors the working Postman request: Bearer auth, Content-Type text/plain, raw text body.
/// </summary>
public sealed class DianApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ApiSettings _apiSettings;

    public DianApiClient(HttpClient httpClient, ApiSettings apiSettings)
    {
        _httpClient = httpClient;
        _apiSettings = apiSettings;
        _httpClient.DefaultRequestHeaders.ExpectContinue = false;
    }

    /// <summary>
    /// Sends the raw document content as the POST body using Bearer authentication.
    /// </summary>
    public async Task<ApiSendResult> SendDocumentAsync(string documentContent, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _apiSettings.EndpointUrl);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiSettings.BearerToken.Trim());
        request.Headers.Accept.Clear();
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var bodyText = _apiSettings.WrapBodyAsJsonString
            ? JsonSerializer.Serialize(documentContent)
            : documentContent;

        // Exact Postman header value: "text/plain" (no charset). Avoid MediaTypeHeaderValue
        // defaults that may append "; charset=utf-8".
        var mediaType = string.IsNullOrWhiteSpace(_apiSettings.ContentType)
            ? "text/plain"
            : _apiSettings.ContentType.Trim();

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(bodyText));
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ApiSendResult
        {
            StatusCode = (int)response.StatusCode,
            IsSuccessStatusCode = response.IsSuccessStatusCode,
            ResponseBody = responseBody,
            RequestContentType = mediaType
        };
    }
}

/// <summary>
/// Result of a single API send call.
/// </summary>
public sealed class ApiSendResult
{
    public int StatusCode { get; init; }
    public bool IsSuccessStatusCode { get; init; }
    public string ResponseBody { get; init; } = string.Empty;
    public string RequestContentType { get; init; } = string.Empty;
}
