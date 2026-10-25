using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>
/// OPD prescription slip, ported from the desktop's PrescriptionPrinter.cs —
/// boxed clinic header, identity grid, Rx table, follow-up. Body text runs two
/// points larger than the pharmacy bill and the diagnostic bill print at, by
/// request; every size below is the document's original size plus that same +2.
///
/// Where this deliberately differs from the desktop, each at the clinic's
/// request or because this system holds data the desktop does not:
/// <list type="bullet">
/// <item><b>No Visit No, Patient No or Token</b> in the identity grid. The
/// desktop still prints all three; the clinic asked for them off this slip, and
/// for the doctor's registration number to take their place. The registration
/// therefore has its own cell and is no longer repeated in the credentials line.</item>
/// <item><b>Height, heart rate and SpO₂</b> after the vitals the desktop prints
/// (weight, blood pressure, temperature). The desktop has nowhere to record
/// them; here a clinician may have, and dropping them from the paper would lose
/// what they entered.</item>
/// </list>
/// </summary>
public static class PrescriptionDocument
{
    private const double SizeDelta = 2;

    public static byte[] Generate(Visit visit, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme, null);

        doc.Add(Text(visit.Doctor.Name, 10 + SizeDelta, Weight.SemiBold, align: Align.Center));

        if (!string.IsNullOrWhiteSpace(visit.Doctor.Speciality))
            doc.Add(Text(visit.Doctor.Speciality!, 8 + SizeDelta, brush: Muted, align: Align.Center, role: SizeRole.Contact));

        // Date and time side by side; the patient in row 2; the doctor, their
        // speciality and registration in row 3.
        var grid = NewTable(1, 1, 1);
        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Date", $"{visit.ScheduledOn:dd/MM/yyyy}"),
            ("Time", $"{visit.ScheduledOn:hh\\:mm tt}")));
        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Patient", visit.Patient.Name),
            ("Age / Sex", $"{visit.Patient.Age} / {visit.Patient.Gender}")));
        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Doctor", visit.Doctor.Name),
            ("Speciality", visit.Doctor.Speciality ?? ""),
            ("Reg. No", visit.Doctor.RegistrationNo ?? "—")));
        doc.Add(grid);
        doc.Add(Rule());

        var vitals = new List<string>();
        if (visit.WeightKg is { } w) vitals.Add($"Wt {w:0.#} kg");
        if (!string.IsNullOrWhiteSpace(visit.BloodPressure)) vitals.Add($"BP {visit.BloodPressure}");
        if (visit.TemperatureF is { } t) vitals.Add($"Temp {t:0.#} °F");
        if (visit.HeightCm is { } h) vitals.Add($"Ht {h:0.#} cm");
        if (visit.HeartRateBpm is { } hr) vitals.Add($"HR {hr}/min");
        if (visit.Spo2Percent is { } spo2) vitals.Add($"SpO₂ {spo2}%");
        if (vitals.Count > 0)
            doc.Add(Text(string.Join("   ·   ", vitals), 8 + SizeDelta, brush: Muted, role: SizeRole.Contact));

        if (!string.IsNullOrWhiteSpace(visit.Complaint))
            doc.Add(LabelValueText("Complaint", visit.Complaint, 8.5 + SizeDelta, topMargin: 3, role: SizeRole.Body));

        if (!string.IsNullOrWhiteSpace(visit.Diagnosis))
            doc.Add(LabelValueText("Diagnosis", visit.Diagnosis, 8.5 + SizeDelta, role: SizeRole.Body));

        doc.Add(Text("Rx", 15 + SizeDelta, Weight.Bold, topMargin: 6, bottomMargin: 1));

        if (visit.Prescription.Count > 0)
        {
            var table = NewTable(3.4, 1.4, 1.2, 0.8, 0.8);
            table.Rows.Add(Row(true, SizeDelta, "MEDICINE", "DOSE", "FREQUENCY", "DAYS", "QTY"));

            foreach (var item in visit.Prescription)
            {
                table.Rows.Add(Row(false, SizeDelta,
                    item.MedicineName,
                    item.Dosage ?? "",
                    item.Frequency ?? "",
                    item.Days > 0 ? item.Days.ToString() : "",
                    item.Quantity > 0 ? item.Quantity.ToString() : ""));

                if (!string.IsNullOrWhiteSpace(item.Instructions))
                    table.Rows.Add(Row(false, SizeDelta, $"    {item.Instructions}", "", "", "", ""));
            }

            doc.Add(table);
        }
        else
        {
            doc.Add(Text("No medicines prescribed.", 8.5 + SizeDelta, brush: Muted, role: SizeRole.Body));
        }

        if (visit.DiagnosticRequests.Count > 0)
        {
            doc.Add(Text("Investigations advised", 12 + SizeDelta, Weight.Bold, topMargin: 8, bottomMargin: 1));

            var tests = NewTable(1);
            tests.Rows.Add(Row(true, SizeDelta, "TEST"));

            foreach (var request in visit.DiagnosticRequests)
                tests.Rows.Add(Row(false, SizeDelta, request.TestName));

            doc.Add(tests);
        }

        if (!string.IsNullOrWhiteSpace(visit.Notes))
            doc.Add(Text($"Advice: {visit.Notes}", 8.5 + SizeDelta, topMargin: 4, role: SizeRole.Body));

        if (visit.FollowUpOn is { } follow)
            doc.Add(Text($"Review on {follow:dd MMM yyyy}", 9 + SizeDelta, Weight.SemiBold, topMargin: 4, role: SizeRole.Body));

        // The clinic's own footer, set on the Clinic settings tab, takes over from
        // the shared Reports footer once it is typed.
        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.6 + SizeDelta, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));

        doc.Add(Text(visit.Doctor.Name, 8.5 + SizeDelta, Weight.SemiBold, align: Align.Right, topMargin: 20));
        doc.Add(Text("Signature", 7.4 + SizeDelta, brush: Muted, align: Align.Right, role: SizeRole.Footer));

        return doc.ToPdf(theme);
    }
}
