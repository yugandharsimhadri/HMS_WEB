using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The way in, and the way the system keeps everyone else out. Runs from an already-signed-in
/// state (as every workflow does) by signing out first, so it can show the whole door: a rejected
/// credential, then a successful sign-in, and the session surviving a page reload.
/// </summary>
public sealed class SignInWorkflow() : Workflow(
    key: "SignIn",
    displayName: "Signing In",
    module: "Security",
    businessPurpose: "Put every screen behind a named login, so only a clinic's own staff can see its patients and its money.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Signing out returns to the sign-in screen — the only way into the product.",
            async () =>
            {
                await c.Button("Sign out").ClickAsync();
                await WorkflowContext.Expect(c.Page).ToHaveURLAsync(new Regex(@"/login"));
                await c.ExpectVisibleAsync("Sign in to your clinic");
            });

        await c.StepAsync(
            "A wrong password is refused, and the form says only that the credentials are wrong.",
            async () =>
            {
                await c.Page.GetByPlaceholder("you@your-clinic").FillAsync(DemoData.AdminUsername);
                await c.Page.GetByLabel("Password", new() { Exact = true }).FillAsync("not-the-password");
                await c.Button("Sign in").ClickAsync();
                await c.ExpectVisibleAsync("Incorrect username or password.");
            });

        await c.StepAsync(
            "The right username and password let the admin through to the clinic shell.",
            async () =>
            {
                await c.Page.GetByLabel("Password", new() { Exact = true }).FillAsync(DemoData.AdminPassword);
                await c.Button("Sign in").ClickAsync();
                await WorkflowContext.Expect(c.Page.GetByRole(AriaRole.Navigation).First)
                    .ToBeVisibleAsync(new() { Timeout = 30_000 });
            });

        await c.StepAsync(
            "The clinic's own name is shown in the shell, on every screen from here on.",
            () => c.ExpectVisibleAsync(DemoData.ClinicName));

        await c.StepAsync(
            "Signed-in state survives a reload — the session lives in the browser, not just in memory.",
            async () =>
            {
                await c.Page.ReloadAsync();
                await WorkflowContext.Expect(c.Page.GetByRole(AriaRole.Navigation).First)
                    .ToBeVisibleAsync(new() { Timeout = 30_000 });
            });
    }
}
