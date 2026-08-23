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

    // ── What a person may choose at signup ──────────────────────────────
    // Registration asks for the local part only and appends the clinic
    // itself, so these rules decide what half of every username at a clinic
    // will look like for as long as it exists.

    [Theory]
    [InlineData("Reception", "reception")]          // folded, since sign-in folds too
    [InlineData("  dr.rao  ", "dr.rao")]            // trimmed
    [InlineData("front_desk-2", "front_desk-2")]
    public void Accepts_and_folds_a_chosen_username(string input, string expected)
    {
        Assert.True(UserName.TryNormaliseLocalPart(input, out var localPart, out var error));
        Assert.Equal(expected, localPart);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]                 // nothing typed
    [InlineData("ab")]               // too short to be worth having
    [InlineData("-leading")]         // must start with a letter or number
    [InlineData("has space")]
    [InlineData("dr@sunrise")]       // the clinic is appended, never typed
    [InlineData("someone@gmail.com")]
    public void Rejects_a_username_that_would_not_survive_being_read_aloud(string input)
    {
        Assert.False(UserName.TryNormaliseLocalPart(input, out _, out var error));

        // The message is the point: this is read by someone mid-signup who
        // needs to know what to type instead, not that they were wrong.
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Typing_the_clinic_into_the_username_says_so_specifically()
    {
        UserName.TryNormaliseLocalPart("admin@twinkle", out _, out var error);

        // The likeliest signup mistake by far, given every example of a
        // username in this product contains an "@".
        Assert.Contains("automatically", error);
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
