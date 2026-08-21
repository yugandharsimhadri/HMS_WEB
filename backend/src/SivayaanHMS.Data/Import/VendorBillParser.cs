using System.Globalization;
using System.Text.RegularExpressions;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data.Import;

/// <summary>
/// Turns a vendor CSV into a <see cref="VendorBill"/> using the chosen profile.
/// Pure: no database, no UI. Everything questionable becomes an issue rather than
/// an exception, so the user sees the whole picture before anything is committed.
/// </summary>
public partial class VendorBillParser(ImportProfile profile, IClock? clock = null)
{
    private readonly Dictionary<string, string> _map = profile.ParseColumnMap();
    private readonly IClock _clock = clock ?? new SystemClock();

    public VendorBill Parse(string path) => Parse(CsvFile.Load(path), Path.GetFileName(path));

    public VendorBill Parse(CsvFile csv, string fileName)
    {
        var bill = new VendorBill();

        if (csv.Rows.Count == 0)
        {
            bill.Add(ImportSeverity.Error, 0, "File", $"{fileName} has no data rows.");
            return bill;
        }

        // A profile pointed at the wrong vendor's file is the most likely mistake,
        // and the clearest symptom is columns that simply are not there.
        var missing = ImportField.Required
            .Select(Column)
            .Where(c => c is not null && !csv.HasColumn(c))
            .ToList();

        if (missing.Count > 0)
        {
            bill.Add(ImportSeverity.Error, 0, "Profile",
                $"Profile '{profile.Name}' expects columns not present in {fileName}: {string.Join(", ", missing)}. " +
                "Check the profile matches this vendor.");
            return bill;
        }

        ReadHeader(bill, csv.Rows[0]);

        foreach (var row in csv.Rows)
        {
            var billNo = Text(row, ImportField.BillNo);
            if (billNo is not null && !string.Equals(billNo, bill.BillNo, StringComparison.OrdinalIgnoreCase))
            {
                bill.Add(ImportSeverity.Error, row.LineNumber, ImportField.BillNo,
                    $"This file mixes bills ({bill.BillNo} and {billNo}). Import one bill per file.");
                continue;
            }

            var line = ReadLine(bill, row);
            if (line is not null) bill.Lines.Add(line);
        }

        if (bill.Lines.Count == 0 && !bill.HasErrors)
            bill.Add(ImportSeverity.Error, 0, "File", "No usable lines were found.");

        Reconcile(bill);
        return bill;
    }

    // ── Header ─────────────────────────────────────────────────────────────

    private void ReadHeader(VendorBill bill, CsvRow row)
    {
        bill.BillNo = Text(row, ImportField.BillNo) ?? "";
        bill.CustomerName = Text(row, ImportField.CustomerName);

        if (string.IsNullOrWhiteSpace(bill.BillNo))
            bill.Add(ImportSeverity.Error, row.LineNumber, ImportField.BillNo,
                "The vendor's bill number is missing; it is what prevents the same bill being imported twice.");

        var rawDate = Text(row, ImportField.BillDate);
        if (TryDate(rawDate, out var billDate))
        {
            bill.BillDate = billDate;

            // 04-07-2026 is 4 July under dd-MM-yyyy but 7 April under MM-dd-yyyy.
            // The profile decides, and the user sees the result before committing.
            if (rawDate is not null && IsAmbiguousDayMonth(rawDate))
                bill.Add(ImportSeverity.Info, row.LineNumber, ImportField.BillDate,
                    $"'{rawDate}' read as {billDate:dd MMM yyyy}. Check this is right before importing.");
        }
        else
        {
            bill.BillDate = _clock.Now.Date;
            bill.Add(ImportSeverity.Warning, row.LineNumber, ImportField.BillDate,
                $"Could not read the bill date '{rawDate}'; using today instead.");
        }

        bill.SubTotal = Money(row, ImportField.SubTotal);
        bill.DiscountPercent = Money(row, ImportField.DiscountPercent);
        bill.TotalDiscount = Money(row, ImportField.TotalDiscount);
        bill.TaxableValue = Money(row, ImportField.TaxableValue);
        bill.TaxAmount = Money(row, ImportField.TaxAmount);
        bill.RoundOff = Money(row, ImportField.RoundOff);
        bill.NetAmount = Money(row, ImportField.NetAmount);
    }

