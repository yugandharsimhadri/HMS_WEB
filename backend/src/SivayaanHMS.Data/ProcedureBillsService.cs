using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>A procedure bill line as assembled on screen, before it is persisted.</summary>
public class ProcedureBillLine
{
    public Guid? ProcedureId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Shared small-procedure master and flat billing — used by Pediatrics today
/// and by Dentist's simpler procedures. Deliberately mirrors
/// <see cref="DiagnosticsService"/>'s own shape line for line: no batches, no
/// stock, no GST — a procedure bill is a patient, a list of priced items,
/// and a status.
/// </summary>
public class ProcedureBillsService(IDbContextFactory<AppDbContext> factory)
{
    // ── Procedure master ───────────────────────────────────────────────────

    public async Task<List<Procedure>> SearchProceduresAsync(ProcedureDepartment department, string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Procedures.AsNoTracking().Where(p => !p.IsDeleted && p.Department == department);

        if (activeOnly) query = query.Where(p => p.Active);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = SearchText.Pattern(term);
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), pattern) || EF.Functions.Like(p.Category.ToLower(), pattern));
        }

        return await query.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();
    }

    public async Task<List<string>> GetCategoriesAsync(ProcedureDepartment department)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Procedures.AsNoTracking().Where(p => !p.IsDeleted && p.Department == department)
            .Select(p => p.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    public async Task SaveProcedureAsync(Procedure procedure)
    {
        if (string.IsNullOrWhiteSpace(procedure.Name))
            throw new InvalidOperationException("Procedure name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = procedure.Id == Guid.Empty ? null : await db.Procedures.FirstOrDefaultAsync(p => p.Id == procedure.Id);

        if (existing is null)
        {
            if (procedure.Id == Guid.Empty) procedure.Id = Guid.NewGuid();
            db.Procedures.Add(procedure);
        }
        else
        {
            existing.Name = procedure.Name.Trim();
            existing.Category = procedure.Category.Trim();
            existing.Price = procedure.Price;
            existing.Active = procedure.Active;
            // Department is fixed once set — a Pediatrics procedure never
            // becomes a Dentist one by editing it from a shared screen.
        }

        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(Guid procedureId, bool active)
    {
        await using var db = await factory.CreateDbContextAsync();
        var procedure = await db.Procedures.FirstOrDefaultAsync(p => p.Id == procedureId);
        if (procedure is null) return;

        procedure.Active = active;
        await db.SaveChangesAsync();
    }

    /// <summary>Refuses once billed — same guard shape as
    /// <see cref="DiagnosticsService.DeleteTestAsync"/>.</summary>
    public async Task DeleteProcedureAsync(Guid procedureId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.ProcedureBillItems.AnyAsync(i => i.ProcedureId == procedureId))
            throw new InvalidOperationException(
                "This procedure has already been billed and cannot be deleted. Deactivate it instead.");

        var procedure = await db.Procedures.FirstOrDefaultAsync(p => p.Id == procedureId);
        if (procedure is null) return;

        db.Procedures.Remove(procedure);
        await db.SaveChangesAsync();
    }

    // ── Billing ────────────────────────────────────────────────────────────

    public async Task<ProcedureBill> SaveBillAsync(ProcedureBill bill, IReadOnlyList<ProcedureBillLine> lines)
    {
        if (lines.Count == 0) throw new InvalidOperationException("Add at least one procedure to the bill.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var existing = bill.Id == Guid.Empty
            ? null
            : await db.ProcedureBills.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == bill.Id);

        ProcedureBill entity;

        if (existing is null)
        {
            entity = bill;
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            entity.BillNo = await NumberService.NextAsync(db, NumberService.ProcedureBill);
            entity.Status = ProcedureBillStatus.Ordered;
            db.ProcedureBills.Add(entity);
        }
        else
        {
            if (existing.Status == ProcedureBillStatus.Completed)
                throw new InvalidOperationException("This bill is Completed and can no longer be edited.");

            entity = existing;
            entity.PatientId = bill.PatientId;
            entity.PatientName = bill.PatientName;
            entity.PatientNo = bill.PatientNo;
            entity.PaymentMode = bill.PaymentMode;
            entity.TransactionNo = bill.TransactionNo;
            entity.Discount = bill.Discount;
            entity.VisitId = bill.VisitId;
            entity.ReferredBy = bill.ReferredBy;

            db.ProcedureBillItems.RemoveRange(entity.Items);
            entity.Items.Clear();
        }

        decimal total = 0;
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"Quantity must be at least 1 for {line.ProcedureName}.");
            if (line.Price < 0)
                throw new InvalidOperationException($"The price of {line.ProcedureName} cannot be negative.");

            var amount = line.Price * line.Quantity;
            total += amount;

            db.ProcedureBillItems.Add(new ProcedureBillItem
            {
                BillId = entity.Id,
                ProcedureId = line.ProcedureId,
                ProcedureName = line.ProcedureName,
                Price = line.Price,
                Quantity = line.Quantity,
                Amount = amount
            });
        }

        entity.TotalAmount = total;
        entity.FinalAmount = Math.Max(0, total - entity.Discount);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return entity;
    }

    /// <summary>Refuses once Completed — the same rule <see cref="SaveBillAsync"/>
    /// enforces for edits.</summary>
    public async Task DeleteBillAsync(Guid billId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bill = await db.ProcedureBills.FirstOrDefaultAsync(b => b.Id == billId);
        if (bill is null) return;

        if (bill.Status == ProcedureBillStatus.Completed)
            throw new InvalidOperationException("This bill is Completed and can no longer be deleted.");

        db.ProcedureBills.Remove(bill);
        await db.SaveChangesAsync();
    }

    public async Task UpdateStatusAsync(Guid billId, ProcedureBillStatus status)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bill = await db.ProcedureBills.FirstOrDefaultAsync(b => b.Id == billId);
        if (bill is null) return;

        bill.Status = status;
        await db.SaveChangesAsync();
    }

    public async Task<ProcedureBill?> GetBillAsync(Guid billId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ProcedureBills.AsNoTracking().Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == billId);
    }

    public async Task<List<ProcedureBill>> GetBillsByPatientAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ProcedureBills.AsNoTracking().Include(b => b.Items)
            .Where(b => b.PatientId == patientId)
            .OrderByDescending(b => b.BillDate)
            .ToListAsync();
    }

    /// <summary>Every bill raised in a date range (inclusive), for Reports.
    /// Not filtered by department — a bill does not carry one directly, only
    /// its line items do, via each line's own <c>Procedure</c>.</summary>
    public async Task<List<ProcedureBill>> SearchBillsAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var toExclusive = to.Date.AddDays(1);

        return await db.ProcedureBills.AsNoTracking().Include(b => b.Items)
            .Where(b => b.BillDate >= from.Date && b.BillDate < toExclusive)
            .OrderByDescending(b => b.BillDate)
            .ToListAsync();
    }
}
