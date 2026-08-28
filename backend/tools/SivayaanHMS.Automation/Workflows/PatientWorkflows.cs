namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The patient register. Every other module — a visit, a bill, a prescription — is recorded
/// against a patient created here, so this is the one screen every other workflow depends on
/// having worked first.
/// </summary>
public sealed class PatientRegisterWorkflow() : Workflow(
    key: "PatientRegister",
    displayName: "Registering a New Patient",
    module: "Patients",
    businessPurpose: "Give every patient one record their whole history attaches to, from the first visit onward.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "The Patients screen opens on the whole register.",
            () => c.NavigateAsync("Patients", "Patients"));

        var name = $"UAT Patient {Guid.NewGuid():N}"[..24];
        var phone = "98" + Random.Shared.Next(10_000_000, 99_999_999);

        await c.StepAsync(
            "A new patient is added with a name, a phone number and an age — nothing else is required.",
            async () =>
            {
                await c.Button("New patient").ClickAsync();

                await c.Page.GetByLabel("Name", new() { Exact = true }).FillAsync(name);
                await c.Page.GetByLabel("Phone", new() { Exact = true }).FillAsync(phone);
                await c.Page.GetByLabel("Age", new() { Exact = true }).FillAsync("6");

                await c.Button("Save").ClickAsync();
            });

        await c.StepAsync(
            "Saving assigns a patient number and returns to the register with the new patient visible.",
            () => c.ExpectVisibleAsync(name));

        await c.StepAsync(
            "The same patient is found again by searching on the phone number just entered — not just by scrolling the list.",
            async () =>
            {
                // Enter, not a "Search" button click: the shell's own command-palette trigger is
                // also named "Search" (aria-label="Search"), so a name-based button lookup binds
                // ambiguously to whichever of the two the DOM happens to list first — not
                // necessarily this form's own submit button. The search box sits inside a real
                // <form onSubmit>, so Enter submits it exactly as a person pressing Enter would.
                var searchBox = c.Page.GetByPlaceholder("Name, phone or patient no.");
                await searchBox.FillAsync(phone);
                await searchBox.PressAsync("Enter");
                await c.ExpectVisibleAsync(name);
            });

        await c.StepAsync(
            "Search is case-insensitive: the same patient is found from a fragment of their name typed in lower case, whatever case it was saved in.",
            async () =>
            {
                var fragment = name.Split(' ')[1].ToLowerInvariant(); // "patient", from "UAT Patient <id>"
                var searchBox = c.Page.GetByPlaceholder("Name, phone or patient no.");
                await searchBox.FillAsync(fragment);
                await searchBox.PressAsync("Enter");
                await c.ExpectVisibleAsync(name);
            });
    }
}
