using System.Text;
using SivayaanHMS.Automation;
using SivayaanHMS.Automation.Workflows;
using Xunit.Abstractions;

namespace SivayaanHMS.UatTests;

/// <summary>
/// Base for the acceptance classes. Each test gets its own browser session, signs in for real
/// through the login form, runs one workflow from <see cref="WorkflowCatalog"/>, and passes only
/// if every verification inside that workflow held.
/// </summary>
[Collection(UatCollection.Name)]
public abstract class UatTestBase(UatFixture fixture, ITestOutputHelper output)
{
    /// <summary>
    /// Runs the named workflow end to end and asserts it completed. On failure the narration
    /// steps that did run are attached to the assertion message, so the report names the business
    /// step that broke rather than just a locator.
    /// </summary>
    protected async Task RunWorkflowAsync(string workflowKey)
    {
        var workflow = WorkflowCatalog.Find(workflowKey)
            ?? throw new InvalidOperationException($"No workflow named '{workflowKey}' in the catalog.");

        await using var session = await ClinicSession.StartAsync(fixture.Options, output.WriteLine);
        await session.LoginAsync();

        var result = await WorkflowRunner.RunAsync(session, workflow, output.WriteLine);

        Assert.True(result.Succeeded, BuildFailureMessage(workflow, result));
    }

    private static string BuildFailureMessage(IWorkflow workflow, WorkflowRunResult result)
    {
        var message = new StringBuilder()
            .AppendLine($"UAT scenario '{workflow.DisplayName}' ({workflow.Key}) failed.")
            .AppendLine($"Purpose: {workflow.BusinessPurpose}")
            .AppendLine()
            .AppendLine($"Failed after {result.NarrationSteps.Count} step(s):");

        foreach (var (step, index) in result.NarrationSteps.Select((s, i) => (s, i + 1)))
            message.AppendLine($"  {index,2}. {step}");

        message.AppendLine().AppendLine($"Reason: {result.FailureMessage}");

        if (result.ScreenshotPath is not null)
            message.AppendLine($"Screenshot: {result.ScreenshotPath}");

        return message.ToString();
    }
}
