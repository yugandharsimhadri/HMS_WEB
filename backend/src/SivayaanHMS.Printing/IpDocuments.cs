using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>
/// The in-patient prints: admission slip, interim / final bill, discharge summary
/// and payment receipts. Same letterhead, sizes and grid as the OPD receipt, so a
/// patient's file reads as one set of papers.
/// </summary>
public static class IpDocuments
{
    private static string Category(IpChargeCategory c) => c switch
    {
        IpChargeCategory.Room => "Room charges",
        IpChargeCategory.Nursing => "Nursing charges",
        IpChargeCategory.DoctorVisit => "Doctor visits",
        IpChargeCategory.Procedure => "Procedures",
        IpChargeCategory.Investigation => "Investigations",
        IpChargeCategory.Medicine => "Medicines & consumables",
        _ => "Other charges"
    };

    private static string ModeName(PaymentMode m) => m == PaymentMode.Upi ? "UPI" : m.ToString();

    private static string Discharge(DischargeType? t) => t switch
    {
        DischargeType.Dama => "DAMA",
        DischargeType.Lama => "LAMA",
        DischargeType.Referred => "Referred",
        DischargeType.Death => "Death",
        DischargeType.Normal => "Normal",
        _ => ""
    };

    /// <summary>Who, where and under whom - the top of every IP paper.</summary>
    private static Tbl Identity(Admission a, params (string Label, string Value)[] extra)
    {
        var t = NewTable(1, 1, 1, 1);
        t.Rows.Add(IdentityRow(
            ("IP No", a.IpNo),
            ("Patient", a.Patient.Name),
            ("Age / Sex", $"{a.Patient.AgeText} / {a.Patient.Gender}"),
            ("Patient No", a.Patient.PatientNo)));
        t.Rows.Add(IdentityRow(
            ("Admitted", $"{a.AdmittedOn:dd/MM/yyyy HH:mm}"),
            ("Doctor", a.Doctor.Name),
            ("Bed", $"{a.Bed.BedNo} · {a.Bed.Ward}"),
            ("Payer", a.PayerType == PayerType.Insurance ? (a.InsurerName ?? "Insurance") : "Self")));
        if (extra.Length > 0) t.Rows.Add(IdentityRow(extra));
        return t;
    }

