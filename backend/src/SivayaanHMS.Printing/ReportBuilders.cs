using System.Globalization;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Printing;

/// <summary>
/// The desktop's reports, shaped from their data.
///
/// The desktop lays a report out three times - the grid, the PDF and the workbook - and they do not
/// carry the same columns. Each report below is therefore built once from its data into those
/// three views; the screen is the table itself and the other two ride along on it
/// (<see cref="ReportTable.Pdf"/>, <see cref="ReportTable.Excel"/>).
///
/// Pure functions of the rows they are given - no database - so the layouts can be checked against
/// the desktop's without one.
/// </summary>
public static class ReportBuilders
{
    // ── Column shorthands ──────────────────────────────────────────────────
    //
    // The desktop lays a report out three times - the grid, the PDF and the workbook - and they do
    // not carry the same columns. Each report below is therefore built once from its data into
    // those three views; the screen is the table itself and the other two ride along on it.

    /// <summary>A text column, left-aligned.</summary>
    public static ReportColumn Left(string header, double width = 1, ReportTone tone = ReportTone.Text)
        => new(header, ReportAlign.Left, ReportFormat.Text, width, tone);

    /// <summary>A figure column, right-aligned.</summary>
    public static ReportColumn Right(string header, double width = 1, ReportTone tone = ReportTone.Number)
        => new(header, ReportAlign.Right, ReportFormat.Text, width, tone);

    /// <summary>A workbook column: typed, so it carries its own number format.</summary>
    public static ReportColumn Typed(string header, ReportFormat format, bool sum = false, string? formula = null)
        => new(header, ReportAlign.Left, format, 1, ReportTone.Text, sum, formula);

    public static string N2(decimal value) => ReportFormats.Num(value);

    public static ReportTotal Count(string label, int value) => new(label, value, ReportFormat.Integer);

    // ── Day book ───────────────────────────────────────────────────────────

    /// <summary>The grid and the PDF share their cells here; only the widths and the alignment of
    /// the headings differ, and those live on the columns.</summary>
    public static List<ReportColumn> DayBookScreenColumns() =>
    [
        Left("BILL NO", 1.3, ReportTone.Name), Left("TIME", 0.8, ReportTone.Muted), Left("CUSTOMER", 2),
        Left("DOCTOR", 1.6), Right("ITEMS", 0.7), Right("TAXABLE"), Right("CGST"), Right("SGST"),
        Right("NET", 1.1, ReportTone.Amount), Left("MODE", 0.9),
    ];

    public static List<ReportColumn> DayBookPdfColumns() =>
    [
        Left("BILL NO", 1.3), Left("TIME", 0.8), Left("CUSTOMER", 2), Left("DOCTOR", 1.6),
        Right("ITEMS", 0.7), Right("TAXABLE"), Right("CGST"), Right("SGST"), Right("NET", 1.1), Left("MODE", 0.9),
    ];

    public static ReportRow DayBookRow(Sale s) => DayBookRow(s, false);

    /// <summary><paramref name="withDate"/> when the book spans more than one day, so the time alone
    /// does not say which day a bill belongs to.</summary>
    public static ReportRow DayBookRow(Sale s, bool withDate) => new(
        [s.BillNo, s.BillDate.ToString(withDate ? "dd MMM HH:mm" : "HH:mm"), s.CustomerName, s.DoctorName ?? "", s.Items.Count.ToString(),
         N2(s.TaxableAmount), N2(s.CgstAmount), N2(s.SgstAmount), N2(s.NetAmount), ModeOf(s)],
        Id: s.Id);

    /// <summary>How the bill was paid - or "IP" when it went on an admitted patient's IP bill and
    /// took no money at the counter.</summary>
    public static string ModeOf(Sale s) => s.AdmissionId is null ? s.PaymentMode.ToString() : "IP";