    // ── Lines ──────────────────────────────────────────────────────────────

    private VendorBillLine? ReadLine(VendorBill bill, CsvRow row)
    {
        var name = Text(row, ImportField.ProductName);
        if (name is null)
        {
            bill.Add(ImportSeverity.Warning, row.LineNumber, ImportField.ProductName,
                "Line skipped: no medicine name.");
            return null;
        }

        var line = new VendorBillLine
        {
            SourceLine = row.LineNumber,
            ProductCode = Text(row, ImportField.ProductCode),
            ProductName = Squash(name),
            PackSize = Text(row, ImportField.PackSize),
            BatchNo = Text(row, ImportField.BatchNo) ?? "",
            Quantity = Whole(row, ImportField.Quantity),
            FreeQuantity = Whole(row, ImportField.FreeQuantity),
            Rate = Money(row, ImportField.Rate),
            Mrp = Money(row, ImportField.Mrp),
            LineValue = Money(row, ImportField.LineValue),
            Manufacturer = Text(row, ImportField.Manufacturer) is { } m ? Squash(m) : null,
            HsnCode = Text(row, ImportField.HsnCode)
        };

        var previousMrp = Money(row, ImportField.PreviousMrp);
        if (previousMrp > 0) line.PreviousMrp = previousMrp;

        var gst = Money(row, ImportField.GstPercent);
        line.GstPercent = gst > 0 ? SnapGstRate(gst) : profile.DefaultGstRate;

        // Batch and expiry are what the printed bill has to show, so both are hard
        // requirements rather than something to guess at.
        if (string.IsNullOrWhiteSpace(line.BatchNo))
            bill.Add(ImportSeverity.Error, row.LineNumber, ImportField.BatchNo,
                $"{line.ProductName}: no batch number.");

        var rawExpiry = Text(row, ImportField.Expiry);
        if (TryExpiry(rawExpiry, out var expiry))
        {
            line.Expiry = expiry;

            if (expiry.Date < _clock.Now.Date)
                bill.Add(ImportSeverity.Warning, row.LineNumber, ImportField.Expiry,
                    $"{line.ProductName} batch {line.BatchNo} expired on {expiry:dd MMM yyyy}. " +
                    "It will be received but cannot be sold.");
        }
        else
        {
            bill.Add(ImportSeverity.Error, row.LineNumber, ImportField.Expiry,
                $"{line.ProductName}: could not read the expiry '{rawExpiry}'.");
        }

        if (line.Quantity <= 0 && line.FreeQuantity <= 0)
            bill.Add(ImportSeverity.Error, row.LineNumber, ImportField.Quantity,
                $"{line.ProductName}: no quantity on this line.");

        if (line.Mrp <= 0)
            bill.Add(ImportSeverity.Error, row.LineNumber, ImportField.Mrp,
                $"{line.ProductName}: no MRP. The counter prices from MRP, so it cannot be sold without one.");

        // A vendor rate above MRP means every sale of it loses money.
        if (line.Mrp > 0 && line.Rate > line.Mrp)
            bill.Add(ImportSeverity.Warning, row.LineNumber, ImportField.Rate,
                $"{line.ProductName}: cost {line.Rate:0.00} is above MRP {line.Mrp:0.00}.");

        if (line.PreviousMrp is { } old && old > 0 && Math.Abs(old - line.Mrp) > 0.005m)
        {
            var direction = line.Mrp < old ? "down from" : "up from";
            bill.Add(ImportSeverity.Info, row.LineNumber, ImportField.Mrp,
                $"{line.ProductName}: MRP {direction} {old:0.00} to {line.Mrp:0.00}. " +
                "Stock already on the shelf keeps its own MRP.");
        }

        // Every line here priced at qty x rate; a mismatch means a column is misread.
        var expected = Math.Round(line.Quantity * line.Rate, 2, MidpointRounding.AwayFromZero);
        if (line.LineValue > 0 && Math.Abs(expected - line.LineValue) > 0.05m)
            bill.Add(ImportSeverity.Warning, row.LineNumber, ImportField.LineValue,
                $"{line.ProductName}: {line.Quantity} x {line.Rate:0.00} is {expected:0.00}, " +
                $"but the file says {line.LineValue:0.00}.");

        return line;
    }

