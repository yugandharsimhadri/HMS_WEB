using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// Signing up with a clinic code the seed data already took. The full clinic name may duplicate
/// freely — two unrelated "City Hospital"s is ordinary — but the code goes inside every username
/// at the clinic, so it is the one thing at signup that must be unique platform-wide.
/// </summary>
public sealed class RegistrationDuplicateCodeWorkflow() : Workflow(
    key: "RegistrationDuplicateCode",
    displayName: "Registering With a Taken Clinic Code",
    module: "Security",
    businessPurpose: "Refuse a clinic code already in use, with a message that says why and offers a way past it — never silently hand out '<code>-2'.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Signing out reaches the registration form the same way any new clinic finds it.",
            async () =>
            {
                await c.Button("Sign out").ClickAsync();
                await WorkflowContext.Expect(c.Page).ToHaveURLAsync(new Regex(@"/login"));
                await c.Link("Register here").ClickAsync();
                await WorkflowContext.Expect(c.Page).ToHaveURLAsync(new Regex(@"/register"));
            });

        await c.StepAsync(
            "Filling in the form with the clinic code this run's own seed data already registered.",
            async () =>
            {
                // By placeholder for the two fields whose <label> also wraps a hint <span>: the
                // accessible name computed for a wrapping label concatenates all of its text
                // content, hint included, so GetByLabel("Clinic name", Exact: true) would not
                // match "Clinic name" alone. Password and Username carry no such hint and are
                // matched by label further down.
                //
                // Exact: true on "twinkle" specifically — Playwright's placeholder matching is a
                // case-insensitive substring by default, and "Twinkle Children's Hospital" (the
                // clinic-name field's own placeholder) contains "twinkle" too, so the un-exact
                // form resolves ambiguously to both fields.
                await c.Page.GetByPlaceholder("Twinkle Children's Hospital").FillAsync("A Second Test Clinic");
                await c.Page.GetByPlaceholder("twinkle", new() { Exact = true }).FillAsync(DemoData.ClinicCode);
                await c.Page.GetByPlaceholder("yourname").FillAsync("someoneelse");
                await c.Page.GetByLabel("Password", new() { Exact = true }).FillAsync("SecondClinic@1");
                await c.Page.GetByLabel("Retype password", new() { Exact = true }).FillAsync("SecondClinic@1");
            });

        await c.StepAsync(
            "The server refuses the code and names it, rather than silently registering a different one.",
            async () =>
            {
                await c.Button("Register clinic").ClickAsync();
                await c.ExpectVisibleAsync($"The clinic code '{DemoData.ClinicCode}' is already taken");
            });

        await c.StepAsync(
            "The form is still on screen with what was typed, so trying a different code costs one field, not the whole form.",
            () => WorkflowContext.Expect(c.Page.GetByPlaceholder("Twinkle Children's Hospital"))
                .ToHaveValueAsync("A Second Test Clinic"));
    }
}

/// <summary>
/// The refusal that stops a clinic locking itself out of its own product: every module switch on
/// one screen, and turning every one of them off at once.
/// </summary>
public sealed class ModuleToggleAllOffWorkflow() : Workflow(
    key: "ModuleToggleAllOff",
    displayName: "Refusing to Switch Off Every Module",
    module: "Settings",
    businessPurpose: "Never let a clinic save a state with nothing switched on — there would be no screen left to turn one back on from.")
{
    private static readonly string[] ToggleLabels =
        ["OPD", "Pharmacy", "Diagnostics", "Appointments", "Pediatrics", "Dentist", "Pathology Lab"];

    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Settings > Features lists every module as a switch.",
            async () =>
            {
                await c.NavigateAsync("Settings", "Settings");
                await c.Button("Features").ClickAsync();
                await c.ExpectVisibleAsync("Switching a module off hides it everywhere");
            });

        await c.StepAsync(
            "Switching every one of them off, including OPD and Pharmacy — both on by default for every existing clinic.",
            async () =>
            {
                foreach (var label in ToggleLabels)
                {
                    var checkbox = c.Page.Locator("label.module-toggle", new() { HasText = label })
                        .Locator("input[type=checkbox]");
                    if (await checkbox.IsCheckedAsync())
                        await checkbox.UncheckAsync();
                }
            });

        await c.StepAsync(
            "Saving is refused with the reason, not a generic failure.",
            async () =>
            {
                await c.Button("Save").ClickAsync();
                await c.ExpectVisibleAsync("At least one module");
                await c.ExpectVisibleAsync("has to stay switched on");
            });

        await c.StepAsync(
            "OPD is still in the nav — the refused save never reached the server's own state, so nothing a clinic actually depends on was touched.",
            async () =>
            {
                await c.Page.ReloadAsync();
                await WorkflowContext.Expect(c.Page.GetByRole(AriaRole.Navigation)
                    .GetByRole(AriaRole.Link, new() { Name = "OPD Queue", Exact = true }))
                    .ToBeVisibleAsync();
            });
    }
}

