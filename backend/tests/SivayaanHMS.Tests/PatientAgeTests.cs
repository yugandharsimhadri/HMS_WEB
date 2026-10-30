using SivayaanHMS.Core;

namespace SivayaanHMS.Tests;

/// <summary>Age in years and months: how it is worked out from a date of birth and how it reads.</summary>
public class PatientAgeTests
{
    [Theory]
    [InlineData(4, 6, "4Y 6M")]
    [InlineData(4, 0, "4Y")]
    [InlineData(0, 6, "6M")]
    [InlineData(0, 0, "0Y")]
    public void Age_reads_as_years_and_months(int years, int months, string expected)
        => Assert.Equal(expected, Patient.FormatAge(years, months));

    [Fact]
    public void A_baby_born_seven_months_ago_is_0_years_7_months()
    {
        var (years, months) = Patient.AgePartsFromDob(DateTime.Today.AddMonths(-7));

        Assert.Equal(0, years);
        Assert.Equal(7, months);
    }

    [Fact]
    public void Months_only_count_once_the_day_of_the_month_is_reached()
    {
        // Two years and six months ago, plus one day short of it.
        var (years, months) = Patient.AgePartsFromDob(DateTime.Today.AddMonths(-30).AddDays(1));

        Assert.Equal(2, years);
        Assert.Equal(5, months);
    }

    [Fact]
    public void Years_from_the_parts_agree_with_the_old_whole_years()
    {
        var dob = new DateTime(2019, 11, 14);

        Assert.Equal(Patient.AgeFromDob(dob), Patient.AgePartsFromDob(dob).Years);
    }
}
