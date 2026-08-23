using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// The support identity's name cannot be taken by a clinic's own user.
///
/// Pinned after the guard silently stopped working: it compared the whole
/// username against "EnterpriseAdmin", which no longer matches anything once
/// every username carries an "@clinic" suffix. "enterpriseadmin@twinkle" was
/// accepted.
///
/// It grants no privilege — support is matched on the bare name before any
/// tenant is resolved — but an account wearing that name inside a clinic
/// exists only to be mistaken for the real one by the staff who see it.
/// </summary>
public class ReservedUsernameTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static async Task<(TestDb Db, AuthService Auth)> SetUp()
    {
        var testDb = new TestDb();
        await testDb.MigrateAsync();
        return (testDb, new AuthService(testDb.CreateFactory(Tenant), new SystemClock()));
    }

    private static User Candidate(string username) => new()
    {
        Username = username,
        DisplayName = "Someone",
        Role = UserRole.Pharmacy,
        IsActive = true,
    };

    [Theory]
    [InlineData("enterpriseadmin@twinkle")]
    [InlineData("EnterpriseAdmin@twinkle")]   // the fold has to hold
    [InlineData("ENTERPRISEADMIN@twinkle")]
    [InlineData("EnterpriseAdmin")]           // and the bare form still
    public async Task The_support_name_cannot_be_taken_by_a_clinic_user(string username)
    {
        var (testDb, auth) = await SetUp();
        using var _ = testDb;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.SaveUserAsync(Candidate(username), "Whatever@2026"));

        Assert.Contains("reserved", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_name_that_merely_contains_it_is_still_fine()
    {
        var (testDb, auth) = await SetUp();
        using var _ = testDb;

        // The rule is about the local part being exactly the reserved name,
        // not about the letters appearing anywhere — refusing "enterprise"
        // would be a rule nobody could predict.
        await auth.SaveUserAsync(Candidate("enterprise@twinkle"), "Whatever@2026");
        await auth.SaveUserAsync(Candidate("admin.enterpriseadmin@twinkle"), "Whatever@2026");

        var users = await auth.GetUsersAsync();
        Assert.Equal(2, users.Count);
    }
}
