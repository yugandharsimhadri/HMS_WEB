namespace SivayaanHMS.Automation.Workflows;

/// <summary>Boilerplate-free base for the concrete workflows: metadata as constructor-set properties, the journey itself as the one method each subclass writes.</summary>
public abstract class Workflow(
    string key,
    string displayName,
    string module,
    string businessPurpose) : IWorkflow
{
    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    public string Module { get; } = module;

    public string BusinessPurpose { get; } = businessPurpose;

    public abstract Task RunAsync(WorkflowContext context);

    public override string ToString() => $"{Key} ({DisplayName})";
}
