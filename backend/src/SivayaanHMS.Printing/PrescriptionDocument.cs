using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// OPD prescription slip. Ported from the desktop's PrescriptionPrinter with
/// the same content in the same order: clinic letterhead, the doctor and
/// their credentials, a three-row identity grid, vitals, complaint and
/// diagnosis, the Rx table (with per-line instructions indented beneath their
/// medicine), investigations advised, advice, follow-up, and a signature
/// block.
/// </summary>
public static class PrescriptionDocument
{
    public static byte[] Generate(Visit visit, ClinicProfile clinic, DocumentTheme theme)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, visit, clinic, theme)).GeneratePdf();
    }

    private static void Compose(IDocumentContainer container, Visit visit, ClinicProfile clinic, DocumentTheme theme)
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
                       clinic.GstRegistered ? clinic.Gstin : null, null, theme, d);

                col.Item().PaddingTop(3).AlignCenter().Text(visit.Doctor.Name)
                    .FontSize(Body(theme, 10, d)).SemiBold();

                var credentials = new List<string>();
                if (!string.IsNullOrWhiteSpace(visit.Doctor.Speciality)) credentials.Add(visit.Doctor.Speciality!);
                if (!string.IsNullOrWhiteSpace(visit.Doctor.RegistrationNo)) credentials.Add($"Reg. No: {visit.Doctor.RegistrationNo}");
                if (credentials.Count > 0)
                    col.Item().AlignCenter().Text(string.Join("   |   ", credentials))
                        .FontSize(Body(theme, 8, d)).FontColor(Muted);

                IdentityRow(col, theme, d,
                    ("Visit No", visit.VisitNo),
                    ("Date", $"{visit.ScheduledOn:dd/MM/yyyy}"),
                    ("Time", $"{visit.ScheduledOn:hh\\:mm tt}"));
                IdentityRow(col, theme, d,
                    ("Patient", visit.Patient.Name),
                    ("Patient No", visit.Patient.PatientNo),
                    ("Age / Sex", $"{visit.Patient.Age} / {visit.Patient.Gender}"));
                IdentityRow(col, theme, d,
                    ("Doctor", visit.Doctor.Name),
                    ("Speciality", visit.Doctor.Speciality ?? ""),
                    ("Token", visit.TokenNo.ToString()));

                Rule(col);

                var vitals = new List<string>();
                if (visit.WeightKg is { } w) vitals.Add($"Wt {w:0.#} kg");
                if (!string.IsNullOrWhiteSpace(visit.BloodPressure)) vitals.Add($"BP {visit.BloodPressure}");
                if (visit.TemperatureF is { } t) vitals.Add($"Temp {t:0.#} °F");
                if (visit.HeightCm is { } h) vitals.Add($"Ht {h:0.#} cm");
                if (visit.HeartRateBpm is { } hr) vitals.Add($"HR {hr}/min");
                if (visit.Spo2Percent is { } spo2) vitals.Add($"SpO₂ {spo2}%");
                if (vitals.Count > 0)
                    col.Item().Text(string.Join("   ·   ", vitals)).FontSize(Body(theme, 8, d)).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(visit.Complaint))
                    LabelValue(col, theme, d, "Complaint", visit.Complaint!);

                if (!string.IsNullOrWhiteSpace(visit.Diagnosis))
                    LabelValue(col, theme, d, "Diagnosis", visit.Diagnosis!);

                col.Item().PaddingTop(6).Text("Rx").FontSize(Body(theme, 15, d)).Bold();

                if (visit.Prescription.Count > 0)
                {
                    col.Item().PaddingTop(1).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3.4f); c.RelativeColumn(1.4f);
                            c.RelativeColumn(1.2f); c.RelativeColumn(0.8f); c.RelativeColumn(0.8f);
                        });

                        foreach (var head in new[] { "MEDICINE", "DOSE", "FREQUENCY", "DAYS", "QTY" })
                            table.Cell().BorderBottom(1).PaddingBottom(2).Text(head)
                                .FontSize(Body(theme, 7.5f, d)).Bold();

                        foreach (var item in visit.Prescription)
                        {
                            Cell(table, item.MedicineName);
                            Cell(table, item.Dosage ?? "");
                            Cell(table, item.Frequency ?? "");
                            Cell(table, item.Days > 0 ? item.Days.ToString() : "");
                            Cell(table, item.Quantity > 0 ? item.Quantity.ToString() : "");

                            // Instructions ride under their own medicine, indented,
                            // rather than in a column of their own - they are usually
                            // a sentence and would squeeze every other column.
                            if (!string.IsNullOrWhiteSpace(item.Instructions))
                            {
                                table.Cell().ColumnSpan(5).PaddingLeft(12).PaddingBottom(2)
                                    .Text(item.Instructions).FontSize(Body(theme, 8, d)).FontColor(Muted);
                            }
                        }

                        void Cell(TableDescriptor t, string value)
                            => t.Cell().PaddingVertical(1.5f).Text(value).FontSize(Body(theme, 8.5f, d));
                    });
                }
                else
                {
                    col.Item().Text("No medicines prescribed.").FontSize(Body(theme, 8.5f, d)).FontColor(Muted);
                }

                if (visit.DiagnosticRequests.Count > 0)
                {
                    col.Item().PaddingTop(8).Text("Investigations advised")
                        .FontSize(Body(theme, 12, d)).Bold();

                    foreach (var request in visit.DiagnosticRequests)
                        col.Item().PaddingLeft(8).Text($"•  {request.TestName}").FontSize(Body(theme, 8.5f, d));
                }

                if (!string.IsNullOrWhiteSpace(visit.Notes))
                    col.Item().PaddingTop(4).Text($"Advice: {visit.Notes}").FontSize(Body(theme, 8.5f, d));

                if (visit.FollowUpOn is { } follow)
                    col.Item().PaddingTop(4).Text($"Review on {follow:dd MMM yyyy}")
                        .FontSize(Body(theme, 9, d)).SemiBold();

                var footer = Footer(clinic.FooterText, theme);
                if (!string.IsNullOrWhiteSpace(footer))
                    col.Item().PaddingTop(6).AlignCenter().Text(footer)
                        .FontSize(Body(theme, 7.6f, d)).FontColor(Muted);

                col.Item().PaddingTop(20).AlignRight().Text(visit.Doctor.Name)
                    .FontSize(Body(theme, 8.5f, d)).SemiBold();
                col.Item().AlignRight().Text("Signature").FontSize(Body(theme, 7.4f, d)).FontColor(Muted);
            });
        });
    }
}
