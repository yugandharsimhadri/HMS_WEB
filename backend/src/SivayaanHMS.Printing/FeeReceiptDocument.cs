using SivayaanHMS.Core;
using SivayaanHMS.Data;
using static SivayaanHMS.Printing.Wpf;

namespace SivayaanHMS.Printing;

/// <summary>
/// Receipt for a consultation fee, ported line for line from the desktop's
/// FeeReceiptDocument.cs. Deliberately short — the parent needs the amount,
/// who it was paid to, and a number they can quote, and nothing else. Body text
/// runs two points larger than the pharmacy bill and the diagnostic bill
/// print at, by request; every size below is the document's original size plus
/// that same +2.
///
/// No GST is shown even when the clinic is GST-registered: a consultation is a
/// service, not a taxable supply of goods.
///
/// Three things the desktop prints that this system has no data for yet, and
/// what is printed instead — each is what the desktop itself prints when that
/// data is absent, so the page is still the desktop's page:
/// <list type="bullet">
/// <item>The doctor's degrees (<c>Doctor.Qualification</c>): the name alone.</item>
/// <item>The visit type (<c>Visit.IsReview</c>): "New". There are no review
/// visits here, so every visit is one.</item>
/// <item>The free-review cover date (<c>FreeFollowUpUntil</c>): no line.</item>
/// </list>
/// </summary>
public static class FeeReceiptDocument
{
    private const double SizeDelta = 2;

    public static byte[] Generate(Visit visit, ClinicProfile clinic, DocumentTheme theme, bool isReprint = false)
    {
        var doc = new FlowDoc();

        AddClinicHeader(doc, clinic, theme, "CASH RECEIPT");

        // Two rows of four: what this receipt is, then who it is about.
        //
        // Date and time are one field because they are one fact. The speciality is
        // not repeated here; it names the consultation on the particulars line.
        // Neither the visit number nor the patient number is printed — both are
        // internal references the parent cannot act on; the receipt number is
        // what they are asked to quote.
        var when = visit.FeePaidOn ?? visit.ScheduledOn;

        var grid = NewTable(1.0, 1.1, 1.4, 0.9);

        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Receipt No", visit.FeeReceiptNo ?? "(not issued)"),
            ("Date & time", $"{when:dd/MM/yyyy}  {when:hh\\:mm tt}"),
            ("Visit", "New"),
            ("Token No", visit.TokenNo.ToString())));

        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Patient", visit.Patient.Name),
            ("Age / Sex", $"{visit.Patient.AgeText} / {visit.Patient.Gender}"),
            ("Doctor", visit.Doctor.Name),
            ("Reg. No", visit.Doctor.RegistrationNo ?? "—")));

        doc.Add(grid);
        doc.Add(Rule());

        var lines = NewTable(3, 0.2, 1.2);
        lines.Rows.Add(Row(true, SizeDelta, "PARTICULARS", "", "AMOUNT"));

        var particulars = string.IsNullOrWhiteSpace(visit.Doctor.Speciality)
            ? "Consultation fee"
            : $"Consultation fee — {visit.Doctor.Speciality}";

        lines.Rows.Add(Row(false, SizeDelta, particulars, "", visit.Fee.ToString("0.00")));
        doc.Add(lines);

        doc.Add(Rule());

        doc.Add(Text($"RECEIVED   ₹{visit.Fee:0.00}", 13 + SizeDelta, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));

        doc.Add(Text($"Paid by {visit.FeePaymentMode?.ToString() ?? "Cash"}",
                     8 + SizeDelta, brush: Muted, align: Align.Right, role: SizeRole.Contact));

        doc.Add(Text(AmountInWords.Convert(visit.Fee), 8 + SizeDelta, brush: Muted,
                     align: Align.Right, topMargin: 4, role: SizeRole.Contact));

        if (visit.FollowUpOn is { } follow)
            doc.Add(Text($"Review on {follow:dd MMM yyyy}", 9 + SizeDelta, Weight.SemiBold, topMargin: 5, role: SizeRole.Body));

        doc.Add(Text("Consultation services. Fees once paid are not refundable.",
                     7.4 + SizeDelta, brush: Muted, align: Align.Center, topMargin: 8, role: SizeRole.Footer));

        // The clinic's own footer, set on the Clinic settings tab, takes over from
        // the shared Reports footer once it is typed.
        var footer = string.IsNullOrWhiteSpace(clinic.FooterText) ? theme.Footer : clinic.FooterText;
        if (!string.IsNullOrWhiteSpace(footer))
            doc.Add(Text(footer, 7.4 + SizeDelta, brush: Muted, align: Align.Center, topMargin: 3, role: SizeRole.Footer));

        if (isReprint)
            doc.Add(Text("DUPLICATE", 9 + SizeDelta, Weight.Bold, Muted, Align.Center, topMargin: 5));

        return doc.ToPdf(theme);
    }
}
