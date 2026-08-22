using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.DocumentStyle;

namespace SivayaanHMS.Printing;

/// <summary>
/// A patient's vaccination record, printable as a simple certificate-style
/// list — what a parent hands to a school or asks for before travel. Prints
/// even when empty, saying so: a card showing no doses is a real answer,
/// and refusing to print one would send the parent away with nothing.
/// </summary>
public static class VaccinationHistoryDocument
{
    public static byte[] Generate(
        Patient patient, IReadOnlyList<VaccinationRecord> records, ClinicProfile clinic, DocumentTheme theme)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(c => Compose(c, patient, records, clinic, theme)).GeneratePdf();
    }

    private static void Compose(
        IDocumentContainer container, Patient patient, IReadOnlyList<VaccinationRecord> records,
        ClinicProfile clinic, DocumentTheme theme)
    {
        const float d = ClinicalSizeDelta;

        container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontFamily(BodyFont(theme)).FontSize(Body(theme, 9, d)));

            page.Content().Column(col =>
            {
                Header(col, clinic.Name, clinic.AddressLine, clinic.AddressLine2, clinic.Phone,
                       clinic.GstRegistered ? clinic.Gstin : null, "VACCINATION RECORD", theme, d);

                IdentityRow(col, theme, d,
                    ("Patient", patient.Name),
                    ("Patient No", patient.PatientNo),
                    ("Age", $"{patient.Age} yrs"));
                IdentityRow(col, theme, d,
                    ("Gender", patient.Gender.ToString()),
                    ("Guardian", patient.GuardianName ?? "-"),
                    ("Printed on", $"{DateTime.Now:dd/MM/yyyy}"));

                Rule(col);

                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1f); c.RelativeColumn(1.8f); c.RelativeColumn(1.8f);
                        c.RelativeColumn(0.6f); c.RelativeColumn(1f); c.RelativeColumn(1.2f);
                    });

                    HeaderRow(table, theme, d, "GIVEN ON", "TYPE", "BRAND GIVEN", "DOSE", "BATCH", "SITE");

                    foreach (var record in records.OrderBy(r => r.GivenOn))
                    {
                        // The brand is the manufacturer's, and a parent
                        // travelling may be asked for exactly it.
                        var brand = record.ProductName is { } product
                            ? (record.Manufacturer is { } maker ? $"{product} ({maker})" : product)
                            : "-";

                        Row(table, theme, d,
                            $"{record.GivenOn:dd/MM/yyyy}",
                            record.VaccineName,
                            brand,
                            record.DoseNumber.ToString(),
                            record.BatchNo ?? "-",
                            record.SiteOfInjection ?? "-");
                    }
                });

                if (records.Count == 0)
                    col.Item().PaddingTop(6).Text("No doses recorded yet.")
                        .FontSize(Body(theme, 9, d)).FontColor(Muted);

                Rule(col);

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

    private static void Row(TableDescriptor table, DocumentTheme theme, float d, params string[] cells)
    {
        foreach (var cell in cells)
            table.Cell().PaddingVertical(1.5f).Text(cell).FontSize(Body(theme, 8.5f, d));
    }
}
