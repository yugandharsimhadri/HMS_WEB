using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SivayaanHMS.Printing;

/// <summary>
/// A printable PDF of whichever report is on screen — the desktop's
/// <c>ReportPdfBuilder</c>, with the same page, the same header, the same cell
/// chrome and the same totals bar. QuestPDF on both sides, so its numbers are
/// copied rather than converted: A4 landscape, 24 pt margin, Segoe UI 9.
///
/// The cells arrive as text, already laid out as the desktop's PDF columns
/// (see <see cref="ReportTable.Pdf"/>): only alignment, the red "EXPIRED" and
/// the totals bar are decided here.
/// </summary>
public static class ReportPdfBuilder
{
    public static byte[] Generate(ReportTable report, string clinicName)
    {
        // Stock Register is Excel-only by design — a wide, analysis-oriented
        // dump, not a printable statement. The screen disables the PDF button
        // there; this makes the limitation explicit if it is ever bypassed.
        if (report.Kind == ReportKind.StockRegister)
            throw new NotSupportedException(
                "PDF export is not available for the Stock Register — use Export Excel instead.");

        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, report.ForPdf, clinicName)).GeneratePdf();
    }

    private static void Compose(IDocumentContainer container, ReportTable table, string clinicName)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI"));

            // No letterhead here. It belongs on what a patient is handed —
            // a receipt, a prescription, a bill — and these are the clinic's
            // own working reports. Spending a third of every page of a
            // twenty-page register on a letterhead nobody outside the clinic
            // will read is paper and toner for nothing.
            page.Header().Column(col =>
            {
                col.Item().AlignCenter().Text(clinicName).FontSize(16).Bold();
                col.Item().AlignCenter().Text("OPD & Pharmacy").FontSize(10).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(6).AlignCenter().Text(table.Title).FontSize(13).SemiBold();
                col.Item().AlignCenter().Text(table.DateLabel).FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Element(c => ComposeTable(c, table));
                col.Item().PaddingTop(10).Element(c => ComposeTotals(c, table));
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

    // ── Cell chrome ───────────────────────────────────────────────────────

    private static IContainer HeaderCell(IContainer c) => c
        .Background(Colors.Grey.Lighten4)
        .BorderBottom(1).BorderColor(Colors.Grey.Darken1)
        .PaddingVertical(5).PaddingHorizontal(4)
        .DefaultTextStyle(x => x.SemiBold().FontSize(8).FontColor(Colors.Grey.Darken3));

    private static IContainer Cell(IContainer c) => c
        .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
        .PaddingVertical(4).PaddingHorizontal(4);

    // ── The table ─────────────────────────────────────────────────────────

    private static void ComposeTable(IContainer container, ReportTable report)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                foreach (var column in report.Columns) c.RelativeColumn((float)column.Width);
            });

            // The headings are all left-aligned, over right-aligned figures. That is how the
            // desktop prints them, so that is how they are printed.
            table.Header(h =>
            {
                foreach (var column in report.Columns)
                    h.Cell().Element(HeaderCell).Text(column.Header);
            });

            foreach (var row in report.Rows)
            {
                for (var i = 0; i < report.Columns.Count; i++)
                {
                    var column = report.Columns[i];
                    var text = i < row.Cells.Count ? Convert.ToString(row.Cells[i]) ?? "" : "";

                    var cell = table.Cell().Element(Cell);
                    var span = (column.Align == ReportAlign.Right ? cell.AlignRight() : cell).Text(text);

                    if (column.Alert && row.Emphasise) span.FontColor(Colors.Red.Medium).Bold();
                }
            }
        });
    }

    // ── Totals ────────────────────────────────────────────────────────────

    private static void ComposeTotals(IContainer container, ReportTable table)
    {
        if (table.Totals.Count == 0) return;

        container.PaddingTop(6).BorderTop(1).BorderColor(Colors.Grey.Darken1).PaddingTop(6).Row(row =>
        {
            row.RelativeItem();

            foreach (var total in table.Totals)
            {
                row.AutoItem().PaddingLeft(18).Text(t =>
                {
                    t.Span(total.Label + ": ").FontSize(9).FontColor(Colors.Grey.Darken2);
                    t.Span(TotalText(total)).FontSize(10).Bold();
                });
            }
        });
    }

    /// <summary>Money gets the rupee sign; counts are plain.</summary>
    private static string TotalText(ReportTotal total) => total.Format switch
    {
        ReportFormat.Money => total.Value is null ? "" : ReportFormats.Inr(ReportFormats.ToDecimal(total.Value)),
        _ => Convert.ToString(total.Value) ?? ""
    };
}
