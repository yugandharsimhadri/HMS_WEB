namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The morning-glance screen: today's numbers, and a tile for each figure that opens the real
/// screen behind it rather than being a dead end in itself.
/// </summary>
public sealed class DashboardWorkflow() : Workflow(
    key: "Dashboard",
    displayName: "The Dashboard",
    module: "Overview",
    businessPurpose: "Show today's patients, queue and revenue at a glance, each figure one click from the screen that explains it.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Dashboard is reached from the nav like any other screen — a normal sign-in lands on the OPD Queue, not here.",
            () => c.NavigateAsync("Dashboard", "Dashboard"));

        await c.StepAsync(
            "It reports how many patients have been seen today, and today's revenue.",
            async () =>
            {
                await c.ExpectVisibleAsync("Patients today");
                await c.ExpectVisibleAsync("Revenue today");
            });

        await c.StepAsync(
            "And how many are in the queue right now — the seeded visit counts towards it.",
            () => c.ExpectVisibleAsync("In queue now"));

        await c.StepAsync(
            "The patients-today tile is a real link back to the queue, not a static number.",
            async () =>
            {
                await WorkflowContext.Visible(c.Page.GetByText("Patients today")).ClickAsync();
                await c.ExpectHeadingAsync("OPD Queue");
            });
    }
}
