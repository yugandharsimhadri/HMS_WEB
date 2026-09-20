using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

var builder = WebApplication.CreateBuilder(args);

// Loaded last so it overrides appsettings.json, and git-ignored so it never
// leaves the machine. This is where a developer's own database password and
// the platform-support password live — see docs/POSTGRESQL_SETUP.md and
// docs/PLATFORM_ADMIN.md for the few lines to put in it. Optional in the
// sense that the app starts without it as long as the database password
// arrives some other way (Database__Password); EnterpriseAdmin is then
// simply unable to sign in, which is the correct failure for a credential
// nobody set.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ── Database ─────────────────────────────────────────────────────────────
// Everything about the database comes from the Database section of
// appsettings — host, port, name, role, password, SSL, pool sizes, timeouts
// — bound once here into DatabaseOptions, which is the only place a
// connection string is ever assembled. See docs/POSTGRESQL_SETUP.md.
//
// Validated up front, so a missing password fails at startup with a message
// that names the key, rather than at the first request with an
// authentication error that names nothing.
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));

var database = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
    ?? throw new InvalidOperationException("The Database section is not configured.");

if (string.IsNullOrWhiteSpace(database.ConnectionString) && string.IsNullOrWhiteSpace(database.Password))
    throw new InvalidOperationException(
        "Database:Password is not configured. Put it in appsettings.Local.json (development), " +
        "appsettings.Production.json (server), or the Database__Password environment variable.");

var connectionString = database.BuildConnectionString();

// AddDbContextFactory rather than AddDbContext: every domain service takes
// IDbContextFactory<AppDbContext> and opens a short-lived context per call
// (the same pattern the desktop's services already used with
// IDbContextFactory), not one context per request.
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

// Deliberately no EnableRetryOnFailure() yet.
//
// It is the standard advice for a networked database, and turning it on
// today would break nine call sites at *runtime* rather than at compile
// time: EF Core refuses to let user code open its own transaction under a
// retrying execution strategy, because it cannot safely replay a
// transaction it did not begin. PharmacyService, DiagnosticsService,
// PathologyLabService, ProcedureBillsService and DataHealthService all do
// exactly that.
//
// Enabling it is a deliberate piece of work — wrap each of those bodies in
// CreateExecutionStrategy().ExecuteAsync(...) and re-examine anything inside
// them that is not safe to run twice. Until then a transient network fault
// surfaces as an error rather than a retry, which against a local instance
// is the right trade.

// ── Tenancy and identity ────────────────────────────────────────────────
// HttpCurrentTenantContext/HttpCurrentUserContext read the validated JWT
// per request; AppDbContext's constructor picks them up automatically
// through IDbContextFactory<AppDbContext> above.
//
// Singleton, not Scoped: AddDbContextFactory resolves AppDbContext's
// constructor arguments from the root provider (that's what lets the
// factory itself be a long-lived singleton), and the root provider refuses
// to hand out a Scoped service - ASP.NET Core throws at the first call
// rather than risk one request's scoped instance leaking into another's
// context. That's safe here specifically because neither class holds any
// per-request state itself - both just read IHttpContextAccessor.HttpContext
// fresh on every property access, and that accessor is itself safe to read
// from anywhere via AsyncLocal.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentTenantContext, HttpCurrentTenantContext>();
builder.Services.AddSingleton<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddSingleton<IClock, SystemClock>();

// ── Domain services ──────────────────────────────────────────────────────
// Every one of these is the same class the desktop's Pharma.App used,
// unchanged in shape - only the collaborators (IDbContextFactory, IClock,
// ILogger) differ in where they come from.
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OpdService>();
builder.Services.AddScoped<PharmacyService>();
builder.Services.AddScoped<AppointmentsService>();
builder.Services.AddScoped<PediatricsService>();
builder.Services.AddScoped<DentistService>();
builder.Services.AddScoped<DiagnosticsService>();
builder.Services.AddScoped<PathologyLabService>();
builder.Services.AddScoped<ProcedureBillsService>();
builder.Services.AddScoped<DataHealthService>();
builder.Services.AddScoped<SivayaanHMS.Data.Import.PurchaseImportService>();

// ── Auth ─────────────────────────────────────────────────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

