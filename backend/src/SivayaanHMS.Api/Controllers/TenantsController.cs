using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// What the signup form collects.
///
/// <paramref name="ClinicName"/> is the full name and is free to duplicate —
/// two unrelated "City Hospital"s in different towns is ordinary, and
/// turning the second one away would be absurd. <paramref name="ClinicCode"/>
/// is the short handle ("twinkle" for Twinkle Children's Hospital) that goes
/// inside every username at the clinic, and that is the one that must be
/// unique across the platform.
///
/// The password is confirmed in the browser rather than sent twice — a
/// mistyped confirmation is a typing mistake to catch before the request
/// leaves, and sending the same secret twice only widens where it can leak.
/// </summary>
public record RegisterTenantRequest(string ClinicName, string ClinicCode, string Username, string Password, string? Phone);

public record RegisterTenantResponse(Guid TenantId, string Slug, string AdminUsername);

/// <summary>
/// Clinic signup — the SaaS entry point the desktop never had. A new
/// clinic is usable within seconds: provisioning runs every master-data
/// seeder plus the starter catalogue, exactly as Settings → Features
/// already worked on the desktop, just triggered by registration instead
/// of first launch. See TenantProvisioner and SAAS_MIGRATION.md's own
/// framing of the seeders as tenant provisioning.
///
/// Two names are asked for, and they do different jobs. The full name is
/// what appears on printed prescriptions and bills, and may duplicate freely
/// — unrelated clinics share names all the time. The clinic code is the
/// short handle that goes inside every username here ("admin@twinkle"), and
/// it is the one thing at signup that must be unique platform-wide.
///
/// Deriving the code from the full name was the obvious shortcut and is
/// wrong: it would refuse the second "City Hospital" on the platform for no
/// reason a customer could accept. Asking for it directly also lets a clinic
/// pick something short enough to type every morning, which a full hospital
/// name never is.
///
/// Whoever registers is the clinic's Admin, its owner. Everyone else is
/// added later from Settings, by them.
/// </summary>
[ApiController]
[Route("api/tenants")]
public partial class TenantsController(
    DbContextOptions<AppDbContext> dbOptions,
    ILoggerFactory loggerFactory,
    SivayaanHMS.Data.Messaging.IMessageSender messages) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<RegisterTenantResponse>> Register(RegisterTenantRequest request)
    {
        var clinicName = (request.ClinicName ?? string.Empty).Trim();
        if (clinicName.Length < 3)
            return BadRequest("Enter the clinic's name.");

        if (!UserName.TryNormaliseLocalPart(request.Username, out var localPart, out var usernameError))
            return BadRequest(usernameError);

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest("Choose a password of at least 8 characters.");

        // Required at signup, unlike on staff accounts: this is the one
        // account that cannot be recovered by asking an admin, because it IS
        // the admin. Without a number on it, a forgotten password means
        // phoning support.
        //
        // Counted in digits so spaces, hyphens and a +91 all pass, and no
        // country is assumed — see User.Phone.
        var phone = (request.Phone ?? string.Empty).Trim();
        var phoneDigits = phone.Count(char.IsDigit);
        if (phoneDigits < 8 || phoneDigits > 15)
            return BadRequest("Enter the mobile number that should receive password-reset codes.");

        var slug = NormaliseSlug(request.ClinicCode ?? string.Empty);
        if (slug.Length < 3)
            return BadRequest("Choose a clinic code of at least 3 letters or numbers — a short version of " +
                              "the clinic's name, like 'twinkle'.");

        // Slug uniqueness is checked against the Tenants table, which is
        // deliberately not tenant-filtered — any pinned context can read
        // it, since it is what tenant-scoping is scoped against.
        var tenantId = Guid.NewGuid();
        await using var db = new TenantScopedDbContextFactory(dbOptions, tenantId).CreateDbContext();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
        {
            // Deliberately not auto-suffixed to "-2". The signup form has
            // already told this person, in so many words, that they will
            // sign in as "them@code"; quietly handing them "them@code-2"
            // instead means the first thing the product does is change their
            // answer without mentioning it. Note this is the *only*
            // uniqueness rule at signup — the clinic's full name above may
            // duplicate freely.
            return Conflict($"The clinic code '{slug}' is already taken. Try adding your town or a " +
                            $"distinguishing word — '{slug}-north', say.");
        }

        var username = UserName.For(localPart, slug);

        // IgnoreQueryFilters: usernames are unique platform-wide, not per
        // clinic, so this has to see past the (brand-new, empty) tenant.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username))
            return Conflict($"'{username}' is already taken. Choose a different username.");

        db.Tenants.Add(new Tenant { Id = tenantId, Slug = slug, ClinicName = clinicName });
        await db.SaveChangesAsync();

        var logger = loggerFactory.CreateLogger("TenantProvisioning");
        var admin = await TenantProvisioner.ProvisionAsync(db, slug, logger, localPart);

        // The seeder gives every clinic the same starter password (see
        // UserSeeder) — reset it to the one the signer-up actually chose
        // before handing the account over.
        var (hash, salt) = PasswordHasher.Hash(request.Password);
        admin.PasswordHash = hash;
        admin.PasswordSalt = salt;

        // The owner chose this password themselves a moment ago; forcing an
        // immediate change teaches only that the product is not listening.
        // MustChangePassword stays on for the temporary passwords
        // EnterpriseAdmin issues, which is what it is actually for.
        admin.MustChangePassword = false;
        admin.Phone = phone;
        await db.SaveChangesAsync();

        // Welcome message. Deliberately carries the clinic name and the
        // username and *not* the password: they chose that password thirty
        // seconds ago, so repeating it teaches them nothing and leaves their
        // credentials sitting in a chat history, a phone backup and the
        // provider's logs. A forgotten one is handled by the reset code
        // flow, which is safe to send.
        var welcome = $"Welcome to Sivayaan HMS, {clinicName}. " +
                      $"Your admin username is {admin.Username} — sign in with the password you chose. " +
                      "Password reset codes will come to this number.";

        try
        {
            await messages.SendAsync(phone, SivayaanHMS.Data.Messaging.MessagePurpose.Welcome, welcome);
        }
        catch (Exception ex)
        {
            // The clinic exists and the account works; a failed welcome text
            // is not a reason to fail the signup they just completed.
            logger.LogError(ex, "Clinic {Slug} registered but the welcome message could not be sent.", slug);
        }

        return Ok(new RegisterTenantResponse(tenantId, slug, admin.Username));
    }

    /// <summary>
    /// Tidies the clinic code into the form that can live in a username and a
    /// URL. Someone typing "Twinkle" gets "twinkle"; someone typing "St
    /// Mary's" gets "st-marys", not "st-mary-s", because the second is what a
    /// person would get wrong reading it back over a phone.
    ///
    /// Forgiving rather than rejecting: a signup form is the wrong place to
    /// argue about capitals and apostrophes when the intent is obvious. What
    /// it settles on is shown back before they commit.
    /// </summary>
    private static string NormaliseSlug(string raw)
    {
        var lowered = Apostrophes().Replace(raw.Trim().ToLowerInvariant(), "");
        var slug = NonSlugCharacters().Replace(lowered, "-").Trim('-');
        slug = CollapseHyphens().Replace(slug, "-");

        // Capped short on purpose. This is about to live inside every
        // username at the clinic and be typed by hand every morning, so
        // "twinkle" is the goal and a pasted full hospital name is not.
        return slug.Length <= 24 ? slug : slug[..24].TrimEnd('-');
    }

    [GeneratedRegex("['’\"]")]
    private static partial Regex Apostrophes();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex("-{2,}")]
    private static partial Regex CollapseHyphens();
}
