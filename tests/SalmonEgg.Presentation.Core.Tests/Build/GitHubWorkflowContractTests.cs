using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class GitHubWorkflowContractTests
{
    private static readonly string[] AllWorkflowNames =
    [
        "ci-acp-sdk.yml",
        "ci-core.yml",
        "code-quality.yml",
        "codeql.yml",
        "gui-smoke-gates.yml",
        "platform-build-gates.yml",
        "release-packaging.yml",
        "wasm-smoke-gates.yml"
    ];

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

    [Fact]
    public void ReleaseWorkflow_NormalizesMsysPathBeforeWindowsCliMsiBuild()
    {
        var workflow = ReadWorkflow("release-packaging.yml");

        Assert.Contains("CLI_EXECUTABLE: ${{ steps.build-cli.outputs.executable-path }}", workflow, StringComparison.Ordinal);
        Assert.Contains("if ($executable -match '^/([A-Za-z])/(.+)$')", workflow, StringComparison.Ordinal);
        Assert.Contains("$executable = \"$($matches[1].ToUpperInvariant()):\\$($matches[2] -replace '/', '\\')\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-Executable $executable -Version $env:CLI_VERSION", workflow, StringComparison.Ordinal);

        var msiScript = TestSourceFiles.ReadAllText(@"scripts\release\build-cli-msi.ps1").Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("$database = $installer.OpenDatabase($msiPath, 0)", msiScript, StringComparison.Ordinal);
        Assert.Contains("$view = $database.OpenView('SELECT `Name`, `Value` FROM `Environment`')", msiScript, StringComparison.Ordinal);
        Assert.Contains("$view.Execute()", msiScript, StringComparison.Ordinal);
        Assert.Contains("$record = $view.Fetch()", msiScript, StringComparison.Ordinal);
        Assert.Contains("Name  = $record.StringData(1)", msiScript, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWorkflow_DeclaresLeastPrivilegePermissions()
    {
        // Without an explicit block a workflow inherits the repository default, which is a setting that
        // can be widened in the UI with no diff to review here.
        foreach (var workflowName in AllWorkflowNames)
        {
            var workflow = ReadWorkflow(workflowName);

            Assert.Contains("permissions:\n  contents: read", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryWorkflow_PinsActionsToCommitShas()
    {
        // A floating tag can be repointed at new code by whoever controls the action repository. Pinning
        // is only durable if a later edit that reintroduces `@v5` fails here.
        var floatingTag = new Regex(@"uses:\s+[^\s@]+@v\d", RegexOptions.None, TimeSpan.FromSeconds(5));
        var pinnedSha = new Regex(@"uses:\s+[^\s@]+@[0-9a-f]{40}", RegexOptions.None, TimeSpan.FromSeconds(5));

        foreach (var workflowName in AllWorkflowNames)
        {
            var workflow = ReadWorkflow(workflowName);

            Assert.DoesNotMatch(floatingTag, workflow);
            Assert.Matches(pinnedSha, workflow);
        }
    }

    [Fact]
    public void CiCore_RunsTestsExactlyOnceWithoutAutomaticRetry()
    {
        // An automatic second attempt turns a flaky test into a green run, which removes the only signal
        // that the flakiness exists. Re-running is a human decision, recorded in the run history.
        var workflow = ReadWorkflow("ci-core.yml");

        Assert.DoesNotContain("Retrying once", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Test pass 1", workflow, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workflow, "dotnet test"));
    }

    [Fact]
    public void CiCore_SkipsOnlyTheGateWorkOtherStepsAlreadyDid()
    {
        // The gate script's unique contribution is the restricted single-TFM app build. Its restore and
        // its contract suites duplicate the steps above it, but the app build must never be skipped.
        var workflow = ReadWorkflow("ci-core.yml");

        Assert.Contains("-SkipSolutionRestore", workflow, StringComparison.Ordinal);
        Assert.Contains("-SkipContractSuites", workflow, StringComparison.Ordinal);

        var gateScript = TestSourceFiles.ReadAllText(@"scripts\gates\run-core-gates.ps1")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain("-SkipAppBuild", gateScript, StringComparison.Ordinal);
        Assert.Contains("-p:SalmonEggTargetFrameworks=net10.0-desktop", gateScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_HasOneAuthoritativeTriggerAndNoCrossJobSecretReads()
    {
        var workflow = ReadWorkflow("release-packaging.yml");

        // `release: [created]` made the same tag reachable through two independent runs, both racing to
        // upload the same assets. docs/release-guide.md documents pushing a tag as the release action.
        Assert.Contains("tags: [\"v*\"]", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("types: [created]", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("github.event.release.tag_name", workflow, StringComparison.Ordinal);

        // Job-level env is not visible to other jobs, so the publish job cannot re-evaluate the macOS
        // signing secrets. Reading them there silently disabled the .dmg upload entirely.
        Assert.Contains("dmg-produced: ${{ steps.collect-dmg.outputs.dmg-produced }}", workflow, StringComparison.Ordinal);
        Assert.Contains("if: needs.package-macos.outputs.dmg-produced == 'true'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_VerifiesEveryArtifactBeforeUpload()
    {
        // The CLI already executed and installed what it built. These four had a successful publish as
        // their only evidence.
        var workflow = ReadWorkflow("release-packaging.yml");

        Assert.Contains("run-release-artifact-contract-gate.sh wasm publish/wasm", workflow, StringComparison.Ordinal);
        Assert.Contains("run-release-artifact-contract-gate.sh macos-bundle", workflow, StringComparison.Ordinal);
        Assert.Contains("run-msix-package-contract-gate.ps1 -Package", workflow, StringComparison.Ordinal);
        Assert.Contains("Verify Windows Skia MSI contract", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactGateSelfTests_RunOnEveryChange()
    {
        // A gate whose own failure cases are never rehearsed is indistinguishable from a gate that
        // passes everything. Both gates run against real artifacts only on Windows or at release time.
        var workflow = ReadWorkflow("ci-core.yml");

        Assert.Contains("run-msix-package-contract-gate.ps1 -SelfTest", workflow, StringComparison.Ordinal);
        Assert.Contains("run-release-artifact-contract-gate.sh --self-test", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformGates_RunOnAScheduleAndSurviveConcurrentPushes()
    {
        var workflow = ReadWorkflow("platform-build-gates.yml");

        // The Apple and Android toolchains move without any commit here, so only a scheduled run can
        // separate "our change broke it" from "the platform moved".
        Assert.Contains("schedule:", workflow, StringComparison.Ordinal);

        // A canary that a push cancels is not a canary.
        Assert.Contains("cancel-in-progress: ${{ github.event_name != 'schedule' }}", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ github.event_name }}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MacOsJobs_PinTheRunnerImage()
    {
        // macos-latest rolls to a new major image on GitHub's schedule, moving Xcode underneath a commit
        // that changed nothing.
        foreach (var workflowName in AllWorkflowNames)
        {
            var workflow = ReadWorkflow(workflowName);
            // Assert on the lines themselves, not on a collection-contains: DoesNotContain over an
            // IEnumerable<string> compares whole elements, so "runs-on: macos-latest" would not match the
            // bare needle "macos-latest" and the check would pass while the defect was present. A probe
            // that reverted a runner to macos-latest proved exactly that.
            var unpinned = workflow
                .Split('\n')
                .Where(line => line.Contains("runs-on:", StringComparison.Ordinal))
                .Where(line => line.Contains("macos-latest", StringComparison.Ordinal))
                .Select(line => $"{workflowName}:{line.Trim()}")
                .ToArray();

            Assert.Empty(unpinned);
        }
    }

    [Fact]
    public void DocOnlyFilters_DoNotExcludeThePackagedAcpReadme()
    {
        // src/SalmonEgg.Acp/README.md is the nupkg's PackageReadmeFile, so it is shipping content. A
        // blanket `**.md` filter would stop gating changes to it.
        foreach (var workflowName in PullRequestGateWorkflowNames)
        {
            if (workflowName == "ci-acp-sdk.yml")
            {
                continue;
            }

            var workflow = ReadWorkflow(workflowName);

            Assert.Contains("paths-ignore:", workflow, StringComparison.Ordinal);
            Assert.Contains("- \"*.md\"", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("- \"**.md\"", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("- \"**/*.md\"", workflow, StringComparison.Ordinal);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ReadWorkflow(string workflowName) =>
        TestSourceFiles.ReadAllText($@".github\workflows\{workflowName}").Replace("\r\n", "\n", StringComparison.Ordinal);
}
