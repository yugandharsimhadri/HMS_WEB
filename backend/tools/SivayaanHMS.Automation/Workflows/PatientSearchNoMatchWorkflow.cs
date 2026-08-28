namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// Searching for someone who is not on the register — a wrong number, a first visit not yet
/// booked. What the booking dialog offers instead of a silent empty list.
/// </summary>
public sealed class PatientSearchNoMatchWorkflow() : Workflow(
    key: "PatientSearchNoMatch",
    displayName: "Searching for Someone Not on the Register",
    module: "OPD",
    businessPurpose: "Say plainly that nobody matches, rather than an empty list that reads the same as a search that has not run yet — and offer registering them on the spot.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Book visit opens the same booking dialog every walk-in goes through.",
            async () =>
            {
                await c.NavigateAsync("OPD Queue", "OPD Queue");
                await c.Button("Book visit").ClickAsync();
            });

        await c.StepAsync(
            "A phone number nobody in this clinic is registered under finds nothing, and says so by the number that was actually typed.",
            async () =>
            {
                await c.Page.GetByPlaceholder("Name or phone number").FillAsync("0000000000");
                await c.Button("Find").ClickAsync();
                await c.ExpectVisibleAsync("No one matches '0000000000'");
            });

        await c.StepAsync(
            "Rather than a dead end, the dialog opens straight into adding them — the phone number just searched is already there, so it is not retyped.",
            async () =>
            {
                await c.ExpectVisibleAsync("Add them as a new patient");

                // Exact: true — the dialog's own top search box is placeholder "Name or phone
                // number", which case-insensitively contains "Phone" too, so the un-exact form
                // resolves ambiguously to both that box and the add-patient sub-form's real Phone
                // field.
                await WorkflowContext.Expect(c.Dialog.GetByPlaceholder("Phone", new() { Exact = true }))
                    .ToHaveValueAsync("0000000000");
            });
    }
}
