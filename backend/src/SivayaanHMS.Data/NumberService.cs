using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SivayaanHMS.Data;

/// <summary>
/// Hands out gap-tolerant, duplicate-proof sequential document numbers, per
/// tenant. The desktop version (SAAS_MIGRATION.md finding 1) read
/// Counter.LastNumber, incremented it in memory, and relied on the caller's
/// own SaveChanges to persist it — safe with one user, and a source of
/// duplicate invoice numbers the instant two requests land at once, because
/// nothing stopped both from reading the same starting value.
///
/// This version does the read-increment-write inside one short transaction
/// that takes an update lock on the counter row, so two concurrent callers
/// serialize on the row instead of racing in memory.
///
/// <para>
/// UPDATE-then-INSERT rather than MERGE. MERGE reads more neatly and has a
/// long history of correctness and deadlock bugs under exactly this
/// insert-or-increment pattern; this shape is duller and better understood.
/// </para>
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
    /// Called outside a transaction, this commits on its own, and the
    /// trade-off is gaps rather than duplicates: a number allocated for a
    /// document that then fails to save is burned, not reused. Every atomic
    /// allocator shaped like this one makes the same trade, and it is normal
    /// for invoice numbering; a duplicate invoice number in a statutory
    /// register is the thing that is not acceptable.
    ///
    /// Called *inside* a caller's transaction — which is the usual case, since
    /// most numbered documents are written in one — it enrols in that
    /// transaction instead. The number and the document then commit or roll
    /// back together, so a rolled-back sale leaves no gap at all.
    /// </summary>
    public static async Task<string> NextAsync(AppDbContext db, string name, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();

        // Most callers allocate a number *inside* a transaction they already
        // opened — a sale, a diagnostic bill, a stock entry. SQL Server
        // refuses to run a command on a connection with a pending local
        // transaction unless the command is enrolled in it, so without this
        // every numbered document created inside those nine transaction
        // sites would fail. SQLite never enforced it, which is why this only
        // appeared on the provider move.
        //
        // When enrolled, the outer transaction governs: the UPDLOCK is then
        // held until *it* commits, which is stricter than this method needs
        // and exactly what the caller wants — the number and the document it
        // belongs to commit together.
        var ambient = db.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
        cmd.Transaction = ambient;

        // Only manage a transaction when nobody else is. Opening one inside
        // an ambient transaction would just nest, and committing the nested
        // level says nothing about the outer one.
        var ownTransaction = ambient is null;

        // UPDLOCK takes the lock that a later UPDATE would need, at read
        // time, so a second caller waits here rather than reading the same
        // LastNumber. HOLDLOCK keeps it to the end of the transaction, which
        // also stops a second caller inserting the same counter in the gap
        // between the UPDATE finding nothing and the INSERT running.
        //
        // OUTPUT returns the incremented value from the statement that made
        // it, so nothing re-reads the row and no second caller can slip
        // between the write and the read.
        //
        // XACT_ABORT ON: on any error the transaction is rolled back rather
        // than left open, which on a pooled connection would otherwise leak
        // locks to whoever gets it next.
        //
        // Both OUTPUT clauses collect into one table variable, and the batch
        // returns a single SELECT at the end.
        //
        // Emitting the two OUTPUTs directly would send back *two* result
        // sets, and when the UPDATE matches nothing the first of them is
        // empty — so a reader positioned on it finds no row and the insert's
        // result is never looked at. The single trailing SELECT means the
        // caller reads one result set whichever branch ran.
        cmd.CommandText = $"""
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @allocated TABLE (Prefix nvarchar(10), LastNumber int);

            {(ownTransaction ? "BEGIN TRANSACTION;" : "")}

            UPDATE Counters WITH (UPDLOCK, HOLDLOCK)
            SET LastNumber = LastNumber + 1
            OUTPUT inserted.Prefix, inserted.LastNumber INTO @allocated
            WHERE TenantId = @tenantId AND Name = @name AND IsDeleted = 0;

            IF NOT EXISTS (SELECT 1 FROM @allocated)
                INSERT INTO Counters (Id, TenantId, Name, Prefix, LastNumber, CreatedAt, IsDeleted, RowVersion)
                OUTPUT inserted.Prefix, inserted.LastNumber INTO @allocated
                VALUES (@id, @tenantId, @name, @prefix, 1, SYSDATETIME(), 0, @rowVersion);

            {(ownTransaction ? "COMMIT TRANSACTION;" : "")}

            SELECT Prefix, LastNumber FROM @allocated;
            """;

        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = Guid.NewGuid() });
        cmd.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.UniqueIdentifier) { Value = db.TenantId });
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 100) { Value = name });
        cmd.Parameters.Add(new SqlParameter("@prefix", SqlDbType.NVarChar, 10) { Value = DefaultPrefix(name) });
        cmd.Parameters.Add(new SqlParameter("@rowVersion", SqlDbType.VarBinary, 16) { Value = Guid.NewGuid().ToByteArray() });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var prefix = reader.GetString(0);

        // Counter.LastNumber is an int, and SQL Server returns it as Int32.
        // SQLite returned every INTEGER as 64-bit, so the old GetInt64 worked
        // there and throws here — one of the quiet ones in a provider move.
        var lastNumber = reader.GetInt32(1);

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
