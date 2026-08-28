using Microsoft.Playwright;

namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// Everything a workflow needs to drive the app, plus the navigation and verification helpers
/// every workflow repeats. Verification uses Playwright's own web-first assertions, which retry
/// until the timeout, so a step reads the same whether the page responds in ten milliseconds or
/// two seconds.
///
/// This is TransTrack.Automation's <c>WorkflowContext</c> with the mobile-vs-desktop branch
/// removed from every helper: SivayaanHMS has one nav rail, present at every screen width, so
/// there is exactly one way to reach a screen rather than two.
/// </summary>
public sealed class WorkflowContext(IPage page, AutomationOptions options)
{
    private readonly List<string> _steps = [];

    public IPage Page { get; } = page;

    public AutomationOptions Options { get; } = options;

    /// <summary>The narration beats of the workflow run so far, in order. Read by <see cref="WorkflowRunner"/> to build a failure message that names the business step that broke, not just a locator.</summary>
    public IReadOnlyList<string> Steps => _steps;

    /// <summary>Records a step, then runs it.</summary>
    public async Task StepAsync(string narration, Func<Task> action)
    {
        _steps.Add(narration);
        await action();
    }

    /// <summary>
    /// How long <see cref="ExpectVisibleAsync"/> and <see cref="ExpectHeadingAsync"/> wait.
    ///
    /// Not Playwright's own 5s default. <see cref="Page.SetDefaultTimeout"/>, set once when the
    /// session's page is created, governs actions — click, fill — but not this static assertion
    /// API, which keeps its own default regardless. Every server-generated report on this app is
    /// a real round trip, and this suite was built and is normally run on a machine that also has
    /// SQL Server, the API and several other processes competing for the same CPU — 5s, then 15s,
    /// both proved too tight often enough to notice, on a scenario (the Reports workflow) whose
    /// data and endpoint were independently confirmed correct by hand each time it flaked. That
    /// history is the actual justification for a number this generous: it costs nothing when the
    /// machine is genuinely idle, and a screen that is truly broken still fails well within it.
    /// </summary>
    private static readonly LocatorAssertionsToBeVisibleOptions VisibleOptions = new() { Timeout = 25_000 };

    /// <summary>Playwright's retrying assertion for a locator.</summary>
    public static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    /// <summary>Playwright's retrying assertion for the page (URL, title).</summary>
    public static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    /// <summary>
    /// Asserts a piece of text is on screen, filtered to the visible match first. Several screens
    /// in this app keep two renderings of the same data in the DOM at once behind a state
    /// toggle — a selected patient's history versus the empty-state prompt, a tab's contents
    /// versus another tab's — and a plain <c>First()</c> can bind to one that exists but is not
    /// currently shown, then wait out its timeout for a visibility that will never come.
    /// </summary>
    public Task ExpectVisibleAsync(string text)
        => Expect(Visible(Page.GetByText(text))).ToBeVisibleAsync(VisibleOptions);

    /// <summary>The visible one of a set of matches — see <see cref="ExpectVisibleAsync"/> for why this matters.</summary>
    public static ILocator Visible(ILocator locator)
        => locator.Filter(new LocatorFilterOptions { Visible = true }).First;

    /// <summary>
    /// The currently open modal — found empirically, not assumed. Every dialog and editor in this
    /// app shares the same wrapper class, and the page behind it is never unmounted while it is
    /// open, only visually covered. CSS visibility does not know about z-order, so an unscoped
    /// role-based lookup for a control that also exists on the page underneath — a
    /// filter dropdown, a session selector — can bind to the wrong one even though it is
    /// genuinely invisible to a person looking at the screen. Scoping to this locator first is
    /// what <see cref="ExpectVisibleAsync"/>'s visibility filter cannot do on its own.
    /// </summary>
    public ILocator Dialog => Visible(Page.Locator(".overlay"));

    /// <summary>The visible link with this name.</summary>
    public ILocator Link(string name)
        => Visible(Page.GetByRole(AriaRole.Link, new() { Name = name }));

    /// <summary>The visible button whose accessible name contains this text (not exact — several buttons carry a trailing keyboard-shortcut badge, e.g. "Book visit F2").</summary>
    public ILocator Button(string name, bool exact = false)
        => Visible(Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = exact }));

    /// <summary>Asserts the page heading, which is how every screen in this product announces itself.</summary>
    public Task ExpectHeadingAsync(string heading)
        => Expect(Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).First).ToBeVisibleAsync(VisibleOptions);

    /// <summary>
    /// Moves to one of the product's screens by clicking the nav rail, never by typing a URL — a
    /// navigation that only works when driven from the address bar is not a navigation a user
    /// has. Scoped to the navigation landmark: several labels ("Reports", "Settings") are also
    /// button and heading text on the pages themselves, and an unscoped lookup goes strict-mode
    /// ambiguous the moment a page links onward to a screen the nav also lists.
    /// </summary>
    public async Task NavigateAsync(string label, string expectedHeading)
    {
        await Visible(Page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = label, Exact = true })).ClickAsync();

        await ExpectHeadingAsync(expectedHeading);
    }

    /// <summary>A short, unlabelled pause — used sparingly, only where there is genuinely nothing to wait on a locator for (a chart finishing its layout, say).</summary>
    public Task BeatAsync(int milliseconds = 300) => Page.WaitForTimeoutAsync(milliseconds);
}