/// <summary>
/// What happens when the token this browser is holding stops being valid — the same failure a
/// clinic sees for real once a session outlasts the eight-hour token lifetime. Regression coverage
/// for a real bug: before this was fixed, an expired token left the browser believing it was still
/// signed in while every request behind it failed silently or with a per-screen error, which read
/// to the person at the desk as the system having lost their patients.
/// </summary>
public sealed class SessionExpiryWorkflow() : Workflow(
    key: "SessionExpiry",
    displayName: "A Session That Has Expired",
    module: "Security",
    businessPurpose: "Send someone back to sign in the moment their session is no longer valid, with a plain reason — never leave the screen looking signed in while nothing behind it works.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Starting from a screen that has to ask the server for something.",
            () => c.NavigateAsync("Patients", "Patients"));

        await c.StepAsync(
            "The stored token is corrupted, standing in for one that has genuinely expired — this browser now holds a session the server will not honour.",
            () => c.Page.EvaluateAsync("localStorage.setItem('sivayaanhms.token', 'not-a-real-token')"));

        await c.StepAsync(
            "The next screen that asks the server anything is bounced to sign-in, with a reason on screen and the stale session cleared from local storage.",
            async () =>
            {
                // Not c.NavigateAsync: that helper asserts arrival on the *target* screen, and
                // this click is deliberately never going to land on one — the 401 redirects to
                // /login before "Reports" the heading ever renders. Clicking the link directly,
                // then asserting on the redirect itself, is what this scenario actually needs.
                await WorkflowContext.Visible(c.Page.GetByRole(AriaRole.Navigation)
                    .GetByRole(AriaRole.Link, new() { Name = "Reports", Exact = true })).ClickAsync();

                await WorkflowContext.Expect(c.Page).ToHaveURLAsync(new Regex(@"/login\?expired=1"));
                await c.ExpectVisibleAsync("Your session ended");

                var stillHasToken = await c.Page.EvaluateAsync<bool>(
                    "localStorage.getItem('sivayaanhms.token') !== null");
                if (stillHasToken)
                    throw new InvalidOperationException("The invalid token was still in local storage after the redirect.");
            });
    }
}

/// <summary>
/// An address that matches nothing in the app, typed by hand or followed from a stale bookmark.
/// </summary>
public sealed class NotFoundRouteWorkflow() : Workflow(
    key: "NotFoundRoute",
    displayName: "An Address That Matches Nothing",
    module: "Navigation",
    businessPurpose: "Answer a stale link or a typed URL with a page that says so and a way back, rather than a blank screen indistinguishable from the application having crashed.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "A path nothing in the router recognises still gets a real page, not a blank one.",
            async () =>
            {
                await c.Page.GotoAsync("/this-page-does-not-exist");
                await c.ExpectVisibleAsync("Page not found");
            });

        await c.StepAsync(
            "Signed in, the way back offered is straight to the queue — not to sign in again for no reason.",
            async () =>
            {
                await c.Link("Back to the queue").ClickAsync();
                await c.ExpectHeadingAsync("OPD Queue");
            });
    }
}
