using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>What the staff list shows. No password material of any kind —
/// not the hash, not the salt, not a placeholder. A DTO rather than the
/// entity is what guarantees that.</summary>
public record ClinicUser(
    Guid Id, string Username, string DisplayName, UserRole Role,
    bool IsActive, bool MustChangePassword, DateTime? LastLoginOn, bool IsYou);

/// <summary>
/// Adding or editing one member of staff.
///
/// <paramref name="LocalPart"/> is the part before the clinic — "reception",
/// not "reception@twinkle". The clinic half is appended server-side from the
/// tenant this request is already scoped to, so a caller cannot mint a
/// username into somebody else's clinic by typing a different suffix.
///
/// <paramref name="Password"/> is optional on an edit: absent, the person's
/// existing password stands. Supplied, it is a temporary one and they are
/// made to change it at next sign-in.
/// </summary>
public record SaveClinicUserRequest(
    Guid? Id, string LocalPart, string DisplayName, UserRole Role, bool IsActive, string? Password);

public record TemporaryPasswordResponse(string Username, string TemporaryPassword);

/// <summary>
/// The staff a clinic's Admin manages.
///
/// This is what makes roles mean anything: without it a clinic has exactly
/// one account, so the receptionist and the pharmacist both sign in as the
/// owner, and every row's CreatedBy names the owner regardless of who
/// actually did the work.
///
/// Admin-only, via ClinicAdminPolicy — which requires the tenant claim as
/// well as the role. Using <c>[Authorize(Roles = "Admin")]</c> here would
/// have replaced the default clinic policy instead of adding to it.
/// </summary>
[ApiController]
[Authorize(Policy = TenantClaimTypes.ClinicAdminPolicy)]
[Route("api/users")]
public class UsersController(
    AuthService auth,
    IDbContextFactory<AppDbContext> factory,
    ICurrentTenantContext tenant,
    ICurrentUserContext currentUser,
    ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ClinicUser>>> List()
    {
        var me = currentUser.UserId;

        return Ok((await auth.GetUsersAsync())
            .Select(u => new ClinicUser(
                u.Id, u.Username, u.DisplayName, u.Role, u.IsActive,
                u.MustChangePassword, u.LastLoginOn, u.Id == me))
            .ToList());
    }

    [HttpPost]
    public async Task<ActionResult<TemporaryPasswordResponse>> Save(SaveClinicUserRequest request)
    {
        if (!UserName.TryNormaliseLocalPart(request.LocalPart, out var localPart, out var error))
            return BadRequest(error);

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest("Enter the person's name, so the rest of the clinic knows who this is.");

        await using var db = await factory.CreateDbContextAsync();

        // The clinic half comes from the tenant this request is scoped to,
        // never from the request body.
        var slug = (await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenant.TenantId))?.Slug;
        if (slug is null) return BadRequest("This clinic could not be resolved.");

        var isNew = request.Id is null;
        var existing = isNew ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id);

        if (!isNew && existing is null) return NotFound("No such user.");

        // The two ways an Admin can lock their own clinic out of its own
        // records. Refused rather than warned about: there is no other Admin
        // to undo it afterwards, and the only route back would be a support
        // call to EnterpriseAdmin.
        if (existing is not null && existing.Id == currentUser.UserId)
        {
            if (request.Role != UserRole.Admin)
                return BadRequest("You cannot remove your own Admin role — no one would be left to manage staff. " +
                                  "Make somebody else an Admin first.");

            if (!request.IsActive)
                return BadRequest("You cannot deactivate your own account.");
        }

        // A password is required for a new user and generated when not
        // supplied, so an Admin is never quietly allowed to create an account
        // with no way in.
        var password = request.Password;
        var generated = false;

        if (string.IsNullOrWhiteSpace(password))
        {
            if (isNew) { password = GenerateTemporaryPassword(); generated = true; }
        }
        else if (password.Length < 8)
        {
            return BadRequest("A password must be at least 8 characters.");
        }

        var user = existing ?? new User();
        user.Username = UserName.For(localPart, slug);
        user.DisplayName = request.DisplayName.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive;

        try
        {
            // SaveUserAsync owns the rules that matter — platform-wide
            // username uniqueness, the reserved EnterpriseAdmin name, and
            // forcing a change on any password set here.
            await auth.SaveUserAsync(user, password);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        logger.LogInformation(
            "{Action} clinic user {Username} ({Role}).",
            isNew ? "Created" : "Updated", user.Username, user.Role);

        // Returned only when this endpoint invented it. A password the Admin
        // typed is one they already know, and echoing it back would put it
        // in a response body for no reason.
        return Ok(generated || !string.IsNullOrWhiteSpace(request.Password)
            ? new TemporaryPasswordResponse(user.Username, password!)
            : new TemporaryPasswordResponse(user.Username, string.Empty));
    }

    /// <summary>
    /// Issues a new temporary password for a member of staff who is locked
    /// out — the everyday version of what platform support does, kept inside
    /// the clinic so a forgotten password is not a support call.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<ActionResult<TemporaryPasswordResponse>> ResetPassword(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user is null) return NotFound("No such user.");

        var temporary = GenerateTemporaryPassword();

        try
        {
            await auth.SaveUserAsync(user, temporary);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        logger.LogWarning("Admin reset the password for {Username}.", user.Username);
        return Ok(new TemporaryPasswordResponse(user.Username, temporary));
    }

    /// <summary>
    /// A temporary password that survives being read aloud across a desk. No
    /// 0/O, 1/l/I, 5/S — the characters people mishear are the ones that turn
    /// one handover into three. Same generator the support console uses.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKMNPQRTUVWXYZabcdefghijkmnpqrtuvwxyz23456789";
        var chars = RandomNumberGenerator.GetString(alphabet, 12);
        return $"{chars[..4]}-{chars[4..8]}-{chars[8..]}";
    }
}
