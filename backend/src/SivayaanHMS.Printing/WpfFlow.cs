using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Data;

namespace SivayaanHMS.Printing;

/// <summary>
/// The desktop's printed-page layout model, reproduced in QuestPDF.
///
/// HMS_WPF builds every bill from a WPF <c>FlowDocument</c>, and a
/// FlowDocument is not a set of coordinates — it is a *layout model* with
/// rules of its own. This copies the rules rather than the numbers, because
/// the numbers alone cannot reproduce the page. The ones that matter, each
/// measured off a real WPF render rather than assumed:
///
/// <list type="bullet">
/// <item><b>Units.</b> WPF sizes are device-independent pixels, 1/96". QuestPDF
/// works in points, 1/72". Every size here is stated in WPF's px and converted
/// once, by <see cref="Pt"/>. Copying WPF's 9 into QuestPDF as 9 prints 33%
/// larger than the desktop — which is exactly what the web did before this.</item>
/// <item><b>Page.</b> A4, 794 x 1123 px, 30 px of padding — what the desktop's
/// print preview paginates to. The web printed A5.</item>
/// <item><b>Unset margins.</b> A paragraph that never sets a margin is not
/// margin-less: WPF gives it one line of its own font above and below
/// (1.33 x its size for Segoe UI). Most of the letterhead's height is this.</item>
/// <item><b>Collapsing.</b> Between two blocks the gap is the larger of the first
/// one's bottom margin and the second one's top margin — not their sum. A
/// margin that is unset does not apply at the edge of a container.</item>
/// <item><b>Palette.</b> Black ink, #333333 for secondary text, #222018 for
/// every rule and border. The web used grey.</item>
/// </list>
///
/// Validated against real WPF renders, not by reading the source: the
/// letterhead box computes to 133.9 px by this model and measures 134.
/// </summary>
internal static class Flow
{
    /// <summary>1 WPF device-independent pixel in QuestPDF points.</summary>
    public const double PxToPt = 0.75;

    public static float Pt(double px) => (float)(px * PxToPt);

    // The print-safe palette from DocumentBuilder: paper is white and ink is dark.
    public const string Ink = "#000000";
    public const string Secondary = "#333333";
    public const string Line = "#222018";

    /// <summary>Segoe UI's line spacing (hhea: 2724 / 2048). WPF resolves an
    /// unset paragraph margin to one line of the paragraph's own font.</summary>
    public const double LineSpacing = 2724.0 / 2048.0;

    /// <summary>The desktop's print-preview page, A4 at 96 dpi.</summary>
    public const double PagePaddingPx = 30;

    public const double MinFontSize = 4;

    public static string DefaultFont => "Segoe UI";
}

internal enum Weight { Normal, SemiBold, Bold }

/// <summary>
/// Which of the clinic's per-element size settings a paragraph belongs to, if any. The desktop has
/// no such settings - its sizes are literals - so a paragraph keeps the desktop's literal as its
/// size and its role only says which setting may *shift* it: by (the clinic's value - the default),
/// which is zero for an untouched install. That is what lets the web keep the settings without
/// ever drifting from the desktop's page.
/// </summary>
internal enum SizeRole { None, Letterhead, Contact, Kind, Body, Row, Header, Totals, Footer }

internal enum Align { Left, Center, Right, Justify }

/// <summary>A run of text inside a paragraph. A null weight inherits the paragraph's.</summary>
internal readonly record struct Run(string Text, Weight? Weight = null);

/// <summary>
/// One block of a flow document. A margin of <c>null</c> is WPF's unset
/// (<c>NaN</c>) margin — see <see cref="Flow"/> — which is not the same thing as 0.
/// </summary>
internal abstract class FlowBlock
{
    public double? MarginTop { get; init; } = 0;
    public double? MarginBottom { get; init; } = 0;
    public double MarginLeft { get; init; }
    public double MarginRight { get; init; }
}

internal sealed class Para : FlowBlock
{
    public required IReadOnlyList<Run> Runs { get; init; }
    public double Size { get; init; } = 9;
    public Weight Weight { get; init; } = Weight.Normal;
    public string Color { get; init; } = Flow.Ink;
    public Align Align { get; init; } = Align.Left;
    public bool Underline { get; init; }
    public string? Family { get; init; }
    public SizeRole Role { get; init; }
}

/// <summary>WPF's BlockUIContainer holding a 1 px top border with a 4 px margin
/// each side — 9 px of height, however its neighbours' margins fall.</summary>
internal sealed class RuleBlock : FlowBlock;

internal sealed class TblCell
{
    public List<FlowBlock> Blocks { get; } = [];

    /// <summary>Bottom border thickness in px; 0 for none.</summary>
    public double BorderBottom { get; init; }
}

internal sealed class TblRow
{
    public List<TblCell> Cells { get; } = [];
}

