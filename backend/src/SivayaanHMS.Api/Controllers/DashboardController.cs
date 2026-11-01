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
public record DashboardTrendDay(DateTime Day, decimal Opd, decimal Pharmacy, decimal Diagnostics, decimal Inpatient);

public record DashboardLowStockRow(Guid ProductId, string Name, decimal StockOnHand, int ReorderLevel);

/// <summary>
/// Everything the landing screen shows, computed in one pass.
///
/// One endpoint rather than several on purpose: the KPI tile and the donut
/// have to agree with each other, and they only reliably do so when they
/// come out of the same arithmetic over the same snapshot. Splitting this
/// into "revenue" and "the split" would let the two be fetched either side
/// of a sale.
///
/// The figures are for the chosen From/To range and are compared with the
/// period of the same length just before it. The queue, the restock list and
/// the beds in use are about now, whatever the range.
/// </summary>
public record DashboardResponse(
    DateTime From, DateTime To,
    int Patients, double PatientsDeltaPercent,
    int InQueueNow,
    decimal Revenue, double RevenueDeltaPercent,
    decimal OpdRevenue, decimal PharmacyRevenue, decimal DiagnosticsRevenue, decimal InpatientRevenue,
    decimal RevenueTrendTotal,
    int LowStockCount,
    bool DiagnosticsEnabled, bool PediatricsEnabled, bool DentistEnabled, bool PathologyLabEnabled, bool InpatientEnabled,
    int IpOccupied, int IpBeds, int IpAdmitted, int IpDischarged,
    List<DashboardTrendDay> Trend,
    List<DashboardLowStockRow> LowStock,
    DateTime ActivityDay,
    List<DashboardActivityRow> RecentActivity);

