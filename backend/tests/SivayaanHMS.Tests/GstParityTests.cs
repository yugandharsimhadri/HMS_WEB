using SivayaanHMS.Core;

namespace SivayaanHMS.Tests;

/// <summary>
/// The counter shows a running total computed in TypeScript (frontend
/// src/clinical/gst.ts) while the server recomputes it in C# when the bill is
/// saved. If the two ever disagree, the operator quotes one number and the
/// printed bill carries another — which is the kind of thing a customer
/// notices at the counter and nobody can explain.
///
/// These cases pin the C# side of that contract. Each expected value was
/// verified against the running app: the same figures appeared in the browser
/// preview and in the saved Sale row. If a change here makes one fail, the
/// TypeScript port has to move with it.
/// </summary>
public class GstParityTests
{
    [Theory]
    // 6 tablets out of a 15-tablet strip at Rs 25 — the partial-pack case.
    // Unit price rounds to 1.67, so gross is 10.02, not 10.00.
    [InlineData(25.00, 15, 6, 12.0, 10.02, 8.95, 0.53, 0.54, -0.02, 10.00)]
    // A whole strip prices at exactly the printed MRP, never 15 x 1.67.
    [InlineData(25.00, 15, 15, 12.0, 25.00, 22.32, 1.34, 1.34, 0.00, 25.00)]
    // A sealed box of 10 at Rs 50, 5% GST.
    [InlineData(50.00, 10, 10, 5.0, 50.00, 47.62, 1.19, 1.19, 0.00, 50.00)]
    // Nil-rated: no tax comes out of the MRP at all.
    [InlineData(50.00, 10, 10, 0.0, 50.00, 50.00, 0.00, 0.00, 0.00, 50.00)]
    public void Bill_totals_match_what_the_counter_previews(
        decimal mrp, int unitsPerPack, int quantity, decimal gstRate,
        decimal expectedGross, decimal expectedTaxable, decimal expectedCgst,
        decimal expectedSgst, decimal expectedRoundOff, decimal expectedNet)
    {
        var line = GstCalculator.Line(mrp, unitsPerPack, quantity, 0m, gstRate);
        var bill = GstCalculator.Bill([line]);

        Assert.Equal(expectedGross, bill.Gross);
        Assert.Equal(expectedTaxable, bill.Taxable);
        Assert.Equal(expectedCgst, bill.Cgst);
        Assert.Equal(expectedSgst, bill.Sgst);
        Assert.Equal(expectedRoundOff, bill.RoundOff);
        Assert.Equal(expectedNet, bill.Net);
    }

    [Fact]
    public void Cgst_and_sgst_always_add_back_to_the_gst_taken_out()
    {
        // Any half-paise remainder goes to CGST so the two halves reconcile
        // exactly - a bill whose CGST + SGST != GST is one an auditor queries.
        foreach (var quantity in Enumerable.Range(1, 60))
        {
            var line = GstCalculator.Line(25.00m, 15, quantity, 0m, 12.0m);
            Assert.Equal(line.Gst, line.Cgst + line.Sgst);
        }
    }

    [Fact]
    public void Part_of_a_pack_never_costs_more_than_the_whole_pack()
    {
        // The unit price is rounded up to the paisa, and rounding can make the
        // remainder dearer than the pack it came out of. Nobody may be charged
        // more for part of something than for all of it.
        for (var perPack = 2; perPack <= 30; perPack++)
        {
            for (var loose = 1; loose < perPack; loose++)
            {
                var partial = PackMath.Gross(1.00m, perPack, loose);
                Assert.True(partial <= 1.00m,
                    $"{loose} of a {perPack}-pack came to {partial}, more than the pack's own 1.00.");
            }
        }
    }
}
