namespace SivayaanHMS.Data;

/// <summary>
/// The one thing <see cref="AppDbContext"/> needs to know about who is using
/// the application: their id, so every row it saves can be stamped with it.
/// Kept to just that — the full logged-in identity (name, role, sign
/// in/out) is a UI concern and lives in Pharma.App's CurrentUserService,
/// which implements this.
/// </summary>
public interface ICurrentUserContext
{
    Guid? UserId { get; }
}

/// <summary>The default when nobody is signed in — every design-time and
/// test context that has no real session gets this, so a missing DI
/// registration is a null user rather than a startup crash.</summary>
public class NullCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId => null;
}
