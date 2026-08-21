using System;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class GitHubWorkflowContractTests
{
    private static readonly string[] PullRequestGateWorkflowNames =
    [
        "ci-acp-sdk.yml",
        "ci-core.yml",
        "code-quality.yml",
        "gui-smoke-gates.yml",
        "platform-build-gates.yml",
        "wasm-smoke-gates.yml"
    ];

    [Fact]
    public void PullRequestGates_TargetDevelopBaseline()
    {
        foreach (var workflowName in PullRequestGateWorkflowNames)
        {
            var workflow = ReadWorkflow(workflowName);

            Assert.Contains("pull_request:\n    branches: [develop]", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("pull_request:\n    branches: [main]", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CodeQuality_UsesCurrentBaseBranchMergeBaseForPullRequests()
    {
        var workflow = ReadWorkflow("code-quality.yml");

        Assert.Contains("$baseRef = \"${{ github.base_ref }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("$head = \"${{ github.event.pull_request.head.sha }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("$base = git merge-base \"origin/$baseRef\" $head", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event.pull_request.base.sha", workflow, StringComparison.Ordinal);
    }

    private static string ReadWorkflow(string workflowName) =>
        TestSourceFiles.ReadAllText($@".github\workflows\{workflowName}").Replace("\r\n", "\n", StringComparison.Ordinal);
}
