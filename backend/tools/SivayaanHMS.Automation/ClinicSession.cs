using Microsoft.Playwright;
using SivayaanHMS.Automation.Workflows;

namespace SivayaanHMS.Automation;

/// <summary>
/// One browser session against the SivayaanHMS client: owns Playwright, the browser, the page,
/// and knows how to sign in. Every UAT test builds its <see cref="WorkflowContext"/> from an
/// instance of this — the counterpart to TransTruckSession in TransTrack.Automation, trimmed of
/// the parts that exist there only to serve a video recorder this project does not have: no
/// viewport switching (SivayaanHMS has one desktop-first layout, not a distinct mobile one), no
/// narrator overlay, no capture-size bookkeeping.
/// </summary>
public sealed class ClinicSession : IAsyncDisposable
{
    /// <summary>
    /// Fixed rather than left to the OS default so a screenshot taken on any machine crops the
    /// same content — wide enough that the nav rail and a report table are both on screen at once
    /// without the horizontal scroll the wide-table fix (index.css §8) exists to handle.
    /// </summary>
    private const int ViewportWidth = 1600;
    private const int ViewportHeight = 900;

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;

    private ClinicSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page, AutomationOptions options)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        Page = page;
        Options = options;
    }

    public IPage Page { get; }

    public AutomationOptions Options { get; }

    /// <summary>
    /// Launches headless Chromium against <see cref="AutomationOptions.BaseUrl"/>. The servers are
    /// expected to be up already — the UAT fixture starts them once for the whole run rather than
    /// once per session, exactly as TransTrack.Automation's fixture does.
    /// </summary>
    public static async Task<ClinicSession> StartAsync(AutomationOptions options, Action<string>? log = null)
    {
        IPlaywright? playwright = null;
        IBrowser? browser = null;

        try
        {
            playwright = await Playwright.CreateAsync();

            browser = await BrowserProvisioning.LaunchChromiumAsync(
                playwright,
                new BrowserTypeLaunchOptions { Headless = true },
                log);

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = ViewportWidth, Height = ViewportHeight },
                BaseURL = options.BaseUrl,
                Locale = "en-IN",
                // Printed documents and on-screen dates are formatted for the viewer's zone; a
                // fixed zone keeps an assertion on a date string the same on any machine this runs on.
                TimezoneId = "Asia/Kolkata",
            });

            var page = await context.NewPageAsync();

            // Playwright's own default is 5s. Every server-generated report on this app is a real
            // round trip — click, fetch, render — on a machine that in practice also has SQL
            // Server, the API and several other processes competing for the same CPU during a
            // test run. 5s proved too tight for that combination even when the data and the
            // server were both already confirmed correct; 15s is generous without hiding a
            // genuinely broken screen, which would still time out well before it.
            page.SetDefaultTimeout(15_000);

            log?.Invoke($"Browser ready — viewport {ViewportWidth}x{ViewportHeight}");

            return new ClinicSession(playwright, browser, context, page, options);
        }
        catch
        {
            if (browser is not null) await browser.CloseAsync();
            playwright?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Signs in through the real form — never by injecting a token — so the credential path and
    /// the post-login redirect are exercised by every scenario that needs an authenticated
    /// screen. A normal sign-in lands on <c>/</c> (the OPD Queue), not on Dashboard — the app
    /// picks Dashboard only as a place a workflow chooses to navigate to, same as any other screen.
    /// </summary>
    public async Task LoginAsync(string? username = null, string? password = null)
    {
        await Page.GotoAsync("/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var usernameField = Page.GetByPlaceholder("you@your-clinic");
        await Assertions.Expect(usernameField).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await usernameField.FillAsync(username ?? DemoData.AdminUsername);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(password ?? DemoData.AdminPassword);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // Asserted on the shell's own nav landmark, not on a navigation event: signing in is a
        // client-side route change, and WaitForURLAsync would wait for a `load` a single-page app
        // never fires after the initial one.
        await Assertions.Expect(Page.GetByRole(AriaRole.Navigation).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>Builds the context handed to a workflow.</summary>
    public WorkflowContext CreateWorkflowContext() => new(Page, Options);

    /// <summary>
    /// Saves a screenshot under <c>artifacts/uat</c>, used for recording what the page looked like
    /// when a scenario failed.
    /// </summary>
    public async Task<string> CaptureScreenshotAsync(string name)
    {
        Directory.CreateDirectory(RepoPaths.ArtifactsDir);

        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(RepoPaths.ArtifactsDir, $"{safe}.png");

        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        try { await _context.CloseAsync(); } catch { /* already closing */ }
        try { await _browser.CloseAsync(); } catch { /* already closing */ }
        _playwright.Dispose();
    }
}
