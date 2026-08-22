using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// Receipt for one instalment against a dental case. Unlike every other
/// receipt here it prints the case's whole standing — total, paid to date,
/// balance due — because dentistry is paid across sittings and the question
/// the patient actually asks at the counter is "how much is left", not
/// "what did I just hand over".
/// </summary>
public static class DentalReceiptDocument
{
    public static byte[] Generate(
        DentalPayment payment, DentalCase dentalCase, ClinicProfile clinic, DocumentTheme theme,
        bool isReprint = false)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, payment, dentalCase, clinic, theme, isReprint)).GeneratePdf();
    }

    private static void Compose(
        IDocumentContainer container, DentalPayment payment, DentalCase dentalCase,
        ClinicProfile clinic, DocumentTheme theme, bool isReprint)
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
                       isReprint ? "DENTAL PAYMENT RECEIPT (DUPLICATE)" : "DENTAL PAYMENT RECEIPT", theme, d);

                IdentityRow(col, theme, d,
                    ("Receipt No", payment.ReceiptNo),
                    ("Date", $"{payment.PaidOn:dd/MM/yyyy}"),
                    ("Time", $"{payment.PaidOn:HH:mm}"));
                IdentityRow(col, theme, d,
                    ("Patient", dentalCase.PatientName),
                    ("Procedure / Package", dentalCase.ProcedureName ?? dentalCase.PackageName ?? ""),
                    ("Tooth", dentalCase.ToothNumber ?? ""));

                Rule(col);

                col.Item().PaddingTop(1).AlignRight()
                    .Text($"AMOUNT RECEIVED   Rs. {payment.Amount:0.00}")
                    .FontSize(Body(theme, 13, d)).Bold();

                col.Item().AlignRight().Text($"Paid by {payment.PaymentMode}")
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(payment.TransactionNo))
                    col.Item().AlignRight().Text($"Ref: {payment.TransactionNo}")
                        .FontSize(Body(theme, 8, d)).FontColor(Muted);

                col.Item().PaddingTop(3).AlignRight().Text(AmountInWords.Convert(payment.Amount))
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                Rule(col);

                TotalLine(col, theme, d, "Case total", dentalCase.TotalCost.ToString("0.00"));
                TotalLine(col, theme, d, "Paid to date", dentalCase.AmountPaid.ToString("0.00"));

                col.Item().PaddingTop(4).AlignRight()
                    .Text($"BALANCE DUE   Rs. {dentalCase.Balance:0.00}")
                    .FontSize(Body(theme, 11, d)).Bold()
                    .FontColor(dentalCase.Balance > 0 ? Muted : Colors.Black);

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

    private static void TotalLine(ColumnDescriptor col, DocumentTheme theme, float d, string label, string value)
        => col.Item().Row(row =>
        {
            row.RelativeItem().AlignRight().Text(label).FontSize(Body(theme, 9, d));
            row.ConstantItem(70).AlignRight().Text(value).FontSize(Body(theme, 9, d));
        });
}
