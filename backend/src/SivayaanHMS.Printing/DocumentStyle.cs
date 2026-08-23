using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Data;

namespace SivayaanHMS.Printing;

/// <summary>
/// The pieces every printed document shares: the clinic/pharmacy letterhead,
/// the three-across identity grid, rules, and the label/value pairs. Mirrors
/// the desktop's DocumentBuilder — same structure, same field order, so a
/// clinic that has been handing these to patients for years gets the same
/// piece of paper.
///
/// The desktop's prescription and fee receipt both print two points larger
/// than the pharmacy bill, by explicit request from the clinic; that +2 is
/// carried over here as <see cref="ClinicalSizeDelta"/> rather than being
/// quietly normalised away.
/// </summary>
public static class DocumentStyle
{
    /// <summary>Prescription and fee receipt run +2pt over the bill — the
    /// desktop's own `SizeDelta`, kept because it was asked for.</summary>
    public const float ClinicalSizeDelta = 2f;

    public static string Muted => Colors.Grey.Darken1;

    public static float Body(DocumentTheme theme, float baseSize, float delta = 0)
        => baseSize + (float)theme.PrintFontSizeDelta + delta;

    public static string BodyFont(DocumentTheme theme) => theme.PrintFontFamily ?? "Segoe UI";

    public static string TitleFont(DocumentTheme theme)
        => theme.TitleFontFamily ?? theme.PrintFontFamily ?? "Segoe UI";

    /// <summary>
    /// The letterhead: name, address lines, phone, optional GSTIN, and the
    /// document kind ("CASH RECEIPT") when there is one.
    /// </summary>
    public static void Header(
        ColumnDescriptor col, string name, string? addressLine, string? addressLine2,
        string? phone, string? gstin, string? documentKind, DocumentTheme theme, float delta = 0)
    {
        col.Item().AlignCenter().Text(name)
            .FontFamily(TitleFont(theme))
            .FontSize(Body(theme, 14, delta + (float)theme.TitleFontSizeDelta)).Bold();

        if (!string.IsNullOrWhiteSpace(addressLine))
            col.Item().AlignCenter().Text(addressLine).FontSize(Body(theme, 8, delta)).FontColor(Muted);
        if (!string.IsNullOrWhiteSpace(addressLine2))
            col.Item().AlignCenter().Text(addressLine2).FontSize(Body(theme, 8, delta)).FontColor(Muted);
        if (!string.IsNullOrWhiteSpace(phone))
            col.Item().AlignCenter().Text($"Phone: {phone}").FontSize(Body(theme, 8, delta)).FontColor(Muted);
        if (!string.IsNullOrWhiteSpace(gstin))
            col.Item().AlignCenter().Text($"GSTIN: {gstin}").FontSize(Body(theme, 8, delta)).FontColor(Muted);

        if (!string.IsNullOrWhiteSpace(documentKind))
            col.Item().PaddingTop(4).AlignCenter().Text(documentKind)
                .FontSize(Body(theme, 11, delta)).Bold();

        col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Muted);
    }

    /// <summary>
    /// One row of the identity grid — three labelled cells across. The
    /// desktop's IdentityRow; keeping the same shape is what makes the
    /// printed page recognisable.
    /// </summary>
    public static void IdentityRow(
        ColumnDescriptor col, DocumentTheme theme, float delta,
        (string Label, string Value) a, (string Label, string Value) b, (string Label, string Value) c)
    {
        col.Item().PaddingTop(2).Row(row =>
        {
            Cell(row.RelativeItem(), a);
            Cell(row.RelativeItem(), b);
            Cell(row.RelativeItem(), c);
        });

        void Cell(IContainer container, (string Label, string Value) pair)
            => container.Text(text =>
            {
                text.Span($"{pair.Label}: ").FontSize(Body(theme, 7.5f, delta)).FontColor(Muted);
                text.Span(pair.Value).FontSize(Body(theme, 8.5f, delta));
            });
    }

    /// <summary>
    /// Two fields across the same width as three, for a row that would
    /// otherwise carry a blank cell. An empty pair renders as a stray ": ",
    /// which reads as a field whose value went missing rather than a field
    /// that was never there.
    /// </summary>
    public static void IdentityRow(
        ColumnDescriptor col, DocumentTheme theme, float delta,
        (string Label, string Value) a, (string Label, string Value) b)
    {
        col.Item().PaddingTop(2).Row(row =>
        {
            Cell(row.RelativeItem(), a);
            Cell(row.RelativeItem(2), b);
        });

        void Cell(IContainer container, (string Label, string Value) pair)
            => container.Text(text =>
            {
                text.Span($"{pair.Label}: ").FontSize(Body(theme, 7.5f, delta)).FontColor(Muted);
                text.Span(pair.Value).FontSize(Body(theme, 8.5f, delta));
            });
    }

    public static void Rule(ColumnDescriptor col)
        => col.Item().PaddingVertical(3).LineHorizontal(0.75f).LineColor(Muted);

    public static void LabelValue(ColumnDescriptor col, DocumentTheme theme, float delta, string label, string value)
        => col.Item().PaddingTop(2).Text(text =>
        {
            text.Span($"{label}: ").FontSize(Body(theme, 8.5f, delta)).FontColor(Muted);
            text.Span(value).FontSize(Body(theme, 8.5f, delta));
        });

    /// <summary>The footer a clinic or pharmacy typed for itself, falling back
    /// to the shared document-theme one when it is blank — the desktop's own
    /// precedence, preserved so a clinic that never visits the field keeps
    /// printing what it always did.</summary>
    public static string? Footer(string? ownFooter, DocumentTheme theme)
        => string.IsNullOrWhiteSpace(ownFooter) ? theme.Footer : ownFooter;
}
