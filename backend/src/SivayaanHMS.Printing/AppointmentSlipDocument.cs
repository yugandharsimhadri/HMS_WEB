using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// A printed confirmation slip for a booked-ahead appointment — deliberately
/// short, the same reasoning <see cref="FeeReceiptDocument"/> gives for its
/// own brevity: the patient needs when, with whom, and a number they can
/// quote if they call to change it, and nothing else.
/// </summary>
public static class AppointmentSlipDocument
{
    public static byte[] Generate(Appointment appointment, ClinicProfile clinic, DocumentTheme theme)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, appointment, clinic, theme)).GeneratePdf();
    }

    private static void Compose(
        IDocumentContainer container, Appointment appointment, ClinicProfile clinic, DocumentTheme theme)
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
                       clinic.GstRegistered ? clinic.Gstin : null, "APPOINTMENT CONFIRMATION", theme, d);

                IdentityRow(col, theme, d,
                    ("Appointment No", appointment.AppointmentNo),
                    ("Date", $"{appointment.ScheduledOn:dd/MM/yyyy}"),
                    ("Time", $"{appointment.ScheduledOn:hh\\:mm tt}"));
                IdentityRow(col, theme, d,
                    ("Patient", appointment.PatientName),
                    ("Doctor", appointment.DoctorName),
                    ("Phone", appointment.PatientPhone));

                Rule(col);

                if (!string.IsNullOrWhiteSpace(appointment.Reason))
                    col.Item().PaddingTop(6).Text($"Reason: {appointment.Reason}")
                        .FontSize(Body(theme, 9, d));

                col.Item().PaddingTop(8).AlignCenter()
                    .Text("Please arrive a few minutes early. Call the clinic to cancel or reschedule.")
                    .FontSize(Body(theme, 7.4f, d)).FontColor(Muted);

                var footer = Footer(clinic.FooterText, theme);
                if (!string.IsNullOrWhiteSpace(footer))
                    col.Item().PaddingTop(3).AlignCenter().Text(footer)
                        .FontSize(Body(theme, 7.4f, d)).FontColor(Muted);
            });
        });
    }
}
