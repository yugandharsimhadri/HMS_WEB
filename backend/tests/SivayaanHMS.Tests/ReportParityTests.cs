using ClosedXML.Excel;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// The reports against the desktop's own: the same columns, in the same order, with the same
/// labels, on each of the three surfaces. The expected lists below are copied from HMS_WPF's
/// ReportsView.xaml (the grids), ReportPdfBuilder.cs and ReportExcelBuilder.cs, which do not
/// agree with one another - that is the point of the test.
/// </summary>
public class ReportParityTests
{
    private static readonly DateTime Today = DateTime.Today;

    private static Sale Sale(string no, int hour, string mode, decimal net) => new()
    {
        BillNo = no, BillDate = Today.AddHours(hour), CustomerName = "Baby Anika", DoctorName = "Dr. A. Kumar",
        PaymentMode = mode == "UPI" ? PaymentMode.Upi : PaymentMode.Cash, Status = SaleStatus.Completed,
        TaxableAmount = net - 120m, CgstAmount = 60m, SgstAmount = 60m, NetAmount = net,
        Items = { new SaleItem(), new SaleItem() }
    };

    private static Visit Visit(bool paid) => new()
    {
        VisitNo = "V00001", TokenNo = 1, ScheduledOn = Today.AddHours(10), Fee = 350m, FeePaid = paid,
        Patient = new Patient { Name = "Baby Anika", Age = 4, Gender = Gender.Female },
        Doctor = new Doctor { Name = "Dr. A. Kumar" }
    };

    private static Batch Batch(int days) => new()
    {
        Product = new Product { Name = "Paracetamol 500", Manufacturer = "Acme", PackSize = "10", RackLocation = "R2", ReorderLevel = 20 },
        BatchNo = "PX-1", ExpiryDate = Today.AddDays(days), QtyOnHand = 47, Mrp = 22.5m, PurchaseRate = 15m, SupplierName = "Sri"
    };

    private static string[] Headers(ReportTable t) => t.Columns.Select(c => c.Header).ToArray();

    [Fact]
    public void Day_book_columns_match_the_desktop_on_every_surface()
    {
        var t = ReportBuilders.DayBook([Sale("INV1", 11, "Cash", 1120m)], "Day Book", "x");

        string[] grid = ["BILL NO", "TIME", "CUSTOMER", "DOCTOR", "ITEMS", "TAXABLE", "CGST", "SGST", "NET", "MODE"];
        Assert.Equal(grid, Headers(t));
        Assert.Equal(grid, Headers(t.ForPdf));
        Assert.Equal(["Bill No", "Time", "Customer", "Doctor", "Items", "Taxable", "CGST", "SGST", "Net", "Mode"], Headers(t.ForExcel));

        Assert.Equal(["Taxable", "CGST", "SGST", "Net Sales", "Cash", "UPI"], t.Totals.Select(x => x.Label));
        Assert.Equal(["Taxable", "CGST", "SGST", "Net sales", "Cash", "UPI"], t.ForPdf.Totals.Select(x => x.Label));
        Assert.Equal(["Cash total", "UPI total"], t.ForExcel.Totals.Select(x => x.Label));
    }

    [Fact]
    public void Opd_register_has_three_different_layouts()
    {
        var t = ReportBuilders.OpdRegister([Visit(true)], "OPD Register", "x");

        Assert.Equal(["VISIT NO", "TIME", "PATIENT", "AGE", "GENDER", "DOCTOR", "STATUS", "FEE", "PAID"], Headers(t));
        Assert.Equal(["VISIT NO", "TIME", "PATIENT", "AGE/GENDER", "DOCTOR", "FEE", "PAID"], Headers(t.ForPdf));
        Assert.Equal(["Visit No", "Date", "Time", "Patient", "Age", "Gender", "Doctor", "Fee", "Paid"], Headers(t.ForExcel));

        // The PDF folds age and the first letter of the gender into one cell.
        Assert.Equal("4/F", t.ForPdf.Rows[0].Cells[3]);
        // The grid's PAID is a tick box, not the word.
        Assert.Equal(ReportFormat.Check, t.Columns[^1].Format);
        Assert.Equal(true, t.Rows[0].Cells[^1]);
    }

