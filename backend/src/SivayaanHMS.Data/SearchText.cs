namespace SivayaanHMS.Data;

/// <summary>
/// One way of building a "contains this text" search, used by every screen
/// that has a search box.
///
/// Case is folded in the query rather than left to the database. SQL Server
/// happens to be installed here with a case-insensitive collation, so a name
/// typed in lower case does find "BHAVYA" today — but nothing in the
/// application says so, and an instance installed with a case-sensitive
/// collation, or a restore onto a differently configured server, would
/// silently start returning nothing for the same search. A receptionist
/// typing a name in a hurry is the last person who should discover that.
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