// A clinic's whole patient record sits behind this key. The development
// default in appsettings.json is deliberately obvious filler text so a
// forgotten override is loud, not a quiet vulnerability discovered later.
if (!builder.Environment.IsDevelopment() && jwt.Key.Contains("REPLACE-BEFORE-PRODUCTION"))
    throw new InvalidOperationException(
        "Jwt:Key is still the development placeholder. Set a real signing key via environment " +
        "variable or secret store before running outside Development.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

// ── Platform support identity ────────────────────────────────────────────
// EnterpriseAdmin belongs to no clinic. Its password comes from config, not
// source: the desktop could compile it in because the binary sat on one
// clinic's PC, but on a server it is a single key to every clinic on the
// estate, and source is the one place a secret is certain to be cloned,
// pushed and kept forever in history.
builder.Services.Configure<PlatformAdminOptions>(
    builder.Configuration.GetSection(PlatformAdminOptions.SectionName));
builder.Services.AddSingleton<PlatformAdminService>();

var platformAdmin = builder.Configuration.GetSection(PlatformAdminOptions.SectionName)
    .Get<PlatformAdminOptions>() ?? new PlatformAdminOptions();

if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(platformAdmin.Password))
    throw new InvalidOperationException(
        "PlatformAdmin:Password is not configured. This account can reset any clinic admin's password " +
        "on the platform — set one via the PlatformAdmin__Password environment variable or a secret " +
        "store before running outside Development.");

// ── Authorization ────────────────────────────────────────────────────────
// Two audiences that must never overlap. The clinic policy is the *default*,
// so a bare [Authorize] anywhere in this API means "a signed-in user of some
// clinic" — and a support token, which carries no tenant claim, fails it.
// That keeps EnterpriseAdmin out of patient data by construction, rather
// than by every controller author remembering to say so.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(TenantClaimTypes.ClinicPolicy, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(TenantClaimTypes.TenantId));

    // Note the tenant claim is required here too, not just the role — see
    // ClinicAdminPolicy's own comment for why that matters.
    options.AddPolicy(TenantClaimTypes.ClinicAdminPolicy, policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim(TenantClaimTypes.TenantId)
              .RequireRole(nameof(UserRole.Admin)));

    options.AddPolicy(TenantClaimTypes.PlatformAdminPolicy, policy =>
        policy.RequireAuthenticatedUser().RequireRole(TenantClaimTypes.PlatformAdminRole));

    options.DefaultPolicy = options.GetPolicy(TenantClaimTypes.ClinicPolicy)!;
});

// Enums as strings ("Male", not 1) - a JSON API read by a TypeScript
// frontend should never make someone go look up what 1 means in a C# enum.
//
// IgnoreCycles: this schema has real bidirectional navigations (Visit.Patient
// <-> Patient.Visits, DentalCase.Sittings/Payments, LabOrder.Reports.Results,
// and more) - ported straight off the desktop, where nothing ever serialized
// the object graph, so nothing ever surfaced this. The first controller that
// returns a Visit with its Patient Included threw "possible object cycle
// detected" here in real browser testing, not a hunch - this guards every
// future endpoint against the same class of bug rather than patching one
// controller.
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOpenApi();

// ── CORS ─────────────────────────────────────────────────────────────────
// The frontend is a separate origin (its own dev server today, its own
// deployed domain later) - without this every request the browser makes
// is blocked before it reaches a controller at all. Origins come from
// config, not a wildcard: a bearer-token API allowing "*" would let any
// page on the internet read a signed-in clinic's data via a visitor's
// browser.
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Apply pending migrations at startup — on by default in Development so a
// fresh checkout just works, off everywhere else because a real deployment
// applies migrations as its own release step (deploy/Migrate-Database.ps1),
// not as a race between app instances. Database:MigrateOnStartup overrides
// either way; MigrateAsync also creates the database itself when the role
// is allowed to, which is what the UAT harness relies on.
if (database.MigrateOnStartup ?? app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Behind a tunnel or reverse proxy the request reaches Kestrel as plain HTTP
// from the loopback address, and the browser's original scheme survives only
// in X-Forwarded-Proto. Without this the app believes every request arrived
// over http://, which matters in two ways: UseHttpsRedirection below starts
// issuing redirects the moment an HTTPS port is configured, and a redirected
// CORS preflight reaches the browser as an opaque "CORS error" rather than
// anything that names the redirect — an afternoon lost to the wrong problem.
//
// Only the loopback proxies are trusted, and the proxy runs on this machine.
// Trusting these headers from anywhere else would let a caller claim any
// scheme and any client IP simply by setting them.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
};
forwarded.KnownProxies.Add(IPAddress.Loopback);
forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
app.UseForwardedHeaders(forwarded);

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
