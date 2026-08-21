using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SivayaanHMS.Data;

/// <summary>
/// Hands out gap-tolerant, duplicate-proof sequential document numbers, per
/// tenant. The desktop version (SAAS_MIGRATION.md finding 1) read
/// Counter.LastNumber, incremented it in memory, and relied on the caller's
/// own SaveChanges to persist it — safe with one user, and a source of
/// duplicate invoice numbers the instant two requests land at once, because
/// nothing stopped both from reading the same starting value.
///
/// This version does the read-increment-write as a single atomic SQLite
/// statement (`INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING`), so two
/// concurrent callers serialize on the row instead of racing in memory.
/// </summary>
public static class NumberService
{
    public const string Patient = "Patient";
    public const string Visit = "Visit";
    public const string Bill = "Bill";
    public const string StockEntry = "StockEntry";
    public const string FeeReceipt = "FeeReceipt";
    public const string DiagnosticBill = "DiagnosticBill";
    public const string Appointment = "Appointment";
    public const string ProcedureBill = "ProcedureBill";
    public const string DentalPayment = "DentalPayment";
    public const string LabOrder = "LabOrder";

    /// <summary>
    /// Allocates and returns the next number for <paramref name="name"/>
    /// under the calling context's tenant (<see cref="AppDbContext.TenantId"/>).
    ///
    /// This commits immediately, independently of the caller's own
    /// <c>SaveChangesAsync</c> — unlike the desktop version, which rode
    /// along with whatever else that DbContext saved. The trade-off is
    /// gaps, not duplicates: if the caller allocates a number here and then
    /// fails to save the document it belongs to, that number is burned
    /// rather than reused. Every atomic allocator shaped like this one
    /// (Stripe, Shopify, etc.) makes the same trade, and it is normal for
    /// invoice numbering; a duplicate invoice number in a statutory
    /// register is the thing that isn't acceptable.
    /// </summary>
    public static async Task<string> NextAsync(AppDbContext db, string name, CancellationToken ct = default)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        // SQLite allows one writer at a time. Without a busy timeout, a
        // second concurrent caller gets SQLITE_BUSY immediately instead of
        // queuing behind the first — exactly the two-counters-at-once
        // moment this method exists to serialize safely. WAL lets readers
        // proceed without waiting on a writer at all; both are connection-
        // or database-level settings, cheap to reassert on every call.
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
            await pragma.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Counters (Id, TenantId, Name, Prefix, LastNumber, CreatedAt, IsDeleted, RowVersion)
            VALUES ($id, $tenantId, $name, $prefix, 1, $now, 0, $rowVersion)
            ON CONFLICT(TenantId, Name) DO UPDATE SET LastNumber = LastNumber + 1
            RETURNING Prefix, LastNumber;
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$tenantId", db.TenantId.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prefix", DefaultPrefix(name));
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        cmd.Parameters.AddWithValue("$rowVersion", Guid.NewGuid().ToByteArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var prefix = reader.GetString(0);
        var lastNumber = reader.GetInt64(1);
        return $"{prefix}{lastNumber:D5}";
    }

    private static string DefaultPrefix(string name) => name switch
    {
        Patient => "P",
        Visit => "V",
        Bill => "INV",
        StockEntry => "GRN",
        FeeReceipt => "RCP",
        DiagnosticBill => "DX",
        Appointment => "APT",
        ProcedureBill => "PRC",
        DentalPayment => "DPR",
        LabOrder => "LAB",
        _ => "DOC"
    };
}
