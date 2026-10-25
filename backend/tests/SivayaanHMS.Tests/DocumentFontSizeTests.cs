using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// The per-element sizes in Settings > Document branding. Two things have to
/// hold: an untouched install prints exactly the desktop's page, and a changed
/// number actually reaches it.
///
/// Sizes are stated here in the desktop's own unit — WPF device-independent
/// pixels, 1/96" — and converted with <see cref="Px"/> to the points a PDF
/// records. They used to be asserted as raw points, which is how the web came to
/// print every size 33% larger than the desktop: it copied WPF's numbers and
/// read them as points.
/// </summary>
public class DocumentFontSizeTests
{
    /// <summary>A WPF size as the PDF will record it.</summary>
    private static double Px(double px) => px * 0.75;

    private static Visit SampleVisit() => new()
    {
        VisitNo = "V00001",
        FeeReceiptNo = "RCP00001",
        ScheduledOn = new DateTime(2026, 8, 24, 10, 15, 0),
        FeePaidOn = new DateTime(2026, 8, 24, 10, 20, 0),
        Fee = 300m,
        FeePaymentMode = PaymentMode.Cash,
        // Set so the "Review on" line renders - it is the only place the
        // receipt uses the plain body size, and without it that size never
        // reaches the page to be checked.
        FollowUpOn = new DateTime(2026, 9, 7),
        Patient = new Patient { Name = "Aarav Rao", Age = 5, Gender = Gender.Male, PatientNo = "P00001" },
        Doctor = new Doctor { Name = "Dr. A. Kumar", Speciality = "Paediatrics", RegistrationNo = "TNMC/12345" }
    };

    private static ClinicProfile SampleClinic() => new()
    {
        Name = "Twinkle Children's Hospital",
        AddressLine = "12 MG Road, Chennai",
        Phone = "044-12345678"
    };

    private static IReadOnlyList<double> ReceiptSizes(DocumentTheme theme)
        => PdfContent.FontSizes(FeeReceiptDocument.Generate(SampleVisit(), SampleClinic(), theme));

    /// <summary>The defaults are the desktop's own sizes, and the receipt's body prints two
    /// larger than the pharmacy bill's, by request — so the letterhead is the part of the page
    /// that appears exactly as configured.</summary>
    [Fact]
    public void Defaults_print_the_desktop_sizes()
    {
        var sizes = ReceiptSizes(new DocumentTheme());

        PdfSizeAssert.Contains(Px(15), sizes);    // clinic name, which takes no delta
        PdfSizeAssert.Contains(Px(8.6), sizes);   // "CASH RECEIPT", likewise; also an identity label (6.6 + 2)
        PdfSizeAssert.Contains(Px(8), sizes);     // address and phone
        PdfSizeAssert.Contains(Px(11), sizes);    // body 9 + clinical 2 ("Review on")
        PdfSizeAssert.Contains(Px(10.4), sizes);  // table row 8.4 + clinical 2
        PdfSizeAssert.Contains(Px(9.6), sizes);   // table header 7.6 + clinical 2
        PdfSizeAssert.Contains(Px(9.8), sizes);   // identity value 7.8 + clinical 2
        PdfSizeAssert.Contains(Px(10), sizes);    // "Paid by", amount in words: 8 + clinical 2
        PdfSizeAssert.Contains(Px(9.4), sizes);   // footer 7.4 + clinical 2
        // RECEIVED lands on 15 too (13 + 2), so it is not asserted here - a match would not tell
        // us which of the two produced it. The totals size gets its own test below.
    }

    [Theory]
    [InlineData(22d)]
    [InlineData(11d)]
    public void Letterhead_size_reaches_the_page(double size)
    {
        var sizes = ReceiptSizes(new DocumentTheme { LetterheadNameSize = size });

        PdfSizeAssert.Contains(Px(size), sizes);
    }

    [Fact]
    public void Table_and_totals_sizes_reach_the_page()
    {
        var sizes = ReceiptSizes(new DocumentTheme { TableRowSize = 11.5, TotalsSize = 18 });

        PdfSizeAssert.Contains(Px(13.5), sizes); // 11.5 + clinical 2
        PdfSizeAssert.Contains(Px(20), sizes);   // 18 + clinical 2
    }

    /// <summary>The global nudge still works, and still moves the per-element
    /// sizes with it rather than being replaced by them.</summary>
    [Fact]
    public void Global_delta_still_stacks_on_top_of_the_element_sizes()
    {
        var sizes = ReceiptSizes(new DocumentTheme { LetterheadNameSize = 20, PrintFontSizeDelta = 1 });

        PdfSizeAssert.Contains(Px(21), sizes);
    }

    /// <summary>The identity grid follows the table sizes - the relationship that makes the block
    /// under the letterhead read as a grid rather than as loose text. On the desktop the label sits
    /// 1.0 under the table header and the value 0.6 under the table row; shifting a table size
    /// shifts the grid by the same amount.</summary>
    [Fact]
    public void Identity_grid_follows_the_table_sizes()
    {
        var sizes = ReceiptSizes(new DocumentTheme { TableHeaderSize = 12, TableRowSize = 14 });

        PdfSizeAssert.Contains(Px(13.0), sizes); // label: 6.6 + (12 - 7.6) + clinical 2
        PdfSizeAssert.Contains(Px(15.4), sizes); // value: 7.8 + (14 - 8.4) + clinical 2
    }

    /// <summary>
    /// The table defaults are the desktop's 8.4 and 7.6; they said 8.5 and 7.5 until a real print
    /// of each was measured side by side. (Installs that saved the old values are read back as
    /// untouched by <c>SettingsService.GetDocumentThemeAsync</c>, which needs a database to test.)
    /// </summary>
    [Fact]
    public void The_table_defaults_are_the_desktops_own_sizes()
    {
        Assert.Equal(8.4, new DocumentTheme().TableRowSize);
        Assert.Equal(7.6, new DocumentTheme().TableHeaderSize);
    }
}
