using System.Globalization;
using System.Text.Json.Serialization;

namespace SivayaanHMS.Printing;

public enum ReportKind
{
    /// <summary>A tab with nothing to export — it holds its place in the tab
    /// order rather than shifting everything after it.</summary>
    None = -1,

    DayBook,
    GstSummary,
    OpdRegister,
    ExpiringSoon,
    LowStock,
    StockRegister,
    ScheduleH1,

    /// <summary>Money in, from wherever it came, grouped by how it was paid.
    /// Read over a range like the GST summary: a till is reconciled for one
    /// day, but a month of UPI is what gets checked against a bank
    /// statement.</summary>
    Collections,

    /// <summary>OPD activity grouped by doctor over a range — how many
    /// patients each one saw and what their consultations brought in, read
    /// over a week or a month the same way Collections is: one day rarely
    /// says much about a doctor's own load.</summary>
    OpdByDoctor,

    /// <summary>Who is in now, with the bill so far. A snapshot, like the stock reports.</summary>
    IpCensus,

    /// <summary>Admissions and discharges in a date range.</summary>
    IpRegister,

    /// <summary>Final IP bills made in a date range, split by category, doctor and payer.</summary>
    IpBills,

    /// <summary>Discharged patients who still owe, as of now.</summary>
    IpDues,

    /// <summary>Bed use per ward over a date range.</summary>
    BedOccupancy
}

/// <summary>Display name and file-naming rules shared by the PDF and Excel
/// exporters, so both always agree with what is on screen.</summary>
public static class ReportNaming
{
    public static string Title(ReportKind kind) => kind switch
    {
        ReportKind.DayBook => "Day Book",
        ReportKind.GstSummary => "GST Summary",
        ReportKind.OpdRegister => "OPD Register",
        ReportKind.ExpiringSoon => "Expiring Soon",
        ReportKind.LowStock => "Low Stock",
        ReportKind.StockRegister => "Stock Register",
        ReportKind.ScheduleH1 => "Schedule H1 Register",
        ReportKind.Collections => "Collections by payment mode",
        ReportKind.OpdByDoctor => "OPD by Doctor",
        ReportKind.IpCensus => "IP Census",
        ReportKind.IpRegister => "Admission & Discharge Register",
        ReportKind.IpBills => "IP Bills",
        ReportKind.IpDues => "IP Dues",
        ReportKind.BedOccupancy => "Bed Occupancy",
        _ => "Report"
    };

    /// <summary>The reports read over a From/To range - every report about what happened.
    /// The stock views and the IP census and dues are about now, and take no dates.</summary>
    public static bool IsRangeBased(ReportKind kind)
        => kind is ReportKind.DayBook or ReportKind.GstSummary or ReportKind.OpdRegister or ReportKind.ScheduleH1
            or ReportKind.Collections or ReportKind.OpdByDoctor
            or ReportKind.IpRegister or ReportKind.IpBills or ReportKind.BedOccupancy;

    /// <summary>A report of how things stand now rather than of a period.</summary>
    public static bool IsSnapshot(ReportKind kind)
        => kind is ReportKind.StockRegister or ReportKind.IpCensus or ReportKind.IpDues;

    public static string DateLabel(ReportKind kind, DateTime date, DateTime from, DateTime to)
    {
        // A snapshot is not tied to any date picker at all.
        if (IsSnapshot(kind)) return $"As of {DateTime.Now:dd MMM yyyy, HH:mm}";

        if (!IsRangeBased(kind)) return date.ToString("dd MMM yyyy");

        var (start, end) = from <= to ? (from, to) : (to, from);
        return start.Date == end.Date
            ? start.ToString("dd MMM yyyy")
            : $"{start:dd MMM yyyy} to {end:dd MMM yyyy}";
    }

    public static string FileName(ReportKind kind, DateTime date, DateTime from, DateTime to, string extension)
    {
        var stem = kind switch
        {
            ReportKind.DayBook => "DayBook",
            ReportKind.GstSummary => "GSTSummary",
            ReportKind.OpdRegister => "OPDRegister",
            ReportKind.ExpiringSoon => "ExpiringSoon",
            ReportKind.LowStock => "LowStock",
            ReportKind.StockRegister => "StockRegister",
            ReportKind.ScheduleH1 => "ScheduleH1",
            ReportKind.Collections => "Collections",
            ReportKind.OpdByDoctor => "OPDByDoctor",
            ReportKind.IpCensus => "IPCensus",
            ReportKind.IpRegister => "IPRegister",
            ReportKind.IpBills => "IPBills",
            ReportKind.IpDues => "IPDues",
            ReportKind.BedOccupancy => "BedOccupancy",
            _ => "Report"
        };

        string suffix;
        if (IsSnapshot(kind))
        {
            // A live snapshot — the filename is today's date, not whatever the
            // page's (unrelated) Date picker happens to be set to.
            suffix = DateTime.Today.ToString("yyyy-MM-dd");
        }
        else if (IsRangeBased(kind))
        {
            var (start, end) = from <= to ? (from, to) : (to, from);
            suffix = start.Date == end.Date
                ? start.ToString("yyyy-MM-dd")
                : $"{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}";
        }
        else
        {
            suffix = date.ToString("yyyy-MM-dd");
        }

        return Sanitize($"{stem}_{suffix}.{extension}");
    }

    private static string Sanitize(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
        return fileName;
    }
}

public enum ReportAlign { Left, Right, Center }

/// <summary>How a cell is rendered and, in Excel, what number format it
/// carries — so a money column is a real currency cell that sums, not text
/// that looks like one. <c>Check</c> is a ticked box on screen (the desktop's
/// "Paid" column); <c>Percent</c> a number that is already a percentage.</summary>
public enum ReportFormat { Text, Money, Number, Integer, Date, Time, DateTime, Percent, Check }

