namespace SivayaanHMS.Core;

/// <summary>
/// Logical fields an import profile maps onto a vendor's column names. Adding a
/// vendor whose CSV uses different headings is a profile row, not a code change.
/// </summary>
public static class ImportField
{
    // Bill level
    public const string BillNo = "BillNo";
    public const string BillDate = "BillDate";
    public const string CustomerName = "CustomerName";
    public const string SubTotal = "SubTotal";
    public const string DiscountPercent = "DiscountPercent";
    public const string TotalDiscount = "TotalDiscount";
    public const string TaxableValue = "TaxableValue";
    public const string TaxAmount = "TaxAmount";
    public const string RoundOff = "RoundOff";
    public const string NetAmount = "NetAmount";

    // Line level
    public const string ProductCode = "ProductCode";
    public const string ProductName = "ProductName";
    public const string PackSize = "PackSize";
    public const string BatchNo = "BatchNo";
    public const string Quantity = "Quantity";
    public const string FreeQuantity = "FreeQuantity";
    public const string Rate = "Rate";
    public const string Mrp = "Mrp";
    public const string LineValue = "LineValue";
    public const string GstPercent = "GstPercent";
    public const string Expiry = "Expiry";
    public const string Manufacturer = "Manufacturer";
    public const string HsnCode = "HsnCode";
    public const string PreviousMrp = "PreviousMrp";

    /// <summary>A line cannot be imported without these.</summary>
    public static readonly string[] Required =
        [BillNo, BillDate, ProductName, BatchNo, Quantity, Rate, Mrp, Expiry];
}

public enum ImportSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// One thing worth telling the user about a parsed file. Errors block the import;
/// warnings are shown but do not stop it.
/// </summary>
public record ImportIssue(ImportSeverity Severity, int Line, string Field, string Message)
{
    public override string ToString()
        => Line > 0 ? $"{Severity} (line {Line}) {Field}: {Message}"
                    : $"{Severity} {Field}: {Message}";
}

/// <summary>A named source configuration, chosen alongside the file at import time.</summary>
public class ImportProfile : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Logical field to CSV column, one per line: "BillNo=FeedNo".</summary>
    public string ColumnMap { get; set; } = string.Empty;

    /// <summary>Accepted bill-date formats, most likely first, pipe separated.</summary>
    public string DateFormats { get; set; } = "dd/MM/yyyy|dd-MM-yyyy|yyyy-MM-dd";

    /// <summary>Accepted expiry formats. An expiry is always taken as month end.</summary>
    public string ExpiryFormats { get; set; } = "M/yyyy|MM/yyyy|MMM-yy|MMM-yyyy|MM-yyyy|MM/yy";

    /// <summary>Used when the file carries no GST rate for a line.</summary>
    public decimal DefaultGstRate { get; set; } = 5m;

    public bool IsActive { get; set; } = true;

    public Dictionary<string, string> ParseColumnMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in ColumnMap.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2) map[parts[0].Trim()] = parts[1].Trim();
        }

        return map;
    }

    public string[] SplitDateFormats => DateFormats.Split('|', StringSplitOptions.RemoveEmptyEntries);
    public string[] SplitExpiryFormats => ExpiryFormats.Split('|', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>One purchase line as read from the vendor file, before any matching.</summary>
public class VendorBillLine
{
    public int SourceLine { get; set; }
    public string? ProductCode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? PackSize { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public int Quantity { get; set; }
    public int FreeQuantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Mrp { get; set; }
    public decimal? PreviousMrp { get; set; }
    public decimal GstPercent { get; set; }
    public string? Manufacturer { get; set; }
    public string? HsnCode { get; set; }
    public decimal LineValue { get; set; }

    public int TotalUnits => Quantity + FreeQuantity;

    /// <summary>What the stock actually cost per unit once free goods are counted.</summary>
    public decimal EffectiveUnitCost
        => TotalUnits == 0 ? 0m : Math.Round(Quantity * Rate / TotalUnits, 2, MidpointRounding.AwayFromZero);
}

/// <summary>A parsed vendor bill: header, lines, and everything worth flagging.</summary>
public class VendorBill
{
    public string BillNo { get; set; } = string.Empty;
    public DateTime BillDate { get; set; }
    public string? CustomerName { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetAmount { get; set; }

    public List<VendorBillLine> Lines { get; } = [];
    public List<ImportIssue> Issues { get; } = [];

    public bool HasErrors => Issues.Any(i => i.Severity == ImportSeverity.Error);
    public int TotalUnits => Lines.Sum(l => l.TotalUnits);

    public void Add(ImportSeverity severity, int line, string field, string message)
        => Issues.Add(new ImportIssue(severity, line, field, message));
}
