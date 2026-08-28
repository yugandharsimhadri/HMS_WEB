namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The clinic's own catalogue of what everything else is billed and recorded against —
/// vaccines, procedures and the rest — seeded automatically at registration exactly as the
/// starter medicines are, so a clinic that turns Pediatrics on already has the standard
/// vaccination schedule rather than an empty list to type out by hand.
/// </summary>
public sealed class MastersWorkflow() : Workflow(
    key: "Masters",
    displayName: "The Vaccine Schedule",
    module: "Masters",
    businessPurpose: "Give a clinic the standard vaccination schedule the moment Pediatrics is switched on, instead of an empty master list nobody has time to type out.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Masters is reached from the nav, beside Settings rather than inside it — set up once, edited occasionally.",
            () => c.NavigateAsync("Masters", "Masters"));

        await c.StepAsync(
            "The Vaccines tab exists because Pediatrics is switched on, and opens on the seeded IAP schedule.",
            async () =>
            {
                await c.Button("Vaccines").ClickAsync();
                await c.ExpectVisibleAsync("BCG");
            });
    }
}