internal sealed class Tbl : FlowBlock
{
    public Tbl(params double[] widths)
    {
        Widths = widths;
        MarginTop = 4;
        MarginBottom = 4;
    }

    public double[] Widths { get; }
    public List<TblRow> Rows { get; } = [];
}

/// <summary>WPF's Section: the boxed letterhead (border + padding) or a plain
/// grouping of blocks.</summary>
internal sealed class Sec : FlowBlock
{
    public Sec() { MarginBottom = 4; }

    public bool Boxed { get; init; }
    public double PadLeft { get; init; }
    public double PadTop { get; init; }
    public double PadRight { get; init; }
    public double PadBottom { get; init; }
    public List<FlowBlock> Blocks { get; } = [];

    /// <summary>The clinic's logo, in a column of its own beside the text.</summary>
    public byte[]? Logo { get; init; }
}

/// <summary>
/// A document under construction, and the renderer that lays it out.
/// </summary>
internal sealed class FlowDoc
{
    public List<FlowBlock> Blocks { get; } = [];

    public void Add(FlowBlock block) => Blocks.Add(block);

    public byte[] ToPdf(DocumentTheme theme)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container => Compose(container, theme)).GeneratePdf();
    }

    private void Compose(IDocumentContainer container, DocumentTheme theme)
    {
        var family = string.IsNullOrWhiteSpace(theme.PrintFontFamily) ? Flow.DefaultFont : theme.PrintFontFamily!;
        var ctx = new Ctx(family, theme.PrintFontSizeDelta, theme);

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(Flow.Pt(Flow.PagePaddingPx));
            page.DefaultTextStyle(t => t.FontFamily(family).FontSize(Flow.Pt(9)).FontColor(Flow.Ink));
            page.Content().Column(col => Stack(col, Blocks, ctx, isRoot: true));
        });
    }

    // ── Layout ───────────────────────────────────────────────────────────

    internal sealed record Ctx(string Family, double SizeDelta, DocumentTheme Theme)
    {
        private static readonly DocumentTheme Defaults = new();

        /// <summary>
        /// The desktop's ApplyPrintSettings: the clinic's size nudge is added to every paragraph,
        /// floored at 4, after which the paragraph lays out at its new size - including the unset
        /// margin that is derived from it. A paragraph's role may shift it first.
        /// </summary>
        public double Effective(Para p) => Math.Max(Flow.MinFontSize, p.Size + RoleShift(p.Role) + SizeDelta);

        private double RoleShift(SizeRole role) => role switch
        {
            SizeRole.Letterhead => Theme.LetterheadNameSize - Defaults.LetterheadNameSize,
            SizeRole.Contact => Theme.ContactLineSize - Defaults.ContactLineSize,
            SizeRole.Kind => Theme.DocumentKindSize - Defaults.DocumentKindSize,
            SizeRole.Body => Theme.BodyTextSize - Defaults.BodyTextSize,
            SizeRole.Row => Theme.TableRowSize - Defaults.TableRowSize,
            SizeRole.Header => Theme.TableHeaderSize - Defaults.TableHeaderSize,
            SizeRole.Totals => Theme.TotalsSize - Defaults.TotalsSize,
            SizeRole.Footer => Theme.FooterSize - Defaults.FooterSize,
            _ => 0
        };
    }

    private static double Top(FlowBlock b, Ctx ctx, bool inCell) => b.MarginTop ?? (inCell ? 0 : Unset(b, ctx));
    private static double Bottom(FlowBlock b, Ctx ctx, bool inCell)
        => b is Sec { Logo: not null, Boxed: false } ? Math.Max(b.MarginBottom ?? 0, LogoTableMargin)
         : b.MarginBottom ?? (inCell ? 0 : Unset(b, ctx));

    // The logo letterhead is a Table with no margin set: one line of the document font. Inside a
    // border it stays inside; with no border it collapses into the gap after the section.
    private static readonly double LogoTableMargin = 9 * Flow.LineSpacing;

    private static double Unset(FlowBlock b, Ctx ctx)
        => b is Para p ? ctx.Effective(p) * Flow.LineSpacing : 0;

    private static void Stack(ColumnDescriptor col, IReadOnlyList<FlowBlock> blocks, Ctx ctx, bool isRoot = false, bool inCell = false)
    {
        FlowBlock? previous = null;

        foreach (var block in blocks)
        {
            // Between two blocks the margins collapse to the larger. At the
            // leading edge an unset margin applies nothing, but one that was
            // set does.
            var gap = previous is null
                ? block.MarginTop ?? 0
                : Math.Max(Bottom(previous, ctx, inCell), Top(block, ctx, inCell));

            var captured = block;
            col.Item().PaddingTop(Flow.Pt(gap)).Element(c => Render(c, captured, ctx));
            previous = block;
        }

        // The trailing edge is asymmetric to the leading one, and measured: an unset
        // margin applies nothing at the start of a container but one full line at the
        // end. The letterhead of a prescription ends on the phone line, whose margin
        // is unset, and WPF prints its box 10 px taller for it than for a receipt's,
        // which ends on an explicitly-zero title.
        if (previous is not null && !isRoot)
        {
            var tail = Bottom(previous, ctx, inCell);
            if (tail > 0) col.Item().Height(Flow.Pt(tail));
        }
    }

    private static void Render(IContainer c, FlowBlock block, Ctx ctx)
    {
        switch (block)
        {
            case Para p: RenderPara(c, p, ctx); break;
            case RuleBlock: RenderRule(c); break;
            case Tbl t: RenderTable(c, t, ctx); break;
            case Sec s: RenderSection(c, s, ctx); break;
        }
    }

    private static void RenderPara(IContainer c, Para p, Ctx ctx)
    {
        var size = Flow.Pt(ctx.Effective(p));
        var family = p.Family ?? ctx.Family;

        var body = c.PaddingLeft(Flow.Pt(p.MarginLeft)).PaddingRight(Flow.Pt(p.MarginRight));

        body.Text(text =>
        {
            text.DefaultTextStyle(s => s.FontFamily(family).FontSize(size).FontColor(p.Color));

            switch (p.Align)
            {
                case Align.Center: text.AlignCenter(); break;
                case Align.Right: text.AlignRight(); break;
                case Align.Justify: text.Justify(); break;
                default: text.AlignLeft(); break;
            }

            foreach (var run in p.Runs)
            {
                var span = text.Span(run.Text);
                var weight = run.Weight ?? p.Weight;
                if (weight == Weight.Bold) span.Bold();
                else if (weight == Weight.SemiBold) span.SemiBold();
                if (p.Underline) span.Underline();
            }
        });
    }

    private static void RenderRule(IContainer c)
        => c.PaddingVertical(Flow.Pt(4)).LineHorizontal(Flow.Pt(1)).LineColor(Flow.Line);

    private static void RenderTable(IContainer c, Tbl t, Ctx ctx)
    {
        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                foreach (var w in t.Widths) cols.RelativeColumn((float)w);
            });

            // Every cell is placed by row and column. WPF rows may have fewer cells than the
            // table has columns (a one-cell "Payment" row under a three-column grid), and
            // QuestPDF would otherwise pull the next row's first cell into the gap.
            for (var r = 0; r < t.Rows.Count; r++)
            {
                var row = t.Rows[r];

                for (var k = 0; k < row.Cells.Count; k++)
                {
                    var cell = row.Cells[k];
                    // A WPF cell border takes up layout space; a QuestPDF one is drawn inside the
                    // box and reserves none. Reserving it by hand is what makes a bordered row
                    // the same height as the desktop's (measured: 1 px short per rule without it).
                    var holder = table.Cell().Row((uint)(r + 1)).Column((uint)(k + 1)).Element(x =>
                        cell.BorderBottom > 0
                            ? x.BorderBottom(Flow.Pt(cell.BorderBottom)).BorderColor(Flow.Line)
                                .PaddingBottom(Flow.Pt(cell.BorderBottom))
                            : x);

                    holder.Column(inner => Stack(inner, cell.Blocks, ctx, inCell: true));
                }
            }
        });
    }

    private static void RenderSection(IContainer c, Sec s, Ctx ctx)
    {
        // The 1 px frame takes up space in WPF (it adds to the section's height and inset), so
        // it is reserved here as well as drawn.
        var box = s.Boxed ? c.Border(Flow.Pt(1)).BorderColor(Flow.Line).Padding(Flow.Pt(1)) : c;

        var padded = box
            .PaddingLeft(Flow.Pt(s.PadLeft)).PaddingRight(Flow.Pt(s.PadRight))
            .PaddingTop(Flow.Pt(s.PadTop)).PaddingBottom(Flow.Pt(s.PadBottom));

        if (s.Logo is null)
        {
            padded.Column(inner => Stack(inner, s.Blocks, ctx));
            return;
        }

        // The desktop's logo column: 28 of 100 shares, the text in the other 72,
        // the image fitted inside a 190 x 50 px box with 4 px of its own padding.
        // The logo and the text are the two cells of a WPF Table, and that Table has no margin set, so
        // it ends with one line of the document font (measured: 12 px at 9 px) - the text cell, like
        // every table cell, resolving its own unset margins to nothing.
        padded.Column(outer =>
        {
            outer.Item().Row(row =>
            {
            row.RelativeItem(28).Padding(Flow.Pt(4)).AlignMiddle().AlignCenter()
                .MaxWidth(Flow.Pt(190)).MaxHeight(Flow.Pt(50)).Image(s.Logo).FitArea();

            row.RelativeItem(72).Column(inner => Stack(inner, s.Blocks, ctx, inCell: true));
            });
            if (s.Boxed) outer.Item().Height(Flow.Pt(LogoTableMargin));
        });
    }
}