/// <summary>The desktop's four ways of saying what a grid column is: the row's
/// name (semibold), supporting detail (muted), a figure, and the amount that
/// is asked for or paid (semibold figure).</summary>
public enum ReportTone { Text, Name, Muted, Number, Amount }

/// <summary>
/// One column of one surface of a report.
/// </summary>
/// <param name="Sum">Excel: the TOTAL row carries a live SUM of this column.</param>
/// <param name="Formula">Excel: the cell is a live formula over the same row, written with 1-based
/// column numbers — <c>{7}*{10}</c> is "column G times column J". An edited quantity then reflows.</param>
/// <param name="Alert">PDF: the cell prints red and bold when its row is emphasised.</param>
public record ReportColumn(
    string Header,
    ReportAlign Align = ReportAlign.Left,
    ReportFormat Format = ReportFormat.Text,
    double Width = 1,
    ReportTone Tone = ReportTone.Text,
    bool Sum = false,
    string? Formula = null,
    bool Alert = false);

/// <summary>One row. <paramref name="Note"/> carries the reason a row is
/// emphasised — the desktop reads "Expired" out in words rather than leaving
/// the red tint to say it alone, so a colour-blind or low-vision reader gets
/// the same signal a sighted one gets.</summary>
/// <param name="Id">The record behind the row, when there is one worth
/// acting on — a bill to reprint, say. Null for a row that is only a
/// figure (a GST slab, a stock total). Carried here rather than as a magic
/// column so nothing about it can end up printed.</param>
public record ReportRow(
    List<object?> Cells, bool Emphasise = false, string? Note = null, Guid? Id = null);

public record ReportTotal(string Label, object? Value, ReportFormat Format = ReportFormat.Money);

/// <summary>
/// One report, as the screen shows it.
///
/// The desktop's three surfaces for a report — the grid, the PDF and the workbook — do not carry
/// the same columns: the grid has a Status and a Returnable column the PDF leaves out, the PDF
/// folds age and gender into one cell, the workbook splits the visit time into a date and a time
/// and adds an EXPIRED/OK flag. So a report here is built once from its data into three views,
/// each laid out as its desktop counterpart is; <see cref="Pdf"/> and <see cref="Excel"/> are
/// those views, and a report that has no separate layout for a surface (Collections, OPD by
/// Doctor, which the desktop does not have) simply uses this one for it.
/// </summary>
/// <param name="TotalLabel">Excel: what the TOTAL row says — GRAND TOTAL on the GST summary.</param>
public record ReportTable(
    ReportKind Kind,
    string Title,
    string DateLabel,
    List<ReportColumn> Columns,
    List<ReportRow> Rows,
    List<ReportTotal> Totals,
    string TotalLabel = "TOTAL",
    [property: JsonIgnore] ReportTable? Pdf = null,
    [property: JsonIgnore] ReportTable? Excel = null)
{
    [JsonIgnore] public ReportTable ForPdf => Pdf ?? this;
    [JsonIgnore] public ReportTable ForExcel => Excel ?? this;
}

/// <summary>The number formats the desktop's PDF and grids use, in one place.</summary>
public static class ReportFormats
{
    private static readonly CultureInfo InCulture = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>A figure as a grid or a PDF cell shows it: grouped, two decimals, no symbol.</summary>
    public static string Num(decimal value) => value.ToString("N2", InCulture);

    /// <summary>A total: the rupee sign and the same figure.</summary>
    public static string Inr(decimal value) => "₹" + Num(value);

    public static decimal ToDecimal(object value) => value switch
    {
        decimal d => d,
        int i => i,
        long l => l,
        double db => (decimal)db,
        System.Text.Json.JsonElement je when je.TryGetDecimal(out var jd) => jd,
        _ => decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0
    };

    public static DateTime? ToDate(object value) => value switch
    {
        DateTime dt => dt,
        _ => DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) ? p : null
    };

    /// <summary>
    /// A typed table as text cells, the way the desktop's grids and PDFs write them. For the two
    /// reports with no desktop counterpart, which are built typed once (the workbook wants typed
    /// cells) and read from that for the screen and the PDF.
    /// </summary>
    public static ReportTable AsText(ReportTable typed, bool dateTimeWithYear)
    {
        string Cell(object? value, ReportFormat format)
        {
            if (value is null) return "";

            return format switch
            {
                ReportFormat.Money or ReportFormat.Number => Num(ToDecimal(value)),
                ReportFormat.Integer => ToDecimal(value).ToString("0"),
                ReportFormat.Date => ToDate(value)?.ToString("dd MMM yyyy") ?? "",
                ReportFormat.Time => ToDate(value)?.ToString("HH:mm") ?? "",
                ReportFormat.DateTime => ToDate(value)?.ToString(dateTimeWithYear ? "dd MMM yyyy HH:mm" : "dd MMM yy HH:mm") ?? "",
                _ => value.ToString() ?? ""
            };
        }

        static bool IsFigure(ReportFormat f) => f is ReportFormat.Money or ReportFormat.Number or ReportFormat.Integer;

        return typed with
        {
            Pdf = null,
            Excel = null,
            Columns = typed.Columns
                .Select(c => c with
                {
                    Header = c.Header.ToUpperInvariant(),
                    Format = ReportFormat.Text,
                    Align = IsFigure(c.Format) ? ReportAlign.Right : c.Align
                })
                .ToList(),
            Rows = typed.Rows
                .Select(r => r with { Cells = r.Cells.Select((v, i) => (object?)Cell(v, typed.Columns[i].Format)).ToList() })
                .ToList()
        };
    }
}
