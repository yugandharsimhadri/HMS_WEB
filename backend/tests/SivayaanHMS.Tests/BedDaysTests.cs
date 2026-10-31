using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// Bed days decide most of an IP bill, so every rule is pinned here with the
/// worked examples from the plan.
/// </summary>
public class BedDaysTests
{
    private static StaySpan Stay(string room, decimal rent, DateTime from, DateTime? to = null, decimal nursing = 0)
        => new("B1", room, rent, nursing, 0, from, to);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    [Fact]
    public void Midnight_count_is_one_day_per_midnight()
    {
        // 29 Oct 22:40 to 1 Nov 11:15 crosses three midnights.
        var days = BedDays.Count(At(29, 22, 40), new DateTime(2026, 11, 1, 11, 15, 0),
            [Stay("General", 800, At(29, 22, 40))], BedDayRule.MidnightCount);

        Assert.Equal(3, days.Count);
        Assert.Equal([At(29, 0).Date, At(30, 0).Date, At(31, 0).Date], days.Select(d => d.Date));
    }

    [Fact]
    public void Same_day_admission_and_discharge_is_one_day()
    {
        var days = BedDays.Count(At(29, 9), At(29, 17), [Stay("General", 800, At(29, 9), At(29, 17))], BedDayRule.MidnightCount);

        Assert.Single(days);
    }

    [Theory]
    [InlineData(26, 1)]   // 26 h - 2 h grace = 24 h -> one block
    [InlineData(27, 2)]   // 25 h -> two
    [InlineData(5, 1)]    // never fewer than one
    [InlineData(60, 3)]   // 58 h -> 2.4 -> three
    public void Twenty_four_hour_blocks_round_up_after_the_grace(int hours, int expected)
    {
        var from = At(29, 22, 40);
        var days = BedDays.Count(from, from.AddHours(hours), [Stay("General", 800, from)], BedDayRule.TwentyFourHours, graceHours: 2);

        Assert.Equal(expected, days.Count);
    }

    [Fact]
    public void A_transfer_day_is_charged_at_the_higher_rate()
    {
        // General ward from 29 Oct, to ICU at 03:10 on 31 Oct, discharged 2 Nov.
        var general = Stay("General", 800, At(29, 10), At(31, 3, 10));
        var icu = Stay("ICU", 4500, At(31, 3, 10));

        var days = BedDays.Count(At(29, 10), new DateTime(2026, 11, 2, 11, 0, 0), [general, icu], BedDayRule.MidnightCount);

        Assert.Equal([800m, 800m, 4500m, 4500m], days.Select(d => d.Stay.DailyRent));
    }

    [Fact]
    public void A_step_down_day_is_still_charged_at_the_higher_rate()
    {
        var icu = Stay("ICU", 4500, At(29, 10), At(30, 20));
        var general = Stay("General", 800, At(30, 20));

        var days = BedDays.Count(At(29, 10), At(31, 12), [icu, general], BedDayRule.MidnightCount);

        Assert.Equal([4500m, 4500m], days.Select(d => d.Stay.DailyRent));
    }

    [Fact]
    public void Consecutive_days_in_one_room_become_one_line()
    {
        var general = Stay("General", 800, At(25, 10), At(28, 9));
        var icu = Stay("ICU", 4500, At(28, 9));
        var days = BedDays.Count(At(25, 10), At(30, 10), [general, icu], BedDayRule.MidnightCount);

        var runs = BedDays.Runs(days);

        Assert.Equal(2, runs.Count);
        Assert.Equal((3, "General"), (runs[0].Days, runs[0].Stay.RoomTypeName));   // 25, 26, 27
        Assert.Equal((2, "ICU"), (runs[1].Days, runs[1].Stay.RoomTypeName));       // 28, 29
    }

    [Fact]
    public void The_running_bill_puts_room_and_nursing_before_manual_charges()
    {
        var stays = new List<BedStay>
        {
            new() { BedNo = "PW-02", RoomTypeName = "Paediatric ward", DailyRent = 1200, NursingPerDay = 250, From = At(31, 11, 40) }
        };
        var charges = new List<IpCharge>
        {
            new() { Category = IpChargeCategory.DoctorVisit, Description = "Dr. A. Kumar - visit", Quantity = 4, Rate = 500, Amount = 2000, ChargeDate = At(31, 0) },
        };

        var (lines, bedDays) = InpatientService.BuildRunningLines(
            At(31, 11, 40), new DateTime(2026, 11, 2, 11, 15, 0), stays, charges, BedDayRule.MidnightCount, 2);

        Assert.Equal(2, bedDays);
        Assert.Equal([IpChargeCategory.Room, IpChargeCategory.Nursing, IpChargeCategory.DoctorVisit], lines.Select(l => l.Category));
        Assert.Equal([2400m, 500m, 2000m], lines.Select(l => l.Amount));
    }
}

/// <summary>The balance on an IP bill under each way of settling it.</summary>
public class IpMoneyTests
{
    private static IpPayment Pay(IpPaymentKind kind, decimal amount) => new() { Kind = kind, Amount = amount };

    [Fact]
    public void Advance_more_than_the_bill_is_a_refund_due()
    {
        var money = InpatientService.Money(12460, 0, 500, 0, [Pay(IpPaymentKind.Advance, 15000)]);

        Assert.Equal(11960m, money.Net);
        Assert.Equal(-3040m, money.Balance);
    }

    [Fact]
    public void A_refund_brings_the_balance_to_zero()
    {
        var money = InpatientService.Money(12460, 0, 500, 0,
            [Pay(IpPaymentKind.Advance, 15000), Pay(IpPaymentKind.Refund, 3040)]);

        Assert.Equal(0m, money.Balance);
    }

    [Fact]
    public void Insurance_pays_part_and_the_patient_pays_the_rest()
    {
        // Bill 50,000; insurer pays 42,000; the patient owes 8,000 less their 5,000 advance.
        var afterInsurance = InpatientService.Money(50000, 0, 0, 0,
            [Pay(IpPaymentKind.Advance, 5000), Pay(IpPaymentKind.Insurance, 42000)]);
        Assert.Equal(3000m, afterInsurance.Balance);

        var settled = InpatientService.Money(50000, 0, 0, 0,
            [Pay(IpPaymentKind.Advance, 5000), Pay(IpPaymentKind.Insurance, 42000), Pay(IpPaymentKind.Settlement, 3000)]);
        Assert.Equal(0m, settled.Balance);
    }

    [Fact]
    public void Adjusting_to_the_insurance_amount_writes_off_the_rest()
    {
        // Bill 50,000; insurer pays 42,000 and the hospital accepts it as full payment.
        var money = InpatientService.Money(50000, 0, 0, 8000, [Pay(IpPaymentKind.Insurance, 42000)]);

        Assert.Equal(50000m, money.Net);
        Assert.Equal(8000m, money.InsuranceAdjustment);
        Assert.Equal(0m, money.Balance);
    }
}
