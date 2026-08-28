namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// A patient's record changes over time — a phone number, an age at the next birthday — and the
/// same register entry has to carry that change forward rather than growing a second record for
/// the same person.
/// </summary>
public sealed class PatientEditWorkflow() : Workflow(
    key: "PatientEdit",
    displayName: "Editing an Existing Patient",
    module: "Patients",
    businessPurpose: "Correct a patient's own details in place, so their one record stays theirs rather than accumulating a second one every time something changes.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        var newPhone = "97" + Random.Shared.Next(10_000_000, 99_999_999);

        await c.StepAsync(
            "Selecting the seeded patient from the register.",
            async () =>
            {
                await c.NavigateAsync("Patients", "Patients");
                await WorkflowContext.Visible(c.Page.GetByText(DemoData.PatientOneName)).ClickAsync();
            });

        await c.StepAsync(
            "Edit only unlocks once a row is selected — it is disabled otherwise, so nobody can open a blank editor by mistake and mistake it for a new patient.",
            async () =>
            {
                var editButton = c.Button("Edit", exact: true);
                await WorkflowContext.Expect(editButton).ToBeEnabledAsync();
                await editButton.ClickAsync();
            });

        await c.StepAsync(
            "The editor opens already carrying this patient's own name and number — an edit, not a blank form.",
            async () =>
            {
                await WorkflowContext.Expect(c.Dialog.GetByText(DemoData.PatientOneName)).ToBeVisibleAsync();
                await WorkflowContext.Expect(c.Dialog.GetByLabel("Phone", new() { Exact = true }))
                    .ToHaveValueAsync(DemoData.PatientOnePhone);
            });

        await c.StepAsync(
            "Changing the phone number and saving.",
            async () =>
            {
                var phoneField = c.Dialog.GetByLabel("Phone", new() { Exact = true });
                await phoneField.FillAsync(newPhone);
                await c.Button("Save", exact: true).ClickAsync();
            });

        await c.StepAsync(
            "Saving returns to the register, and searching on the new number finds the same patient — not a second one.",
            async () =>
            {
                var searchBox = c.Page.GetByPlaceholder("Name, phone or patient no.");
                await searchBox.FillAsync(newPhone);
                await searchBox.PressAsync("Enter");
                await c.ExpectVisibleAsync(DemoData.PatientOneName);
            });

        await c.StepAsync(
            "The patient number is unchanged — an edit, never a new registration.",
            async () =>
            {
                await WorkflowContext.Visible(c.Page.GetByText(DemoData.PatientOneName)).ClickAsync();
                await c.ExpectVisibleAsync("P00001");
            });
    }
}
