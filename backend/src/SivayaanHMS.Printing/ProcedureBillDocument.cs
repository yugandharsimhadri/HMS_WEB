using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>Procedure bill, ported line for line from the desktop's ProcedureBillDocument.cs.</summary>
public static class ProcedureBillDocument
{
    public static byte[] Generate(
        ProcedureBill bill, ClinicProfile clinic, DocumentTheme theme, bool isReprint = false)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme, isReprint ? "PROCEDURE BILL (DUPLICATE)" : "PROCEDURE BILL");

        var head = NewTable(1, 1, 1);
        head.Rows.Add(IdentityRow(
            ("Bill No", bill.BillNo),
            ("Date", $"{bill.BillDate:dd/MM/yyyy}"),
            ("Time", $"{bill.BillDate:HH:mm}")));
        head.Rows.Add(IdentityRow(
            ("Patient", bill.PatientName),
            ("Patient No", bill.PatientNo),
            ("Status", bill.Status.ToString())));

        if (!string.IsNullOrWhiteSpace(bill.ReferredBy))
            head.Rows.Add(IdentityRow(("Referred by", bill.ReferredBy), ("Payment", bill.PaymentMode.ToString())));
        else
            head.Rows.Add(IdentityRow(("Payment", bill.PaymentMode.ToString())));

        doc.Add(head);
        doc.Add(Rule());

        var items = NewTable(3.6, 0.8, 1.0, 1.2);
        items.Rows.Add(Row(true, "PROCEDURE", "QTY", "PRICE", "AMOUNT"));

        foreach (var item in bill.Items)
            items.Rows.Add(Row(false, item.ProcedureName, item.Quantity.ToString(),
                item.Price.ToString("0.00"), item.Amount.ToString("0.00")));

        doc.Add(items);
        doc.Add(Rule());

        var totals = NewTable(3, 0.2, 1.2);
        totals.Rows.Add(TotalRow("Total", bill.TotalAmount.ToString("0.00")));
        if (bill.Discount > 0)
            totals.Rows.Add(TotalRow("Discount", $"-{bill.Discount:0.00}"));
        doc.Add(totals);

        doc.Add(Text($"AMOUNT PAYABLE   ₹{bill.FinalAmount:0.00}", 13, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));
        doc.Add(Text($"Paid by {bill.PaymentMode}", 8, brush: Muted, align: Align.Right, role: SizeRole.Contact));
        doc.Add(Text(AmountInWords.Convert(bill.FinalAmount), 8, brush: Muted, align: Align.Right, topMargin: 3, role: SizeRole.Contact));

        doc.Add(Rule());

        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));

        if (isReprint)
            doc.Add(Text("DUPLICATE", 9, Weight.Bold, Muted, Align.Center, topMargin: 5));

        return doc.ToPdf(theme);
    }
}
