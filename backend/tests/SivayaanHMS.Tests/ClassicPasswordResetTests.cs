using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Tests;

/// <summary>
/// The original, one-step reset — what HMS_WEB runs with SivaComm:Enabled off,
/// the default. PasswordResetService was refactored to share its code check
/// with the SivaComm flow; these pin that the original behaviour did not move:
/// the code is checked and the password set in one call, wrong codes are
/// counted, five kill the code, a code works once, and a new code kills the old.
/// </summary>
public sealed class ClassicPasswordResetTests : IAsyncLifetime
{
    private readonly TestDb _db = new();
    private readonly MovableClock _clock = new(DateTime.Now);
    private readonly Guid _tenant = Guid.NewGuid();
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _db.MigrateAsync();
        var (hash, salt) = PasswordHasher.Hash("Old-Pass-1234");
        var user = new User { Username = "drsri@sunrise", DisplayName = "Dr Sri", PasswordHash = hash, PasswordSalt = salt, Role = UserRole.Admin, IsActive = true, Phone = "9876543210" };
        await using var db = _db.CreateContext(_tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _userId = user.Id;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private PasswordResetService Service => new(_db.CreateFactory(_tenant), _clock);

    private async Task<bool> PasswordIsAsync(string password)
    {
        await using var db = _db.CreateContext(_tenant);
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == _userId);
        return PasswordHasher.Verify(password, u.PasswordHash, u.PasswordSalt);
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task The_right_code_sets_the_password_in_one_call()
    {
        var code = (await Service.IssueAsync(_userId, _tenant))!;

        Assert.Equal(ResetOutcome.Success, await Service.RedeemAsync(_userId, code, "New-Pass-5678"));
        Assert.True(await PasswordIsAsync("New-Pass-5678"));
    }

    [Fact]
    public async Task A_wrong_code_changes_nothing()
    {
        var code = (await Service.IssueAsync(_userId, _tenant))!;

        Assert.Equal(ResetOutcome.InvalidCode, await Service.RedeemAsync(_userId, Wrong(code), "New-Pass-5678"));
        Assert.True(await PasswordIsAsync("Old-Pass-1234"));

        // Still usable after one wrong try.
        Assert.Equal(ResetOutcome.Success, await Service.RedeemAsync(_userId, code, "New-Pass-5678"));
    }

    [Fact]
    public async Task Five_wrong_codes_kill_it_even_for_the_right_one()
    {
        var code = (await Service.IssueAsync(_userId, _tenant))!;

        for (var i = 0; i < 4; i++)
            Assert.Equal(ResetOutcome.InvalidCode, await Service.RedeemAsync(_userId, Wrong(code), "New-Pass-5678"));
        Assert.Equal(ResetOutcome.TooManyAttempts, await Service.RedeemAsync(_userId, Wrong(code), "New-Pass-5678"));

        Assert.NotEqual(ResetOutcome.Success, await Service.RedeemAsync(_userId, code, "New-Pass-5678"));
        Assert.True(await PasswordIsAsync("Old-Pass-1234"));
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var code = (await Service.IssueAsync(_userId, _tenant))!;
        _clock.Now = _clock.Now.AddMinutes(11);

        Assert.Equal(ResetOutcome.Expired, await Service.RedeemAsync(_userId, code, "New-Pass-5678"));
        Assert.True(await PasswordIsAsync("Old-Pass-1234"));
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var code = (await Service.IssueAsync(_userId, _tenant))!;
        await Service.RedeemAsync(_userId, code, "New-Pass-5678");

        Assert.Equal(ResetOutcome.InvalidCode, await Service.RedeemAsync(_userId, code, "Third-Pass-999"));
        Assert.True(await PasswordIsAsync("New-Pass-5678"));
    }

    [Fact]
    public async Task A_new_code_kills_the_old_one()
    {
        var first = (await Service.IssueAsync(_userId, _tenant))!;
        var second = (await Service.IssueAsync(_userId, _tenant))!;

        if (first != second)
            Assert.Equal(ResetOutcome.InvalidCode, await Service.RedeemAsync(_userId, first, "New-Pass-5678"));
        Assert.Equal(ResetOutcome.Success, await Service.RedeemAsync(_userId, second, "New-Pass-5678"));
    }

    [Fact]
    public async Task The_send_cap_is_three_per_window()
    {
        for (var i = 0; i < 3; i++) Assert.NotNull(await Service.IssueAsync(_userId, _tenant));
        Assert.Null(await Service.IssueAsync(_userId, _tenant));
    }

    [Fact]
    public async Task The_classic_flow_writes_no_reset_transactions()
    {
        var code = (await Service.IssueAsync(_userId, _tenant))!;
        await Service.RedeemAsync(_userId, code, "New-Pass-5678");

        await using var db = _db.CreateContext(_tenant);
        Assert.Equal(0, await db.PasswordResetTransactions.IgnoreQueryFilters().CountAsync());
    }
}
