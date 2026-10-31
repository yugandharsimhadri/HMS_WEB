using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>Every IP print renders from a realistic admission, interim and final.</summary>
public class IpDocumentsTests
{
    internal static Admission SampleAdmission()
    {
        var bed = new Bed { BedNo = "PW-02", Ward = "Paediatric ward", RoomType = new RoomType { Name = "Paediatric ward", DailyRent = 1200, NursingPerDay = 250 } };
        return new Admission
        {
            IpNo = "IP00042",
            Patient = new Patient { Name = "Aarav Rao", PatientNo = "P00012", Age = 4, AgeMonths = 6, Gender = Gender.Male, Phone = "9848012345" },
            Doctor = new Doctor { Name = "Dr. A. Kumar", RegistrationNo = "TSMC/1234" },
            Bed = bed,
            AdmittedOn = new DateTime(2026, 10, 31, 11, 40, 0),
            Reason = "Acute gastroenteritis with dehydration",
            AttendantName = "Sita Rao", AttendantRelation = "Mother", AttendantPhone = "9848012345",
            PayerType = PayerType.Insurance, InsurerName = "Star Health", PolicyNo = "SH-889911",
            Payments =
            [
                new IpPayment { ReceiptNo = "ADV00118", PaidOn = new DateTime(2026, 10, 31, 11, 44, 0), Kind = IpPaymentKind.Advance, Amount = 10000, Mode = PaymentMode.Upi, Reference = "UTR 4302" },
                new IpPayment { ReceiptNo = "INS00003", PaidOn = new DateTime(2026, 11, 5, 15, 0, 0), Kind = IpPaymentKind.Insurance, Amount = 2000, Reference = "CLM-7781" },
            ],
        };
    }

    private static IpBillView SampleBill(bool final)
    {
        var stays = new List<BedStay> { new() { BedNo = "PW-02", RoomTypeName = "Paediatric ward", DailyRent = 1200, NursingPerDay = 250, From = new DateTime(2026, 10, 31, 11, 40, 0) } };
        var charges = new List<IpCharge>
        {
            new() { Category = IpChargeCategory.DoctorVisit, Description = "Dr. A. Kumar - visit", Quantity = 4, Rate = 500, Amount = 2000, ChargeDate = new DateTime(2026, 10, 31) },
            new() { Category = IpChargeCategory.Investigation, Description = "CBC, CRP, electrolytes", Quantity = 1, Rate = 1410, Amount = 1410, ChargeDate = new DateTime(2026, 10, 31) },
        };
        var (lines, days) = InpatientService.BuildRunningLines(new DateTime(2026, 10, 31, 11, 40, 0), new DateTime(2026, 11, 2, 11, 15, 0), stays, charges, BedDayRule.MidnightCount, 2);
        var a = SampleAdmission();
        var money = InpatientService.Money(lines.Sum(l => l.Amount), 0, final ? 500 : 0, 0, a.Payments);
        return new IpBillView(final, final ? Guid.NewGuid() : null, final ? "IPB00017" : null, new DateTime(2026, 11, 2, 11, 15, 0), days, lines, money);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_bill_prints(bool final)
    {
        var a = SampleAdmission();
        var pdf = IpDocuments.Bill(a, SampleBill(final), a.Payments, new ClinicProfile { Name = "Twinkle Children's Hospital" }, new DocumentTheme());
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void The_slip_summary_and_receipts_print()
    {
        var a = SampleAdmission();
        var clinic = new ClinicProfile { Name = "Twinkle Children's Hospital" };
        var theme = new DocumentTheme();

        Assert.True(IpDocuments.AdmissionSlip(a, a.Payments, clinic, theme).Length > 1000);
        Assert.True(IpDocuments.Summary(a, new DischargeSummary { FinalDiagnosis = "AGE, resolved", CourseInHospital = "Line one\nLine two", FollowUpOn = new DateTime(2026, 11, 7) }, clinic, theme).Length > 1000);
        foreach (var p in a.Payments)
            Assert.True(IpDocuments.Receipt(a, p, clinic, theme).Length > 1000);
    }
}
