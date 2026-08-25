using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// The per-element sizes in Settings > Document branding. Two things have to
/// hold: an untouched install still prints the desktop's numbers, and a
/// changed number actually reaches the page.
/// </summary>
public class DocumentFontSizeTests
{
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

    /// <summary>The defaults are the desktop's own sizes. The clinical +2 sits
    /// on the body of this document, so the letterhead is the one size that
    /// appears on the page exactly as configured.</summary>
    [Fact]
    public void Defaults_print_the_desktop_sizes()
    {
        var sizes = ReceiptSizes(new DocumentTheme());

        PdfSizeAssert.Contains(15d, sizes);   // clinic name, which takes no delta
        PdfSizeAssert.Contains(8.6d, sizes);  // "CASH RECEIPT", likewise
        PdfSizeAssert.Contains(11d, sizes);   // body 9 + clinical 2
        PdfSizeAssert.Contains(10.5d, sizes); // table row 8.5 + clinical 2
        PdfSizeAssert.Contains(9.5d, sizes);  // table header 7.5 + clinical 2
        PdfSizeAssert.Contains(10d, sizes);   // contact line 8 + clinical 2
        PdfSizeAssert.Contains(9.4d, sizes);  // footer 7.4 + clinical 2
        // Totals land on 15 too (13 + 2), so they are not asserted here -
        // a match would not tell us which of the two produced it. The
        // totals size gets its own test below.
    }

    [Theory]
    [InlineData(22d)]
    [InlineData(11d)]
    public void Letterhead_size_reaches_the_page(double size)
    {
        var sizes = ReceiptSizes(new DocumentTheme { LetterheadNameSize = size });

        PdfSizeAssert.Contains(size, sizes);
    }

    [Fact]
    public void Table_and_totals_sizes_reach_the_page()
    {
        var sizes = ReceiptSizes(new DocumentTheme { TableRowSize = 11.5, TotalsSize = 18 });

        PdfSizeAssert.Contains(13.5d, sizes); // 11.5 + clinical 2
        PdfSizeAssert.Contains(20d, sizes);   // 18 + clinical 2
    }

    /// <summary>The global nudge still works, and still moves the per-element
    /// sizes with it rather than being replaced by them.</summary>
    [Fact]
    public void Global_delta_still_stacks_on_top_of_the_element_sizes()
    {
        var sizes = ReceiptSizes(new DocumentTheme { LetterheadNameSize = 20, PrintFontSizeDelta = 1 });

        PdfSizeAssert.Contains(21d, sizes);
    }

    /// <summary>The identity grid is derived from the table sizes, so moving
    /// the table moves the grid with it - the relationship that makes the
    /// block under the letterhead read as a grid rather than as loose text.
    /// </summary>
    [Fact]
    public void Identity_grid_follows_the_table_sizes()
    {
        var sizes = ReceiptSizes(new DocumentTheme { TableHeaderSize = 12, TableRowSize = 14 });

        PdfSizeAssert.Contains(13.1d, sizes); // label: 12 - 0.9 + clinical 2
        PdfSizeAssert.Contains(15.3d, sizes); // value: 14 - 0.7 + clinical 2
    }
}
