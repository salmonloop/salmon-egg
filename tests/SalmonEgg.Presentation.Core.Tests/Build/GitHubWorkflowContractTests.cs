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

        // The rule must stay in the shared script the push-time gate rehearses. Inlining it again is how
        // it last shipped unverifiable.
        Assert.Contains(". ./scripts/release/DesktopMsiContract.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Assert-DesktopMsiContract -Database $database", workflow, StringComparison.Ordinal);

        // Windows Installer has no aggregate functions, so this shape fails inside OpenView rather than
        // failing an assertion. It took down the v1.3.0 release build. Scan the executable lines only:
        // the comments above that step name the defect on purpose.
        var executableLines = workflow
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            .ToList();
        Assert.DoesNotContain(executableLines, line => line.Contains("COUNT(*)", StringComparison.Ordinal));

        var contract = TestSourceFiles.ReadAllText(@"scripts\release\DesktopMsiContract.ps1")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("Assert-MsiQuerySupported -Query $Query", contract, StringComparison.Ordinal);
        Assert.Contains("while ($null -ne $view.Fetch())", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactGateSelfTests_RunOnEveryChange()
    {
        // A gate whose own failure cases are never rehearsed is indistinguishable from a gate that
        // passes everything. Both gates run against real artifacts only on Windows or at release time.
        var workflow = ReadWorkflow("ci-core.yml");

        Assert.Contains("run-msix-package-contract-gate.ps1 -SelfTest", workflow, StringComparison.Ordinal);
        Assert.Contains("run-release-artifact-contract-gate.sh --self-test", workflow, StringComparison.Ordinal);

        // This one was missing, and the omission is what let the desktop MSI rule reach a tag with SQL
        // Windows Installer cannot parse: the release step is the only place it ran.
        Assert.Contains("run-desktop-msi-contract-gate.ps1", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformGates_RunOnASchedule()
    {
        var workflow = ReadWorkflow("platform-build-gates.yml");

        // The Apple and Android toolchains move without any commit here, so only a scheduled run can
        // separate "our change broke it" from "the platform moved".
        Assert.Contains("schedule:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryScheduledWorkflow_SurvivesConcurrentPushes()
    {
        // A scheduled run always executes against the default branch, so a concurrency group keyed only on
        // the ref shares a slot with pushes to that branch, and the next commit cancels the one run whose
        // whole purpose is to complete against a tree nobody touched.
        //
        // Written as a rule over every scheduled workflow rather than a check on one file, because the
        // single-file version is what let the defect recur: the assertion named platform-build-gates.yml,
        // so codeql.yml shipped with a ref-only group and a weekly security scan a push could cancel.
        var scheduled = AllWorkflowNames
            .Select(workflowName => (Name: workflowName, Body: ReadWorkflow(workflowName)))
            .Where(entry => entry.Body.Contains("\n  schedule:\n", StringComparison.Ordinal))
            .ToArray();

        // Naming the members keeps the rule below from going vacuous. "No scheduled workflow violates X"
        // is trivially true of an empty set, so a change that dropped a schedule would leave this test
        // green while deleting the continuous coverage that schedule exists to provide.
        Assert.Equal(
            ["codeql.yml", "platform-build-gates.yml"],
            scheduled.Select(entry => entry.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // Assert on the group line itself rather than searching the whole file for the event-name
        // expression: `${{ github.event_name }}` appears in step conditions too, so a whole-file contains
        // would pass on a workflow whose group had never been scoped at all.
        var unprotected = scheduled
            .Where(entry =>
                !ConcurrencyGroupLine(entry.Body).Contains("${{ github.event_name }}", StringComparison.Ordinal)
                || !entry.Body.Contains("cancel-in-progress: ${{ github.event_name != 'schedule' }}", StringComparison.Ordinal))
            .Select(entry => $"{entry.Name}: {ConcurrencyGroupLine(entry.Body)}")
            .ToArray();

        Assert.Empty(unprotected);
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

    [Fact]
    public void MsixPackagingIsIdenticalAcrossGateAndRelease_ExceptForSigning()
    {
        // The PR-level gate exists to catch packaging regressions before a release. It only does that if it
        // drives the same packaging chain the release drives; if the two drift, the gate starts proving
        // something about a configuration nobody ships. Signing is the one intended difference.
        var gate = ReadWorkflow("platform-build-gates.yml");
        var release = ReadWorkflow("release-packaging.yml");

        string[] sharedArguments =
        [
            "/p:TargetFramework=net10.0-windows10.0.26100.0",
            "/p:PublishProfile=Properties/PublishProfiles/win-msix-x64.pubxml",
            "/p:EnableWinUIBuild=true",
            "/p:IsolatedMsixBuild=true",
            "/p:BuildProjectReferences=false",
            "/p:DisableCustomWinSdkXamlReferences=true",
            "/p:Restore=false"
        ];

        foreach (var argument in sharedArguments)
        {
            Assert.Contains(argument, gate, StringComparison.Ordinal);
            Assert.Contains(argument, release, StringComparison.Ordinal);
        }

        // The gate must never require signing secrets, and the release must never stop signing.
        Assert.Contains("/p:AppxPackageSigningEnabled=false", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("/p:AppxPackageSigningEnabled=true", gate, StringComparison.Ordinal);
        Assert.Contains("/p:AppxPackageSigningEnabled=true", release, StringComparison.Ordinal);
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

    private static string ConcurrencyGroupLine(string workflow) =>
        workflow
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("concurrency:", StringComparison.Ordinal))
            .FirstOrDefault(line => line.TrimStart().StartsWith("group:", StringComparison.Ordinal), string.Empty)
            .Trim();

    private static string ReadWorkflow(string workflowName) =>
        TestSourceFiles.ReadAllText($@".github\workflows\{workflowName}").Replace("\r\n", "\n", StringComparison.Ordinal);
}
