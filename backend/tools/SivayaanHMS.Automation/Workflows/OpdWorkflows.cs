using Microsoft.Playwright;

namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The front desk's whole day: who is waiting, and putting the next walk-in into that queue. The
/// seed data (<see cref="DemoDataSeeder"/>) already books one visit for
/// <see cref="DemoData.PatientOneName"/> so this workflow opens on a queue that is not empty —
/// closer to a real morning than a screen with nothing on it.
/// </summary>
public sealed class OpdQueueWorkflow() : Workflow(
    key: "OpdQueue",
    displayName: "The Front Desk Queue",
    module: "OPD",
    businessPurpose: "Book a walk-in against a doctor and a time, and have them show up in the queue everyone at the desk is reading from.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "The queue opens on today, already carrying the visit the seed data booked.",
            async () =>
            {
                await c.NavigateAsync("OPD Queue", "OPD Queue");
                await c.ExpectVisibleAsync(DemoData.PatientOneName);
            });

        await c.StepAsync(
            "Book visit opens the booking dialog.",
            () => c.Button("Book visit").ClickAsync());

        await c.StepAsync(
            "Typing a phone number and searching finds the second seeded patient.",
            async () =>
            {
                await c.Page.GetByPlaceholder("Name or phone number").FillAsync(DemoData.PatientTwoPhone);
                await c.Button("Find").ClickAsync();
                await c.ExpectVisibleAsync(DemoData.PatientTwoName);
            });

        await c.StepAsync(
            "Selecting them from the results, then choosing the doctor they are booked against.",
            async () =>
            {
                await WorkflowContext.Visible(c.Page.GetByText(DemoData.PatientTwoName)).ClickAsync();

                // Scoped to the dialog, not the whole page: the OPD Queue screen behind it has its
                // own session <select> (Full day/Morning/Evening) in the page header, which is
                // just as "visible" by CSS as the dialog's doctor select — only the dialog is
                // actually on top, and c.Dialog is what tells the two apart.
                await WorkflowContext.Visible(c.Dialog.GetByRole(AriaRole.Combobox))
                    .SelectOptionAsync(new SelectOptionValue { Label = DemoData.StarterDoctorName });
            });

        await c.StepAsync(
            "Book visit assigns a token and closes the dialog.",
            () => c.Button("Book visit", exact: true).ClickAsync());

        await c.StepAsync(
            "The new booking appears in the waiting list alongside the seeded one — the queue holds both, not just the most recent.",
            async () =>
            {
                await c.ExpectVisibleAsync(DemoData.PatientTwoName);
                await c.ExpectVisibleAsync(DemoData.PatientOneName);
            });
    }
}