/// <summary>
/// The landing screen: the period's numbers, which way revenue is moving,
/// where it comes from, what needs restocking, and what just happened.
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
    PathologyLabService pathologyLab,
    InpatientService inpatient) : ControllerBase
{
    /// <summary>The trend always shows at least this many days, so a one-day range still has a line to read.</summary>
    private const int MinTrendDays = 14;

    /// <summary>A year and a bit - the trend is one point a day.</summary>
    private const int MaxRangeDays = 400;

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var general = await settings.GetGeneralAsync();

        var end = (to ?? DateTime.Today).Date;
        var start = (from ?? end).Date;
        if (start > end) (start, end) = (end, start);
        if ((end - start).Days >= MaxRangeDays) start = end.AddDays(-(MaxRangeDays - 1));

        var days = (end - start).Days + 1;

        // The same length again, just before - "vs previous period". For a single day
        // that is yesterday, which is what the tiles always compared against.
        var previousStart = start.AddDays(-days);
        var trendStart = days < MinTrendDays ? end.AddDays(-(MinTrendDays - 1)) : start;
        var readFrom = previousStart < trendStart ? previousStart : trendStart;

        // Aggregate queries returning one number per day, not range queries
        // returning the period's bills and visits to be grouped here.
        //
        // What each counts is unchanged and is the part that matters: only a
        // Completed sale is revenue, since a returned one is not money the
        // clinic kept; an OPD fee counts on the day it was *paid*, not the
        // day the visit was booked; patients counts who was *seen*, so it
        // keys off the visit date and excludes cancellations; and a bill put
        // on an IP bill counts when the IP money arrives, not twice.
        //
        // Issued together, not one after another: each takes its own
        // short-lived context from the factory and none depends on another.
        var pharmacyByDayTask = pharmacy.GetDailyNetAsync(readFrom, end);
        var opdTotalsTask = opd.GetDailyOpdTotalsAsync(readFrom, end);
        var diagByDayTask = general.DiagnosticsEnabled
            ? diagnostics.GetDailyRevenueAsync(readFrom, end)
            : Task.FromResult(new Dictionary<DateTime, decimal>());
        var ipByDayTask = general.InpatientEnabled
            ? inpatient.GetDailyCollectionsAsync(readFrom, end)
            : Task.FromResult(new Dictionary<DateTime, decimal>());
        var ipCountsTask = general.InpatientEnabled
            ? inpatient.GetDashboardCountsAsync(start, end)
            : Task.FromResult((0, 0, 0, 0));

        // The queue is about now; the activity feed shows the last day of the range.
        var todaysVisitsTask = opd.GetVisitsAsync(DateTime.Today);
        var activityVisitsTask = end == DateTime.Today ? todaysVisitsTask : opd.GetVisitsAsync(end);
        var lowStockTask = pharmacy.GetLowStockAsync();
        var salesTask = pharmacy.GetSalesAsync(end);
        var diagBillsTask = general.DiagnosticsEnabled
            ? diagnostics.SearchBillsAsync(end, end)
            : Task.FromResult(new List<DiagnosticBill>());
        var procedureBillsTask = general.PediatricsEnabled || general.DentistEnabled
            ? procedureBills.SearchBillsAsync(end, end)
            : Task.FromResult(new List<ProcedureBill>());
        var dentalPaymentsTask = general.DentistEnabled
            ? dentist.SearchPaymentsAsync(end, end)
            : Task.FromResult(new List<DentalPayment>());
        var labOrdersTask = general.PathologyLabEnabled
            ? pathologyLab.SearchOrdersAsync(end, end)
            : Task.FromResult(new List<LabOrder>());

        await Task.WhenAll(
            pharmacyByDayTask, opdTotalsTask, diagByDayTask, ipByDayTask, ipCountsTask,
            todaysVisitsTask, activityVisitsTask, lowStockTask,
            salesTask, diagBillsTask, procedureBillsTask, dentalPaymentsTask, labOrdersTask);

        var pharmacyByDay = pharmacyByDayTask.Result;
        var (opdFeesByDay, patientsByDay) = opdTotalsTask.Result;
        var diagByDay = diagByDayTask.Result;
        var ipByDay = ipByDayTask.Result;

        DashboardTrendDay Day(DateTime day) => new(day,
            opdFeesByDay.GetValueOrDefault(day), pharmacyByDay.GetValueOrDefault(day),
            diagByDay.GetValueOrDefault(day), ipByDay.GetValueOrDefault(day));

        static decimal Total(DashboardTrendDay d) => d.Opd + d.Pharmacy + d.Diagnostics + d.Inpatient;

        List<DashboardTrendDay> Span(DateTime first, DateTime last)
            => Enumerable.Range(0, (last - first).Days + 1).Select(i => Day(first.AddDays(i))).ToList();

        int PatientsIn(DateTime first, DateTime last)
            => Enumerable.Range(0, (last - first).Days + 1).Sum(i => patientsByDay.GetValueOrDefault(first.AddDays(i)));

        var period = Span(start, end);
        var previous = Span(previousStart, start.AddDays(-1));
        var trend = Span(trendStart, end);

        var revenue = period.Sum(Total);
        var patients = PatientsIn(start, end);

        var inQueue = todaysVisitsTask.Result.Count(v =>
            v.Status is VisitStatus.Booked or VisitStatus.Waiting or VisitStatus.InConsultation);

        var lowStock = lowStockTask.Result;
        var (ipOccupied, ipBeds, ipAdmitted, ipDischarged) = ipCountsTask.Result;

        var activity = BuildActivity(
            activityVisitsTask.Result, salesTask.Result, diagBillsTask.Result,
            procedureBillsTask.Result, dentalPaymentsTask.Result, labOrdersTask.Result);

        return Ok(new DashboardResponse(
            start, end,
            patients, DeltaPercent(patients, PatientsIn(previousStart, start.AddDays(-1))),
            inQueue,
            revenue, DeltaPercent((double)revenue, (double)previous.Sum(Total)),
            period.Sum(d => d.Opd), period.Sum(d => d.Pharmacy), period.Sum(d => d.Diagnostics), period.Sum(d => d.Inpatient),
            trend.Sum(Total),
            lowStock.Count,
            general.DiagnosticsEnabled, general.PediatricsEnabled,
            general.DentistEnabled, general.PathologyLabEnabled, general.InpatientEnabled,
            ipOccupied, ipBeds, ipAdmitted, ipDischarged,
            trend,
            lowStock.Take(5)
                .Select(p => new DashboardLowStockRow(p.Id, p.Name, p.StockOnHand, p.ReorderLevel))
                .ToList(),
            end,
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
                s.BillDate, s.BillNo, s.CustomerName, s.AdmissionId is null ? "Pharmacy" : "Pharmacy · IP", s.NetAmount)));

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
