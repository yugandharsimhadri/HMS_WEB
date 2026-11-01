using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// Counter bills put on an IP bill: they join the running bill in their category, and
/// they never count as money taken at the counter.
/// </summary>
public class IpLinkedBillingTests
{
    private static DateTime At(int day, int hour) => new(2026, 10, day, hour, 0, 0);

    [Fact]
    public void Counter_bills_join_the_running_bill_in_their_category()
    {
        var stays = new List<BedStay>
        {
            new() { BedNo = "GW-01", RoomTypeName = "General", DailyRent = 800, From = At(29, 10) }
        };
        var charges = new List<IpCharge>
        {
            new() { Category = IpChargeCategory.DoctorVisit, Description = "Dr. A. Kumar - visit", Quantity = 1, Rate = 500, Amount = 500, ChargeDate = At(30, 0) },
            new() { Category = IpChargeCategory.Other, Description = "Ambulance", Quantity = 1, Rate = 1000, Amount = 1000, ChargeDate = At(29, 0) },
        };
        var saleId = Guid.NewGuid();
        var linked = new List<IpBillLineView>
        {
            new(IpChargeCategory.Medicine, At(30, 0), "Pharmacy bill B00012: Ondansetron, ORS", 1, 640, 640, 0, null, "Pharmacy", saleId),
            new(IpChargeCategory.Investigation, At(29, 0), "Lab order L00007: CBC", 1, 350, 350, 0, null, "Lab", Guid.NewGuid()),
        };

        var (lines, _) = InpatientService.BuildRunningLines(
            At(29, 10), At(31, 9), stays, charges, BedDayRule.MidnightCount, 2, linked);

        Assert.Equal(
            [IpChargeCategory.Room, IpChargeCategory.DoctorVisit, IpChargeCategory.Investigation, IpChargeCategory.Medicine, IpChargeCategory.Other],
            lines.Select(l => l.Category));

        var medicine = lines.Single(l => l.Category == IpChargeCategory.Medicine);
        Assert.Equal(("Pharmacy", saleId, (Guid?)null), (medicine.Source, medicine.SourceId, medicine.ChargeId));
        Assert.Equal(0, medicine.GstPercent);   // sold at MRP, GST already inside
        Assert.Equal(1600m + 500 + 350 + 640 + 1000, lines.Sum(l => l.Amount));
    }

    [Fact]
    public void A_pharmacy_bill_on_an_ip_bill_is_not_counted_as_cash()
    {
        Sale Bill(string no, PaymentMode mode, decimal net, Guid? admission) => new()
        {
            BillNo = no, BillDate = DateTime.Today.AddHours(11), CustomerName = "Ravi", PaymentMode = mode,
            Status = SaleStatus.Completed, NetAmount = net, AdmissionId = admission, Items = { new SaleItem() }
        };

        var t = ReportBuilders.DayBook(
            [Bill("B1", PaymentMode.Cash, 300m, null), Bill("B2", PaymentMode.Cash, 640m, Guid.NewGuid()), Bill("B3", PaymentMode.Upi, 100m, null)],
            "Day Book", "x");

        decimal Total(string label) => (decimal)t.Totals.Single(x => x.Label == label).Value!;
        Assert.Equal(1040m, Total("Net Sales"));     // still a sale
        Assert.Equal(300m, Total("Cash"));            // but not cash in the drawer
        Assert.Equal(100m, Total("UPI"));
        Assert.Equal(640m, Total("On IP bills"));
        Assert.Equal("IP", t.Rows.Single(r => (string)r.Cells[0]! == "B2").Cells[^1]);
    }

    [Fact]
    public void A_day_book_without_ip_bills_keeps_its_desktop_totals()
    {
        var sale = new Sale
        {
            BillNo = "B1", BillDate = DateTime.Today, PaymentMode = PaymentMode.Cash, Status = SaleStatus.Completed, NetAmount = 10m
        };

        var t = ReportBuilders.DayBook([sale], "Day Book", "x");

        Assert.DoesNotContain(t.Totals, x => x.Label == "On IP bills");
    }

    [Fact]
    public void A_multi_day_book_shows_the_date_with_the_time()
    {
        var sale = new Sale
        {
            BillNo = "B1", BillDate = new DateTime(2026, 10, 30, 14, 5, 0), PaymentMode = PaymentMode.Cash, Status = SaleStatus.Completed
        };

        Assert.Equal("14:05", ReportBuilders.DayBook([sale], "Day Book", "x").Rows[0].Cells[1]);
        Assert.Equal("30 Oct 14:05", ReportBuilders.DayBook([sale], "Day Book", "x", multiDay: true).Rows[0].Cells[1]);
    }

    [Fact]
    public void Reports_about_a_period_take_a_range_and_snapshots_take_none()
    {
        Assert.True(ReportNaming.IsRangeBased(ReportKind.DayBook));
        Assert.True(ReportNaming.IsRangeBased(ReportKind.OpdRegister));
        Assert.True(ReportNaming.IsRangeBased(ReportKind.IpBills));
        Assert.False(ReportNaming.IsRangeBased(ReportKind.IpCensus));

        var from = new DateTime(2026, 10, 1);
        var to = new DateTime(2026, 10, 31);
        Assert.Equal("01 Oct 2026 to 31 Oct 2026", ReportNaming.DateLabel(ReportKind.DayBook, to, from, to));
        Assert.StartsWith("As of ", ReportNaming.DateLabel(ReportKind.IpDues, to, from, to));
        Assert.Equal("IPBills_2026-10-01_to_2026-10-31.pdf", ReportNaming.FileName(ReportKind.IpBills, to, from, to, "pdf"));
    }
}