    private static void Footer(FlowDoc doc, ClinicProfile clinic, DocumentTheme theme)
    {
        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4, brush: Muted, align: Align.Center, topMargin: 6, role: SizeRole.Footer));
    }

    // ── Admission slip ─────────────────────────────────────────────────────

    public static byte[] AdmissionSlip(Admission a, IReadOnlyList<IpPayment> payments, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();
        AddClinicHeader(doc, clinic, theme, "ADMISSION SLIP");

        doc.Add(Identity(a));
        doc.Add(Rule());

        var t = NewTable(1, 2);
        void Line(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) t.Rows.Add(Row(false, label, value)); }
        Line("Reason for admission", a.Reason);
        Line("Phone", a.Patient.Phone);
        Line("Address", a.Patient.Address);
        Line("Attendant", string.Join(" · ", new[] { a.AttendantName, a.AttendantRelation, a.AttendantPhone }.Where(x => !string.IsNullOrWhiteSpace(x))));
        if (a.PayerType == PayerType.Insurance) Line("Insurance / policy", $"{a.InsurerName} {a.PolicyNo}".Trim());
        if (a.IsMlc) Line("Medico-legal case", "Yes");
        doc.Add(t);

        var advance = payments.Where(p => p.Kind == IpPaymentKind.Advance).Sum(p => p.Amount);
        if (advance > 0)
            doc.Add(Text($"Advance received: ₹{advance:0.00}", 9, Weight.Bold, align: Align.Right, topMargin: 4, role: SizeRole.Body));

        doc.Add(Rule());
        doc.Add(Text("I agree to the admission and to pay the hospital charges as billed.", 8, brush: Muted, topMargin: 6, role: SizeRole.Contact));

        doc.Add(Text(" ", 9, topMargin: 16));
        var sign = NewTable(1, 1);
        sign.Rows.Add(Row(false, "Signature of patient / attendant", "Admitting staff"));
        doc.Add(sign);

        Footer(doc, clinic, theme);
        return doc.ToPdf(theme);
    }

    // ── Bill ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The interim bill while the patient is in, the final bill once it is made.
    /// A summary by head first, then every line, as hospital bills are read.
    /// </summary>
    public static byte[] Bill(Admission a, IpBillView bill, IReadOnlyList<IpPayment> payments, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();
        AddClinicHeader(doc, clinic, theme, bill.IsFinal ? "IN-PATIENT FINAL BILL" : "INTERIM BILL");

        doc.Add(Identity(a,
            bill.IsFinal ? ("Bill No", bill.BillNo ?? "") : ("Bill", "Interim - not a final bill"),
            (a.Status == AdmissionStatus.Discharged || bill.IsFinal ? "Discharged" : "Up to", $"{bill.UpTo:dd/MM/yyyy HH:mm}"),
            ("Bed days", bill.BedDays.ToString())));
        doc.Add(Rule());

        var summary = NewTable(3.6, 0.2, 1.2);
        summary.Rows.Add(Row(true, "PARTICULARS", "", "AMOUNT"));
        foreach (var group in bill.Lines.GroupBy(l => l.Category).OrderBy(g => g.Key))
            summary.Rows.Add(Row(false, Category(group.Key), "", group.Sum(l => l.Amount).ToString("0.00")));
        doc.Add(summary);
        doc.Add(Rule());

        var m = bill.Money;
        var totals = NewTable(3, 0.2, 1.2);
        totals.Rows.Add(TotalRow("Gross", m.Gross.ToString("0.00")));
        if (m.Gst > 0) totals.Rows.Add(TotalRow("GST", m.Gst.ToString("0.00")));
        if (m.Discount > 0) totals.Rows.Add(TotalRow("Less: discount", $"-{m.Discount:0.00}"));
        totals.Rows.Add(TotalRow("Net bill", m.Net.ToString("0.00")));
        if (m.Advances > 0) totals.Rows.Add(TotalRow("Less: advances", $"-{m.Advances:0.00}"));
        if (m.Insurance > 0) totals.Rows.Add(TotalRow("Less: insurance paid", $"-{m.Insurance:0.00}"));
        if (m.Settlements > 0) totals.Rows.Add(TotalRow("Less: paid at settlement", $"-{m.Settlements:0.00}"));
        if (m.Refunds > 0) totals.Rows.Add(TotalRow("Add: refunded", m.Refunds.ToString("0.00")));
        if (m.InsuranceAdjustment > 0) totals.Rows.Add(TotalRow("Less: adjusted to insurance payment", $"-{m.InsuranceAdjustment:0.00}"));
        doc.Add(totals);

        var label = m.Balance > 0 ? "BALANCE DUE" : m.Balance < 0 ? "REFUND DUE" : "BALANCE";
        doc.Add(Text($"{label}   ₹{Math.Abs(m.Balance):0.00}", 13, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));
        if (bill.IsFinal)
            doc.Add(Text(AmountInWords.Convert(m.Net), 8, brush: Muted, align: Align.Right, topMargin: 3, role: SizeRole.Contact));

        // Every line, by date within its head.
        doc.Add(Rule());
        doc.Add(Text("DETAILS", 7.6, Weight.SemiBold, Muted, topMargin: 4, role: SizeRole.Header));
        var detail = NewTable(0.9, 3.4, 0.6, 0.9, 1.1);
        detail.Rows.Add(Row(true, "DATE", "ITEM", "QTY", "RATE", "AMOUNT"));
        foreach (var l in bill.Lines)
            detail.Rows.Add(Row(false, l.ChargeDate is { } d ? $"{d:dd/MM}" : "", l.Description,
                l.Quantity.ToString("0.##"), l.Rate.ToString("0.00"), l.Amount.ToString("0.00")));
        doc.Add(detail);

        if (payments.Count > 0)
        {
            doc.Add(Text("PAYMENTS", 7.6, Weight.SemiBold, Muted, topMargin: 8, role: SizeRole.Header));
            var pay = NewTable(1.1, 1.3, 1.6, 1.0);
            pay.Rows.Add(Row(true, "RECEIPT", "DATE", "TYPE / MODE", "AMOUNT"));
            foreach (var p in payments.OrderBy(p => p.PaidOn))
                pay.Rows.Add(Row(false, p.ReceiptNo, $"{p.PaidOn:dd/MM/yyyy}",
                    $"{p.Kind}{(p.Mode is { } mode ? $" · {ModeName(mode)}" : "")}",
                    $"{(p.Kind == IpPaymentKind.Refund ? "-" : "")}{p.Amount:0.00}"));
            doc.Add(pay);
        }

        doc.Add(Text(" ", 9, topMargin: 16));
        var sign = NewTable(1, 1);
        sign.Rows.Add(Row(false, "Patient / attendant", "Cashier"));
        doc.Add(sign);

        Footer(doc, clinic, theme);
        return doc.ToPdf(theme);
    }

    // ── Discharge summary ──────────────────────────────────────────────────

    public static byte[] Summary(Admission a, DischargeSummary s, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();
        AddClinicHeader(doc, clinic, theme, "DISCHARGE SUMMARY");

        doc.Add(Identity(a,
            ("Discharged", a.DischargedOn is { } d ? $"{d:dd/MM/yyyy HH:mm}" : "-"),
            ("Discharge type", Discharge(a.DischargeType)),
            ("Follow-up", s.FollowUpOn is { } f ? $"{f:dd/MM/yyyy}" : "-")));
        doc.Add(Rule());

        void Section(string heading, string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            doc.Add(Text(heading.ToUpperInvariant(), 7.6, Weight.SemiBold, Muted, topMargin: 6, role: SizeRole.Header));
            foreach (var line in body.Split('\n'))
                doc.Add(Text(line.TrimEnd(), 9, topMargin: 1, role: SizeRole.Body));
        }

        Section("Reason for admission", a.Reason);
        Section("Final diagnosis", s.FinalDiagnosis);
        Section("Course in hospital", s.CourseInHospital);
        Section("Treatment given", s.Treatment);
        Section("Condition at discharge", s.ConditionAtDischarge);
        Section("Medicines on discharge", s.Medicines);
        Section("Advice", s.Advice);

        doc.Add(Rule());
        doc.Add(Text(s.DoctorName ?? a.Doctor.Name, 9, Weight.Bold, align: Align.Right, topMargin: 24, role: SizeRole.Body));
        if (!string.IsNullOrWhiteSpace(a.Doctor.RegistrationNo))
            doc.Add(Text($"Reg. No: {a.Doctor.RegistrationNo}", 8, brush: Muted, align: Align.Right, role: SizeRole.Contact));

        Footer(doc, clinic, theme);
        return doc.ToPdf(theme);
    }

    // ── Receipt ────────────────────────────────────────────────────────────

    public static byte[] Receipt(Admission a, IpPayment p, ClinicProfile clinic, DocumentTheme theme)
    {
        var doc = new FlowDoc();
        var title = p.Kind switch
        {
            IpPaymentKind.Advance => "ADVANCE RECEIPT",
            IpPaymentKind.Refund => "REFUND RECEIPT",
            IpPaymentKind.Insurance => "INSURANCE PAYMENT RECEIPT",
            _ => "PAYMENT RECEIPT"
        };
        AddClinicHeader(doc, clinic, theme, title);

        doc.Add(Identity(a,
            ("Receipt No", p.ReceiptNo),
            ("Date", $"{p.PaidOn:dd/MM/yyyy HH:mm}"),
            ("Mode", p.Mode is { } m ? ModeName(m) : "Insurer transfer")));
        doc.Add(Rule());

        var verb = p.Kind == IpPaymentKind.Refund ? "REFUNDED" : "RECEIVED";
        doc.Add(Text($"{verb}   ₹{p.Amount:0.00}", 13, Weight.Bold, align: Align.Right, topMargin: 4, role: SizeRole.Totals));
        doc.Add(Text(AmountInWords.Convert(p.Amount), 8, brush: Muted, align: Align.Right, topMargin: 3, role: SizeRole.Contact));
        if (!string.IsNullOrWhiteSpace(p.Reference))
            doc.Add(Text($"Reference: {p.Reference}", 8, brush: Muted, align: Align.Right, role: SizeRole.Contact));
        if (!string.IsNullOrWhiteSpace(p.Remarks))
            doc.Add(Text(p.Remarks, 8, brush: Muted, topMargin: 4, role: SizeRole.Contact));

        doc.Add(Rule());
        doc.Add(Text(" ", 9, topMargin: 16));
        var sign = NewTable(1, 1);
        sign.Rows.Add(Row(false, p.Kind == IpPaymentKind.Refund ? "Received by" : "", "Cashier"));
        doc.Add(sign);

        Footer(doc, clinic, theme);
        return doc.ToPdf(theme);
    }
}
