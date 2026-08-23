using SivayaanHMS.Core;

namespace SivayaanHMS.Tests;

/// <summary>
/// The licence boundaries, pinned because two callers depend on them
/// agreeing: sign-in decides whether to open the door, and the support
/// console reports how long that stays true. If these ever disagree, a
/// clinic the console calls "Active" gets turned away at the login page —
/// a support call nobody can diagnose from either screen alone.
/// </summary>
public class LicenseRulesTests
{
    private static readonly DateTime Today = new(2026, 8, 23);

    [Fact]
    public void A_licence_expiring_today_still_works_today()
    {
        // The single most expensive off-by-one available here: a clinic that
        // paid through the 23rd being locked out on the morning of the 23rd.
        Assert.False(LicenseRules.IsExpired(Today, Today));

        var (status, days) = LicenseRules.Describe(Today, Today);
        Assert.Equal(LicenseRules.Expiring, status);
        Assert.Equal(0, days);
    }

    [Fact]
    public void A_licence_that_ran_out_yesterday_is_expired()
    {
        var yesterday = Today.AddDays(-1);

        Assert.True(LicenseRules.IsExpired(yesterday, Today));

        var (status, days) = LicenseRules.Describe(yesterday, Today);
        Assert.Equal(LicenseRules.Expired, status);
        Assert.Equal(-1, days);
    }

    [Fact]
    public void The_time_of_day_never_decides_it()
    {
        // Tenant.LicenseExpiresOn is a date, but nothing stops a caller
        // handing in a timestamp; a licence must not expire at whatever hour
        // the support agent happened to tick the box.
        var lateToday = Today.AddHours(23).AddMinutes(59);
        var earlyToday = Today.AddHours(0).AddMinutes(1);

        Assert.False(LicenseRules.IsExpired(lateToday, earlyToday));
        Assert.False(LicenseRules.IsExpired(earlyToday, lateToday));
    }

    [Fact]
    public void No_expiry_never_locks_anyone_out()
    {
        Assert.False(LicenseRules.IsExpired(null, Today));

        var (status, days) = LicenseRules.Describe(null, Today);
        Assert.Equal(LicenseRules.NoExpiry, status);

        // int.MaxValue rather than 0, so sorting by urgency puts these last
        // instead of first.
        Assert.Equal(int.MaxValue, days);
    }

    [Theory]
    [InlineData(31, LicenseRules.Active)]
    [InlineData(30, LicenseRules.Expiring)]
    [InlineData(1, LicenseRules.Expiring)]
    public void Expiring_starts_thirty_days_out(int daysAway, string expected)
    {
        var (status, days) = LicenseRules.Describe(Today.AddDays(daysAway), Today);

        Assert.Equal(expected, status);
        Assert.Equal(daysAway, days);
    }
}
