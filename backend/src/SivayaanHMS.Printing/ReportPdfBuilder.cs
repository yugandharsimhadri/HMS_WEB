using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SivayaanHMS.Printing;

/// <summary>
/// A printable PDF of whichever report is on screen, from exactly the rows
/// and totals the screen was given — the export always matches what the user
/// is looking at, because it is the same <see cref="ReportTable"/>.
/// </summary>
public static class ReportPdfBuilder
{
    private static readonly CultureInfo InCulture = CultureInfo.GetCultureInfo("en-IN");

    public static byte[] Generate(ReportTable table, string clinicName)
    {
        // Stock Register is Excel-only by design — a wide, analysis-oriented
        // dump, not a printable statement. The screen disables the PDF button
        // there; this makes the limitation explicit if it is ever bypassed.
        if (table.Kind == ReportKind.StockRegister)
            throw new NotSupportedException(
                "PDF export is not available for the Stock Register — use Export Excel instead.");

        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, table, clinicName)).GeneratePdf();
    }

    private static void Compose(IDocumentContainer container, ReportTable table, string clinicName)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI"));

            // No letterhead. That belongs on what a patient is handed — a
            // receipt, a prescription, a bill. These are the clinic's own
            // working reports, and spending a third of every page of a
            // twenty-page register on a letterhead nobody outside the clinic
            // reads is paper and toner for nothing.
            page.Header().Column(col =>
            {
                col.Item().AlignCenter().Text(clinicName).FontSize(16).Bold();
                col.Item().PaddingTop(6).AlignCenter().Text(table.Title).FontSize(13).SemiBold();
                col.Item().AlignCenter().Text(table.DateLabel)
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        foreach (var column in table.Columns) c.RelativeColumn((float)column.Width);
                    });

                    foreach (var column in table.Columns)
                    {
                        var cell = t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3);
                        Align(cell, column.Align).Text(column.Header).FontSize(8).Bold();
                    }

                    foreach (var row in table.Rows)
                    {
                        for (var i = 0; i < table.Columns.Count; i++)
                        {
                            var column = table.Columns[i];
                            var value = i < row.Cells.Count ? row.Cells[i] : null;

                            var cell = t.Cell().PaddingVertical(2);
                            var span = Align(cell, column.Align)
                                .Text(Format(value, column.Format))
                                .FontSize(8.5f);

                            if (row.Emphasise) span.SemiBold();
                        }
                    }
                });

                if (table.Rows.Count == 0)
                    col.Item().PaddingTop(8).AlignCenter().Text("Nothing to report.")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);

                if (table.Totals.Count > 0)
                {
                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    foreach (var total in table.Totals)
                        col.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text(total.Label).FontSize(9);
                            row.ConstantItem(110).AlignRight()
                                .Text(Format(total.Value, total.Format)).FontSize(9).Bold();
                        });
                }
            });

            page.Footer().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"Generated {DateTime.Now:dd MMM yyyy HH:mm}")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);

                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });
    }

    private static IContainer Align(IContainer container, ReportAlign align) => align switch
    {
        ReportAlign.Right => container.AlignRight(),
        ReportAlign.Center => container.AlignCenter(),
        _ => container
    };

    /// <summary>
    /// "Rs." rather than the rupee sign, and a plain hyphen for an empty
    /// cell — the embedded font subset these PDFs use has neither ₹ nor an
    /// en dash, and a report full of replacement characters is worse than a
    /// plainer one.
    /// </summary>
    internal static string Format(object? value, ReportFormat format)
    {
        if (value is null) return "-";

        return format switch
        {
            ReportFormat.Money => "Rs. " + ToDecimal(value).ToString("N2", InCulture),
            ReportFormat.Number => ToDecimal(value).ToString("N2", InCulture),
            ReportFormat.Integer => ToDecimal(value).ToString("N0", InCulture),
            ReportFormat.Date => ToDate(value)?.ToString("dd-MMM-yyyy") ?? "-",
            ReportFormat.Time => ToDate(value)?.ToString("HH:mm") ?? "-",
            ReportFormat.DateTime => ToDate(value)?.ToString("dd-MMM-yyyy HH:mm") ?? "-",
            _ => value.ToString() ?? "-"
        };
    }

    internal static decimal ToDecimal(object value) => value switch
    {
        decimal d => d,
        int i => i,
        long l => l,
        double db => (decimal)db,
        System.Text.Json.JsonElement je when je.TryGetDecimal(out var jd) => jd,
        _ => decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0
    };

    internal static DateTime? ToDate(object value) => value switch
    {
        DateTime dt => dt,
        _ => DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) ? p : null
    };
}
