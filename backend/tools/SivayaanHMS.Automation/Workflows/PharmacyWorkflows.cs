namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The starter catalogue every new clinic gets — six everyday medicines, seeded automatically by
/// TenantProvisioner so the counter is usable on first launch rather than opening on an empty
/// shelf. This proves that catalogue is real by finding one of its own medicines through the same
/// search a counter clerk would use.
/// </summary>
public sealed class MedicineCatalogueWorkflow() : Workflow(
    key: "MedicineCatalogue",
    displayName: "The Starter Medicine Catalogue",
    module: "Pharmacy",
    businessPurpose: "Have a working shelf of everyday medicines from the moment a clinic registers, so the counter is not opened on an empty catalogue.")
{
    public override async Task RunAsync(WorkflowContext c)
    {
        await c.StepAsync(
            "Medicines opens already listing the starter catalogue — nothing had to be typed in first.",
            async () =>
            {
                await c.NavigateAsync("Medicines", "Medicines");
                await c.ExpectVisibleAsync(DemoData.StarterMedicineName);
            });

        await c.StepAsync(
            "Searching by name, maker or rack narrows to just that medicine.",
            async () =>
            {
                await c.Page.GetByPlaceholder("Name, maker or rack").FillAsync("Paracetamol");
                await c.ExpectVisibleAsync(DemoData.StarterMedicineName);
            });

        await c.StepAsync(
            "The Pharmacy counter — where a clinic actually bills a sale — opens on the same catalogue.",
            async () =>
            {
                await c.NavigateAsync("Pharmacy", "Pharmacy Counter");
            });
    }
}
