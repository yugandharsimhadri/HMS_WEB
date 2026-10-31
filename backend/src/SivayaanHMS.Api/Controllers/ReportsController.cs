using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;
using static SivayaanHMS.Printing.ReportBuilders;

namespace SivayaanHMS.Api.Controllers;

/// <summary>The summary cards above the day book — collected, split by mode,
/// and the OPD side, which is not pharmacy revenue and never mixed into it.</summary>
public record DayBookSummary(
    decimal TotalCollected, decimal CashTotal, decimal UpiTotal,
    decimal TaxableTotal, decimal CgstTotal, decimal SgstTotal, decimal NetTotal,
    decimal ConsultationTotal, int VisitCount);

/// <summary>
/// The clinic's own working reports: a day book, a GST summary, an OPD
/// register, four stock views and the Schedule H1 register.
///
/// Every report is built as one <see cref="ReportTable"/> and handed to
/// whichever renderer is asked for — the screen, a PDF, or a workbook. The
/// desktop keeps a bespoke builder per report for each of those three
/// surfaces and a comment explaining that their naming is shared "so both
/// always agree with what is on screen"; building the rows once means they
/// cannot disagree at all.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController(
    PharmacyService pharmacy,
    OpdService opd,
    DiagnosticsService diagnostics,
    ProcedureBillsService procedures,
    PathologyLabService lab,
    DentistService dentist,
    InpatientService inpatient,
    SettingsService settings) : ControllerBase
{
    // ── The report itself ──────────────────────────────────────────────────

    [HttpGet("{kind}")]
    public async Task<ActionResult<ReportTable>> Get(
        ReportKind kind,
        [FromQuery] DateTime? date, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int expiringDays = 90,
        [FromQuery] bool includeZeroStock = false,
        [FromQuery] string? search = null,
        [FromQuery] PaymentMode? mode = null)
        => Ok(await BuildAsync(kind, date ?? DateTime.Today, from ?? DateTime.Today, to ?? DateTime.Today,
                               expiringDays, includeZeroStock, search, mode));

    /// <summary>The cards above the day book. Separate from the table because
    /// they are not rows of it — and because the OPD figure deliberately sits
    /// beside pharmacy revenue rather than inside it.</summary>
    [HttpGet("day-book/summary")]
    public async Task<ActionResult<DayBookSummary>> DayBookSummaryFor([FromQuery] DateTime? date)
    {
        var on = date ?? DateTime.Today;

        var completed = (await pharmacy.GetSalesAsync(on))
            .Where(s => s.Status == SaleStatus.Completed)
            .ToList();
        var visits = await opd.GetVisitsAsync(on);

        var collected = completed.Sum(s => s.NetAmount);

        return Ok(new DayBookSummary(
            collected,
            completed.Where(s => s.PaymentMode == PaymentMode.Cash).Sum(s => s.NetAmount),
            completed.Where(s => s.PaymentMode == PaymentMode.Upi).Sum(s => s.NetAmount),
            completed.Sum(s => s.TaxableAmount),
            completed.Sum(s => s.CgstAmount),
            completed.Sum(s => s.SgstAmount),
            collected,
            visits.Where(v => v.FeePaid).Sum(v => v.Fee),
            visits.Count(v => v.Status != VisitStatus.Cancelled)));
    }

    /// <summary>
    /// Looks a bill up across every date. A walk-in coming back for a copy
    /// rarely remembers which day they bought on — only the name or the
    /// number.
    /// </summary>
    [HttpGet("find-bill")]
    public async Task<ActionResult<ReportTable>> FindBill([FromQuery] string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Ok(await BuildAsync(ReportKind.DayBook, DateTime.Today, DateTime.Today, DateTime.Today, 90, false, null));

        var sales = await pharmacy.SearchSalesAsync(term);
        return Ok(new ReportTable(
            ReportKind.DayBook,
            "Day Book",
            $"{sales.Count} bill(s) matching “{term}”, across all dates",
            DayBookScreenColumns(),
            sales.OrderByDescending(s => s.BillDate).Select(DayBookRow).ToList(),
            []));
    }

    // ── Exports ────────────────────────────────────────────────────────────

    [HttpGet("{kind}/pdf")]
    public async Task<IActionResult> Pdf(
        ReportKind kind,
        [FromQuery] DateTime? date, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int expiringDays = 90,
        [FromQuery] bool includeZeroStock = false,
        [FromQuery] string? search = null,
        [FromQuery] PaymentMode? mode = null)
    {
        if (kind == ReportKind.StockRegister)
            return BadRequest("PDF export is not available for the Stock Register — use Export Excel instead.");

        var d = date ?? DateTime.Today;
        var f = from ?? DateTime.Today;
        var t = to ?? DateTime.Today;

        var table = await BuildAsync(kind, d, f, t, expiringDays, includeZeroStock, search, mode);
        if (table.Rows.Count == 0) return BadRequest("No data available to export.");

        var clinic = await settings.GetClinicAsync();
        return File(ReportPdfBuilder.Generate(table, clinic.Name), "application/pdf",
                    ReportNaming.FileName(kind, d, f, t, "pdf"));
    }

    [HttpGet("{kind}/excel")]
    public async Task<IActionResult> Excel(
        ReportKind kind,
        [FromQuery] DateTime? date, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int expiringDays = 90,
        [FromQuery] bool includeZeroStock = false,
        [FromQuery] string? search = null,
        [FromQuery] PaymentMode? mode = null)
    {
        var d = date ?? DateTime.Today;
        var f = from ?? DateTime.Today;
        var t = to ?? DateTime.Today;

        var table = await BuildAsync(kind, d, f, t, expiringDays, includeZeroStock, search, mode);
        if (table.Rows.Count == 0) return BadRequest("No data available to export.");

        var clinic = await settings.GetClinicAsync();
        return File(ReportExcelBuilder.Generate(table, clinic.Name),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ReportNaming.FileName(kind, d, f, t, "xlsx"));
    }

    // ── Builders ───────────────────────────────────────────────────────────

    private async Task<ReportTable> BuildAsync(
        ReportKind kind, DateTime date, DateTime from, DateTime to,
        int expiringDays, bool includeZeroStock, string? search, PaymentMode? mode = null)
    {
        // A backwards range is a slip, not an error worth refusing — the
        // desktop quietly swaps the ends, and so does this.
        var (start, end) = from <= to ? (from, to) : (to, from);

        var label = ReportNaming.DateLabel(kind, date, start, end);
        var title = ReportNaming.Title(kind);

        return kind switch
        {
            ReportKind.DayBook => DayBook(await pharmacy.GetSalesAsync(date), title, label),
            ReportKind.GstSummary => GstSummary(await pharmacy.GetGstTotalsAsync(start, end), title, label),
            ReportKind.OpdRegister => OpdRegister(await opd.GetVisitsAsync(date), title, label),
            ReportKind.ExpiringSoon => ExpiringSoon(await pharmacy.GetExpiringAsync(expiringDays), expiringDays, title, label),
            ReportKind.LowStock => LowStock(await pharmacy.GetLowStockAsync(), title, label),
            ReportKind.StockRegister => StockRegister(await pharmacy.GetAllBatchesAsync(includeZeroStock), search, title, label),
            ReportKind.ScheduleH1 => ScheduleH1(await pharmacy.GetH1RegisterAsync(start, end), title, label),
            ReportKind.Collections => await CollectionsAsync(start, end, mode, title, label),
            ReportKind.OpdByDoctor => await OpdByDoctorAsync(start, end, title, label),
            _ => new ReportTable(kind, title, label, [], [], [])
        };
    }


    /// <summary>
    /// OPD activity grouped by doctor over a range — how many patients each
    /// one saw and what their consultations brought in, the two figures a
    /// clinic actually reviews per-doctor rather than per-visit. Grouped by
    /// <c>DoctorId</c> rather than the loaded <see cref="Doctor"/> itself:
    /// <c>AsNoTracking</c> hands back a fresh instance per row, so grouping
    /// by the entity would silently split one doctor into as many groups as
    /// they had visits.
    ///
    /// The desktop has no such report, so it is laid out the way its siblings are: built typed once
    /// (the workbook wants typed cells) and read as text for the screen and the PDF.
    /// </summary>
    private async Task<ReportTable> OpdByDoctorAsync(DateTime from, DateTime to, string title, string label)
    {
        var visits = await opd.GetVisitsWithDoctorAsync(from, to);

        var rows = visits
            .GroupBy(v => v.DoctorId)
            .Select(g => new { Doctor = g.First().Doctor, Visits = g.ToList() })
            .OrderBy(g => g.Doctor.Name)
            .Select(g => new ReportRow(
                [g.Doctor.Name, g.Doctor.Speciality,
                 g.Visits.Count(v => v.Status != VisitStatus.Cancelled),
                 g.Visits.Count(v => v.Status == VisitStatus.Cancelled),
                 g.Visits.Where(v => v.FeePaid).Sum(v => v.Fee)]))
            .ToList();

        return Triple(new ReportTable(ReportKind.OpdByDoctor, title, label,
            [
                Typed("Doctor", ReportFormat.Text), Typed("Speciality", ReportFormat.Text),
                Typed("Patients seen", ReportFormat.Integer, sum: true), Typed("Cancelled", ReportFormat.Integer, sum: true),
                Typed("Fee collected", ReportFormat.Money, sum: true),
            ],
            rows,
            [
                Count("Doctors", rows.Count),
                Count("Patients seen", visits.Count(v => v.Status != VisitStatus.Cancelled)),
                new("Fee collected", visits.Where(v => v.FeePaid).Sum(v => v.Fee)),
            ]));
    }

    // ── Collections ────────────────────────────────────────────────────────

    /// <summary>One receipt, wherever in the clinic the money came from.</summary>
    /// <summary><paramref name="DocumentId"/> is the record the money was taken
    /// against — the visit, sale, bill, order or payment — so any row here can
    /// be reprinted. <paramref name="Source"/> says which kind it is, and the
    /// screen maps that to the matching print route.</summary>
    private sealed record Collection(
        DateTime TakenOn, PaymentMode? Mode, string Source, string Reference,
        string Who, decimal Amount, string? TransactionNo, Guid DocumentId);

    /// <summary>
    /// Every rupee taken across the clinic in a date range, by how it was
    /// paid. Filtering to Cash gives the till to count; filtering to UPI gives
    /// the list to check a bank statement against, which is the job nobody
    /// could do before.
    ///
    /// It reaches into every module deliberately. A clinic's takings are not
    /// pharmacy takings plus a bit — a day's cash is consultation fees,
    /// medicines, tests, procedures, lab work and dental instalments, and a
    /// report that shows only some of them cannot be reconciled against a
    /// drawer.
    ///
    /// Each source contributes on the date the money arrived. That matters
    /// most for consultation fees, which are collected separately from the
    /// visit — see OpdService.GetFeeCollectionsAsync — and for dental
    /// instalments, which are paid long after the case opened.
    /// </summary>
    private async Task<ReportTable> CollectionsAsync(
        DateTime from, DateTime to, PaymentMode? mode, string title, string label)
    {
        var taken = new List<Collection>();

        foreach (var v in await opd.GetFeeCollectionsAsync(from, to))
            taken.Add(new Collection(
                v.FeePaidOn!.Value, v.FeePaymentMode ?? PaymentMode.Cash, "Consultation",
                v.FeeReceiptNo ?? "", v.Patient?.Name ?? "", v.Fee, v.FeeTransactionNo, v.Id));

        // Cancelled and returned bills took no money, so they are not takings.
        foreach (var s in (await pharmacy.GetSalesAsync(from, to)).Where(s => s.Status == SaleStatus.Completed))
            taken.Add(new Collection(
                s.BillDate, s.PaymentMode, "Pharmacy", s.BillNo,
                s.CustomerName, s.NetAmount, s.TransactionNo, s.Id));

        foreach (var b in await diagnostics.SearchBillsAsync(from, to))
            taken.Add(new Collection(
                b.BillDate, b.PaymentMode, "Diagnostics", b.BillNo,
                b.PatientName, b.FinalAmount, b.TransactionNo, b.Id));

        foreach (var b in await procedures.SearchBillsAsync(from, to))
            taken.Add(new Collection(
                b.BillDate, b.PaymentMode, "Procedures", b.BillNo,
                b.PatientName, b.FinalAmount, b.TransactionNo, b.Id));

        foreach (var o in await lab.SearchOrdersAsync(from, to))
            taken.Add(new Collection(
                o.OrderDate, o.PaymentMode, "Pathology Lab", o.OrderNo,
                o.PatientName, o.FinalAmount, o.TransactionNo, o.Id));

        foreach (var p in await dentist.SearchPaymentsAsync(from, to))
            taken.Add(new Collection(
                p.PaidOn, p.PaymentMode, "Dentist", p.ReceiptNo,
                p.DentalCase?.PatientName ?? "", p.Amount, p.TransactionNo, p.Id));

        // In-patient money: advances and settlements as they arrive, a refund as money
        // going back out, and an insurer's payment with no counter mode of its own.
        foreach (var p in await inpatient.GetPaymentsAsync(from, to))
            taken.Add(new Collection(
                p.Payment.PaidOn, p.Payment.Mode, "In-patient", p.Payment.ReceiptNo,
                p.PatientName,
                p.Payment.Kind == IpPaymentKind.Refund ? -p.Payment.Amount : p.Payment.Amount,
                p.Payment.Reference, p.Payment.Id));

        // Totals are computed before the filter, so a report narrowed to UPI
        // still says what share of the whole that was. A UPI figure with
        // nothing to compare it against is half an answer.
        var everything = taken.Sum(t => t.Amount);

        var rows = (mode is { } m ? taken.Where(t => t.Mode == m) : taken)
            .OrderBy(t => t.TakenOn)
            .ToList();

        static string ModeName(PaymentMode? each) => each switch { PaymentMode.Upi => "UPI", null => "Insurance", _ => each.ToString()! };

        var totals = new List<ReportTotal>
        {
            Count("Receipts", rows.Count),
            new(mode is { } only ? $"{ModeName(only)} collected" : "Collected", rows.Sum(t => t.Amount)),
        };

        // The per-mode split is the point of the report when nothing is
        // filtered — it is what gets checked against the drawer and the bank.
        if (mode is null)
        {
            foreach (var each in Enum.GetValues<PaymentMode>())
                totals.Add(new(ModeName(each), taken.Where(t => t.Mode == each).Sum(t => t.Amount)));
            if (taken.Any(t => t.Mode is null))
                totals.Add(new(ModeName(null), taken.Where(t => t.Mode is null).Sum(t => t.Amount)));
        }
        else if (everything > 0)
        {
            totals.Add(new("Share of all takings",
                Math.Round(rows.Sum(t => t.Amount) / everything * 100, 1), ReportFormat.Text));
        }

        return Triple(new ReportTable(ReportKind.Collections, title,
            mode is { } shown ? $"{label} · {ModeName(shown)} only" : label,
            [
                Typed("When", ReportFormat.DateTime), Typed("Mode", ReportFormat.Text), Typed("Source", ReportFormat.Text),
                Typed("Reference", ReportFormat.Text), Typed("Patient", ReportFormat.Text), Typed("Txn ref", ReportFormat.Text),
                Typed("Amount", ReportFormat.Money, sum: true),
            ],
            rows.Select(t => new ReportRow(
                [t.TakenOn, ModeName(t.Mode), t.Source, t.Reference, t.Who, t.TransactionNo ?? "", t.Amount],
                Id: t.DocumentId))
                .ToList(),
            totals));
    }

    // ── The two tabs with no export of their own ──────────────────────────

    /// <summary>
    /// Stock put on the shelf at the counter with no supplier bill behind it.
    /// Purchases will not tie out against sales until each is matched to the
    /// real bill, so this list is the reconciliation worklist.
    /// </summary>
    [HttpGet("to-reconcile")]
    public async Task<ActionResult<ReportTable>> ToReconcile()
    {
        var batches = await pharmacy.GetProvisionalBatchesAsync();

        return Ok(new ReportTable(ReportKind.None, "Stock to reconcile", $"{batches.Count} provisional batch(es)",
            [
                // Whose bill is missing, and which one. Without these the list can say that
                // something needs a bill but not whose, which is most of the work of reconciling.
                Left("ADDED", 1, ReportTone.Muted), Left("MEDICINE", 2, ReportTone.Name), Left("BATCH", 1, ReportTone.Muted),
                Left("EXPIRY", 1, ReportTone.Muted), Right("ON HAND"), Right("MRP"), Right("RATE PAID"),
                Left("SUPPLIER", 1.4, ReportTone.Muted), Left("THEIR BILL", 1.2, ReportTone.Muted),
            ],
            batches.OrderBy(b => b.ReceivedOn)
                .Select(b => new ReportRow(
                    [b.ReceivedOn.ToString("dd MMM yyyy"), b.Product.Name, b.BatchNo, b.ExpiryDate.ToString("MMM yyyy"),
                     b.QtyOnHand.ToString(), N2(b.Mrp), N2(b.PurchaseRate), b.SupplierName ?? "", b.SupplierInvoiceNo ?? ""]))
                .ToList(),
            []));
    }

    /// <summary>
    /// Tail ends of opened strips — less than one full pack left. They expire
    /// where they sit unless somebody pushes them.
    /// </summary>
    [HttpGet("part-packs")]
    public async Task<ActionResult<ReportTable>> PartPacks()
    {
        var batches = await pharmacy.GetPartPacksAsync();

        return Ok(new ReportTable(ReportKind.None, "Part packs", $"{batches.Count} open pack(s)",
            [
                Left("MEDICINE", 2, ReportTone.Name), Left("BATCH"), Left("EXPIRY", 1, ReportTone.Muted),
                Right("LEFT"), Right("OF A PACK OF"), Right("MRP"),
            ],
            batches.OrderBy(b => b.ExpiryDate)
                .Select(b => new ReportRow(
                    [b.Product.Name, b.BatchNo, b.ExpiryDate.ToString("MMM yyyy"), b.QtyOnHand.ToString(),
                     b.UnitsPerPack.ToString(), N2(b.Mrp)]))
                .ToList(),
            []));
    }

    // ── Diagnostics ────────────────────────────────────────────────────────

    /// <summary>
    /// Today's bills off the Date picker; revenue-by-day and the most
    /// frequently ordered tests off the From/To range — the same split the
    /// day book and the GST summary already use.
    /// </summary>
    [HttpGet("diagnostics")]
    public async Task<ActionResult<object>> DiagnosticsReport(
        [FromQuery] DateTime? date, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!(await settings.GetGeneralAsync()).DiagnosticsEnabled)
            return BadRequest("The Diagnostics module is switched off.");

        var on = date ?? DateTime.Today;
        var f = from ?? DateTime.Today;
        var t = to ?? DateTime.Today;
        var (start, end) = f <= t ? (f, t) : (t, f);

        // Today's bills are a list and are read as one. The other two are
        // aggregates and are summed in the database — grouping a year of
        // bills and items here meant loading all of them to produce a few
        // dozen rows.
        var todays = (await diagnostics.SearchBillsAsync(on, on)).OrderByDescending(b => b.BillDate).ToList();
        var revenue = await diagnostics.GetRevenueByDayAsync(start, end);
        var topTests = await diagnostics.GetTopTestsAsync(start, end);

        return Ok(new
        {
            TodayTotal = todays.Sum(b => b.FinalAmount),
            TodaysBills = todays.Select(b => new
            {
                b.Id, b.BillNo, b.BillDate, b.PatientName, b.PatientNo, Tests = b.Items.Count, b.FinalAmount, Status = b.Status.ToString()
            }),
            Revenue = revenue.Select(r => new { r.Day, r.Bills, r.Amount }),
            TopTests = topTests.Select(t => new { t.Test, t.Times, t.Amount })
        });
    }
}
