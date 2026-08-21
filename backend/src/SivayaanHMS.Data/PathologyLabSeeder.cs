using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// Preloads the common analytes and the reports (panels) built from them,
/// so Analyte Master and Report Master aren't empty on day one. Prices and
/// units are reasonable defaults, editable afterward from their own master
/// screens — this never overwrites a row that's already there, so anything
/// already edited survives every later run. Analytes are seeded first,
/// since a report is only ever a named list of analytes that already exist.
/// </summary>
public static class PathologyLabSeeder
{
    private static readonly (string Name, string Category, string Units, LabResultType Type, int Decimals)[] Analytes =
    [
        ("Hemoglobin", "Hematology", "g/dL", LabResultType.Numeric, 1),
        ("Total Leukocyte Count", "Hematology", "/cumm", LabResultType.Numeric, 0),
        ("Platelet Count", "Hematology", "/cumm", LabResultType.Numeric, 0),
        ("RBC Count", "Hematology", "mill/cumm", LabResultType.Numeric, 2),
        ("PCV (Hematocrit)", "Hematology", "%", LabResultType.Numeric, 1),
        ("ESR", "Hematology", "mm/hr", LabResultType.Numeric, 0),

        ("Fasting Blood Glucose", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),
        ("Post Prandial Blood Glucose", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),
        ("Random Blood Sugar", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),

        ("Blood Urea", "Biochemistry", "mg/dL", LabResultType.Numeric, 1),
        ("Serum Creatinine", "Biochemistry", "mg/dL", LabResultType.Numeric, 2),
        ("Uric Acid", "Biochemistry", "mg/dL", LabResultType.Numeric, 1),

        ("Bilirubin Total", "Biochemistry", "mg/dL", LabResultType.Numeric, 2),
        ("SGOT (AST)", "Biochemistry", "U/L", LabResultType.Numeric, 0),
        ("SGPT (ALT)", "Biochemistry", "U/L", LabResultType.Numeric, 0),
        ("Alkaline Phosphatase", "Biochemistry", "U/L", LabResultType.Numeric, 0),

        ("Total Cholesterol", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),
        ("Triglycerides", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),
        ("HDL Cholesterol", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),
        ("LDL Cholesterol", "Biochemistry", "mg/dL", LabResultType.Numeric, 0),

        ("T3", "Thyroid", "ng/mL", LabResultType.Numeric, 2),
        ("T4", "Thyroid", "µg/dL", LabResultType.Numeric, 2),
        ("TSH", "Thyroid", "µIU/mL", LabResultType.Numeric, 2),

        ("Urine Albumin", "Urine", "-", LabResultType.Text, 0),
        ("Urine Sugar", "Urine", "-", LabResultType.Text, 0),
        ("Urine Pus Cells", "Urine", "/hpf", LabResultType.Text, 0)
    ];

    private static readonly (string Name, string Category, decimal Price, string[] AnalyteNames)[] Reports =
    [
        ("Complete Blood Picture (CBC)", "Hematology", 300,
            ["Hemoglobin", "Total Leukocyte Count", "Platelet Count", "RBC Count", "PCV (Hematocrit)"]),
        ("ESR", "Hematology", 150, ["ESR"]),

        ("Blood Sugar (Fasting & PP)", "Biochemistry", 200, ["Fasting Blood Glucose", "Post Prandial Blood Glucose"]),
        ("Random Blood Sugar", "Biochemistry", 100, ["Random Blood Sugar"]),

        ("Kidney Function Test (KFT)", "Biochemistry", 600, ["Blood Urea", "Serum Creatinine", "Uric Acid"]),
        ("Liver Function Test (LFT)", "Biochemistry", 600,
            ["Bilirubin Total", "SGOT (AST)", "SGPT (ALT)", "Alkaline Phosphatase"]),
        ("Lipid Profile", "Biochemistry", 600,
            ["Total Cholesterol", "Triglycerides", "HDL Cholesterol", "LDL Cholesterol"]),

        ("Thyroid Profile (T3 T4 TSH)", "Thyroid", 500, ["T3", "T4", "TSH"]),

        ("Urine Routine", "Urine", 150, ["Urine Albumin", "Urine Sugar", "Urine Pus Cells"])
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        foreach (var (name, category, units, type, decimals) in Analytes)
            await EnsureAnalyteAsync(db, name, category, units, type, decimals, ct);

        await db.SaveChangesAsync(ct);

        foreach (var (name, category, price, analyteNames) in Reports)
            await EnsureReportAsync(db, name, category, price, analyteNames, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAnalyteAsync(
        AppDbContext db, string name, string category, string units, LabResultType type, int decimals, CancellationToken ct)
    {
        if (await db.LabAnalytes.AnyAsync(a => a.Name == name, ct)) return;

        db.LabAnalytes.Add(new LabAnalyte
        {
            Name = name, Category = category, Units = units, ResultType = type, DecimalPlaces = decimals
        });
    }

    private static async Task EnsureReportAsync(
        AppDbContext db, string name, string category, decimal price, string[] analyteNames, CancellationToken ct)
    {
        if (await db.LabReports.AnyAsync(r => r.Name == name, ct)) return;

        var report = new LabReport { Name = name, Category = category, Price = price };
        db.LabReports.Add(report);

        var order = 0;
        foreach (var analyteName in analyteNames)
        {
            var analyte = await db.LabAnalytes.FirstOrDefaultAsync(a => a.Name == analyteName, ct);
            if (analyte is null) continue;

            db.LabReportAnalytes.Add(new LabReportAnalyte
            {
                ReportId = report.Id, AnalyteId = analyte.Id, SequenceOrder = order++
            });
        }
    }
}
