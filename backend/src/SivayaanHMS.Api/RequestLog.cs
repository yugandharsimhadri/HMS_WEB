using System.Diagnostics;

namespace SivayaanHMS.Api;

/// <summary>
/// One line per API request in the main log - who did what, and how it went:
///
///   POST /api/inpatient/admissions/3f2a…/final-bill -> 200 in 84 ms by admin@srilakshmi
///
/// That is the trail a clinic asks for when a bill or a discount is questioned, and the first
/// thing to read when a screen "did nothing". Switched by Logging:Requests:Enabled (on by
/// default); off, the lines stop but a failure is still logged.
///
/// Deliberately thin. Only the method, the path, the status, the time and the username are
/// written - never the query string or the body. Searches carry patient names and phone numbers
/// in the query, and sign-in carries a password in the body.
///
/// A request that throws is always logged, flag or not, with the path and the user beside the
/// exception - Kestrel's own report of it names neither.
/// </summary>
public static class RequestLog
{
    public const string Category = "SivayaanHMS.Requests";

    public static bool Enabled(IConfiguration config) => config.GetValue("Logging:Requests:Enabled", true);

    public static IApplicationBuilder UseRequestLog(this IApplicationBuilder app)
    {
        var log = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        var enabled = Enabled(app.ApplicationServices.GetRequiredService<IConfiguration>());

        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            var path = request.Path.Value ?? "";

            // Preflights and health probes are machinery, not anybody's work.
            if (HttpMethods.IsOptions(request.Method) || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "{Method:l} {Path:l} failed after {Elapsed} ms for {User:l}",
                    request.Method, path, Elapsed(started), Who(context));
                throw;
            }

            if (!enabled) return;

            var status = context.Response.StatusCode;
            log.Log(status >= 500 ? LogLevel.Error : LogLevel.Information,
                "{Method:l} {Path:l} -> {Status} in {Elapsed} ms by {User:l}",
                request.Method, path, status, Elapsed(started), Who(context));
        });
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string Who(HttpContext context) => context.User.Identity?.Name is { Length: > 0 } name ? name : "-";
}
