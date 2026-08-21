using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>A diagnostic bill line as assembled on screen, before it is persisted.</summary>
public class DiagnosticBillLine
{
    public Guid? TestId { get; set; }
    public string TestName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Lab test master and diagnostic billing — the Diagnostics module's data
/// layer. Deliberately small next to <see cref="PharmacyService"/>: no
/// batches, no stock, no GST — a diagnostic bill is a patient, a list of
/// priced tests, and a status.
/// </summary>
public class DiagnosticsService(IDbContextFactory<AppDbContext> factory)
{
    // ── Test master ────────────────────────────────────────────────────────

    public async Task<List<DiagnosticTest>> SearchTestsAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.DiagnosticTests.AsNoTracking().Where(t => !t.IsDeleted);

        if (activeOnly) query = query.Where(t => t.Active);
        if (!string.IsNullOrWhiteSpace(term))
        {
            // Like, not Contains — Contains becomes instr(), which is case
            // sensitive, so "crp" found nothing against "CRP". Same fix as
            // OpdService.SearchPatientsAsync.
            var pattern = $"%{term.Trim()}%";
            query = query.Where(t => EF.Functions.Like(t.Name, pattern) || EF.Functions.Like(t.Category, pattern));
        }

        return await query.OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync();
    }

    /// <summary>Every category already in use, so the Test Master combo offers
    /// what staff have already typed as well as the built-in examples.</summary>
    public async Task<List<string>> GetCategoriesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DiagnosticTests.AsNoTracking().Where(t => !t.IsDeleted)
            .Select(t => t.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    public async Task SaveTestAsync(DiagnosticTest test)
    {
        if (string.IsNullOrWhiteSpace(test.Name))
            throw new InvalidOperationException("Test name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = test.Id == Guid.Empty ? null : await db.DiagnosticTests.FirstOrDefaultAsync(t => t.Id == test.Id);

        if (existing is null)
        {
            if (test.Id == Guid.Empty) test.Id = Guid.NewGuid();
            db.DiagnosticTests.Add(test);
        }
        else
        {
            existing.Name = test.Name.Trim();
            existing.Category = test.Category.Trim();
            existing.Price = test.Price;
            existing.Active = test.Active;
        }

        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(Guid testId, bool active)
    {
        await using var db = await factory.CreateDbContextAsync();
        var test = await db.DiagnosticTests.FirstOrDefaultAsync(t => t.Id == testId);
        if (test is null) return;

        test.Active = active;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a test outright, but only if it has never been billed — one
    /// that has is left alone so <see cref="DeleteTestAsync"/> can never be
    /// the reason a past bill's line loses its test reference at the same
    /// moment it loses its price; the bill already carries both as its own
    /// denormalised copy regardless. Deactivate instead.
    /// </summary>
    public async Task DeleteTestAsync(Guid testId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.DiagnosticBillItems.AnyAsync(i => i.TestId == testId))
            throw new InvalidOperationException(
                "This test has already been billed and cannot be deleted. Deactivate it instead.");

        var test = await db.DiagnosticTests.FirstOrDefaultAsync(t => t.Id == testId);
        if (test is null) return;

        db.DiagnosticTests.Remove(test);
        await db.SaveChangesAsync();
    }

    // ── Billing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a diagnostic bill: totals are recomputed from the lines rather
    /// than trusted from the screen. A new bill gets the next DX number and
    /// starts life <see cref="DiagnosticBillStatus.Ordered"/>; an existing one
    /// keeps both, and is refused once it has reached
    /// <see cref="DiagnosticBillStatus.Completed"/> — editing it after that
    /// point is exactly what the status exists to prevent.
    /// </summary>
    public async Task<DiagnosticBill> SaveBillAsync(DiagnosticBill bill, IReadOnlyList<DiagnosticBillLine> lines)
    {
        if (lines.Count == 0) throw new InvalidOperationException("Add at least one test to the bill.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var existing = bill.Id == Guid.Empty
            ? null
            : await db.DiagnosticBills.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == bill.Id);

        DiagnosticBill entity;

        if (existing is null)
        {
            entity = bill;
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            entity.BillNo = await NumberService.NextAsync(db, NumberService.DiagnosticBill);
            entity.Status = DiagnosticBillStatus.Ordered;
            db.DiagnosticBills.Add(entity);
        }
        else
        {
            if (existing.Status == DiagnosticBillStatus.Completed)
                throw new InvalidOperationException("This bill is Completed and can no longer be edited.");

            entity = existing;
            entity.PatientId = bill.PatientId;
            entity.PatientName = bill.PatientName;
            entity.PatientNo = bill.PatientNo;
            entity.PaymentMode = bill.PaymentMode;
            entity.TransactionNo = bill.TransactionNo;
            entity.Discount = bill.Discount;
            entity.Remarks = bill.Remarks;
            entity.VisitId = bill.VisitId;
            entity.ReferredBy = bill.ReferredBy;

            db.DiagnosticBillItems.RemoveRange(entity.Items);
            entity.Items.Clear();
        }

        decimal total = 0;
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"Quantity must be at least 1 for {line.TestName}.");
            if (line.Price < 0)
                throw new InvalidOperationException($"The price of {line.TestName} cannot be negative.");

            var amount = line.Price * line.Quantity;
            total += amount;

            db.DiagnosticBillItems.Add(new DiagnosticBillItem
            {
                BillId = entity.Id,
                TestId = line.TestId,
                TestName = line.TestName,
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

    /// <summary>Moves a bill along the lab workflow.</summary>
    public async Task UpdateStatusAsync(Guid billId, DiagnosticBillStatus status)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bill = await db.DiagnosticBills.FirstOrDefaultAsync(b => b.Id == billId);
        if (bill is null) return;

        bill.Status = status;
        await db.SaveChangesAsync();
    }

    /// <summary>Refuses once Completed — the same rule <see cref="SaveBillAsync"/>
    /// enforces for edits.</summary>
    public async Task DeleteBillAsync(Guid billId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bill = await db.DiagnosticBills.FirstOrDefaultAsync(b => b.Id == billId);
        if (bill is null) return;

        if (bill.Status == DiagnosticBillStatus.Completed)
            throw new InvalidOperationException("This bill is Completed and can no longer be deleted.");

        db.DiagnosticBills.Remove(bill);
        await db.SaveChangesAsync();
    }

    public async Task<DiagnosticBill?> GetBillAsync(Guid billId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DiagnosticBills.AsNoTracking().Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == billId);
    }

    /// <summary>Every diagnostic bill for a patient, newest first.</summary>
    public async Task<List<DiagnosticBill>> GetBillsByPatientAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DiagnosticBills.AsNoTracking().Include(b => b.Items)
            .Where(b => b.PatientId == patientId)
            .OrderByDescending(b => b.BillDate)
            .ToListAsync();
    }

    /// <summary>
    /// The day's OPD visits that requested tests during consultation and have
    /// not yet been turned into a bill — what "Load diagnostic tests" on the
    /// Billing tab picks from. The diagnostics equivalent of
    /// <c>PharmacyService</c>'s "today's OPD prescription" list at the counter.
    /// </summary>
    public async Task<List<Visit>> GetVisitsWithPendingRequestsAsync(DateTime date)
    {
        await using var db = await factory.CreateDbContextAsync();
        var from = date.Date;
        var to = from.AddDays(1);

        var billedVisitIds = db.DiagnosticBills.Where(b => b.VisitId != null).Select(b => b.VisitId!.Value);

        return await db.Visits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.DiagnosticRequests)
            .Where(v => !v.IsDeleted && v.ScheduledOn >= from && v.ScheduledOn < to
                     && v.DiagnosticRequests.Any(r => !r.IsDeleted)
                     && !billedVisitIds.Contains(v.Id))
            .OrderBy(v => v.TokenNo)
            .ToListAsync();
    }

    // ── Reports ────────────────────────────────────────────────────────────

    /// <summary>Every bill raised in a date range (inclusive), for Reports.</summary>
    public async Task<List<DiagnosticBill>> SearchBillsAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var toExclusive = to.Date.AddDays(1);

        return await db.DiagnosticBills.AsNoTracking().Include(b => b.Items)
            .Where(b => b.BillDate >= from.Date && b.BillDate < toExclusive)
            .OrderByDescending(b => b.BillDate)
            .ToListAsync();
    }
}
