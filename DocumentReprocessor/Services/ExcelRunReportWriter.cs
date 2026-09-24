using ClosedXML.Excel;
using DocumentReprocessor.Models;

namespace DocumentReprocessor.Services;

/// <summary>
/// Creates one Excel workbook per program run with the processed document summary.
/// </summary>
public sealed class ExcelRunReportWriter
{
    private readonly string _reportFolder;

    public ExcelRunReportWriter(string reportFolder)
    {
        _reportFolder = reportFolder;
    }

    /// <summary>
    /// Writes a run report containing document number, company NIT, document date/time,
    /// response codes and messages. Returns the created file path, or null when empty.
    /// </summary>
    public string? Write(IReadOnlyList<DocumentLogEntry> entries, DateTime runStartedAt)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(_reportFolder);

        var fileName = $"RunReport_{runStartedAt:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(_reportFolder, fileName);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("DocumentosProcesados");

        worksheet.Cell(1, 1).Value = "NumeroDocumento";
        worksheet.Cell(1, 2).Value = "NitEmpresa";
        worksheet.Cell(1, 3).Value = "FechaDocumento";
        worksheet.Cell(1, 4).Value = "HoraDocumento";
        worksheet.Cell(1, 5).Value = "CodigoHttp";
        worksheet.Cell(1, 6).Value = "CodigoApi";
        worksheet.Cell(1, 7).Value = "MensajeRespuesta";
        worksheet.Cell(1, 8).Value = "Exitoso";
        worksheet.Cell(1, 9).Value = "Archivo";
        worksheet.Cell(1, 10).Value = "FechaHoraProceso";

        var headerRange = worksheet.Range(1, 1, 1, 10);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = i + 2;

            worksheet.Cell(row, 1).Value = entry.DocumentNumber ?? string.Empty;
            worksheet.Cell(row, 2).Value = entry.CompanyNit ?? string.Empty;
            worksheet.Cell(row, 3).Value = entry.DocumentDate ?? string.Empty;
            worksheet.Cell(row, 4).Value = entry.DocumentTime ?? string.Empty;
            worksheet.Cell(row, 5).Value = entry.StatusCode?.ToString() ?? string.Empty;
            worksheet.Cell(row, 6).Value = entry.ApiStatusCode ?? string.Empty;
            worksheet.Cell(row, 7).Value = BuildCompleteMessage(entry);
            worksheet.Cell(row, 8).Value = entry.Success ? "Si" : "No";
            worksheet.Cell(row, 9).Value = entry.FileName;
            worksheet.Cell(row, 10).Value = entry.Timestamp;
            worksheet.Cell(row, 10).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        }

        worksheet.Columns().AdjustToContents(1, 60);
        worksheet.Column(7).Width = 80;
        worksheet.SheetView.FreezeRows(1);

        workbook.SaveAs(filePath);
        return filePath;
    }

    private static string BuildCompleteMessage(DocumentLogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ResponseMessage))
        {
            return entry.ResponseMessage;
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.StatusMessage))
        {
            parts.Add(entry.StatusMessage);
        }

        if (!string.IsNullOrWhiteSpace(entry.StatusDescription)
            && !string.Equals(entry.StatusDescription, entry.StatusMessage, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(entry.StatusDescription);
        }

        if (parts.Count > 0)
        {
            return string.Join(" | ", parts);
        }

        if (!string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            return entry.ErrorMessage;
        }

        return entry.ResponseBody ?? string.Empty;
    }
}
