using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace SivayaanHMS.Data;

/// <summary>
/// Hands out gap-tolerant, duplicate-proof sequential document numbers, per
/// tenant. The desktop version (SAAS_MIGRATION.md finding 1) read
/// Counter.LastNumber, incremented it in memory, and relied on the caller's
/// own SaveChanges to persist it — safe with one user, and a source of
/// duplicate invoice numbers the instant two requests land at once, because
/// nothing stopped both from reading the same starting value.
///
/// This version is one statement: <c>INSERT ... ON CONFLICT DO UPDATE ...
/// RETURNING</c>. PostgreSQL takes the row lock for the update inside the
/// statement, so two concurrent callers for the same tenant and counter
/// serialize on the row instead of racing in memory, and the incremented
/// value comes back from the same statement that made it — nothing re-reads
/// the row, so no second caller can slip between the write and the read.
///
/// <para>
/// The SQL Server version of this class was a batch: an UPDATE with lock
/// hints, an INSERT if that matched nothing, both OUTPUTing into a table
/// variable. An upsert is the same idea in the dialect that has a word for
/// it, and the unique index on (TenantId, Name) — see AppDbContext — is what
/// ON CONFLICT resolves against.
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
    /// back together, so a rolled-back sale leaves no gap at all; and the
    /// row lock the upsert takes is then held until the caller commits,
    /// which is stricter than this method needs and exactly what the caller
    /// wants.
    /// </summary>
    public static async Task<string> NextAsync(AppDbContext db, string name, CancellationToken ct = default)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();

        // Most callers allocate a number inside a transaction they already
        // opened — a sale, a diagnostic bill, a stock entry. Npgsql runs a
        // command on whatever transaction the connection is in regardless,
        // but naming it here keeps the intent readable and lets ADO.NET
        // validate that the command and the transaction share a connection.
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // Identifiers are double-quoted because EF Core creates them in the
        // model's PascalCase, and PostgreSQL folds anything unquoted to lower
        // case. The soft-delete flag is not consulted: nothing in this
        // application ever soft-deletes a counter, and an "undeleted" row is
        // still the right one to keep numbering from — a register never
        // starts again at 1.
        cmd.CommandText = """
            INSERT INTO "Counters" ("Id", "TenantId", "Name", "Prefix", "LastNumber", "CreatedAt", "IsDeleted", "RowVersion")
            VALUES (@id, @tenantId, @name, @prefix, 1, @createdAt, false, @rowVersion)
            ON CONFLICT ("TenantId", "Name") DO UPDATE
                SET "LastNumber" = "Counters"."LastNumber" + 1
            RETURNING "Prefix", "LastNumber";
            """;

        cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() });
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", NpgsqlDbType.Uuid) { Value = db.TenantId });
        cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = name });
        cmd.Parameters.Add(new NpgsqlParameter("prefix", NpgsqlDbType.Text) { Value = DefaultPrefix(name) });
        // Timestamp, not TimestampTz: every DateTime column in this schema is
        // clinic-local wall-clock time — see AppDbContext.ConfigureConventions.
        cmd.Parameters.Add(new NpgsqlParameter("createdAt", NpgsqlDbType.Timestamp) { Value = DateTime.Now });
        cmd.Parameters.Add(new NpgsqlParameter("rowVersion", NpgsqlDbType.Bytea) { Value = Guid.NewGuid().ToByteArray() });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"Allocating a '{name}' number returned no row.");

        var prefix = reader.GetString(0);
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
