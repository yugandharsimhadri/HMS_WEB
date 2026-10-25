using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>
/// Retail tax invoice, ported line for line from the desktop's BillPrinter.cs.
/// Carries the details a chemist's bill must show in India: GSTIN, drug
/// licence number, and batch plus expiry against every line.
///
/// A pharmacy that is not registered for GST issues a plain invoice. Calling it
/// a tax invoice, or printing a GSTIN on it, would be a false claim.
///
/// Licensing note: QuestPDF's Community licence is free for organisations under
/// roughly $1M USD annual gross revenue (self-certified, no fee). Revisit before
/// that ceases to be true — https://www.questpdf.com/license/ — this is exactly
/// the kind of commercial detail that is cheap to catch now and expensive to
/// catch during a customer's procurement review.
/// </summary>
public static class PharmacyInvoiceDocument
{
    public static byte[] Generate(Sale sale, PharmacyProfile pharmacy, DocumentTheme theme, bool isReprint = false)
    {
        var doc = new FlowDoc();

        var kind = sale.IsTaxInvoice ? "TAX INVOICE" : "INVOICE";

        AddPharmacyHeader(doc, pharmacy, theme, isReprint ? $"{kind} (DUPLICATE)" : kind, showGstin: sale.IsTaxInvoice);
        doc.Add(Rule());

        // Date and time sit side by side rather than one under the other — and the
        // drug licence number gets its own labelled cell here, exactly like Patient,
        // rather than being buried in the header text.
        var head = NewTable(1, 1, 1);
        head.Rows.Add(IdentityRow(
            ("Bill No", sale.BillNo),
            ("Date", $"{sale.BillDate:dd/MM/yyyy}"),
            ("Time", $"{sale.BillDate:HH:mm}")));
        head.Rows.Add(IdentityRow(
            ("Patient", sale.CustomerName),
            ("Doctor", sale.DoctorName ?? ""),
            ("D.L. No", pharmacy.DrugLicenceNo ?? "")));
        doc.Add(head);
        doc.Add(Rule());

        // The GST% column only earns its space on a tax invoice.
        var items = sale.IsTaxInvoice
            ? NewTable(3.2, 1.4, 0.9, 0.7, 0.9, 0.7, 1.1)
            : NewTable(3.6, 1.4, 0.9, 0.8, 1.0, 1.2);

        items.Rows.Add(sale.IsTaxInvoice
            ? Row(true, "MEDICINE", "BATCH", "EXPIRY", "QTY", "MRP", "GST%", "AMOUNT")
            : Row(true, "MEDICINE", "BATCH", "EXPIRY", "QTY", "MRP", "AMOUNT"));

        // One heading per medicine, its batches beneath it. A course that spans two
        // batches is two lines at two prices, and printed flat it reads like a
        // billing mistake to the parent holding it.
        foreach (var medicine in sale.Items.GroupBy(i => i.ProductName))
        {
            var split = medicine.Count() > 1;

            if (split)
            {
                var total = medicine.Sum(i => i.LineTotal).ToString("0.00");
                var quantity = medicine.Sum(i => i.Quantity);
                var unit = medicine.First();

                items.Rows.Add(sale.IsTaxInvoice
                    ? Row(false, medicine.Key, "", "",
                          PackMath.Describe(quantity, unit.UnitsPerPack, unit.PackLabel), "", "", total)
                    : Row(false, medicine.Key, "", "",
                          PackMath.Describe(quantity, unit.UnitsPerPack, unit.PackLabel), "", total));
            }

            foreach (var item in medicine)
            {
                // Quoted separator: a bare "/" is replaced by the machine's date
                // separator, which made the printed expiry differ between PCs.
                var expiry = item.ExpiryDate.ToString("MM'/'yy");
                var qty = item.UnitsPerPack > 1 ? item.QuantityDescription : item.Quantity.ToString();

                // Indented under the heading when this medicine came from more than one
                // batch, so the batches read as detail, not as extra items.
                var name = split ? "    from batch" : item.ProductName;

                items.Rows.Add(sale.IsTaxInvoice
                    ? Row(false, name, item.BatchNo, expiry, qty,
                          item.Mrp.ToString("0.00"), item.GstRate.ToString("0.#"), item.LineTotal.ToString("0.00"))
                    : Row(false, name, item.BatchNo, expiry, qty,
                          item.Mrp.ToString("0.00"), item.LineTotal.ToString("0.00")));
            }
        }

        doc.Add(items);

        // GST summary grouped by rate — what makes this a valid tax invoice.
        var slabs = sale.Items.GroupBy(i => i.GstRate).OrderBy(g => g.Key).ToList();
        if (sale.IsTaxInvoice && slabs.Count > 0)
        {
            doc.Add(Text("GST SUMMARY", 7.4, Weight.SemiBold, Muted, topMargin: 2, role: SizeRole.Header));

            var gst = NewTable(1.2, 1.4, 1.2, 1.2, 1.2);
            gst.Rows.Add(Row(true, "RATE", "TAXABLE", "CGST", "SGST", "TOTAL GST"));

            foreach (var slab in slabs)
            {
                var taxable = slab.Sum(i => i.TaxableAmount);
                var tax = slab.Sum(i => i.GstAmount);
                var half = Math.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);

                gst.Rows.Add(Row(false,
                    $"{slab.Key:0.#}%",
                    taxable.ToString("0.00"),
                    (tax - half).ToString("0.00"),
                    half.ToString("0.00"),
                    tax.ToString("0.00")));
            }

            doc.Add(gst);
        }

        doc.Add(Rule());

        var totals = NewTable(3, 0.2, 1.2);
        totals.Rows.Add(TotalRow("Gross", sale.GrossAmount.ToString("0.00")));
        if (sale.DiscountAmount > 0)
            totals.Rows.Add(TotalRow("Discount", $"-{sale.DiscountAmount:0.00}"));
        if (sale.IsTaxInvoice)
        {
            totals.Rows.Add(TotalRow("Taxable value", sale.TaxableAmount.ToString("0.00")));
            totals.Rows.Add(TotalRow("CGST", sale.CgstAmount.ToString("0.00")));
            totals.Rows.Add(TotalRow("SGST", sale.SgstAmount.ToString("0.00")));
        }
        if (sale.RoundOff != 0)
            totals.Rows.Add(TotalRow("Round off", sale.RoundOff.ToString("+0.00;-0.00")));
        doc.Add(totals);

        doc.Add(Text($"NET PAYABLE   ₹{sale.NetAmount:0.00}", 13, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));
        doc.Add(Text($"Paid by {sale.PaymentMode}", 8, brush: Muted, align: Align.Right, role: SizeRole.Contact));
        doc.Add(Text(AmountInWords.Convert(sale.NetAmount), 8, brush: Muted, align: Align.Right, topMargin: 3, role: SizeRole.Contact));

        doc.Add(Rule());

        if (!string.IsNullOrWhiteSpace(pharmacy.PharmacistName))
            doc.Add(LabelValueText("Pharmacist", pharmacy.PharmacistName, 8, brush: Muted, role: SizeRole.Contact));

        if (sale.Items.Count > 0)
            doc.Add(LabelValueText("HSN", string.Join(", ", sale.Items.Select(i => i.HsnCode).Distinct()),
                                   7.4, brush: Muted, role: SizeRole.Header));

        // The pharmacy's own footer, set on the Pharmacy settings tab, takes over from
        // the shared Reports footer once it is typed.
        var footer = string.IsNullOrWhiteSpace(pharmacy.FooterText) ? theme.Footer : pharmacy.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));

        return doc.ToPdf(theme);
    }
}
