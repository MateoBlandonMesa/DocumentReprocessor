using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocumentReprocessor.Services;

/// <summary>
/// Extracts known fields from an ENR document text payload.
/// </summary>
public static partial class EnrDocumentParser
{
    /// <summary>
    /// Extracts the document number from the FAD05 field (e.g. "@FAD05 A332242").
    /// </summary>
    public static string? ExtractDocumentNumber(string documentContent)
        => ExtractField(documentContent, Fad05Regex());

    /// <summary>
    /// Extracts the company NIT from the FAJ21 field (e.g. "@FAJ21 800215758").
    /// </summary>
    public static string? ExtractCompanyNit(string documentContent)
        => ExtractField(documentContent, Faj21Regex());

    /// <summary>
    /// Extracts the document date from the FAD09 field (e.g. "@FAD09 2025-12-01").
    /// </summary>
    public static string? ExtractDocumentDate(string documentContent)
        => ExtractField(documentContent, Fad09Regex());

    /// <summary>
    /// Extracts the document time from the FAD10 field (e.g. "@FAD10 09:51:35-05:00").
    /// </summary>
    public static string? ExtractDocumentTime(string documentContent)
        => ExtractField(documentContent, Fad10Regex());

    private static string? ExtractField(string documentContent, Regex regex)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            return null;
        }

        var match = regex.Match(documentContent);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [GeneratedRegex(@"^@FAD05\s*(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Fad05Regex();

    [GeneratedRegex(@"^@FAJ21\s*(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Faj21Regex();

    [GeneratedRegex(@"^@FAD09\s*(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Fad09Regex();

    [GeneratedRegex(@"^@FAD10\s*(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Fad10Regex();
}

/// <summary>
/// Parses the SendDIAN JSON response body into structured fields for logging.
/// </summary>
public static class DianResponseParser
{
    /// <summary>
    /// Reads StatusCode, StatusMessage, StatusDescription and related trace fields from the API JSON.
    /// </summary>
    public static DianResponseInfo Parse(string? responseBody)
    {
        var info = new DianResponseInfo
        {
            RawBody = responseBody
        };

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return info;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            info.ApiStatusCode = GetStringProperty(root, "StatusCode");
            info.StatusMessage = GetStringProperty(root, "StatusMessage");
            info.StatusDescription = GetStringProperty(root, "StatusDescription");
            info.TrackId = GetStringProperty(root, "TrackId");
            info.Uuid = GetStringProperty(root, "Uuid");
            info.DocumentNumber = GetStringProperty(root, "DocumentNumber");
            info.QrText = GetStringProperty(root, "QrText");

            // Prefer an explicit human-readable summary for logs and console.
            info.FullMessage = BuildFullMessage(info);
        }
        catch (JsonException)
        {
            // Non-JSON responses are kept as the full message text.
            info.FullMessage = responseBody.Trim();
        }

        return info;
    }

    private static string? BuildFullMessage(DianResponseInfo info)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(info.ApiStatusCode))
        {
            parts.Add($"StatusCode={info.ApiStatusCode}");
        }

        if (!string.IsNullOrWhiteSpace(info.StatusMessage))
        {
            parts.Add(info.StatusMessage);
        }

        if (!string.IsNullOrWhiteSpace(info.StatusDescription)
            && !string.Equals(info.StatusDescription, info.StatusMessage, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(info.StatusDescription);
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            // Case-insensitive fallback for APIs that change casing.
            foreach (var element in root.EnumerateObject())
            {
                if (string.Equals(element.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = element.Value;
                    break;
                }
            }

            if (property.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => property.GetRawText()
        };
    }
}

/// <summary>
/// Structured fields extracted from the SendDIAN response body.
/// </summary>
public sealed class DianResponseInfo
{
    public string? ApiStatusCode { get; set; }
    public string? StatusMessage { get; set; }
    public string? StatusDescription { get; set; }
    public string? FullMessage { get; set; }
    public string? TrackId { get; set; }
    public string? Uuid { get; set; }
    public string? DocumentNumber { get; set; }
    public string? QrText { get; set; }
    public string? RawBody { get; set; }
}
