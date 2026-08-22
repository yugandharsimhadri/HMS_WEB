using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>One row of the mixed activity feed.</summary>
public record DashboardActivityRow(
    DateTime When, string BillNo, string PatientName, string Department, decimal Amount);

/// <summary>One day of the trend, per category. Sent as numbers, not as a
/// drawn path: the server owns the money, the browser owns the geometry.</summary>
public record DashboardTrendDay(DateTime Day, decimal Opd, decimal Pharmacy, decimal Diagnostics);

public record DashboardLowStockRow(Guid ProductId, string Name, decimal StockOnHand, int ReorderLevel);

/// <summary>
/// Everything the landing screen shows, computed in one pass.
///
/// One endpoint rather than several on purpose: the KPI tile and the donut
/// have to agree with each other, and they only reliably do so when they
/// come out of the same arithmetic over the same snapshot. Splitting this
/// into "today's revenue" and "today's split" would let the two be fetched
/// either side of a sale.
/// </summary>
public record DashboardResponse(
    int PatientsToday, double PatientsDeltaPercent,
    int InQueueNow,
    decimal RevenueToday, double RevenueDeltaPercent,
    decimal OpdRevenueToday, decimal PharmacyRevenueToday, decimal DiagnosticsRevenueToday,
    decimal RevenueTrendTotal,
    int LowStockCount,
    bool DiagnosticsEnabled, bool PediatricsEnabled, bool DentistEnabled, bool PathologyLabEnabled,
    List<DashboardTrendDay> Trend,
    List<DashboardLowStockRow> LowStock,
    List<DashboardActivityRow> RecentActivity);

