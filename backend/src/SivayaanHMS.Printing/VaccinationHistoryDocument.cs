using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>Vaccination record, ported line for line from the desktop's VaccinationHistoryDocument.cs.</summary>
public static class VaccinationHistoryDocument
{
    public static byte[] Generate(
        Patient patient, IReadOnlyList<VaccinationRecord> records, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme, "VACCINATION RECORD");

        var head = NewTable(1, 1, 1);
        head.Rows.Add(IdentityRow(
            ("Patient", patient.Name),
            ("Patient No", patient.PatientNo),
            ("Age", patient.AgeText)));
        head.Rows.Add(IdentityRow(
            ("Gender", patient.Gender.ToString()),
            ("Guardian", patient.GuardianName ?? "-"),
            ("Printed on", $"{DateTime.Now:dd/MM/yyyy}")));
        doc.Add(head);
        doc.Add(Rule());

        var table = NewTable(1, 1.8, 1.8, 0.6, 1, 1.2);
        table.Rows.Add(Row(true, "GIVEN ON", "TYPE", "BRAND GIVEN", "DOSE", "BATCH", "SITE"));

        foreach (var record in records.OrderBy(r => r.GivenOn))
        {
            var brand = record.ProductName is { } product
                ? (record.Manufacturer is { } maker ? $"{product} ({maker})" : product)
                : "-";

            table.Rows.Add(Row(false,
                $"{record.GivenOn:dd/MM/yyyy}", record.VaccineName, brand, record.DoseNumber.ToString(),
                record.BatchNo ?? "-", record.SiteOfInjection ?? "-"));
        }

        doc.Add(table);

        if (records.Count == 0)
            doc.Add(Text("No doses recorded yet.", 9, brush: Muted, topMargin: 6, role: SizeRole.Body));

        doc.Add(Rule());

        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));

        return doc.ToPdf(theme);
    }
}
