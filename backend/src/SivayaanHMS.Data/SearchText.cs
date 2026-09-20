namespace SivayaanHMS.Data;

/// <summary>
/// One way of building a "contains this text" search, used by every screen
/// that has a search box.
///
/// Case is folded in the query rather than left to the database. PostgreSQL's
/// default collation is case-sensitive — "bhavya" does not LIKE "Bhavya" —
/// and the SQL Server this replaced happened to be case-insensitive, which
/// hid the question entirely. Nothing in the application should depend on
/// how a server was installed: a restore onto a differently configured one
/// would silently start returning nothing for the same search, and a
/// receptionist typing a name in a hurry is the last person who should
/// discover that. Lowering both sides here is what makes the answer the same
/// everywhere.
///
/// LOWER() on the column means an index cannot be used, which costs nothing
/// here: every one of these patterns starts with a wildcard, and a leading
/// wildcard already rules out a seek.
/// </summary>
public static class SearchText
{
    /// <summary>The pattern for a LIKE, already folded and wildcarded.</summary>
    public static string Pattern(string term) => $"%{term.Trim().ToLower()}%";
}
