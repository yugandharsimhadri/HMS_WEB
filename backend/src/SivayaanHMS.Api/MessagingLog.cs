using System.Diagnostics;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Messaging;

namespace SivayaanHMS.Api;

/// <summary>
/// The log files, and the block written at every start that says how this
/// server sends password-reset codes and welcome messages.
///
/// Two files, both written outside Development:
///
///   * the main log (Logging:File:PathFormat), only when configured, at the
///     levels in Logging:File:LogLevel;
///   * the messages log (messages-{Date}.log): every password-reset request,
///     every call to SivaCom or WhatsApp and what came back, plus errors from
///     anywhere — and nothing else. Its levels are fixed, so turning the main
///     log down never hides why somebody did not get a code. It goes to
///     Logging:Messages:PathFormat, else beside the main log, else into a
///     logs folder beside the application.
///
/// Under IIS the application pool's identity must be allowed to write to
/// that folder (docs/PASSWORD_RESET.md, "Logs"); without it nothing is
/// written and nothing fails.
///
/// No code, password, grant or secret is ever written — only whether each
/// setting is filled in, and phone numbers masked to their last four digits.
/// </summary>
public static class MessagingLog
{
    /// <summary>Categories whose Information lines belong in the messages log.</summary>
    private static readonly string[] Categories =
    [
        "SivayaanHMS.Data.Messaging",
        "SivayaanHMS.Data.PasswordResetFlow",
        "SivayaanHMS.Api.Controllers.AuthController",
        "SivayaanHMS.Messaging",
    ];

    public static string? MessagesLogPath(IConfiguration config, IHostEnvironment env)
    {
        var explicitPath = config["Logging:Messages:PathFormat"];
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;
        if (env.IsDevelopment()) return null; // the console shows everything

        var main = config["Logging:File:PathFormat"];
        var folder = string.IsNullOrWhiteSpace(main) ? Path.Combine(env.ContentRootPath, "logs") : Path.GetDirectoryName(main);
        return Path.Combine(folder ?? "logs", "messages-{Date}.log");
    }

    public static void AddFiles(WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        if (!string.IsNullOrWhiteSpace(config["Logging:File:PathFormat"]))
            builder.Logging.AddFile(config.GetSection("Logging:File"));

        var path = MessagesLogPath(config, builder.Environment);
        if (path is null) return;

        builder.Logging.AddFile(path,
            minimumLevel: LogLevel.Error,
            levelOverrides: Categories.ToDictionary(c => c, _ => LogLevel.Information),
            retainedFileCountLimit: config.GetValue<int?>("Logging:File:RetainedFileCountLimit") ?? 31);
    }

    public static void WriteStartup(WebApplication app, SivaComOptions sivaCom, WhatsAppOptions whatsApp)
    {
        var config = app.Configuration;
        var env = app.Environment;
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SivayaanHMS.Messaging");
        var reset = config.GetSection(PasswordResetOptions.SectionName).Get<PasswordResetOptions>() ?? new PasswordResetOptions();

        var files = new[] { "appsettings.json", $"appsettings.{env.EnvironmentName}.json", "appsettings.Local.json" }
            .Select(name => Path.Combine(env.ContentRootPath, name))
            .Select(p => File.Exists(p) ? $"{Path.GetFileName(p)} (saved {File.GetLastWriteTime(p):yyyy-MM-dd HH:mm:ss})" : $"{Path.GetFileName(p)} (not found)");

        string codes, welcome;
        if (sivaCom.Enabled)
        {
            codes = $"through SivaCom at {sivaCom.BaseUrl} as client {sivaCom.ClientId}, key {sivaCom.KeyId} " +
                    $"(secret {(string.IsNullOrWhiteSpace(sivaCom.Secret) ? "EMPTY" : "set")}, timeout {sivaCom.TimeoutSeconds} s)";
            welcome = "through SivaCom";
        }
        else if (whatsApp.IsConfigured)
        {
            var id = whatsApp.PhoneNumberId is { Length: > 4 } p ? "..." + p[^4..] : "?";
            codes = $"by this server, straight to WhatsApp (phone number id {id}, template {whatsApp.ResetCodeTemplate})";
            welcome = $"straight to WhatsApp (template {whatsApp.WelcomeTemplate})";
        }
        else
        {
            codes = env.IsDevelopment()
                ? "written to this log only (Development: no WhatsApp section)"
                : "NOT SENT - no SivaCom, no WhatsApp section: codes are only written to the log";
            welcome = "not sent (log only)";
        }

        log.LogInformation(
            "HMS messaging at startup\n" +
            "  Environment    : {Environment:l}, process started {Started:yyyy-MM-dd HH:mm:ss}\n" +
            "  Config files   : {Files:l}\n" +
            "  Forgot password: {Reset:l}\n" +
            "  Reset codes    : {Codes:l}\n" +
            "  Welcome        : {Welcome:l}\n" +
            "  Messages log   : {MessagesLog:l}",
            env.EnvironmentName, Process.GetCurrentProcess().StartTime, string.Join(", ", files),
            reset.Enabled ? "offered" : "OFF (PasswordReset:Enabled = false): users ask their clinic admin",
            reset.Enabled ? codes : "-", welcome,
            MessagesLogPath(config, env) ?? "(none: console only)");
    }
}
