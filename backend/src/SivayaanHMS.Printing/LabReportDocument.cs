using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>Laboratory report, ported line for line from the desktop's LabReportDocument.cs.</summary>
public static class LabReportDocument
{
    public static byte[] Generate(LabOrder order, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme, "LABORATORY REPORT");

        var head = NewTable(1, 1, 1);
        head.Rows.Add(IdentityRow(
            ("Order No", order.OrderNo),
            ("Date", $"{order.OrderDate:dd/MM/yyyy}"),
            ("Time", $"{order.OrderDate:HH:mm}")));
        head.Rows.Add(IdentityRow(
            ("Patient", order.PatientName),
            ("Patient No", order.PatientNo),
            ("Referred By", order.ReferredBy ?? "-")));

        if (!string.IsNullOrWhiteSpace(order.SpecimenId) || order.CollectedOn is not null)
            head.Rows.Add(IdentityRow(
                ("Specimen", order.SpecimenId ?? "-"),
                ("Collected", order.CollectedOn is { } collected ? $"{collected:dd/MM/yyyy HH:mm}" : "-"),
                ("Received", order.ReceivedOn is { } received ? $"{received:dd/MM/yyyy HH:mm}" : "-")));

        doc.Add(head);
        doc.Add(Rule());

        foreach (var orderReport in order.Reports.OrderBy(r => r.ReportName))
        {
            doc.Add(Text(orderReport.ReportName.ToUpperInvariant(), 10, Weight.Bold, topMargin: 6, bottomMargin: 2));

            var table = NewTable(2.2, 1, 0.8, 1.6, 0.8);
            table.Rows.Add(Row(true, "ANALYTE", "RESULT", "UNITS", "REFERENCE RANGE", "FLAG"));

            foreach (var result in orderReport.Results.OrderBy(r => r.AnalyteName))
            {
                var flagText = result.Flag == LabResultFlag.Normal ? "" : result.Flag.ToString().ToUpperInvariant();
                table.Rows.Add(Row(false, result.AnalyteName, result.ResultValue, result.Units,
                    result.ReferenceRangeDisplay, flagText));
            }

            doc.Add(table);

            if (!string.IsNullOrWhiteSpace(orderReport.Notes))
                doc.Add(Text($"Interpretation: {orderReport.Notes}", 8, brush: Muted, topMargin: 2, role: SizeRole.Contact));
        }

        doc.Add(Rule());

        var totals = NewTable(3, 0.2, 1.2);
        totals.Rows.Add(TotalRow("Total", order.TotalAmount.ToString("0.00")));
        if (order.Discount > 0) totals.Rows.Add(TotalRow("Discount", $"-{order.Discount:0.00}"));
        doc.Add(totals);

        doc.Add(Text($"AMOUNT PAID   ₹{order.FinalAmount:0.00}", 13, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));
        doc.Add(Text(order.AdmissionId is null ? $"Paid by {order.PaymentMode}" : "Charged to the IP bill", 8, brush: Muted, align: Align.Right, role: SizeRole.Contact));

        if (!string.IsNullOrWhiteSpace(order.Remarks))
            doc.Add(Text($"Remarks: {order.Remarks}", 8, brush: Muted, topMargin: 3, role: SizeRole.Contact));

        doc.Add(Rule());

        var verifiedOn = order.Reports.SelectMany(r => r.Results).Select(r => r.VerifiedOn).Where(v => v is not null).Max();
        var verifiedBy = order.Reports.SelectMany(r => r.Results).Select(r => r.VerifiedBy)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (verifiedOn is not null)
            doc.Add(Text($"Verified by {verifiedBy} on {verifiedOn:dd/MM/yyyy HH:mm}", 7.6, brush: Muted,
                         align: Align.Right, topMargin: 4, role: SizeRole.Footer));

        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));

        return doc.ToPdf(theme);
    }
}
