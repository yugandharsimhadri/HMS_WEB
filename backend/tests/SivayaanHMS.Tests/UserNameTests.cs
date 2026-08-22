using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// Usernames are "local@clinic-slug". The suffix is what lets sign-in ask
/// for a username and a password and nothing else, and what makes every
/// username unique across the platform for free. Both properties are load
/// bearing, so both are pinned here.
/// </summary>
public class UserNameTests
{
    [Theory]
    [InlineData("reception@twinkle", "reception", "twinkle")]
    [InlineData("  Admin@Twinkle  ", "admin", "twinkle")]        // trimmed and folded
    [InlineData("dr.rao@sunrise", "dr.rao", "sunrise")]          // dots are fine
    [InlineData("a.b@c.d@twinkle", "a.b@c.d", "twinkle")]        // last separator wins
    public void Splits_a_username_into_person_and_clinic(string input, string expectedLocal, string expectedSlug)
    {
        Assert.True(UserName.TrySplit(input, out var local, out var slug));
        Assert.Equal(expectedLocal, local);
        Assert.Equal(expectedSlug, slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("admin")]        // no clinic — sign-in cannot tell which one
    [InlineData("admin@")]       // nothing after the separator
    [InlineData("@twinkle")]     // nobody before it
    public void Refuses_anything_that_does_not_name_a_clinic(string? input)
    {
        Assert.False(UserName.TrySplit(input, out _, out _));
    }

    [Fact]
    public void Builds_a_username_in_the_stored_form()
    {
        Assert.Equal("reception@twinkle", UserName.For("Reception", "Twinkle"));
        Assert.Equal("admin@sunrise", UserName.For("  admin  ", "sunrise"));
    }

    [Fact]
    public async Task Two_clinics_can_each_have_their_own_admin()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var twinkle = Guid.NewGuid();
        var sunrise = Guid.NewGuid();

        await Seed(testDb, twinkle, "admin@twinkle");
        await Seed(testDb, sunrise, "admin@sunrise");

        await using var db = testDb.CreateContext(twinkle);
        Assert.Equal(2, await db.Users.IgnoreQueryFilters().CountAsync());

        // And each clinic still only sees its own.
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task The_same_username_cannot_be_taken_twice_even_by_different_clinics()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        await Seed(testDb, Guid.NewGuid(), "admin@twinkle");

        // A second clinic claiming the identical string must be stopped by the
        // database, not merely by whichever code path happened to check first
        // — sign-in resolves a user by this column alone, so a duplicate would
        // make it ambiguous which clinic someone is signing into.
        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => Seed(testDb, Guid.NewGuid(), "admin@twinkle"));
    }

    private static async Task Seed(TestDb testDb, Guid tenantId, string username)
    {
        await using var db = testDb.CreateContext(tenantId);
        db.Users.Add(new User
        {
            Username = username,
            DisplayName = "Admin",
            PasswordHash = "x",
            PasswordSalt = "y",
            Role = UserRole.Admin,
            IsActive = true,
        });
        await db.SaveChangesAsync();
    }
}
