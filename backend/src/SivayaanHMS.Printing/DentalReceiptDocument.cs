using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>
/// Dental payment receipt, ported line for line from the desktop's
/// DentalReceiptDocument.cs. The reprint marking is this system's own addition —
/// the desktop has no reprint of this document — and is styled as the desktop
/// styles it on every document that does.
/// </summary>
public static class DentalReceiptDocument
{
    public static byte[] Generate(
        DentalPayment payment, DentalCase dentalCase, ClinicProfile clinic, DocumentTheme theme,
        bool isReprint = false)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme,
            isReprint ? "DENTAL PAYMENT RECEIPT (DUPLICATE)" : "DENTAL PAYMENT RECEIPT");

        var grid = NewTable(1, 1, 1);
        grid.Rows.Add(IdentityRow(
            ("Receipt No", payment.ReceiptNo),
            ("Date", $"{payment.PaidOn:dd/MM/yyyy}"),
            ("Time", $"{payment.PaidOn:HH:mm}")));
        grid.Rows.Add(IdentityRow(
            ("Patient", dentalCase.PatientName),
            ("Procedure / Package", dentalCase.ProcedureName ?? dentalCase.PackageName ?? ""),
            ("Tooth", dentalCase.ToothNumber ?? "")));
        doc.Add(grid);
        doc.Add(Rule());

        doc.Add(Text($"AMOUNT RECEIVED   ₹{payment.Amount:0.00}", 13, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));
        doc.Add(Text($"Paid by {payment.PaymentMode}", 8, brush: Muted, align: Align.Right, role: SizeRole.Contact));
        doc.Add(Text(AmountInWords.Convert(payment.Amount), 8, brush: Muted, align: Align.Right, topMargin: 3, role: SizeRole.Contact));

        doc.Add(Rule());

        var totals = NewTable(3, 0.2, 1.2);
        totals.Rows.Add(TotalRow("Case total", dentalCase.TotalCost.ToString("0.00")));
        totals.Rows.Add(TotalRow("Paid to date", dentalCase.AmountPaid.ToString("0.00")));
        doc.Add(totals);

        doc.Add(Text($"BALANCE DUE   ₹{dentalCase.Balance:0.00}", 11, Weight.Bold,
                     brush: dentalCase.Balance > 0 ? Muted : null,
                     align: Align.Right, topMargin: 4));

        doc.Add(Rule());

        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));

        if (isReprint)
            doc.Add(Text("DUPLICATE", 9, Weight.Bold, Muted, Align.Center, topMargin: 5));

        return doc.ToPdf(theme);
    }
}
