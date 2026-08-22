using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// Receipt for a consultation fee. Deliberately short, as on the desktop —
/// the parent needs the amount, who it was paid to, and a number they can
/// quote, and nothing else.
///
/// No GST is shown even when the clinic is GST-registered: a consultation is
/// a service, not a taxable supply of goods, and the desktop makes the same
/// call in the same place. Adding it is a decision for the clinic's
/// accountant, not a default.
/// </summary>
public static class FeeReceiptDocument
{
    public static byte[] Generate(Visit visit, ClinicProfile clinic, DocumentTheme theme, bool isReprint = false)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, visit, clinic, theme, isReprint)).GeneratePdf();
    }

    private static void Compose(
        IDocumentContainer container, Visit visit, ClinicProfile clinic, DocumentTheme theme, bool isReprint)
    {
        const float d = ClinicalSizeDelta;
        var when = visit.FeePaidOn ?? visit.ScheduledOn;

        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontFamily(BodyFont(theme)).FontSize(Body(theme, 9, d)));

            page.Content().Column(col =>
            {
                Header(col, clinic.Name, clinic.AddressLine, clinic.AddressLine2, clinic.Phone,
                       clinic.GstRegistered ? clinic.Gstin : null, "CASH RECEIPT", theme, d);

                IdentityRow(col, theme, d,
                    ("Receipt No", visit.FeeReceiptNo ?? "(not issued)"),
                    ("Date", $"{when:dd/MM/yyyy}"),
                    ("Time", $"{when:hh\\:mm tt}"));
                IdentityRow(col, theme, d,
                    ("Patient", visit.Patient.Name),
                    ("Patient No", visit.Patient.PatientNo),
                    ("Age / Sex", $"{visit.Patient.Age} / {visit.Patient.Gender}"));
                IdentityRow(col, theme, d,
                    ("Doctor", visit.Doctor.Name),
                    ("Speciality", visit.Doctor.Speciality ?? ""),
                    ("Token / Visit", $"{visit.TokenNo} · {visit.VisitNo}"));

                Rule(col);

                col.Item().PaddingTop(2).Row(row =>
                {
                    row.RelativeItem(3).Text("PARTICULARS").FontSize(Body(theme, 7.5f, d)).Bold();
                    row.RelativeItem(1).AlignRight().Text("AMOUNT").FontSize(Body(theme, 7.5f, d)).Bold();
                });
                col.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem(3).Text(string.IsNullOrWhiteSpace(visit.Doctor.Speciality)
                        ? "Consultation fee"
                        : $"Consultation fee — {visit.Doctor.Speciality}").FontSize(Body(theme, 8.5f, d));
                    row.RelativeItem(1).AlignRight().Text(visit.Fee.ToString("0.00")).FontSize(Body(theme, 8.5f, d));
                });

                Rule(col);

                col.Item().PaddingTop(1).AlignRight().Text($"RECEIVED   Rs. {visit.Fee:0.00}")
                    .FontSize(Body(theme, 13, d)).Bold();

                col.Item().AlignRight().Text($"Paid by {visit.FeePaymentMode?.ToString() ?? "Cash"}")
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(visit.FeeTransactionNo))
                    col.Item().AlignRight().Text($"Ref: {visit.FeeTransactionNo}")
                        .FontSize(Body(theme, 8, d)).FontColor(Muted);

                col.Item().PaddingTop(4).AlignRight().Text(AmountInWords.Convert(visit.Fee))
                    .FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (visit.FollowUpOn is { } follow)
                    col.Item().PaddingTop(5).Text($"Review on {follow:dd MMM yyyy}")
                        .FontSize(Body(theme, 9, d)).SemiBold();

                // A consultation is a service, not a taxable supply - said out
                // loud so nobody looks for a GST line that was never meant to
                // be there.
                col.Item().PaddingTop(8).AlignCenter()
                    .Text("Consultation services. Fees once paid are not refundable.")
                    .FontSize(Body(theme, 7.4f, d)).FontColor(Muted);

                var footer = Footer(clinic.FooterText, theme);
                if (!string.IsNullOrWhiteSpace(footer))
                    col.Item().PaddingTop(3).AlignCenter().Text(footer)
                        .FontSize(Body(theme, 7.4f, d)).FontColor(Muted);

                if (isReprint)
                    col.Item().PaddingTop(5).AlignCenter().Text("DUPLICATE")
                        .FontSize(Body(theme, 9, d)).Bold().FontColor(Muted);
            });
        });
    }
}
