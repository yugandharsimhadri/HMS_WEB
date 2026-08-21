namespace SivayaanHMS.Data;

/// <summary>
/// Every "now" the domain and data layers need goes through this instead of
/// DateTime.Now/.Today directly. On the desktop the ambient clock was always
/// the clinic's own wall clock, so it was invisible; on a server it is the
/// server's clock, which can be a different timezone than the clinic — see
/// SAAS_MIGRATION.md finding 4 (30 ambient call sites, ported one by one to
/// this as each service is ported). SystemClock is the production default;
/// tests supply a fixed clock instead of sleeping or fighting wall-clock time.
/// </summary>
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    // TODO(SAAS_MIGRATION.md #4): this is the server's local time, not the
    // tenant's. Needs a per-tenant timezone before launch; UTC/localize at
    // the edge is the likely fix, deferred until a tenant model exists.
    public DateTime Now => DateTime.Now;
}