    // ── Reconciliation ─────────────────────────────────────────────────────

    /// <summary>
    /// Re-adds the bill from its own lines. If our arithmetic agrees with the
    /// vendor's, the columns were read correctly — it is the cheapest proof
    /// available that the profile is right for this file.
    /// </summary>
    private static void Reconcile(VendorBill bill)
    {
        if (bill.Lines.Count == 0) return;

        var lineSum = Math.Round(bill.Lines.Sum(l => l.LineValue > 0 ? l.LineValue : l.Quantity * l.Rate), 2);

        if (bill.SubTotal > 0 && Math.Abs(lineSum - bill.SubTotal) > 0.05m)
            bill.Add(ImportSeverity.Warning, 0, "SubTotal",
                $"Lines add up to {lineSum:0.00} but the bill says {bill.SubTotal:0.00}.");

        if (bill.NetAmount <= 0) return;

        var taxable = bill.TaxableValue > 0
            ? bill.TaxableValue
            : Math.Round(lineSum - bill.TotalDiscount, 2);

        var net = Math.Round(taxable + bill.TaxAmount + bill.RoundOff, 2);

        if (Math.Abs(net - bill.NetAmount) > 0.05m)
        {
            bill.Add(ImportSeverity.Warning, 0, "NetAmount",
                $"Taxable {taxable:0.00} + GST {bill.TaxAmount:0.00} + rounding {bill.RoundOff:0.00} " +
                $"is {net:0.00}, but the bill says {bill.NetAmount:0.00}.");
        }
        else
        {
            bill.Add(ImportSeverity.Info, 0, "NetAmount",
                $"Bill totals reconcile: {bill.Lines.Count} line(s), {bill.TotalUnits} unit(s), net {bill.NetAmount:0.00}.");
        }
    }

    // ── Field helpers ──────────────────────────────────────────────────────

    private string? Column(string field) => _map.GetValueOrDefault(field);

    private string? Text(CsvRow row, string field)
        => Column(field) is { } column ? row[column] : null;

    private decimal Money(CsvRow row, string field)
        => decimal.TryParse(Text(row, field), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private int Whole(CsvRow row, string field)
        => (int)Math.Round(Money(row, field), MidpointRounding.AwayFromZero);

    // Only the profile's declared formats are accepted. A permissive fallback
    // silently read "Sep-27" as the 27th of September this year — a wrong expiry
    // that would have put near-dead stock on the shelf. Failing loudly and making
    // the user pick the right profile is the only safe behaviour here.

    private bool TryDate(string? raw, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        return DateTime.TryParseExact(raw.Trim(), profile.SplitDateFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>An expiry names a month; the pack is good until that month ends.</summary>
    private bool TryExpiry(string? raw, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (!DateTime.TryParseExact(raw.Trim(), profile.SplitExpiryFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        value = new DateTime(parsed.Year, parsed.Month, DateTime.DaysInMonth(parsed.Year, parsed.Month));
        return true;
    }

    /// <summary>Nudges an odd rate onto a real GST slab, as the web migration does.</summary>
    private static decimal SnapGstRate(decimal rate) => rate switch
    {
        <= 0m => 0m,
        <= 5m => 5m,
        <= 12m => 12m,
        <= 18m => 18m,
        _ => 28m
    };

    /// <summary>"CIPLOX EYE DROPS       CIPLA" arrives with runs of spaces.</summary>
    public static string Squash(string value) => WhitespaceRun().Replace(value.Trim(), " ");

    private static bool IsAmbiguousDayMonth(string raw)
    {
        var match = LeadingNumbers().Match(raw);
        return match.Success
               && int.TryParse(match.Groups[1].Value, out var first) && first is >= 1 and <= 12
               && int.TryParse(match.Groups[2].Value, out var second) && second is >= 1 and <= 12;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"^(\d{1,2})[/-](\d{1,2})[/-]")]
    private static partial Regex LeadingNumbers();
}
