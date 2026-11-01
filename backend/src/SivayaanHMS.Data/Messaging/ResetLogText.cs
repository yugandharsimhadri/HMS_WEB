using SivayaanHMS.Core;

namespace SivayaanHMS.Data.Messaging;

/// <summary>
/// Wording shared by both reset paths (classic and SivaCom) for the messages
/// log. The log is for the operator only: the browser gets the same answer
/// whatever is written here, so an account's existence is never revealed to
/// the person asking.
/// </summary>
public static class ResetLogText
{
    /// <summary>Why no code can go to this account, or null when one can.</summary>
    public static string? WhyNoCode(Tenant? tenant, User? user) =>
        tenant is null ? "no clinic matches the username typed"
        : user is null ? $"no such username in clinic {tenant.Slug}"
        : !user.IsActive ? $"user {user.Id} ({tenant.Slug}) is inactive"
        : string.IsNullOrWhiteSpace(user.Phone) ? $"user {user.Id} ({tenant.Slug}) has no phone number on file"
        : null;

    /// <summary>"******4929": enough to tell two numbers apart, not to dial.</summary>
    public static string Mask(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "(no number)" : new string('*', digits.Length - 4) + digits[^4..];
    }
}
