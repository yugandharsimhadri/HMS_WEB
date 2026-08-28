using System.Diagnostics;

namespace SivayaanHMS.Automation.Workflows;

/// <summary>The outcome of one workflow run.</summary>
public sealed record WorkflowRunResult(
    string Key,
    string DisplayName,
    bool Succeeded,
    TimeSpan Duration,
    IReadOnlyList<string> NarrationSteps,
    string? FailureMessage,
    string? ScreenshotPath);

/// <summary>
/// Runs a workflow inside a session with the surrounding ceremony every caller needs: timing, a
/// closing screenshot on failure, and a captured failure rather than a thrown one — the same
/// shape as TransTrack.Automation's runner, minus the narrator/title-card calls that framework
/// makes for its video recorder.
/// </summary>
public static class WorkflowRunner
{
    public static async Task<WorkflowRunResult> RunAsync(
        ClinicSession session,
        IWorkflow workflow,
        Action<string>? log = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = session.CreateWorkflowContext();

        log?.Invoke($"[{workflow.Module}] {workflow.DisplayName}");

        try
        {
            await workflow.RunAsync(context);
            stopwatch.Stop();

            log?.Invoke($"PASS {workflow.Key} ({stopwatch.Elapsed.TotalSeconds:0.0}s)");

            return new WorkflowRunResult(
                workflow.Key, workflow.DisplayName, Succeeded: true,
                stopwatch.Elapsed, context.Steps.ToList(), FailureMessage: null, ScreenshotPath: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Best effort: if the page itself is what broke, the screenshot will fail too, and the
            // original exception is the one worth reporting.
            string? screenshot = null;
            try { screenshot = await session.CaptureScreenshotAsync($"{workflow.Key}-FAILED"); } catch { }

            log?.Invoke($"FAIL {workflow.Key} ({stopwatch.Elapsed.TotalSeconds:0.0}s): {ex.Message}");

            return new WorkflowRunResult(
                workflow.Key, workflow.DisplayName, Succeeded: false,
                stopwatch.Elapsed, context.Steps.ToList(), ex.Message, screenshot);
        }
    }
}
