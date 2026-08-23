namespace SivayaanHMS.Core;

/// <summary>
/// What a clinic's licence date means. Two callers need the same answer and
/// must never disagree: sign-in, which decides whether to open the door, and
/// the support console, which shows how long that stays true. A rule
/// duplicated in both would drift, and the drift would show up as a clinic
/// the console calls "Active" being turned away at the login page.
///
/// Everything here works in whole days against a caller-supplied "today",
/// so nothing depends on the machine clock at the point of use and the
/// boundaries are directly testable.
/// </summary>
public static class LicenseRules
{
    /// <summary>How close to expiry starts reading as urgent. Thirty days is
    /// enough for a renewal conversation to happen before a waiting room
    /// does.</summary>
    public const int ExpiringWithinDays = 30;

    public const string Active = "Active";
    public const string Expiring = "Expiring";
    public const string Expired = "Expired";
    public const string NoExpiry = "No expiry";

    /// <summary>
    /// A licence runs to the *end* of its last day, so expiry is strictly
    /// earlier than today, not "not later than". A clinic whose licence
    /// expires today can still work today — being locked out on the morning
    /// of the day you paid through is the kind of detail that costs a
    /// customer.
    /// </summary>
    public static bool IsExpired(DateTime? expiresOn, DateTime today)
        => expiresOn is { } expiry && expiry.Date < today.Date;

    /// <summary>Status and whole days remaining. Days is negative once
    /// expired (how long ago), and int.MaxValue for a clinic on no fixed
    /// term, so callers can sort by urgency without special-casing.</summary>
    public static (string Status, int DaysRemaining) Describe(DateTime? expiresOn, DateTime today)
    {
        if (expiresOn is null) return (NoExpiry, int.MaxValue);

        var days = (expiresOn.Value.Date - today.Date).Days;

        if (days < 0) return (Expired, days);
        return (days <= ExpiringWithinDays ? Expiring : Active, days);
    }
}
