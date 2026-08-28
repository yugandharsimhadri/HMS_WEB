namespace SivayaanHMS.Automation.Workflows;

/// <summary>
/// One end-to-end business journey through SivayaanHMS, written once and run by every UAT test
/// that names its <see cref="Key"/>. The shape is the same as TransTrack.Automation's
/// <c>IWorkflow</c> — a workflow object is a reusable, self-verifying scenario, not test-file
/// boilerplate — trimmed of the fields that framework needs only to drive a video recorder.
/// </summary>
public interface IWorkflow
{
    /// <summary>Stable token used to find this workflow in <see cref="WorkflowCatalog"/> and named in test failure output.</summary>
    string Key { get; }

    /// <summary>Human-readable name, used in the UAT failure message.</summary>
    string DisplayName { get; }

    /// <summary>Which part of the product this belongs to.</summary>
    string Module { get; }

    /// <summary>One sentence on what the workflow proves works, in business terms rather than UI terms.</summary>
    string BusinessPurpose { get; }

    /// <summary>
    /// Runs the journey from wherever sign-in left the page, leaving the app on the workflow's
    /// closing screen. The caller has already signed in; a workflow must not assume any other
    /// prior state, so any subset of workflows can be selected and run in any order.
    /// </summary>
    Task RunAsync(WorkflowContext context);
}
