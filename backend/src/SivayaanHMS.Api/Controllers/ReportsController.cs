using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

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
            DayBookColumns(),
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
            ReportKind.DayBook => await DayBookAsync(date, title, label),
            ReportKind.GstSummary => await GstSummaryAsync(start, end, title, label),
            ReportKind.OpdRegister => await OpdRegisterAsync(date, title, label),
            ReportKind.ExpiringSoon => await ExpiringAsync(expiringDays, title, label),
            ReportKind.LowStock => await LowStockAsync(title, label),
            ReportKind.StockRegister => await StockRegisterAsync(includeZeroStock, search, title, label),
            ReportKind.ScheduleH1 => await H1Async(start, end, title, label),
            ReportKind.Collections => await CollectionsAsync(start, end, mode, title, label),
            ReportKind.OpdByDoctor => await OpdByDoctorAsync(start, end, title, label),
            _ => new ReportTable(kind, title, label, [], [], [])
        };
    }

    private static List<ReportColumn> DayBookColumns() =>
    [
        new("Bill No", ReportAlign.Left, ReportFormat.Text, 1.0),
        new("Time", ReportAlign.Left, ReportFormat.Time, 0.7),
        new("Customer", ReportAlign.Left, ReportFormat.Text, 1.8),
        // Who prescribed it, and how many lines the bill ran to — the two
        // things that let the desk recognise a bill without opening it.
        new("Doctor", ReportAlign.Left, ReportFormat.Text, 1.4),
        new("Items", ReportAlign.Right, ReportFormat.Integer, 0.6),
        new("Taxable", ReportAlign.Right, ReportFormat.Money, 1.0),
        new("CGST", ReportAlign.Right, ReportFormat.Money, 0.9),
        new("SGST", ReportAlign.Right, ReportFormat.Money, 0.9),
        new("Net", ReportAlign.Right, ReportFormat.Money, 1.0),
        new("Mode", ReportAlign.Left, ReportFormat.Text, 0.7),
    ];

    private static ReportRow DayBookRow(Sale s) => new(
        [s.BillNo, s.BillDate, s.CustomerName, s.DoctorName, s.Items.Count,
         s.TaxableAmount, s.CgstAmount, s.SgstAmount, s.NetAmount, s.PaymentMode.ToString()],
        Id: s.Id);

    /// <summary>
    /// The day book shows **completed bills only**. A cancelled or returned
    /// sale is not revenue, and leaving it in would double count against the
    /// summary cards, which already exclude it.
    /// </summary>
    private async Task<ReportTable> DayBookAsync(DateTime date, string title, string label)
    {
        var completed = (await pharmacy.GetSalesAsync(date))
            .Where(s => s.Status == SaleStatus.Completed)
            .OrderByDescending(s => s.BillDate)
            .ToList();

        return new ReportTable(ReportKind.DayBook, title, label,
            DayBookColumns(),
            completed.Select(DayBookRow).ToList(),
            [
                new("Taxable", completed.Sum(s => s.TaxableAmount)),
                new("CGST", completed.Sum(s => s.CgstAmount)),
                new("SGST", completed.Sum(s => s.SgstAmount)),
                new("Net collected", completed.Sum(s => s.NetAmount)),
            ]);
    }

    /// <summary>
    /// GST is summarised per slab from the sale **items**, not per bill — one
    /// bill can carry 5% and 12% lines at once, and a per-bill split could
    /// not separate them.
    /// </summary>
    private async Task<ReportTable> GstSummaryAsync(DateTime from, DateTime to, string title, string label)
    {
        // Aggregated in the database. Loading the period's sales to group
        // them here meant materialising 5,400 bills and 18,700 lines for a
        // financial year to produce five rows.
        var slabs = await pharmacy.GetGstTotalsAsync(from, to);

        var rows = new List<ReportRow>();
        decimal grandTaxable = 0, grandCgst = 0, grandSgst = 0, grandTotal = 0;

        foreach (var slab in slabs)
        {
            var taxable = slab.Taxable;
            var gst = slab.Gst;

            // Half away-from-zero, then the other half as the remainder, so
            // CGST + SGST is always exactly the GST collected — splitting
            // both by rounding could leave a paisa unaccounted for.
            var half = Math.Round(gst / 2m, 2, MidpointRounding.AwayFromZero);
            var cgst = gst - half;
            var sgst = half;
            var total = taxable + gst;

            rows.Add(new ReportRow([$"{slab.GstRate:0.##}%", taxable, cgst, sgst, total]));

            grandTaxable += taxable; grandCgst += cgst; grandSgst += sgst; grandTotal += total;
        }

        return new ReportTable(ReportKind.GstSummary, title, label,
            [
                new("GST slab", ReportAlign.Left, ReportFormat.Text, 0.8),
                new("Taxable", ReportAlign.Right, ReportFormat.Money),
                new("CGST", ReportAlign.Right, ReportFormat.Money),
                new("SGST", ReportAlign.Right, ReportFormat.Money),
                new("Total", ReportAlign.Right, ReportFormat.Money),
            ],
            rows,
            [
                new("Taxable", grandTaxable),
                new("CGST", grandCgst),
                new("SGST", grandSgst),
                new("Total", grandTotal),
            ]);
    }

    private async Task<ReportTable> OpdRegisterAsync(DateTime date, string title, string label)
    {
        var visits = await opd.GetVisitsAsync(date);

        return new ReportTable(ReportKind.OpdRegister, title, label,
            [
                // The visit number, not just the token: a token is unique
                // within a day, and a register read weeks later needs the
                // number that is unique across all of them.
                new("Visit No", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Token", ReportAlign.Right, ReportFormat.Integer, 0.5),
                new("Time", ReportAlign.Left, ReportFormat.Time, 0.7),
                new("Patient", ReportAlign.Left, ReportFormat.Text, 1.6),
                // Age and sex are what make a register identify a person
                // rather than merely name them — two children share a name
                // far more often than a name and an age.
                new("Age", ReportAlign.Right, ReportFormat.Integer, 0.5),
                new("Gender", ReportAlign.Left, ReportFormat.Text, 0.7),
                new("Doctor", ReportAlign.Left, ReportFormat.Text, 1.4),
                new("Status", ReportAlign.Left, ReportFormat.Text, 0.9),
                new("Fee", ReportAlign.Right, ReportFormat.Money, 0.8),
                new("Paid", ReportAlign.Center, ReportFormat.Text, 0.6),
                new("Receipt", ReportAlign.Left, ReportFormat.Text, 1.0),
            ],
            visits.OrderBy(v => v.TokenNo)
                .Select(v => new ReportRow(
                    [v.VisitNo, v.TokenNo, v.ScheduledOn, v.Patient.Name,
                     v.Patient.Age, v.Patient.Gender.ToString(), v.Doctor.Name,
                     v.Status.ToString(), v.Fee, v.FeePaid ? "Yes" : "No", v.FeeReceiptNo],
                    // Only a paid visit has a receipt to reprint.
                    Id: v.FeePaid ? v.Id : null))
                .ToList(),
            [
                new("Collected", visits.Where(v => v.FeePaid).Sum(v => v.Fee)),
                new("Patients seen", visits.Count(v => v.Status != VisitStatus.Cancelled), ReportFormat.Integer),
            ]);
    }

    /// <summary>
    /// OPD activity grouped by doctor over a range — how many patients each
    /// one saw and what their consultations brought in, the two figures a
    /// clinic actually reviews per-doctor rather than per-visit. Grouped by
    /// <c>DoctorId</c> rather than the loaded <see cref="Doctor"/> itself:
    /// <c>AsNoTracking</c> hands back a fresh instance per row, so grouping
    /// by the entity would silently split one doctor into as many groups as
    /// they had visits.
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

        return new ReportTable(ReportKind.OpdByDoctor, title, label,
            [
                new("Doctor", ReportAlign.Left, ReportFormat.Text, 1.8),
                new("Speciality", ReportAlign.Left, ReportFormat.Text, 1.4),
                new("Patients seen", ReportAlign.Right, ReportFormat.Integer, 1.0),
                new("Cancelled", ReportAlign.Right, ReportFormat.Integer, 0.9),
                new("Fee collected", ReportAlign.Right, ReportFormat.Money, 1.1),
            ],
            rows,
            [
                new("Doctors", rows.Count, ReportFormat.Integer),
                new("Patients seen", visits.Count(v => v.Status != VisitStatus.Cancelled), ReportFormat.Integer),
                new("Fee collected", visits.Where(v => v.FeePaid).Sum(v => v.Fee)),
            ]);
    }

    /// <summary>
    /// Already-expired stock is called **"Expired"** in its own column rather
    /// than left to a red row tint to say alone — a colour-blind or
    /// low-vision reader gets the same signal a sighted one does, and a
    /// printed report has no tint at all.
    /// </summary>
    private async Task<ReportTable> ExpiringAsync(int days, string title, string label)
    {
        var batches = await pharmacy.GetExpiringAsync(days);
        var today = DateTime.Today;

        return new ReportTable(ReportKind.ExpiringSoon, title, $"{label} · within {days} days",
            [
                new("Status", ReportAlign.Left, ReportFormat.Text, 0.9),
                new("Medicine", ReportAlign.Left, ReportFormat.Text, 2.0),
                new("Batch", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Expiry", ReportAlign.Left, ReportFormat.Date, 1.0),
                new("Qty left", ReportAlign.Right, ReportFormat.Integer, 0.7),
                // Only sealed packs go back to the distributor; an opened
                // strip cannot. Without this the report says "47 left" and
                // leaves the pharmacist to work out that only 4 strips are
                // claimable and 7 loose are a write-off.
                new("Returnable", ReportAlign.Left, ReportFormat.Text, 1.8),
                new("MRP", ReportAlign.Right, ReportFormat.Money, 0.9),
                // Who to claim from, and how long there is to do it.
                new("Supplier", ReportAlign.Left, ReportFormat.Text, 1.4),
                new("Days remaining", ReportAlign.Right, ReportFormat.Integer, 0.9),
                new("Value at MRP", ReportAlign.Right, ReportFormat.Money, 1.1),
            ],
            batches.OrderBy(b => b.ExpiryDate)
                .Select(b => new ReportRow(
                    [b.ExpiryDate.Date < today ? "Expired" : "Expiring",
                     b.Product.Name, b.BatchNo, b.ExpiryDate,
                     b.QtyOnHand, b.Returnable, b.Mrp, b.SupplierName,
                     (int)(b.ExpiryDate.Date - today).TotalDays,
                     b.QtyOnHand * b.Mrp],
                    Emphasise: b.ExpiryDate.Date < today,
                    Note: b.ExpiryDate.Date < today ? "Expired" : null))
                .ToList(),
            [
                new("Batches", batches.Count, ReportFormat.Integer),
                new("Value at MRP", batches.Sum(b => b.QtyOnHand * b.Mrp)),
            ]);
    }

    private async Task<ReportTable> LowStockAsync(string title, string label)
    {
        var products = await pharmacy.GetLowStockAsync();

        return new ReportTable(ReportKind.LowStock, title, label,
            [
                new("Medicine", ReportAlign.Left, ReportFormat.Text, 2.2),
                new("Manufacturer", ReportAlign.Left, ReportFormat.Text, 1.6),
                // The pack it is ordered in — a reorder is placed in packs,
                // not in loose units, so a shortage of 50 means nothing
                // without knowing whether a pack is 10 or 100.
                new("Pack", ReportAlign.Left, ReportFormat.Text, 0.9),
                new("Rack", ReportAlign.Left, ReportFormat.Text, 0.8),
                new("In stock", ReportAlign.Right, ReportFormat.Integer, 0.8),
                new("Reorder at", ReportAlign.Right, ReportFormat.Integer, 0.8),
                new("Shortage", ReportAlign.Right, ReportFormat.Integer, 0.8),
            ],
            products.Select(p => new ReportRow(
                    [p.Name, p.Manufacturer, p.PackSize, p.RackLocation,
                     p.StockOnHand, p.ReorderLevel,
                     Math.Max(0, p.ReorderLevel - p.StockOnHand)]))
                .ToList(),
            [new("Medicines", products.Count, ReportFormat.Integer)]);
    }

    /// <summary>
    /// A live snapshot of every batch on the shelf, not tied to any date
    /// picker. The search box filters the same rows the totals are computed
    /// from, so a filtered register's totals describe what is on screen
    /// rather than the whole shelf.
    /// </summary>
    private async Task<ReportTable> StockRegisterAsync(bool includeZeroStock, string? search, string title, string label)
    {
        var all = await pharmacy.GetAllBatchesAsync(includeZeroStock);
        var batches = all.Where(b => StockRegisterFilter.Matches(b, search)).ToList();
        var summary = StockSummary.From(batches);

        return new ReportTable(ReportKind.StockRegister, title, label,
            [
                new("Medicine", ReportAlign.Left, ReportFormat.Text, 2.0),
                new("Manufacturer", ReportAlign.Left, ReportFormat.Text, 1.4),
                new("Pack", ReportAlign.Left, ReportFormat.Text, 0.9),
                new("Batch", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Expiry", ReportAlign.Left, ReportFormat.Date, 1.0),
                new("Rack", ReportAlign.Left, ReportFormat.Text, 0.7),
                new("Current stock", ReportAlign.Right, ReportFormat.Integer, 0.8),
                // Carried per row rather than only in the totals: this is the
                // sheet somebody sorts by shortage to build an order from.
                new("Reorder level", ReportAlign.Right, ReportFormat.Integer, 0.8),
                new("Shortage", ReportAlign.Right, ReportFormat.Integer, 0.8),
                new("Purchase rate", ReportAlign.Right, ReportFormat.Money, 0.9),
                new("MRP", ReportAlign.Right, ReportFormat.Money, 0.9),
                new("Cost value", ReportAlign.Right, ReportFormat.Money, 1.0),
                new("MRP value", ReportAlign.Right, ReportFormat.Money, 1.0),
            ],
            batches.OrderBy(b => b.Product.Name).ThenBy(b => b.ExpiryDate)
                .Select(b => new ReportRow(
                    [b.Product.Name, b.Product.Manufacturer, b.Product.PackSize, b.BatchNo,
                     b.ExpiryDate, b.Product.RackLocation, b.QtyOnHand,
                     b.Product.ReorderLevel,
                     Math.Max(0, b.Product.ReorderLevel - b.Product.StockOnHand),
                     b.PurchaseRate, b.Mrp, b.QtyOnHand * b.PurchaseRate, b.QtyOnHand * b.Mrp]))
                .ToList(),
            [
                new("Medicines", summary.TotalProducts, ReportFormat.Integer),
                new("Batches", summary.TotalBatches, ReportFormat.Integer),
                new("Units", summary.TotalUnits, ReportFormat.Integer),
                new("Value at cost", summary.TotalCostValue),
                new("Value at MRP", summary.TotalMrpValue),
            ]);
    }

    /// <summary>
    /// The Schedule H1 register — a statutory record of who was given which
    /// H1 drug, on whose prescription. Kept over a range because that is how
    /// an inspector asks for it.
    /// </summary>
    /// <summary>One receipt, wherever in the clinic the money came from.</summary>
    /// <summary><paramref name="DocumentId"/> is the record the money was taken
    /// against — the visit, sale, bill, order or payment — so any row here can
    /// be reprinted. <paramref name="Source"/> says which kind it is, and the
    /// screen maps that to the matching print route.</summary>
    private sealed record Collection(
        DateTime TakenOn, PaymentMode Mode, string Source, string Reference,
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

        // Totals are computed before the filter, so a report narrowed to UPI
        // still says what share of the whole that was. A UPI figure with
        // nothing to compare it against is half an answer.
        var everything = taken.Sum(t => t.Amount);

        var rows = (mode is { } m ? taken.Where(t => t.Mode == m) : taken)
            .OrderBy(t => t.TakenOn)
            .ToList();

        var totals = new List<ReportTotal>
        {
            new("Receipts", rows.Count, ReportFormat.Integer),
            new(mode is { } only ? $"{only} collected" : "Collected", rows.Sum(t => t.Amount)),
        };

        // The per-mode split is the point of the report when nothing is
        // filtered — it is what gets checked against the drawer and the bank.
        if (mode is null)
        {
            foreach (var each in Enum.GetValues<PaymentMode>())
                totals.Add(new($"— {each}", taken.Where(t => t.Mode == each).Sum(t => t.Amount)));
        }
        else if (everything > 0)
        {
            totals.Add(new("Share of all takings",
                Math.Round(rows.Sum(t => t.Amount) / everything * 100, 1), ReportFormat.Text));
        }

        return new ReportTable(ReportKind.Collections, title,
            mode is { } shown ? $"{label} · {shown} only" : label,
            [
                new("When", ReportAlign.Left, ReportFormat.DateTime, 1.3),
                new("Mode", ReportAlign.Left, ReportFormat.Text, 0.7),
                new("Source", ReportAlign.Left, ReportFormat.Text, 1.1),
                new("Reference", ReportAlign.Left, ReportFormat.Text, 1.1),
                new("Patient", ReportAlign.Left, ReportFormat.Text, 1.8),
                new("Txn ref", ReportAlign.Left, ReportFormat.Text, 1.2),
                new("Amount", ReportAlign.Right, ReportFormat.Money, 0.9),
            ],
            rows.Select(t => new ReportRow(
                [t.TakenOn, t.Mode.ToString(), t.Source, t.Reference, t.Who, t.TransactionNo ?? "", t.Amount],
                Id: t.DocumentId))
                .ToList(),
            totals);
    }

    private async Task<ReportTable> H1Async(DateTime from, DateTime to, string title, string label)
    {
        var entries = await pharmacy.GetH1RegisterAsync(from, to);

        return new ReportTable(ReportKind.ScheduleH1, title, label,
            [
                new("Date", ReportAlign.Left, ReportFormat.Date, 1.0),
                new("Bill No", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Medicine", ReportAlign.Left, ReportFormat.Text, 2.0),
                new("Batch", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Qty", ReportAlign.Right, ReportFormat.Integer, 0.6),
                new("Patient", ReportAlign.Left, ReportFormat.Text, 1.6),
                new("Prescriber", ReportAlign.Left, ReportFormat.Text, 1.6),
            ],
            entries.OrderBy(h => h.SoldOn)
                .Select(h => new ReportRow(
                    [h.SoldOn, h.BillNo, h.ProductName, h.BatchNo, h.Quantity, h.PatientName, h.DoctorName]))
                .ToList(),
            [
                new("Entries", entries.Count, ReportFormat.Integer),
                new("Units dispensed", entries.Sum(h => h.Quantity), ReportFormat.Integer),
            ]);
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
                new("Added", ReportAlign.Left, ReportFormat.Date, 1.0),
                new("Medicine", ReportAlign.Left, ReportFormat.Text, 2.0),
                new("Batch", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Expiry", ReportAlign.Left, ReportFormat.Date, 1.0),
                new("On hand", ReportAlign.Right, ReportFormat.Integer, 0.7),
                new("MRP", ReportAlign.Right, ReportFormat.Money, 0.9),
                new("Rate paid", ReportAlign.Right, ReportFormat.Money, 0.9),
                // Whose bill is missing, and which one. Without these the
                // list can say that something needs a bill but not whose,
                // which is most of the work of reconciling.
                new("Supplier", ReportAlign.Left, ReportFormat.Text, 1.4),
                new("Their bill", ReportAlign.Left, ReportFormat.Text, 1.2),
            ],
            batches.OrderBy(b => b.ReceivedOn)
                .Select(b => new ReportRow(
                    [b.ReceivedOn, b.Product.Name, b.BatchNo, b.ExpiryDate, b.QtyOnHand,
                     b.Mrp, b.PurchaseRate, b.SupplierName, b.SupplierInvoiceNo]))
                .ToList(),
            [new("Batches", batches.Count, ReportFormat.Integer)]));
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
                new("Medicine", ReportAlign.Left, ReportFormat.Text, 2.0),
                new("Batch", ReportAlign.Left, ReportFormat.Text, 1.0),
                new("Expiry", ReportAlign.Left, ReportFormat.Date, 1.0),
                new("Left", ReportAlign.Right, ReportFormat.Integer, 0.7),
                new("Of a pack of", ReportAlign.Right, ReportFormat.Integer, 0.8),
                new("MRP", ReportAlign.Right, ReportFormat.Money, 0.9),
                new("Value at MRP", ReportAlign.Right, ReportFormat.Money, 1.0),
            ],
            batches.OrderBy(b => b.ExpiryDate)
                .Select(b => new ReportRow(
                    [b.Product.Name, b.BatchNo, b.ExpiryDate, b.QtyOnHand, b.UnitsPerPack,
                     b.Mrp, b.QtyOnHand * b.Mrp]))
                .ToList(),
            [new("Value at MRP", batches.Sum(b => b.QtyOnHand * b.Mrp))]));
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
                b.Id, b.BillNo, b.BillDate, b.PatientName, b.PatientNo, b.FinalAmount, Status = b.Status.ToString()
            }),
            Revenue = revenue.Select(r => new { r.Day, r.Bills, r.Amount }),
            TopTests = topTests.Select(t => new { t.Test, t.Times, t.Amount })
        });
    }
}
