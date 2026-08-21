using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

public class AppDbContext : DbContext
{
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentTenantContext _currentTenant;
    private readonly IClock _clock;

    // All three collaborators are optional so every design-time factory and
    // every test that doesn't care about identity/tenancy/time keeps working
    // unchanged — a context built without one gets the null/system default,
    // the same shape as the desktop app's login-optional AppDbContext.
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUserContext? currentUser = null,
        ICurrentTenantContext? currentTenant = null,
        IClock? clock = null)
        : base(options)
    {
        _currentUser = currentUser ?? new NullCurrentUserContext();
        _currentTenant = currentTenant ?? new NullCurrentTenantContext();
        _clock = clock ?? new SystemClock();
    }

    // Read by the global query filter below through a DbContext-instance
    // property rather than a captured local, so EF Core re-evaluates it on
    // every query instead of baking in whatever tenant was current when the
    // model was built. Public so callers that need to allocate a tenant-
    // scoped resource outside the change tracker (NumberService) can read
    // the same value the filter uses, instead of threading a second
    // tenantId parameter through everything.
    public Guid TenantId => _currentTenant.TenantId;

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<VisitDiagnosticRequest> VisitDiagnosticRequests => Set<VisitDiagnosticRequest>();

    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();

    public DbSet<VaccineMaster> VaccineMasters => Set<VaccineMaster>();
    public DbSet<VaccinationRecord> VaccinationRecords => Set<VaccinationRecord>();
    public DbSet<GrowthMeasurement> GrowthMeasurements => Set<GrowthMeasurement>();
    public DbSet<PediatricProfile> PediatricProfiles => Set<PediatricProfile>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<ProcedureBill> ProcedureBills => Set<ProcedureBill>();
    public DbSet<ProcedureBillItem> ProcedureBillItems => Set<ProcedureBillItem>();

    public DbSet<DentalCase> DentalCases => Set<DentalCase>();
    public DbSet<DentalSitting> DentalSittings => Set<DentalSitting>();
    public DbSet<DentalReplacementMaster> DentalReplacementMasters => Set<DentalReplacementMaster>();
    public DbSet<DentalCaseReplacement> DentalCaseReplacements => Set<DentalCaseReplacement>();
    public DbSet<AnesthesiaTypeMaster> AnesthesiaTypeMasters => Set<AnesthesiaTypeMaster>();
    public DbSet<DentalPackageMaster> DentalPackageMasters => Set<DentalPackageMaster>();
    public DbSet<DentalPackageItem> DentalPackageItems => Set<DentalPackageItem>();
    public DbSet<DentalPayment> DentalPayments => Set<DentalPayment>();

    public DbSet<LabAnalyte> LabAnalytes => Set<LabAnalyte>();
    public DbSet<LabAnalyteReferenceRange> LabAnalyteReferenceRanges => Set<LabAnalyteReferenceRange>();
    public DbSet<LabReport> LabReports => Set<LabReport>();
    public DbSet<LabReportAnalyte> LabReportAnalytes => Set<LabReportAnalyte>();
    public DbSet<LabPackageMaster> LabPackageMasters => Set<LabPackageMaster>();
    public DbSet<LabPackageReport> LabPackageReports => Set<LabPackageReport>();
    public DbSet<LabOrder> LabOrders => Set<LabOrder>();
    public DbSet<LabOrderReport> LabOrderReports => Set<LabOrderReport>();
    public DbSet<LabResult> LabResults => Set<LabResult>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockEntry> StockEntries => Set<StockEntry>();
    public DbSet<StockEntryItem> StockEntryItems => Set<StockEntryItem>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();

    public DbSet<DiagnosticTest> DiagnosticTests => Set<DiagnosticTest>();
    public DbSet<DiagnosticBill> DiagnosticBills => Set<DiagnosticBill>();
    public DbSet<DiagnosticBillItem> DiagnosticBillItems => Set<DiagnosticBillItem>();

    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Counter> Counters => Set<Counter>();
    public DbSet<User> Users => Set<User>();
    public DbSet<H1RegisterEntry> H1Register => Set<H1RegisterEntry>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<VendorProductCode> VendorProductCodes => Set<VendorProductCode>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    /// <summary>The tenant registry itself — not tenant-scoped, since it is
    /// what tenant-scoping is scoped against. See Tenant's own class doc.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Money: 12,2 is plenty for a clinic and keeps SQLite storage predictable.
        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(12);
            property.SetScale(2);
        }

        b.Entity<Patient>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PatientNo }).IsUnique();
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.Name);
            e.Ignore(x => x.Display);
        });

        b.Entity<Visit>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.VisitNo }).IsUnique();
            e.HasIndex(x => x.ScheduledOn);
            e.HasOne(x => x.Patient).WithMany(p => p.Visits)
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany()
                .HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Prescription).WithOne(p => p.Visit)
                .HasForeignKey(p => p.VisitId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.DiagnosticRequests).WithOne(r => r.Visit)
                .HasForeignKey(r => r.VisitId).OnDelete(DeleteBehavior.Cascade);

            // Advisory back-reference to the Appointment that produced this
            // visit, if any — no navigation property either side, same shape
            // as DiagnosticBillItem.TestId below. Deleting the appointment
            // later must never cascade into deleting the visit it produced.
            e.HasOne<Appointment>().WithMany()
                .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.SetNull);

            e.Ignore(x => x.IsWaiting);
            e.Ignore(x => x.PatientLine);
            e.Ignore(x => x.FeeBadge);
            e.Ignore(x => x.RowSummary);
            e.Ignore(x => x.WaitedFor);
        });

        b.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Name);

            // The same medicine twice splits its stock and shows up twice at the
            // counter. A filtered unique index so a removed record does not block
            // the name being used again. Scoped to tenant like every other
            // uniqueness constraint below — two clinics can each have their own
            // "Paracetamol 500mg".
            e.HasIndex(x => new { x.TenantId, x.SearchKey })
             .IsUnique()
             .HasFilter("\"IsDeleted\" = 0");

            e.Ignore(x => x.StockOnHand);
            e.Ignore(x => x.PackDescription);
            e.Ignore(x => x.UnitPriceLabel);
            e.Ignore(x => x.PackPriceLabel);
            e.Ignore(x => x.RackLabel);
            e.Ignore(x => x.Shortage);
        });

        b.Entity<Batch>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.BatchNo });
            e.HasOne(x => x.Product).WithMany(p => p.Batches)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.IsExpired);
            e.Ignore(x => x.Display);
            e.Ignore(x => x.UnitPrice);
            e.Ignore(x => x.OnHand);
            e.Ignore(x => x.Returnable);
            e.Ignore(x => x.DaysToExpiry);
            e.Ignore(x => x.EffectivePackCost);
        });

        b.Entity<StockAdjustment>(e =>
        {
            e.HasIndex(x => x.AdjustedOn);
            e.HasOne(x => x.Batch).WithMany()
                .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Change);
            e.Ignore(x => x.Direction);
        });

        b.Entity<VendorProductCode>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.VendorProfile, x.Code }).IsUnique();
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StockEntry>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.EntryNo }).IsUnique();
            e.HasIndex(x => x.SupplierInvoiceNo);
            e.Ignore(x => x.WasImported);
            e.HasMany(x => x.Items).WithOne(i => i.StockEntry)
                .HasForeignKey(i => i.StockEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StockEntryItem>(e =>
        {
            e.Ignore(x => x.LineTotal);
            e.Ignore(x => x.UnitsReceived);
        });

        b.Entity<SaleItem>(e => e.Ignore(x => x.QuantityDescription));

        b.Entity<Sale>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BillNo }).IsUnique();
            e.HasIndex(x => x.BillDate);
            e.HasMany(x => x.Items).WithOne(i => i.Sale)
                .HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DiagnosticTest>(e => e.HasIndex(x => x.Name));

        b.Entity<DiagnosticBill>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BillNo }).IsUnique();
            e.HasIndex(x => x.BillDate);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Visit).WithMany()
                .HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Items).WithOne(i => i.Bill)
                .HasForeignKey(i => i.BillId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DiagnosticBillItem>(e =>
        {
            e.HasOne<DiagnosticTest>().WithMany()
                .HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<VisitDiagnosticRequest>(e =>
        {
            e.HasOne(x => x.Test).WithMany()
                .HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Appointment>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.AppointmentNo }).IsUnique();
            e.HasIndex(x => x.ScheduledOn);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany()
                .HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);

            // Self-referencing — the chain of reschedules a slot went
            // through. No navigation property; RescheduledFromId is only
            // ever read back, never joined against.
            e.HasOne<Appointment>().WithMany()
                .HasForeignKey(x => x.RescheduledFromId).OnDelete(DeleteBehavior.SetNull);

            e.Ignore(x => x.IsPending);
        });

        b.Entity<ReminderLog>(e =>
        {
            e.HasIndex(x => x.DueOn);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.IsActioned);
        });

        b.Entity<VaccineMaster>(e => e.HasIndex(x => x.Name));

        b.Entity<VaccinationRecord>(e =>
        {
            e.HasIndex(x => x.PatientId);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vaccine).WithMany()
                .HasForeignKey(x => x.VaccineId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Batch).WithMany()
                .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<GrowthMeasurement>(e =>
        {
            e.HasIndex(x => x.PatientId);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Visit>().WithMany()
                .HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PediatricProfile>(e =>
        {
            e.HasIndex(x => x.PatientId).IsUnique();
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Procedure>(e => e.HasIndex(x => new { x.Department, x.Name }));

        b.Entity<ProcedureBill>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BillNo }).IsUnique();
            e.HasIndex(x => x.BillDate);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Visit).WithMany()
                .HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Items).WithOne(i => i.Bill)
                .HasForeignKey(i => i.BillId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProcedureBillItem>(e =>
        {
            e.HasOne<Procedure>().WithMany()
                .HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DentalCase>(e =>
        {
            e.HasIndex(x => x.PatientId);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany()
                .HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Procedure>().WithMany()
                .HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<DentalPackageMaster>().WithMany()
                .HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Sittings).WithOne(s => s.DentalCase)
                .HasForeignKey(s => s.DentalCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Replacements).WithOne(r => r.DentalCase)
                .HasForeignKey(r => r.DentalCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne(p => p.DentalCase)
                .HasForeignKey(p => p.DentalCaseId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.TotalCost);
            e.Ignore(x => x.AmountPaid);
            e.Ignore(x => x.Balance);
        });

        b.Entity<DentalSitting>(e =>
        {
            e.HasOne(x => x.Doctor).WithMany()
                .HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AnesthesiaTypeMaster>().WithMany()
                .HasForeignKey(x => x.AnesthesiaTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DentalReplacementMaster>(e => e.HasIndex(x => x.Name));

        b.Entity<DentalCaseReplacement>(e =>
        {
            e.HasOne<DentalReplacementMaster>().WithMany()
                .HasForeignKey(x => x.ReplacementId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AnesthesiaTypeMaster>(e => e.HasIndex(x => x.Name));

        b.Entity<DentalPackageMaster>(e => e.HasIndex(x => x.Name));

        b.Entity<DentalPackageItem>(e =>
        {
            e.HasOne(x => x.Package).WithMany()
                .HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Procedure>().WithMany()
                .HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DentalPayment>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ReceiptNo }).IsUnique();
            e.HasIndex(x => x.PaidOn);
        });

        b.Entity<LabAnalyte>(e => e.HasIndex(x => x.Name));

        b.Entity<LabAnalyteReferenceRange>(e =>
        {
            e.HasOne(x => x.Analyte).WithMany()
                .HasForeignKey(x => x.AnalyteId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LabReport>(e => e.HasIndex(x => x.Name));

        b.Entity<LabReportAnalyte>(e =>
        {
            e.HasOne(x => x.Report).WithMany()
                .HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Analyte).WithMany()
                .HasForeignKey(x => x.AnalyteId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LabPackageMaster>(e => e.HasIndex(x => x.Name));

        b.Entity<LabPackageReport>(e =>
        {
            e.HasOne(x => x.Package).WithMany()
                .HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Report).WithMany()
                .HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LabOrder>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.OrderNo }).IsUnique();
            e.HasIndex(x => x.OrderDate);
            e.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Visit).WithMany()
                .HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<LabPackageMaster>().WithMany()
                .HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Reports).WithOne(r => r.Order)
                .HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LabOrderReport>(e =>
        {
            e.HasOne<LabReport>().WithMany()
                .HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Results).WithOne(r => r.OrderReport)
                .HasForeignKey(r => r.OrderReportId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LabResult>(e =>
        {
            e.HasOne<LabAnalyte>().WithMany()
                .HasForeignKey(x => x.AnalyteId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Setting>(e => e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique());

        // The atomic-numbering fix (SAAS_MIGRATION.md finding 1) leans on
        // this constraint directly: NextAsync does an UPDATE ... RETURNING
        // against the row this index identifies, so two concurrent callers
        // for the same tenant+counter serialize on it instead of racing in
        // memory.
        b.Entity<Counter>(e => e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique());

        b.Entity<User>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Username }).IsUnique();
            e.Ignore(x => x.Display);
        });

        b.Entity<ImportProfile>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.Ignore(x => x.SplitDateFormats);
            e.Ignore(x => x.SplitExpiryFormats);
        });

        b.Entity<Tenant>(e => e.HasIndex(x => x.Slug).IsUnique());

        // Every entity gets the same two-part read filter — soft delete and
        // tenant isolation — applied here once via reflection rather than
        // per query. This is the plan's own recommendation (SAAS_MIGRATION.md
        // decision 1): a single missing filter on the desktop's per-call-site
        // "!IsDeleted" pattern was low-risk; the same gap here would leak one
        // clinic's patients into another's screen. Applying it structurally
        // to every BaseEntity type makes "forgot the filter" impossible
        // rather than merely tested-against.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

            SetTenantAndSoftDeleteFilterMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [b]);

            // SQLite has no computed rowversion column type (that's a SQL
            // Server thing), so RowVersion is a plain BLOB we regenerate
            // ourselves in Stamp() below on every insert/update. Marking it
            // a concurrency token is what makes EF Core include the old
            // value in the WHERE clause and throw
            // DbUpdateConcurrencyException when a save touches a row someone
            // else already changed.
            b.Entity(entityType.ClrType).Property(nameof(BaseEntity.RowVersion)).IsConcurrencyToken();
        }

        base.OnModelCreating(b);
    }

    private static readonly MethodInfo SetTenantAndSoftDeleteFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(SetTenantAndSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void SetTenantAndSoftDeleteFilter<TEntity>(ModelBuilder b) where TEntity : BaseEntity
    {
        b.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted && e.TenantId == TenantId);
    }

    public override int SaveChanges()
    {
        Stamp();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Stamp();
        return base.SaveChangesAsync(ct);
    }

    private void Stamp()
    {
        var userId = _currentUser.UserId;
        var now = _clock.Now;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                // Always the context's current tenant, never whatever the
                // caller happened to set — see ICurrentTenantContext.
                entry.Entity.TenantId = _currentTenant.TenantId;
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedByUserId = userId;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedByUserId = userId;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
