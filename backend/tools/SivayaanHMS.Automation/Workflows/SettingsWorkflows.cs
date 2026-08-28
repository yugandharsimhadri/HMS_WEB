namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// Switching a module on from Settings, and the nav rail picking it up without a page reload.
/// This is the one path that genuinely differs from what the seed data already set up through the
/// API: the seeder turns Diagnostics and Pediatrics on directly, because most workflows need them
/// already present; this workflow proves the same switch works when a clinic actually clicks it.
///
/// Assumes Dentist starts switched off, which is only true against a freshly created database —
/// the normal case, since every <c>dotnet test</c> run creates one. Pointed at a database a
/// previous run already left Dentist on in (the SIVAYAANHMS_UAT_MANAGE_SERVERS=false escape
/// hatch, reused across runs), the opening assertion fails honestly rather than silently passing
/// for the wrong reason.
/// </summary>
public sealed class ModuleToggleWorkflow() : Workflow(
    key: "ModuleToggle",
    displayName: "Turning On a Module",
    module: "Settings",
    businessPurpose: "Let a clinic grow into the product — start with just OPD and Pharmacy, and switch on Dentist, Pediatrics or a lab module the day it is actually needed, with nothing to reinstall.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Settings opens on the clinic's own details.",
            () => c.NavigateAsync("Settings", "Settings"));

        await c.StepAsync(
            "Dentist is not in the nav yet — the module starts switched off for a new clinic.",
            async () =>
            {
                var dentistLink = c.Page.GetByRole(Microsoft.Playwright.AriaRole.Navigation)
                    .GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Dentist", Exact = true });
                await WorkflowContext.Expect(dentistLink).Not.ToBeVisibleAsync();
            });

        await c.StepAsync(
            "The Features tab lists every module as a switch, off by default apart from OPD and Pharmacy.",
            async () =>
            {
                await c.Button("Features").ClickAsync();
                await c.ExpectVisibleAsync("Switching a module off hides it everywhere");
            });

        await c.StepAsync(
            "Switching Dentist on and saving.",
            async () =>
            {
                await c.Page.Locator("label.module-toggle", new() { HasText = "Dentist" })
                    .Locator("input[type=checkbox]")
                    .CheckAsync();

                await c.Button("Save").ClickAsync();
                await c.ExpectVisibleAsync("Saved");
            });

        await c.StepAsync(
            "Dentist now reaches the nav rail without a page reload, and opens its own screen.",
            () => c.NavigateAsync("Dentist", "Dentist"));
    }
}