/// <summary>
/// The landing screen: today's numbers, which way revenue is moving, where
/// it comes from, what needs restocking, and what just happened.
///
/// Nothing here is a destination in its own right — every figure has a real
/// screen behind it for the detail.
/// </summary>
[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(
    OpdService opd,
    PharmacyService pharmacy,
    DiagnosticsService diagnostics,
    SettingsService settings,
    ProcedureBillsService procedureBills,
    DentistService dentist,
    PathologyLabService pathologyLab) : ControllerBase
{
    private const int TrendDays = 14;

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get()
    {
        var general = await settings.GetGeneralAsync();

        var to = DateTime.Today;
        var from = to.AddDays(-(TrendDays - 1));

        // Three range queries, not forty-two daily ones — the same shape the
        // desktop uses, and the same one Reports uses for its own ranges.
        var salesRange = await pharmacy.GetSalesAsync(from, to);
        var visitsRange = await opd.GetVisitsAsync(from, to);
        var diagRange = general.DiagnosticsEnabled
            ? await diagnostics.SearchBillsAsync(from, to)
            : [];

        var trend = new List<DashboardTrendDay>(TrendDays);
        decimal todayOpd = 0, todayPharmacy = 0, todayDiag = 0, yesterdayTotal = 0;
        int todayPatients = 0, yesterdayPatients = 0;

        for (var i = 0; i < TrendDays; i++)
        {
            var day = from.AddDays(i).Date;

            // Only a completed sale is revenue. A returned or cancelled one
            // is not money the clinic kept.
            var dayPharmacy = salesRange
                .Where(s => s.Status == SaleStatus.Completed && s.BillDate.Date == day)
                .Sum(s => s.NetAmount);

            // Revenue counts when the fee was *paid*, not when the visit was
            // booked — a visit booked today and paid tomorrow is tomorrow's
            // money.
            var dayOpd = visitsRange
                .Where(v => v.FeePaid && v.FeePaidOn?.Date == day)
                .Sum(v => v.Fee);

            var dayDiag = diagRange.Where(b => b.BillDate.Date == day).Sum(b => b.FinalAmount);

            // Patients counts who was *seen*, so it keys off the visit date
            // and excludes cancellations.
            var dayPatients = visitsRange.Count(v => v.ScheduledOn.Date == day && v.Status != VisitStatus.Cancelled);

            trend.Add(new DashboardTrendDay(day, dayOpd, dayPharmacy, dayDiag));

            if (day == to)
            {
                todayOpd = dayOpd; todayPharmacy = dayPharmacy; todayDiag = dayDiag;
                todayPatients = dayPatients;
            }
            if (day == to.AddDays(-1))
            {
                yesterdayTotal = dayPharmacy + dayOpd + dayDiag;
                yesterdayPatients = dayPatients;
            }
        }

        var revenueToday = todayOpd + todayPharmacy + todayDiag;

        // Today's own richer query — it includes Patient, which the range
        // query above deliberately skips since a trend never shows a name.
        var todaysVisits = await opd.GetVisitsAsync(to);
        var inQueue = todaysVisits.Count(v =>
            v.Status is VisitStatus.Booked or VisitStatus.Waiting or VisitStatus.InConsultation);

        var lowStock = await pharmacy.GetLowStockAsync();

        var procedureBillsToday = general.PediatricsEnabled || general.DentistEnabled
            ? await procedureBills.SearchBillsAsync(to, to)
            : [];
        var dentalPaymentsToday = general.DentistEnabled ? await dentist.SearchPaymentsAsync(to, to) : [];
        var labOrdersToday = general.PathologyLabEnabled ? await pathologyLab.SearchOrdersAsync(to, to) : [];

        var activity = BuildActivity(
            todaysVisits,
            salesRange.Where(s => s.BillDate.Date == to),
            diagRange.Where(b => b.BillDate.Date == to),
            procedureBillsToday, dentalPaymentsToday, labOrdersToday);

        return Ok(new DashboardResponse(
            todayPatients, DeltaPercent(todayPatients, yesterdayPatients),
            inQueue,
            revenueToday, DeltaPercent((double)revenueToday, (double)yesterdayTotal),
            todayOpd, todayPharmacy, todayDiag,
            trend.Sum(d => d.Opd + d.Pharmacy + d.Diagnostics),
            lowStock.Count,
            general.DiagnosticsEnabled, general.PediatricsEnabled,
            general.DentistEnabled, general.PathologyLabEnabled,
            trend,
            lowStock.Take(5)
                .Select(p => new DashboardLowStockRow(p.Id, p.Name, p.StockOnHand, p.ReorderLevel))
                .ToList(),
            activity));
    }

    private static List<DashboardActivityRow> BuildActivity(
        IEnumerable<Visit> todaysVisits,
        IEnumerable<Sale> todaysSales,
        IEnumerable<DiagnosticBill> todaysDiagBills,
        IEnumerable<ProcedureBill> todaysProcedureBills,
        IEnumerable<DentalPayment> todaysDentalPayments,
        IEnumerable<LabOrder> todaysLabOrders)
    {
        var rows = new List<DashboardActivityRow>();

        rows.AddRange(todaysVisits
            .Where(v => v.FeePaid)
            .Select(v => new DashboardActivityRow(
                v.FeePaidOn ?? v.ScheduledOn, v.FeeReceiptNo ?? "", v.Patient.Name, "OPD", v.Fee)));

        rows.AddRange(todaysSales
            .Where(s => s.Status == SaleStatus.Completed)
            .Select(s => new DashboardActivityRow(
                s.BillDate, s.BillNo, s.CustomerName, "Pharmacy", s.NetAmount)));

        rows.AddRange(todaysDiagBills
            .Select(b => new DashboardActivityRow(
                b.BillDate, b.BillNo, b.PatientName, "Diagnostics", b.FinalAmount)));

        // A procedure bill does not carry which department raised it, so it
        // reads simply "Procedure" rather than guessing Pediatrics or
        // Dentist.
        rows.AddRange(todaysProcedureBills
            .Select(b => new DashboardActivityRow(
                b.BillDate, b.BillNo, b.PatientName, "Procedure", b.FinalAmount)));

        rows.AddRange(todaysDentalPayments
            .Select(p => new DashboardActivityRow(
                p.PaidOn, p.ReceiptNo, p.DentalCase.PatientName, "Dentist", p.Amount)));

        rows.AddRange(todaysLabOrders
            .Select(o => new DashboardActivityRow(
                o.OrderDate, o.OrderNo, o.PatientName, "Pathology Lab", o.FinalAmount)));

        return rows.OrderByDescending(r => r.When).Take(8).ToList();
    }

    /// <summary>
    /// Yesterday at zero is not a divide-by-zero and not an infinite rise:
    /// anything today is "100%", nothing today is "0%".
    /// </summary>
    private static double DeltaPercent(double today, double yesterday)
    {
        if (yesterday <= 0) return today > 0 ? 100 : 0;
        return Math.Round((today - yesterday) / yesterday * 100, 0);
    }
}
