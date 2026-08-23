using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Tests;

/// <summary>
/// Changing your own password is the second half of a platform-support
/// reset, and the half that makes the first half safe: until it runs, the
/// support agent who read a temporary password down the phone still knows
/// the account's password.
///
/// So these pin behaviour, not implementation — that the old password stops
/// working, that the new one starts, and that MustChangePassword is cleared
/// only by a genuine change.
/// </summary>
public class ChangePasswordTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static async Task<(TestDb Db, AuthService Auth, Guid UserId)> SetUp(string password)
    {
        var testDb = new TestDb();
        await testDb.MigrateAsync();

        var (hash, salt) = PasswordHasher.Hash(password);
        var user = new User
        {
            Username = "meera@city",
            DisplayName = "Admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Admin,
            IsActive = true,
            // As a support reset leaves it.
            MustChangePassword = true,
        };

        await using (var db = testDb.CreateContext(Tenant))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var auth = new AuthService(testDb.CreateFactory(Tenant), new SystemClock());
        return (testDb, auth, user.Id);
    }

    [Fact]
    public async Task A_completed_change_retires_the_old_password()
    {
        var (testDb, auth, userId) = await SetUp("Temp-Pass-1234");
        using var _ = testDb;

        Assert.True(await auth.ChangeOwnPasswordAsync(userId, "Temp-Pass-1234", "MeeraCity@2026"));

        // The temporary password the support agent knows must be dead, not
        // merely superseded.
        Assert.Equal(LoginOutcome.Failed, (await auth.LoginAsync("meera@city", "Temp-Pass-1234")).Outcome);

        var result = await auth.LoginAsync("meera@city", "MeeraCity@2026");
        Assert.Equal(LoginOutcome.Success, result.Outcome);

        // Cleared, so the forced-change gate lets them through.
        Assert.False(result.User!.MustChangePassword);
    }

    [Fact]
    public async Task The_wrong_current_password_changes_nothing()
    {
        var (testDb, auth, userId) = await SetUp("Temp-Pass-1234");
        using var _ = testDb;

        Assert.False(await auth.ChangeOwnPasswordAsync(userId, "not-the-old-one", "MeeraCity@2026"));

        // The account is untouched: still on the old password, and still
        // required to change it. A failed attempt must not half-apply.
        var result = await auth.LoginAsync("meera@city", "Temp-Pass-1234");
        Assert.Equal(LoginOutcome.Success, result.Outcome);
        Assert.True(result.User!.MustChangePassword);

        Assert.Equal(LoginOutcome.Failed, (await auth.LoginAsync("meera@city", "MeeraCity@2026")).Outcome);
    }
}
