using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Database ─────────────────────────────────────────────────────────────
// AddDbContextFactory rather than AddDbContext: every domain service takes
// IDbContextFactory<AppDbContext> and opens a short-lived context per call
// (the same pattern the desktop's services already used with
// IDbContextFactory), not one context per request.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

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

builder.Services.AddAuthorization();

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

// Dev convenience: apply pending migrations at startup rather than
// requiring a separate `dotnet ef database update` step. Revisit before
// this runs as more than one instance — a real deployment applies
// migrations as its own release step, not a race between app instances.
if (app.Environment.IsDevelopment())
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

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
