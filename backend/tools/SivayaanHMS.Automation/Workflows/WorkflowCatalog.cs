namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// The definitive list of SivayaanHMS business workflows this suite covers, in the order they
/// make sense as a walkthrough: the way in, the morning view, the register everything else
/// depends on, the front desk, the counter, what gets read back at the end of the day, the
/// clinic's own catalogue, growing into a module that started switched off, and finally the
/// negative paths — the wrong things a clinic, or a stale link, might actually do.
/// </summary>
public static class WorkflowCatalog
{
    public static IReadOnlyList<IWorkflow> All { get; } =
    [
        new SignInWorkflow(),
        new DashboardWorkflow(),
        new PatientRegisterWorkflow(),
        new PatientEditWorkflow(),
        new OpdQueueWorkflow(),
        new PatientSearchNoMatchWorkflow(),
        new MedicineCatalogueWorkflow(),
        new ReportsWorkflow(),
        new MastersWorkflow(),
        new ModuleToggleWorkflow(),
        new ModuleToggleAllOffWorkflow(),
        new RegistrationDuplicateCodeWorkflow(),
        new SessionExpiryWorkflow(),
        new NotFoundRouteWorkflow(),
    ];

    /// <summary>Resolves a workflow by its <see cref="IWorkflow.Key"/>, case-insensitively. Null when unknown.</summary>
    public static IWorkflow? Find(string key)
        => All.FirstOrDefault(w => string.Equals(w.Key, key, StringComparison.OrdinalIgnoreCase));
}
