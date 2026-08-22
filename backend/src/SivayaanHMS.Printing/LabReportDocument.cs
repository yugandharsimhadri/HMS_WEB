using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// The pathology lab report — one section per ordered report, each listing
/// its analytes' results against the reference range snapshotted at entry
/// time.
///
/// Only ever printed once the order has been verified. That is enforced by
/// the controller, not here: this builder renders whatever it is given, so
/// the guard lives at the one door the browser can knock on.
/// </summary>
public static class LabReportDocument
{
    public static byte[] Generate(LabOrder order, ClinicProfile clinic, DocumentTheme theme)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, order, clinic, theme)).GeneratePdf();
    }

    private static void Compose(
        IDocumentContainer container, LabOrder order, ClinicProfile clinic, DocumentTheme theme)
    {
        const float d = ClinicalSizeDelta;

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(24);
            page.DefaultTextStyle(t => t.FontFamily(BodyFont(theme)).FontSize(Body(theme, 9, d)));

            page.Content().Column(col =>
            {
                Header(col, clinic.Name, clinic.AddressLine, clinic.AddressLine2, clinic.Phone,
                       clinic.GstRegistered ? clinic.Gstin : null, "LABORATORY REPORT", theme, d);

                IdentityRow(col, theme, d,
                    ("Order No", order.OrderNo),
                    ("Date", $"{order.OrderDate:dd/MM/yyyy}"),
                    ("Time", $"{order.OrderDate:HH:mm}"));
                IdentityRow(col, theme, d,
                    ("Patient", order.PatientName),
                    ("Patient No", order.PatientNo),
                    ("Referred By", order.ReferredBy ?? "-"));

                if (!string.IsNullOrWhiteSpace(order.SpecimenId) || order.CollectedOn is not null)
                    IdentityRow(col, theme, d,
                        ("Specimen", order.SpecimenId ?? "-"),
                        ("Collected", order.CollectedOn is { } collected ? $"{collected:dd/MM/yyyy HH:mm}" : "-"),
                        ("Received", order.ReceivedOn is { } received ? $"{received:dd/MM/yyyy HH:mm}" : "-"));

                Rule(col);

                foreach (var orderReport in order.Reports.OrderBy(r => r.ReportName))
                {
                    col.Item().PaddingTop(6).PaddingBottom(2)
                        .Text(orderReport.ReportName.ToUpperInvariant())
                        .FontSize(Body(theme, 10, d)).Bold();

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2.2f); c.RelativeColumn(1f); c.RelativeColumn(0.8f);
                            c.RelativeColumn(1.6f); c.RelativeColumn(0.8f);
                        });

                        HeaderRow(table, theme, d, "ANALYTE", "RESULT", "UNITS", "REFERENCE RANGE", "FLAG");

                        foreach (var result in orderReport.Results.OrderBy(r => r.AnalyteName))
                        {
                            // A normal result carries no flag text at all —
                            // a column of "NORMAL" would bury the one row
                            // that is not.
                            var flagText = result.Flag == LabResultFlag.Normal
                                ? ""
                                : result.Flag.ToString().ToUpperInvariant();

                            var abnormal = result.Flag != LabResultFlag.Normal;

                            Cell(table, theme, d, result.AnalyteName, false);
                            Cell(table, theme, d, result.ResultValue, abnormal);
                            Cell(table, theme, d, result.Units, false);
                            Cell(table, theme, d, result.ReferenceRangeDisplay, false);
                            Cell(table, theme, d, flagText, abnormal);
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(orderReport.Notes))
                        col.Item().PaddingTop(2).Text($"Interpretation: {orderReport.Notes}")
                            .FontSize(Body(theme, 8, d)).FontColor(Muted);
                }

                Rule(col);

                TotalLine(col, theme, d, "Total", order.TotalAmount.ToString("0.00"));
                if (order.Discount > 0)
                    TotalLine(col, theme, d, "Discount", $"-{order.Discount:0.00}");

                col.Item().PaddingTop(1).AlignRight()
                    .Text($"AMOUNT PAID   Rs. {order.FinalAmount:0.00}")
                    .FontSize(Body(theme, 13, d)).Bold();

                col.Item().AlignRight().Text($"Paid by {order.PaymentMode}")
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(order.Remarks))
                    col.Item().PaddingTop(3).Text($"Remarks: {order.Remarks}")
                        .FontSize(Body(theme, 8, d)).FontColor(Muted);

                Rule(col);

                // Who stood behind these numbers, and when. The whole status
                // chain exists so this line can be printed truthfully.
                var results = order.Reports.SelectMany(r => r.Results).ToList();
                var verifiedOn = results.Where(r => r.VerifiedOn is not null).Max(r => r.VerifiedOn);
                var verifiedBy = results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.VerifiedBy))?.VerifiedBy;

                if (verifiedOn is not null)
                    col.Item().PaddingTop(4).AlignRight()
                        .Text($"Verified by {verifiedBy} on {verifiedOn:dd/MM/yyyy HH:mm}")
                        .FontSize(Body(theme, 7.6f, d)).FontColor(Muted);

                var footer = Footer(clinic.FooterText, theme);
                if (!string.IsNullOrWhiteSpace(footer))
                    col.Item().PaddingTop(6).AlignCenter().Text(footer)
                        .FontSize(Body(theme, 7.4f, d)).FontColor(Muted);
            });
        });
    }

    private static void HeaderRow(TableDescriptor table, DocumentTheme theme, float d, params string[] cells)
    {
        foreach (var cell in cells)
            table.Cell().BorderBottom(1).PaddingBottom(2).Text(cell)
                .FontSize(Body(theme, 7.5f, d)).Bold();
    }

    private static void Cell(TableDescriptor table, DocumentTheme theme, float d, string text, bool emphasise)
    {
        var span = table.Cell().PaddingVertical(1.5f).Text(text).FontSize(Body(theme, 8.5f, d));
        if (emphasise) span.Bold();
    }

    private static void TotalLine(ColumnDescriptor col, DocumentTheme theme, float d, string label, string value)
        => col.Item().Row(row =>
        {
            row.RelativeItem().AlignRight().Text(label).FontSize(Body(theme, 9, d));
            row.ConstantItem(70).AlignRight().Text(value).FontSize(Body(theme, 9, d));
        });
}
