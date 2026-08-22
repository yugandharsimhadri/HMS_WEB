using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// The diagnostic/lab bill. Deliberately plain next to
/// <see cref="PharmacyInvoiceDocument"/>: no GST, no batch, no HSN — a list
/// of tests, a total, and who paid. A consultation or a test is a service,
/// not a taxable supply of goods, and the desktop draws the line in the same
/// place.
/// </summary>
public static class DiagnosticBillDocument
{
    public static byte[] Generate(
        DiagnosticBill bill, ClinicProfile clinic, DocumentTheme theme, bool isReprint = false)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, bill, clinic, theme, isReprint)).GeneratePdf();
    }

    private static void Compose(
        IDocumentContainer container, DiagnosticBill bill, ClinicProfile clinic,
        DocumentTheme theme, bool isReprint)
    {
        const float d = ClinicalSizeDelta;

        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontFamily(BodyFont(theme)).FontSize(Body(theme, 9, d)));

            page.Content().Column(col =>
            {
                Header(col, clinic.Name, clinic.AddressLine, clinic.AddressLine2, clinic.Phone,
                       clinic.GstRegistered ? clinic.Gstin : null,
                       isReprint ? "DIAGNOSTIC BILL (DUPLICATE)" : "DIAGNOSTIC BILL", theme, d);

                IdentityRow(col, theme, d,
                    ("Bill No", bill.BillNo),
                    ("Date", $"{bill.BillDate:dd/MM/yyyy}"),
                    ("Time", $"{bill.BillDate:HH:mm}"));
                IdentityRow(col, theme, d,
                    ("Patient", bill.PatientName),
                    ("Patient No", bill.PatientNo),
                    ("Status", bill.Status.ToString()));

                // Who referred them, when they did not come through our own
                // OPD. Omitted entirely otherwise rather than printed blank —
                // the bill already carries the visit it belongs to.
                if (!string.IsNullOrWhiteSpace(bill.ReferredBy))
                    LabelValue(col, theme, d, "Referred by", bill.ReferredBy);

                Rule(col);

                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3.6f); c.RelativeColumn(0.8f);
                        c.RelativeColumn(1.0f); c.RelativeColumn(1.2f);
                    });

                    HeaderRow(table, theme, d, "TEST", "QTY", "PRICE", "AMOUNT");

                    foreach (var item in bill.Items)
                        Row(table, theme, d,
                            item.TestName,
                            item.Quantity.ToString(),
                            item.Price.ToString("0.00"),
                            item.Amount.ToString("0.00"));
                });

                Rule(col);

                TotalLine(col, theme, d, "Total", bill.TotalAmount.ToString("0.00"));
                if (bill.Discount > 0)
                    TotalLine(col, theme, d, "Discount", $"-{bill.Discount:0.00}");

                col.Item().PaddingTop(1).AlignRight()
                    .Text($"AMOUNT PAYABLE   Rs. {bill.FinalAmount:0.00}")
                    .FontSize(Body(theme, 13, d)).Bold();

                col.Item().AlignRight().Text($"Paid by {bill.PaymentMode}")
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(bill.TransactionNo))
                    col.Item().AlignRight().Text($"Ref: {bill.TransactionNo}")
                        .FontSize(Body(theme, 8, d)).FontColor(Muted);

                col.Item().PaddingTop(3).AlignRight().Text(AmountInWords.Convert(bill.FinalAmount))
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(bill.Remarks))
                    col.Item().PaddingTop(3).Text($"Remarks: {bill.Remarks}")
                        .FontSize(Body(theme, 8, d)).FontColor(Muted);

                Rule(col);

                var footer = Footer(clinic.FooterText, theme);
                if (!string.IsNullOrWhiteSpace(footer))
                    col.Item().PaddingTop(6).AlignCenter().Text(footer)
                        .FontSize(Body(theme, 7.4f, d)).FontColor(Muted);

                if (isReprint)
                    col.Item().PaddingTop(5).AlignCenter().Text("DUPLICATE")
                        .FontSize(Body(theme, 9, d)).Bold().FontColor(Muted);
            });
        });
    }

    private static void HeaderRow(TableDescriptor table, DocumentTheme theme, float d, params string[] cells)
    {
        foreach (var cell in cells)
            table.Cell().BorderBottom(1).PaddingBottom(2).Text(cell)
                .FontSize(Body(theme, 7.5f, d)).Bold();
    }

    private static void Row(TableDescriptor table, DocumentTheme theme, float d, params string[] cells)
    {
        foreach (var cell in cells)
            table.Cell().PaddingVertical(1.5f).Text(cell).FontSize(Body(theme, 8.5f, d));
    }

    private static void TotalLine(ColumnDescriptor col, DocumentTheme theme, float d, string label, string value)
        => col.Item().Row(row =>
        {
            row.RelativeItem().AlignRight().Text(label).FontSize(Body(theme, 9, d));
            row.ConstantItem(70).AlignRight().Text(value).FontSize(Body(theme, 9, d));
        });
}