    /// <summary>
    /// The day book shows **completed bills only**. A cancelled or returned
    /// sale is not revenue, and leaving it in would double count against the
    /// summary cards, which already exclude it.
    /// </summary>
    public static ReportTable DayBook(IEnumerable<Sale> sales, string title, string label, bool multiDay = false)
    {
        var completed = sales
            .Where(s => s.Status == SaleStatus.Completed)
            .OrderByDescending(s => s.BillDate)
            .ToList();

        var taxable = completed.Sum(s => s.TaxableAmount);
        var cgst = completed.Sum(s => s.CgstAmount);
        var sgst = completed.Sum(s => s.SgstAmount);
        var net = completed.Sum(s => s.NetAmount);
        var paidHere = completed.Where(s => s.AdmissionId is null).ToList();
        var cash = paidHere.Where(s => s.PaymentMode == PaymentMode.Cash).Sum(s => s.NetAmount);
        var upi = paidHere.Where(s => s.PaymentMode == PaymentMode.Upi).Sum(s => s.NetAmount);
        var onIp = completed.Where(s => s.AdmissionId is not null).Sum(s => s.NetAmount);

        var rows = completed.Select(s => DayBookRow(s, multiDay)).ToList();

        // Only when there is any, so a clinic without in-patients sees the book it always did.
        List<ReportTotal> IpTotal(string name) => onIp > 0 ? [new(name, onIp)] : [];

        var pdf = new ReportTable(ReportKind.DayBook, title, label, DayBookPdfColumns(), rows,
            [new("Taxable", taxable), new("CGST", cgst), new("SGST", sgst), new("Net sales", net),
             new("Cash", cash), new("UPI", upi), ..IpTotal("On IP bills")]);

        var excel = new ReportTable(ReportKind.DayBook, title, label,
            [
                Typed("Bill No", ReportFormat.Text),
                multiDay ? Typed("Date / time", ReportFormat.DateTime) : Typed("Time", ReportFormat.Time),
                Typed("Customer", ReportFormat.Text),
                Typed("Doctor", ReportFormat.Text), Typed("Items", ReportFormat.Integer),
                Typed("Taxable", ReportFormat.Money, sum: true), Typed("CGST", ReportFormat.Money, sum: true),
                Typed("SGST", ReportFormat.Money, sum: true), Typed("Net", ReportFormat.Money, sum: true),
                Typed("Mode", ReportFormat.Text),
            ],
            completed.Select(s => new ReportRow(
                [s.BillNo, s.BillDate, s.CustomerName, s.DoctorName ?? "", s.Items.Count,
                 s.TaxableAmount, s.CgstAmount, s.SgstAmount, s.NetAmount, ModeOf(s)])).ToList(),
            [new("Cash total", cash), new("UPI total", upi), ..IpTotal("On IP bills total")]);

        return new ReportTable(ReportKind.DayBook, title, label, DayBookScreenColumns(), rows,
            [new("Taxable", taxable), new("CGST", cgst), new("SGST", sgst), new("Net Sales", net),
             new("Cash", cash), new("UPI", upi), ..IpTotal("On IP bills")],
            Pdf: pdf, Excel: excel);
    }

    // ── GST summary ────────────────────────────────────────────────────────

    /// <summary>
    /// GST is summarised per slab from the sale **items**, not per bill — one
    /// bill can carry 5% and 12% lines at once, and a per-bill split could
    /// not separate them.
    /// </summary>
    public static ReportTable GstSummary(IEnumerable<GstSlabTotal> slabs, string title, string label)
    {

        var screenRows = new List<ReportRow>();
        var pdfRows = new List<ReportRow>();
        var excelRows = new List<ReportRow>();
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

            var rate = slab.GstRate;
            screenRows.Add(new ReportRow([rate.ToString("N1", CultureInfo.InvariantCulture) + "%",
                                          N2(taxable), N2(cgst), N2(sgst), N2(total)]));
            pdfRows.Add(new ReportRow([rate.ToString("0.#", CultureInfo.InvariantCulture) + "%",
                                       N2(taxable), N2(cgst), N2(sgst), N2(cgst + sgst), N2(total)]));
            excelRows.Add(new ReportRow([rate, taxable, cgst, sgst, cgst + sgst, total]));

            grandTaxable += taxable; grandCgst += cgst; grandSgst += sgst; grandTotal += total;
        }

        var pdf = new ReportTable(ReportKind.GstSummary, title, label,
            [Left("GST RATE"), Right("TAXABLE VALUE", 1.4), Right("CGST", 1.2), Right("SGST", 1.2),
             Right("TOTAL TAX", 1.2), Right("INVOICE VALUE", 1.4)],
            pdfRows,
            [new("Taxable", grandTaxable), new("CGST", grandCgst), new("SGST", grandSgst),
             new("Total tax", grandCgst + grandSgst), new("Invoice value", grandTotal)]);

