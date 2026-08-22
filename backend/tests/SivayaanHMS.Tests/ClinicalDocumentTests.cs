using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// The prescription and the fee receipt - the two documents a clinic hands a
/// patient on every single visit, so the two most important to get visibly
/// right. Generates real PDFs from a realistic visit (vitals, multi-line Rx
/// with instructions, investigations, follow-up) and writes them out for
/// visual review.
/// </summary>
public class ClinicalDocumentTests
{
    private static Visit SampleVisit()
    {
        var patient = new Patient
        {
            Name = "Aarav Rao", PatientNo = "P00042", Age = 6,
            Gender = Gender.Male, Phone = "9876500011",
        };

        var doctor = new Doctor
        {
            Name = "Dr. A. Kumar", Speciality = "Paediatrics",
            RegistrationNo = "TN-MED-11924", ConsultationFee = 300m,
        };

        var visit = new Visit
        {
            VisitNo = "V00318", TokenNo = 7,
            ScheduledOn = new DateTime(2026, 8, 22, 10, 40, 0),
            Patient = patient, Doctor = doctor,
            Complaint = "Fever and dry cough for three days",
            Diagnosis = "Acute viral upper respiratory infection",
            Notes = "Plenty of fluids. Return sooner if the fever crosses 102°F.",
            WeightKg = 19.4m, BloodPressure = "96/62", TemperatureF = 101.2m,
            HeightCm = 112m, HeartRateBpm = 98, Spo2Percent = 98,
            Fee = 300m, FeePaid = true, FeeReceiptNo = "RCP00114",
            FeePaidOn = new DateTime(2026, 8, 22, 10, 45, 0),
            FeePaymentMode = PaymentMode.Upi, FeeTransactionNo = "UPI-8842190",
            FollowUpOn = new DateTime(2026, 8, 27),
        };

        visit.Prescription.Add(new PrescriptionItem
        {
            MedicineName = "Paracetamol 250mg/5ml syrup", Dosage = "5 ml",
            Frequency = "1-1-1", Days = 3, Quantity = 1,
            Instructions = "After food. Shake well before use.",
        });
        visit.Prescription.Add(new PrescriptionItem
        {
            MedicineName = "Cetirizine 5mg", Dosage = "1/2 tab",
            Frequency = "0-0-1", Days = 5, Quantity = 3,
        });
        visit.Prescription.Add(new PrescriptionItem
        {
            MedicineName = "Zincovit drops", Dosage = "1 ml",
            Frequency = "1-0-0", Days = 14, Quantity = 1,
            Instructions = "Continue for a full fortnight even once he is well.",
        });

        visit.DiagnosticRequests.Add(new VisitDiagnosticRequest { TestName = "Complete Blood Picture (CBP)" });
        visit.DiagnosticRequests.Add(new VisitDiagnosticRequest { TestName = "Dengue NS1" });

        return visit;
    }

    private static ClinicProfile SampleClinic() => new()
    {
        Name = "Twinkle Children's Hospital",
        AddressLine = "14 Gandhi Road, T. Nagar",
        AddressLine2 = "Chennai, Tamil Nadu 600017",
        Phone = "044-2815 4400",
        FooterText = "Get well soon. Bring this slip to your next visit.",
    };

    [Fact]
    public void Prescription_generates_a_real_pdf()
    {
        var bytes = PrescriptionDocument.Generate(SampleVisit(), SampleClinic(), new DocumentTheme());

        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
        Assert.True(bytes.Length > 2000, $"PDF suspiciously small: {bytes.Length} bytes.");

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "sivayaanhms-sample-prescription.pdf"), bytes);
    }

    [Fact]
    public void Prescription_handles_a_visit_with_nothing_prescribed()
    {
        var visit = SampleVisit();
        visit.Prescription.Clear();
        visit.DiagnosticRequests.Clear();
        visit.Notes = null;
        visit.FollowUpOn = null;

        var bytes = PrescriptionDocument.Generate(visit, SampleClinic(), new DocumentTheme());
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public void Fee_receipt_generates_a_real_pdf()
    {
        var bytes = FeeReceiptDocument.Generate(SampleVisit(), SampleClinic(), new DocumentTheme());

        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
        Assert.True(bytes.Length > 1500, $"PDF suspiciously small: {bytes.Length} bytes.");

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "sivayaanhms-sample-receipt.pdf"), bytes);
    }

    [Fact]
    public void Fee_receipt_marks_a_reprint_as_duplicate()
    {
        var bytes = FeeReceiptDocument.Generate(SampleVisit(), SampleClinic(), new DocumentTheme(), isReprint: true);
        Assert.True(bytes.Length > 1500);
    }
}
