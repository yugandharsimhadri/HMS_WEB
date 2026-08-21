using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// SAAS_MIGRATION.md: "prove the printing... take the pharmacy invoice, with
/// its GST breakdown and amount-in-words, and produce it from a web request
/// as a PDF a pharmacist would accept." This generates a real one from a
/// realistic multi-batch, multi-rate bill and checks it is an actual,
/// non-trivial PDF - not just that the code compiles.
/// </summary>
public class PharmacyInvoiceDocumentTests
{
    private static Sale SampleSale()
    {
        var sale = new Sale
        {
            BillNo = "INV00042",
            BillDate = new DateTime(2026, 8, 21, 11, 30, 0),
            CustomerName = "Aarav Rao",
            DoctorName = "Dr. A. Kumar",
            PaymentMode = PaymentMode.Upi,
            IsTaxInvoice = true
        };

        sale.Items.Add(new SaleItem
        {
            ProductName = "Paracetamol 500mg", BatchNo = "B2201", ExpiryDate = new DateTime(2027, 6, 30),
            HsnCode = "3004", Quantity = 15, UnitsPerPack = 15, PackLabel = "TAB",
            Mrp = 2m, DiscountPercent = 0, GstRate = 12m,
            TaxableAmount = 26.79m, GstAmount = 3.21m, LineTotal = 30m
        });
        sale.Items.Add(new SaleItem
        {
            ProductName = "Amoxicillin 500mg", BatchNo = "B3110", ExpiryDate = new DateTime(2027, 1, 31),
            HsnCode = "3004", Quantity = 10, UnitsPerPack = 10, PackLabel = "CAP",
            Mrp = 8m, DiscountPercent = 0, GstRate = 12m,
            TaxableAmount = 71.43m, GstAmount = 8.57m, LineTotal = 80m
        });
        // Same medicine split across two batches - exercises the grouped-
        // heading path, the one most likely to silently misrender.
        sale.Items.Add(new SaleItem
        {
            ProductName = "ORS Powder", BatchNo = "B4001", ExpiryDate = new DateTime(2026, 12, 31),
            HsnCode = "3004", Quantity = 3, UnitsPerPack = 1, PackLabel = "SACHET",
            Mrp = 15m, DiscountPercent = 0, GstRate = 5m,
            TaxableAmount = 42.86m, GstAmount = 2.14m, LineTotal = 45m
        });
        sale.Items.Add(new SaleItem
        {
            ProductName = "ORS Powder", BatchNo = "B4002", ExpiryDate = new DateTime(2027, 3, 31),
            HsnCode = "3004", Quantity = 2, UnitsPerPack = 1, PackLabel = "SACHET",
            Mrp = 15m, DiscountPercent = 0, GstRate = 5m,
            TaxableAmount = 28.57m, GstAmount = 1.43m, LineTotal = 30m
        });

        sale.GrossAmount = 185m;
        sale.DiscountAmount = 0m;
        sale.TaxableAmount = 169.65m;
        sale.CgstAmount = 7.68m;
        sale.SgstAmount = 7.67m;
        sale.RoundOff = 0m;
        sale.NetAmount = 185m;

        return sale;
    }

    private static PharmacyProfile SamplePharmacy() => new()
    {
        Name = "Sivayaan HMS Pharmacy",
        AddressLine = "12 MG Road, Chennai",
        AddressLine2 = "Tamil Nadu 600001",
        Phone = "044-12345678",
        GstRegistered = true,
        Gstin = "33AAAAA0000A1Z5",
        DrugLicenceNo = "TN-DL-20260001",
        PharmacistName = "R. Meena, B.Pharm"
    };

    [Fact]
    public void Generates_a_real_pdf_for_a_multi_batch_multi_rate_bill()
    {
        var bytes = PharmacyInvoiceDocument.Generate(SampleSale(), SamplePharmacy(), new DocumentTheme());

        Assert.True(bytes.Length > 2000, $"PDF suspiciously small: {bytes.Length} bytes.");
        Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
    }

    [Fact]
    public void Non_tax_invoice_omits_gst_columns_without_throwing()
    {
        var sale = SampleSale();
        sale.IsTaxInvoice = false;

        var bytes = PharmacyInvoiceDocument.Generate(sale, SamplePharmacy(), new DocumentTheme());
        Assert.True(bytes.Length > 1500);
    }

    /// <summary>Not an assertion - writes the PDF to disk so a human can
    /// actually look at it, which is the point of "prove the printing."</summary>
    [Fact]
    public void Writes_a_sample_invoice_for_visual_review()
    {
        var bytes = PharmacyInvoiceDocument.Generate(SampleSale(), SamplePharmacy(), new DocumentTheme(), isReprint: true);

        var path = Path.Combine(Path.GetTempPath(), "sivayaanhms-sample-invoice.pdf");
        File.WriteAllBytes(path, bytes);
    }
}