        var excel = new ReportTable(ReportKind.GstSummary, title, label,
            [Typed("GST Rate", ReportFormat.Percent), Typed("Taxable Value", ReportFormat.Money, sum: true),
             Typed("CGST", ReportFormat.Money, sum: true), Typed("SGST", ReportFormat.Money, sum: true),
             Typed("Total Tax", ReportFormat.Money, sum: true), Typed("Invoice Value", ReportFormat.Money, sum: true)],
            excelRows, [], TotalLabel: "GRAND TOTAL");

        return new ReportTable(ReportKind.GstSummary, title, label,
            [Right("RATE"), Right("TAXABLE VALUE"), Right("CGST"), Right("SGST"), Right("INVOICE VALUE", 1, ReportTone.Amount)],
            screenRows,
            [new("Taxable", grandTaxable), new("CGST", grandCgst), new("SGST", grandSgst), new("Invoice Value", grandTotal)],
            Pdf: pdf, Excel: excel);
    }

    // ── OPD register ───────────────────────────────────────────────────────

    public static ReportTable OpdRegister(IEnumerable<Visit> source, string title, string label, bool multiDay = false)
    {
        // Day by day, then in token order within the day.
        var visits = source.OrderBy(v => v.ScheduledOn.Date).ThenBy(v => v.TokenNo).ToList();
        var timeFormat = multiDay ? "dd MMM HH:mm" : "HH:mm";

        var seen = visits.Count(v => v.Status != VisitStatus.Cancelled);
        var collected = visits.Where(v => v.FeePaid).Sum(v => v.Fee);

        // Only a paid visit has a receipt to reprint.
        var screenRows = visits.Select(v => new ReportRow(
            [v.VisitNo, v.ScheduledOn.ToString(timeFormat), v.Patient.Name, v.Patient.AgeText,
             v.Patient.Gender.ToString(), v.Doctor.Name, v.Status.ToString(), N2(v.Fee), v.FeePaid],
            Id: v.FeePaid ? v.Id : null)).ToList();

        var pdf = new ReportTable(ReportKind.OpdRegister, title, label,
            [Left("VISIT NO", 1.1), Left("TIME", 0.9), Left("PATIENT", 1.8), Left("AGE/GENDER"),
             Left("DOCTOR", 1.6), Right("FEE"), Left("PAID", 0.8)],
            visits.Select(v => new ReportRow(
                [v.VisitNo, v.ScheduledOn.ToString(timeFormat), v.Patient.Name,
                 $"{v.Patient.AgeText}/{v.Patient.Gender.ToString()[0]}", v.Doctor.Name, N2(v.Fee),
                 v.FeePaid ? "Yes" : "No"])).ToList(),
            [Count("Total OPD visits", seen), new("Total consultation fees", collected)]);

        var excel = new ReportTable(ReportKind.OpdRegister, title, label,
            [Typed("Visit No", ReportFormat.Text), Typed("Date", ReportFormat.Date), Typed("Time", ReportFormat.Time),
             Typed("Patient", ReportFormat.Text), Typed("Age", ReportFormat.Text), Typed("Gender", ReportFormat.Text),
             Typed("Doctor", ReportFormat.Text), Typed("Fee", ReportFormat.Money, sum: true), Typed("Paid", ReportFormat.Text)],
            visits.Select(v => new ReportRow(
                [v.VisitNo, v.ScheduledOn, v.ScheduledOn, v.Patient.Name, v.Patient.AgeText,
                 v.Patient.Gender.ToString(), v.Doctor.Name, v.Fee, v.FeePaid ? "Yes" : "No"])).ToList(),
            [Count("Total OPD visits", seen), new("Total consultation fees", collected)]);

        return new ReportTable(ReportKind.OpdRegister, title, label,
            [
                Left("VISIT NO"), Left("TIME", 1, ReportTone.Muted), Left("PATIENT", 2, ReportTone.Name),
                Right("AGE"), Left("GENDER"), Left("DOCTOR"), Left("STATUS"),
                Right("FEE", 1, ReportTone.Amount),
                new("PAID", ReportAlign.Center, ReportFormat.Check, 0.6),
            ],
            screenRows,
            [Count("Total OPD Visits", seen), new("Total Consultation Fees", collected)],
            Pdf: pdf, Excel: excel);
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
    public static ReportTable Triple(ReportTable typed)
        => ReportFormats.AsText(typed, dateTimeWithYear: false) with
        {
            Pdf = ReportFormats.AsText(typed, dateTimeWithYear: true),
            Excel = typed
        };

    // ── Expiring soon ──────────────────────────────────────────────────────

    /// <summary>
    /// Already-expired stock is called **"Expired"** in its own column rather
    /// than left to a red row tint to say alone — a colour-blind or
    /// low-vision reader gets the same signal a sighted one does, and a
    /// printed report has no tint at all.
    /// </summary>
    public static ReportTable ExpiringSoon(IEnumerable<Batch> source, int days, string title, string label)
    {
        var batches = source.OrderBy(b => b.ExpiryDate).ToList();
        var today = DateTime.Today;

        int Remaining(Batch b) => (b.ExpiryDate.Date - today).Days;

        var screenRows = batches.Select(b =>
        {
            var left = Remaining(b);
            return new ReportRow(
                [left < 0 ? $"⚠ Expired {-left} day(s) ago" : $"Expires in {left} day(s)",
                 b.Product.Name, b.BatchNo, b.ExpiryDate.ToString("dd MMM yyyy"), b.QtyOnHand.ToString(),
                 // Only sealed packs go back to the distributor; an opened strip cannot. Without
                 // this the report says "47 left" and leaves the pharmacist to work out that only 4
                 // strips are claimable and 7 loose are a write-off.
                 b.Returnable, N2(b.Mrp), b.SupplierName ?? "", left.ToString()],
                Emphasise: left < 0,
                Note: left < 0 ? "Expired" : null);
        }).ToList();

        var pdf = new ReportTable(ReportKind.ExpiringSoon, title, label,
            [Left("MEDICINE", 2.2), Left("BATCH"), Left("EXPIRY", 1.1), Right("QTY LEFT", 0.9), Right("MRP", 0.9),
             Left("SUPPLIER", 1.6), Right("DAYS REMAINING") with { Alert = true }],
            batches.Select(b => new ReportRow(
                [b.Product.Name, b.BatchNo, b.ExpiryDate.ToString("dd MMM yyyy"), b.QtyOnHand.ToString(),
                 N2(b.Mrp), b.SupplierName ?? "", Remaining(b) < 0 ? "EXPIRED" : Remaining(b).ToString()],
                Emphasise: Remaining(b) < 0)).ToList(),
            [Count("Batches listed", batches.Count)]);

        var excel = new ReportTable(ReportKind.ExpiringSoon, title, label,
            [Typed("Medicine", ReportFormat.Text), Typed("Batch", ReportFormat.Text), Typed("Expiry Date", ReportFormat.Date),
             Typed("Qty Left", ReportFormat.Integer), Typed("MRP", ReportFormat.Money), Typed("Supplier", ReportFormat.Text),
             Typed("Days Remaining", ReportFormat.Integer), Typed("Status", ReportFormat.Text)],
            batches.Select(b => new ReportRow(
                [b.Product.Name, b.BatchNo, b.ExpiryDate, b.QtyOnHand, b.Mrp, b.SupplierName ?? "",
                 Remaining(b), Remaining(b) < 0 ? "EXPIRED" : "OK"],
                Emphasise: Remaining(b) < 0)).ToList(),
            []);

        // The screen's subtitle says how far ahead it is looking; the printed report, like the
        // desktop's, carries only the date.
        return new ReportTable(ReportKind.ExpiringSoon, title, $"{label} · within {days} days",
            [
                Left("STATUS"), Left("MEDICINE", 2, ReportTone.Name), Left("BATCH"),
                Left("EXPIRY", 1, ReportTone.Muted), Right("QTY LEFT"), Left("RETURNABLE", 1.8),
                Right("MRP"), Left("SUPPLIER", 1.4), Right("DAYS REMAINING"),
            ],
            screenRows, [],
            Pdf: pdf, Excel: excel);
    }

    // ── Low stock ──────────────────────────────────────────────────────────

    public static ReportTable LowStock(IReadOnlyList<Product> products, string title, string label)
    {

        // The pack it is ordered in — a reorder is placed in packs, not in loose units, so a
        // shortage of 50 means nothing without knowing whether a pack is 10 or 100.
        var rows = products.Select(p => new ReportRow(
            [p.Name, p.PackSize ?? "", p.RackLocation ?? "", p.StockOnHand.ToString(), p.ReorderLevel.ToString(),
             Math.Max(0, p.ReorderLevel - p.StockOnHand).ToString()])).ToList();

        var pdf = new ReportTable(ReportKind.LowStock, title, label,
            [Left("MEDICINE", 2.4), Left("PACK", 1.2), Left("RACK"), Right("IN STOCK", 1.1), Right("REORDER AT", 1.1), Right("SHORTAGE")],
            rows,
            [Count("Medicines below reorder level", products.Count)]);

        var excel = new ReportTable(ReportKind.LowStock, title, label,
            [Typed("Medicine", ReportFormat.Text), Typed("Pack", ReportFormat.Text), Typed("Rack", ReportFormat.Text),
             Typed("In Stock", ReportFormat.Integer), Typed("Reorder Level", ReportFormat.Integer),
             Typed("Shortage", ReportFormat.Integer, sum: true)],
            products.Select(p => new ReportRow(
                [p.Name, p.PackSize ?? "", p.RackLocation ?? "", p.StockOnHand, p.ReorderLevel,
                 Math.Max(0, p.ReorderLevel - p.StockOnHand)])).ToList(),
            []);

        return new ReportTable(ReportKind.LowStock, title, label,
            [Left("MEDICINE", 2.2, ReportTone.Name), Left("PACK"), Left("RACK"), Right("IN STOCK"), Right("REORDER AT"), Right("SHORTAGE")],
            rows, [],
            Pdf: pdf, Excel: excel);
    }

    // ── Stock register ─────────────────────────────────────────────────────

    /// <summary>
    /// A live snapshot of every batch on the shelf, not tied to any date
    /// picker. The search box filters the same rows the totals are computed
    /// from, so a filtered register's totals describe what is on screen
    /// rather than the whole shelf. Excel-only as a document, as on the desktop.
    /// </summary>
    public static ReportTable StockRegister(IEnumerable<Batch> all, string? search, string title, string label)
    {
        var batches = all.Where(b => StockRegisterFilter.Matches(b, search))
            .OrderBy(b => b.Product.Name).ThenBy(b => b.ExpiryDate)
            .ToList();
        var summary = StockSummary.From(batches);

        // Carried per row rather than only in the totals: this is the sheet somebody sorts by
        // shortage to build an order from.
        int Shortage(Batch b) => Math.Max(0, b.Product.ReorderLevel - b.Product.StockOnHand);

        var excel = new ReportTable(ReportKind.StockRegister, title, label,
            [
                Typed("Medicine", ReportFormat.Text), Typed("Manufacturer", ReportFormat.Text), Typed("Pack Size", ReportFormat.Text),
                Typed("Batch No", ReportFormat.Text), Typed("Expiry Date", ReportFormat.Date), Typed("Rack Location", ReportFormat.Text),
                Typed("Current Stock", ReportFormat.Integer, sum: true), Typed("Reorder Level", ReportFormat.Integer),
                Typed("Shortage", ReportFormat.Integer), Typed("Purchase Rate", ReportFormat.Money), Typed("MRP", ReportFormat.Money),
                Typed("Stock Value (Cost)", ReportFormat.Money, sum: true, formula: "{7}*{10}"),
                Typed("Stock Value (MRP)", ReportFormat.Money, sum: true, formula: "{7}*{11}"),
            ],
            batches.Select(b => new ReportRow(
                [b.Product.Name, b.Product.Manufacturer, b.Product.PackSize, b.BatchNo, b.ExpiryDate,
                 b.Product.RackLocation, b.QtyOnHand, b.Product.ReorderLevel, Shortage(b),
                 b.PurchaseRate, b.Mrp, null, null])).ToList(),
            [
                Count("Total Products", summary.TotalProducts),
                Count("Total Batches", summary.TotalBatches),
                Count("Total Units in Stock", summary.TotalUnits),
                new("Total Stock Cost Value", summary.TotalCostValue),
                new("Total Stock MRP Value", summary.TotalMrpValue),
            ]);

        return new ReportTable(ReportKind.StockRegister, title, label,
            [
                Left("MEDICINE", 2, ReportTone.Name), Left("MANUFACTURER", 1.4), Left("PACK", 0.9), Left("BATCH"),
                Left("EXPIRY", 1, ReportTone.Muted), Left("RACK", 0.7), Right("CURRENT STOCK"), Right("REORDER LEVEL"),
                Right("SHORTAGE"), Right("PURCHASE RATE"), Right("MRP"),
            ],
            batches.Select(b => new ReportRow(
                [b.Product.Name, b.Product.Manufacturer, b.Product.PackSize, b.BatchNo, b.ExpiryDate.ToString("dd MMM yyyy"),
                 b.Product.RackLocation, b.QtyOnHand.ToString(), b.Product.ReorderLevel.ToString(), Shortage(b).ToString(),
                 N2(b.PurchaseRate), N2(b.Mrp)])).ToList(),
            [
                Count("Total Products", summary.TotalProducts),
                Count("Total Batches", summary.TotalBatches),
                Count("Total Units", summary.TotalUnits),
                new("Cost Value", summary.TotalCostValue),
                new("MRP Value", summary.TotalMrpValue),
            ],
            Excel: excel);
    }

    // ── Schedule H1 ────────────────────────────────────────────────────────

    /// <summary>
    /// The Schedule H1 register — a statutory record of who was given which
    /// H1 drug, on whose prescription. Kept over a range because that is how
    /// an inspector asks for it.
    /// </summary>
    public static ReportTable ScheduleH1(IEnumerable<H1RegisterEntry> source, string title, string label)
    {
        var entries = source.OrderBy(h => h.SoldOn).ToList();
        var quantity = entries.Sum(h => h.Quantity);

        var pdf = new ReportTable(ReportKind.ScheduleH1, title, label,
            [Left("DATE", 1.4), Left("BILL NO", 1.1), Left("PATIENT/CUSTOMER", 1.6), Left("DOCTOR", 1.6),
             Left("MEDICINE", 2), Left("BATCH", 1.1), Right("QTY", 0.7)],
            entries.Select(h => new ReportRow(
                [h.SoldOn.ToString("dd MMM yyyy HH:mm"), h.BillNo, h.PatientName, h.DoctorName ?? "",
                 h.ProductName, h.BatchNo, h.Quantity.ToString()])).ToList(),
            [Count("Entries", entries.Count), Count("Total quantity", quantity)]);

        var excel = new ReportTable(ReportKind.ScheduleH1, title, label,
            [Typed("Date", ReportFormat.DateTime), Typed("Bill No", ReportFormat.Text), Typed("Patient/Customer", ReportFormat.Text),
             Typed("Doctor", ReportFormat.Text), Typed("Medicine", ReportFormat.Text), Typed("Batch", ReportFormat.Text),
             Typed("Quantity", ReportFormat.Integer, sum: true)],
            entries.Select(h => new ReportRow(
                [h.SoldOn, h.BillNo, h.PatientName, h.DoctorName ?? "", h.ProductName, h.BatchNo, h.Quantity])).ToList(),
            [Count("Entries", entries.Count)]);

        return new ReportTable(ReportKind.ScheduleH1, title, label,
            [
                Left("DATE", 1, ReportTone.Muted), Left("BILL NO"), Left("MEDICINE", 2, ReportTone.Name),
                Left("BATCH", 1, ReportTone.Muted), Right("QTY"), Left("PATIENT", 1.6), Left("PRESCRIBER", 1.6),
            ],
            entries.Select(h => new ReportRow(
                [h.SoldOn.ToString("dd MMM yy HH:mm"), h.BillNo, h.ProductName, h.BatchNo, h.Quantity.ToString(),
                 h.PatientName, h.DoctorName ?? ""])).ToList(),
            [Count("Entries", entries.Count), Count("Total Quantity", quantity)],
            Pdf: pdf, Excel: excel);
    }

}