    [Fact]
    public void Expiring_soon_says_expired_in_the_ways_the_desktop_does()
    {
        var t = ReportBuilders.ExpiringSoon([Batch(-10), Batch(25)], 90, "Expiring Soon", "x");

        Assert.Equal(["STATUS", "MEDICINE", "BATCH", "EXPIRY", "QTY LEFT", "RETURNABLE", "MRP", "SUPPLIER", "DAYS REMAINING"], Headers(t));
        Assert.Equal(["MEDICINE", "BATCH", "EXPIRY", "QTY LEFT", "MRP", "SUPPLIER", "DAYS REMAINING"], Headers(t.ForPdf));
        Assert.Equal(["Medicine", "Batch", "Expiry Date", "Qty Left", "MRP", "Supplier", "Days Remaining", "Status"], Headers(t.ForExcel));

        Assert.Equal("⚠ Expired 10 day(s) ago", t.Rows[0].Cells[0]);
        Assert.Equal("Expires in 25 day(s)", t.Rows[1].Cells[0]);

        Assert.Equal("EXPIRED", t.ForPdf.Rows[0].Cells[^1]);
        Assert.True(t.ForPdf.Rows[0].Emphasise);
        Assert.True(t.ForPdf.Columns[^1].Alert);
        Assert.Equal("25", t.ForPdf.Rows[1].Cells[^1]);

        Assert.Equal("EXPIRED", t.ForExcel.Rows[0].Cells[^1]);
        Assert.Equal("OK", t.ForExcel.Rows[1].Cells[^1]);
    }

    [Fact]
    public void Gst_rate_is_written_three_ways()
    {
        var t = ReportBuilders.GstSummary([new GstSlabTotal { GstRate = 12m, Taxable = 2000m, Gst = 240m }], "GST Summary", "x");

        Assert.Equal("12.0%", t.Rows[0].Cells[0]);       // grid:  {0:N1}%
        Assert.Equal("12%", t.ForPdf.Rows[0].Cells[0]);  // PDF:   {0:0.#}%
        Assert.Equal(12m, t.ForExcel.Rows[0].Cells[0]);  // Excel: a number under 0.0"%"
        Assert.Equal("GRAND TOTAL", t.ForExcel.TotalLabel);
        Assert.Equal(["GST Rate", "Taxable Value", "CGST", "SGST", "Total Tax", "Invoice Value"], Headers(t.ForExcel));
    }

    [Fact]
    public void The_pdf_uses_the_desktops_sizes()
    {
        var pdf = ReportPdfBuilder.Generate(
            ReportBuilders.DayBook([Sale("INV1", 11, "Cash", 1120m)], "Day Book", "x"), "Twinkle Pharmacy");

        var sizes = PdfContent.FontSizes(pdf);

        // QuestPDF on both sides, so these are points straight from ReportPdfBuilder.cs.
        foreach (var expected in new[] { 16d, 10d, 13d, 9d, 8d })
            PdfSizeAssert.Contains(expected, sizes);
    }

    [Fact]
    public void The_workbook_has_the_desktops_layout()
    {
        var table = ReportBuilders.DayBook(
            [Sale("INV1", 11, "Cash", 1120m), Sale("INV2", 10, "UPI", 263m)], "Day Book", "25 Oct 2026");

        using var wb = new XLWorkbook(new MemoryStream(ReportExcelBuilder.Generate(table, "Twinkle Pharmacy")));
        var ws = wb.Worksheet(1);

        Assert.Equal("Twinkle Pharmacy", ws.Cell(1, 1).GetString());
        Assert.Equal("OPD & Pharmacy", ws.Cell(2, 1).GetString());
        Assert.Equal("Day Book", ws.Cell(3, 1).GetString());
        Assert.Equal("25 Oct 2026", ws.Cell(4, 1).GetString());

        // Headers on row 6, data from row 7, then TOTAL with live sums over the money columns.
        Assert.Equal("Bill No", ws.Cell(6, 1).GetString());
        Assert.Equal("INV1", ws.Cell(7, 1).GetString());
        Assert.Equal("TOTAL", ws.Cell(9, 1).GetString());
        Assert.Equal("SUM(I7:I8)", ws.Cell(9, 9).FormulaA1);
        Assert.Equal("\"₹\"#,##0.00", ws.Cell(9, 9).Style.NumberFormat.Format);

        // Then a blank row and the labelled extras.
        Assert.Equal("Cash total", ws.Cell(11, 1).GetString());
        Assert.Equal("UPI total", ws.Cell(12, 1).GetString());

        Assert.True(ws.AutoFilter.IsEnabled);
        Assert.Equal(6, ws.SheetView.SplitRow);
    }

    [Fact]
    public void The_stock_register_workbook_keeps_its_live_formulas()
    {
        var table = ReportBuilders.StockRegister([Batch(200)], null, "Stock Register", "x");

        using var wb = new XLWorkbook(new MemoryStream(ReportExcelBuilder.Generate(table, "Twinkle Pharmacy")));
        var ws = wb.Worksheet(1);

        Assert.Equal("G7*J7", ws.Cell(7, 12).FormulaA1);
        Assert.Equal("G7*K7", ws.Cell(7, 13).FormulaA1);
        Assert.Equal("Total Units in Stock", ws.Cell(12, 1).GetString());
    }

    [Fact]
    public void The_stock_register_has_no_pdf()
    {
        var table = ReportBuilders.StockRegister([Batch(200)], null, "Stock Register", "x");

        Assert.Throws<NotSupportedException>(() => ReportPdfBuilder.Generate(table, "Twinkle Pharmacy"));
    }
}
