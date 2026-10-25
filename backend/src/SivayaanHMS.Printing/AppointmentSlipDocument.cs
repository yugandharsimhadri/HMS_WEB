using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>Appointment confirmation, ported line for line from the desktop's AppointmentSlipDocument.cs.</summary>
public static class AppointmentSlipDocument
{
    public static byte[] Generate(Appointment appointment, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme, "APPOINTMENT CONFIRMATION");

        var grid = NewTable(1, 1, 1);
        grid.Rows.Add(IdentityRow(
            ("Appointment No", appointment.AppointmentNo),
            ("Date", $"{appointment.ScheduledOn:dd/MM/yyyy}"),
            ("Time", $"{appointment.ScheduledOn:hh\\:mm tt}")));
        grid.Rows.Add(IdentityRow(
            ("Patient", appointment.PatientName),
            ("Doctor", appointment.DoctorName),
            ("Phone", appointment.PatientPhone)));
        doc.Add(grid);
        doc.Add(Rule());

        if (!string.IsNullOrWhiteSpace(appointment.Reason))
            doc.Add(Text($"Reason: {appointment.Reason}", 9, topMargin: 6, role: SizeRole.Body));

        doc.Add(Text(
            "Please arrive a few minutes early. Call the clinic to cancel or reschedule.",
            7.4, brush: Muted, topMargin: 8, role: SizeRole.Footer));

        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, topMargin: 3, role: SizeRole.Footer));

        return doc.ToPdf(theme);
    }
}
