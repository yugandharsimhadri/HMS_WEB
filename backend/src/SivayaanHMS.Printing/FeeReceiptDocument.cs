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
/// Three things depend on the doctor's OPD validity window, exactly as the
/// desktop prints them:
/// <list type="bullet">
/// <item>The doctor's degrees (<c>Doctor.Qualification</c>) print beside the
/// name, and nothing extra when none are set.</item>
/// <item>The visit type reads New or Review (<c>Visit.VisitKind</c>).</item>
/// <item>The free-review cover date (<c>FreeFollowUpUntil</c>) prints under the
/// amount on the receipt of the payment that opened it.</item>
/// </list>
///
/// A review prints the same page with the amount stated as 0.00 rather than
/// left off — a receipt with no figure invites the question the figure
/// answers — and quotes the receipt number the fee was actually taken on.
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

        // A review is never given a receipt number of its own. No money is
        // taken, and burning a number on a nil amount would put a hole in the
        // day's collection when the receipts are reconciled. It carries the
        // number the fee was actually taken on instead, under the same label —
        // so a review slip and the receipt it rides on quote the same number,
        // and the Visit line and the nil amount are what tell them apart.
        var receiptNo = visit.IsReview
            ? visit.FeeWaivedAgainstVisit?.FeeReceiptNo ?? "—"
            : visit.FeeReceiptNo ?? "(not issued)";

        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Receipt No", receiptNo),
            ("Date & time", $"{when:dd/MM/yyyy}  {when:hh\\:mm tt}"),
            ("Visit", visit.VisitKind),
            ("Token No", visit.TokenNo.ToString())));

        // Degrees beside the name and the registration under its own label — a
        // receipt is a medico-legal document, and who gave the consultation is
        // what it is expected to state plainly.
        grid.Rows.Add(IdentityRow(SizeDelta,
            ("Patient", visit.Patient.Name),
            ("Age / Sex", $"{visit.Patient.AgeText} / {visit.Patient.Gender}"),
            ("Doctor", visit.Doctor.NameWithQualification),
            ("Reg. No", visit.Doctor.RegistrationNo ?? "—")));

        doc.Add(grid);
        doc.Add(Rule());

        var lines = NewTable(3, 0.2, 1.2);
        lines.Rows.Add(Row(true, SizeDelta, "PARTICULARS", "", "AMOUNT"));

        var particulars = string.IsNullOrWhiteSpace(visit.Doctor.Speciality)
            ? "Consultation fee"
            : $"Consultation fee — {visit.Doctor.Speciality}";

        lines.Rows.Add(Row(false, SizeDelta,
            visit.IsReview ? $"{particulars} (review — already paid)" : particulars,
            "", visit.Fee.ToString("0.00")));
        doc.Add(lines);

        doc.Add(Rule());

        // Nil on a review, and said out loud rather than left blank.
        doc.Add(Text($"RECEIVED   ₹{visit.Fee:0.00}", 13 + SizeDelta, Weight.Bold, align: Align.Right, topMargin: 1, role: SizeRole.Totals));

        if (visit.IsReview)
        {
            var paidOn = visit.FeeWaivedAgainstVisit?.FeePaidOn;
            doc.Add(Text(
                paidOn is { } on ? $"No fee — already paid on {on:dd MMM yyyy}" : "No fee — already paid",
                8 + SizeDelta, brush: Muted, align: Align.Right, role: SizeRole.Contact));
        }
        else
        {
            doc.Add(Text($"Paid by {visit.FeePaymentMode?.ToString() ?? "Cash"}",
                         8 + SizeDelta, brush: Muted, align: Align.Right, role: SizeRole.Contact));
        }

        doc.Add(Text(AmountInWords.Convert(visit.Fee), 8 + SizeDelta, brush: Muted,
                     align: Align.Right, topMargin: 4, role: SizeRole.Contact));

        // Says the date, not a number of days to count. This is the line the
        // desk points at when a parent asks whether they have to pay again.
        if (!visit.IsReview && visit.FreeFollowUpUntil is { } coverUntil)
            doc.Add(Text($"Free review with {visit.Doctor.Name} until {coverUntil:dd MMM yyyy}",
                         9 + SizeDelta, Weight.SemiBold, topMargin: 5, role: SizeRole.Body));

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
