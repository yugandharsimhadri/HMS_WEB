using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Printing;

/// <summary>
/// Retail tax invoice, rendered server-side as a PDF. This is the one
/// document SAAS_MIGRATION.md singled out to prove first — "take the
/// pharmacy invoice, with its GST breakdown and amount-in-words, and
/// produce it from a web request as a PDF a pharmacist would accept."
/// Content and layout ported from the desktop's BillPrinter (a WPF
/// FlowDocument, no server equivalent) onto QuestPDF, a pure-.NET PDF
/// library with no browser/native dependency to deploy.
///
/// Licensing note: QuestPDF's Community licence is free for organisations
/// under roughly $1M USD annual gross revenue (self-certified, no fee).
/// Revisit before that ceases to be true — see
/// https://www.questpdf.com/license/ — this is exactly the kind of
/// commercial detail that is cheap to catch now and expensive to catch
/// during a customer's procurement review.
/// </summary>
public static class PharmacyInvoiceDocument
{
    public static byte[] Generate(Sale sale, PharmacyProfile pharmacy, DocumentTheme theme, bool isReprint = false)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container => Compose(container, sale, pharmacy, theme, isReprint))
            .GeneratePdf();
    }

    private static void Compose(IDocumentContainer container, Sale sale, PharmacyProfile pharmacy, DocumentTheme theme, bool isReprint)
    {
        var kind = sale.IsTaxInvoice ? "TAX INVOICE" : "INVOICE";
        var title = isReprint ? $"{kind} (DUPLICATE)" : kind;
        var muted = Colors.Grey.Darken1;

        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontFamily(theme.PrintFontFamily ?? "Segoe UI")
                                         .FontSize((float)(9 + theme.PrintFontSizeDelta)));

            page.Header().Column(col =>
            {
                // This letterhead is the pharmacy's own — its name, its GSTIN,
                // its drug licence — which is why it is built here rather than
                // shared with DocumentStyle.Header. The logo is the clinic's
                // and is common to both, so it comes from the same place.
                var logo = DocumentStyle.DecodeLogo(theme);

                if (logo is null)
                {
                    Identity(col);
                }
                else
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem(DocumentStyle.LogoColumnShare)
                           .AlignMiddle().MaxHeight(52).Image(logo).FitArea();
                        row.RelativeItem(100 - DocumentStyle.LogoColumnShare).Column(Identity);
                    });
                }

                col.Item().PaddingTop(4).AlignCenter().Text(title).FontSize(8.6f).Bold();
                col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(muted);

                void Identity(ColumnDescriptor c)
                {
                    // 13, not the clinic's 15: the desktop prints the pharmacy's
                    // own identity a size smaller and unboxed, matching its
                    // reference bill.
                    c.Item().AlignCenter().Text(pharmacy.Name)
                        .FontFamily(theme.TitleFontFamily ?? theme.PrintFontFamily ?? "Segoe UI")
                        .FontSize((float)(13 + theme.PrintFontSizeDelta + theme.TitleFontSizeDelta)).Bold();

                    if (!string.IsNullOrWhiteSpace(pharmacy.AddressLine))
                        c.Item().AlignCenter().Text(pharmacy.AddressLine).FontSize(8).FontColor(muted);
                    if (!string.IsNullOrWhiteSpace(pharmacy.AddressLine2))
                        c.Item().AlignCenter().Text(pharmacy.AddressLine2).FontSize(8).FontColor(muted);
                    if (!string.IsNullOrWhiteSpace(pharmacy.Phone))
                        c.Item().AlignCenter().Text($"Phone: {pharmacy.Phone}").FontSize(8).FontColor(muted);

                    // A pharmacy not registered for GST issues a plain invoice.
                    // Printing a GSTIN on one, or calling it a tax invoice,
                    // would be a false claim — same rule the desktop enforced.
                    if (sale.IsTaxInvoice && !string.IsNullOrWhiteSpace(pharmacy.Gstin))
                        c.Item().AlignCenter().Text($"GSTIN: {pharmacy.Gstin}").FontSize(8).FontColor(muted);
                }
            });

            page.Content().Column(col =>
            {
                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"Bill No: {sale.BillNo}");
                    row.RelativeItem().AlignCenter().Text($"Date: {sale.BillDate:dd/MM/yyyy}");
                    row.RelativeItem().AlignRight().Text($"Time: {sale.BillDate:HH:mm}");
                });
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Patient: {sale.CustomerName}");
                    row.RelativeItem().AlignCenter().Text($"Doctor: {sale.DoctorName ?? ""}");
                    row.RelativeItem().AlignRight().Text($"D.L. No: {pharmacy.DrugLicenceNo ?? ""}");
                });
                col.Item().PaddingTop(2).LineHorizontal(0.75f).LineColor(muted);

                col.Item().PaddingTop(4).Table(table =>
                {
                    if (sale.IsTaxInvoice)
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3.2f); c.RelativeColumn(1.4f); c.RelativeColumn(0.9f);
                            c.RelativeColumn(0.7f); c.RelativeColumn(0.9f); c.RelativeColumn(0.7f); c.RelativeColumn(1.1f);
                        });
                        Header(table, "MEDICINE", "BATCH", "EXPIRY", "QTY", "MRP", "GST%", "AMOUNT");
                    }
                    else
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3.6f); c.RelativeColumn(1.4f); c.RelativeColumn(0.9f);
                            c.RelativeColumn(0.8f); c.RelativeColumn(1.0f); c.RelativeColumn(1.2f);
                        });
                        Header(table, "MEDICINE", "BATCH", "EXPIRY", "QTY", "MRP", "AMOUNT");
                    }

                    // One heading per medicine, its batches beneath it — a
                    // course split across two batches prints as two lines
                    // at two prices, never flattened into one that would
                    // read like a billing mistake.
                    foreach (var medicine in sale.Items.GroupBy(i => i.ProductName))
                    {
                        var split = medicine.Count() > 1;

                        if (split)
                        {
                            var total = medicine.Sum(i => i.LineTotal).ToString("0.00");
                            var quantity = medicine.Sum(i => i.Quantity);
                            var unit = medicine.First();
                            var qtyDesc = PackMath.Describe(quantity, unit.UnitsPerPack, unit.PackLabel);

                            if (sale.IsTaxInvoice)
                                Row(table, medicine.Key, "", "", qtyDesc, "", "", total);
                            else
                                Row(table, medicine.Key, "", "", qtyDesc, "", total);
                        }

                        foreach (var item in medicine)
                        {
                            var expiry = item.ExpiryDate.ToString("MM'/'yy");
                            var qty = item.UnitsPerPack > 1 ? item.QuantityDescription : item.Quantity.ToString();
                            var name = split ? "    from batch" : item.ProductName;

                            if (sale.IsTaxInvoice)
                                Row(table, name, item.BatchNo, expiry, qty,
                                    item.Mrp.ToString("0.00"), item.GstRate.ToString("0.#"), item.LineTotal.ToString("0.00"));
                            else
                                Row(table, name, item.BatchNo, expiry, qty,
                                    item.Mrp.ToString("0.00"), item.LineTotal.ToString("0.00"));
                        }
                    }
                });

                // GST summary grouped by rate — what makes this a valid tax invoice.
                var slabs = sale.Items.GroupBy(i => i.GstRate).OrderBy(g => g.Key).ToList();
                if (sale.IsTaxInvoice && slabs.Count > 0)
                {
                    col.Item().PaddingTop(6).Text("GST SUMMARY").FontSize(7.5f).SemiBold().FontColor(muted);

                    col.Item().PaddingTop(2).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.2f); c.RelativeColumn(1.4f); c.RelativeColumn(1.2f);
                            c.RelativeColumn(1.2f); c.RelativeColumn(1.2f);
                        });
                        Header(table, "RATE", "TAXABLE", "CGST", "SGST", "TOTAL GST");

                        foreach (var slab in slabs)
                        {
                            var taxable = slab.Sum(i => i.TaxableAmount);
                            var tax = slab.Sum(i => i.GstAmount);
                            var half = Math.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);

                            Row(table, $"{slab.Key:0.#}%", taxable.ToString("0.00"),
                                (tax - half).ToString("0.00"), half.ToString("0.00"), tax.ToString("0.00"));
                        }
                    });
                }

                col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(muted);

                col.Item().PaddingTop(4).AlignRight().Column(totals =>
                {
                    TotalLine(totals, "Gross", sale.GrossAmount.ToString("0.00"));
                    if (sale.DiscountAmount > 0) TotalLine(totals, "Discount", $"-{sale.DiscountAmount:0.00}");
                    if (sale.IsTaxInvoice)
                    {
                        TotalLine(totals, "Taxable value", sale.TaxableAmount.ToString("0.00"));
                        TotalLine(totals, "CGST", sale.CgstAmount.ToString("0.00"));
                        TotalLine(totals, "SGST", sale.SgstAmount.ToString("0.00"));
                    }
                    if (sale.RoundOff != 0) TotalLine(totals, "Round off", sale.RoundOff.ToString("+0.00;-0.00"));
                });

                col.Item().PaddingTop(3).AlignRight().Text($"NET PAYABLE   Rs. {sale.NetAmount:0.00}").FontSize(13).Bold();
                col.Item().AlignRight().Text($"Paid by {sale.PaymentMode}").FontSize(8).FontColor(muted);
                col.Item().PaddingTop(3).AlignRight().Text(AmountInWords.Convert(sale.NetAmount)).FontSize(8).FontColor(muted);

                col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(muted);

                if (!string.IsNullOrWhiteSpace(pharmacy.PharmacistName))
                    col.Item().PaddingTop(2).Text($"Pharmacist: {pharmacy.PharmacistName}").FontSize(8).FontColor(muted);

                if (sale.Items.Count > 0)
                    col.Item().Text($"HSN: {string.Join(", ", sale.Items.Select(i => i.HsnCode).Distinct())}")
                        .FontSize(7.5f).FontColor(muted);

                // The pharmacy's own footer, set on the Pharmacy settings
                // tab, takes over from the shared document theme footer
                // once it is typed.
                var footer = string.IsNullOrWhiteSpace(pharmacy.FooterText) ? theme.Footer : pharmacy.FooterText;
                if (!string.IsNullOrWhiteSpace(footer))
                    col.Item().PaddingTop(6).AlignCenter().Text(footer).FontSize(7.5f).FontColor(muted);
            });
        });
    }

    private static void Header(TableDescriptor table, params string[] cells)
    {
        foreach (var cell in cells)
            table.Cell().BorderBottom(1).PaddingBottom(2).Text(cell).FontSize(7.5f).Bold();
    }

    private static void Row(TableDescriptor table, params string[] cells)
    {
        foreach (var cell in cells)
            table.Cell().PaddingVertical(1.5f).Text(cell).FontSize(8.5f);
    }

    private static void TotalLine(ColumnDescriptor column, string label, string value)
        => column.Item().Row(row =>
        {
            row.RelativeItem().AlignRight().Text(label).FontSize(9);
            row.ConstantItem(70).AlignRight().Text(value).FontSize(9);
        });
}
