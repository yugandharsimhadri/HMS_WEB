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
    OpdByDoctor
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
        _ => "Report"
    };

    /// <summary>GST summary and the Schedule H1 register are read over a
    /// From/To range; everything else follows the single Date picker.</summary>
    public static bool IsRangeBased(ReportKind kind)
        => kind is ReportKind.GstSummary or ReportKind.ScheduleH1 or ReportKind.Collections or ReportKind.OpdByDoctor;

    public static string DateLabel(ReportKind kind, DateTime date, DateTime from, DateTime to)
    {
        // Stock Register is a live snapshot, not tied to the Date picker at all.
        if (kind == ReportKind.StockRegister) return $"As of {DateTime.Now:dd MMM yyyy, HH:mm}";

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
            _ => "Report"
        };

        string suffix;
        if (kind == ReportKind.StockRegister)
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
/// that looks like one.</summary>
public enum ReportFormat { Text, Money, Number, Integer, Date, Time, DateTime }

public record ReportColumn(string Header, ReportAlign Align = ReportAlign.Left, ReportFormat Format = ReportFormat.Text, double Width = 1);

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
/// One report, in the single shape the screen, the PDF and the workbook all
/// render.
///
/// The desktop has a bespoke builder per kind for each of those three
/// surfaces, and a comment on <see cref="ReportNaming"/> explaining that the
/// naming is shared "so both always agree with what is on screen". One shape
/// with three renderers takes that further: they cannot disagree, because
/// there is only one set of rows and one set of totals.
/// </summary>
public record ReportTable(
    ReportKind Kind,
    string Title,
    string DateLabel,
    List<ReportColumn> Columns,
    List<ReportRow> Rows,
    List<ReportTotal> Totals);
