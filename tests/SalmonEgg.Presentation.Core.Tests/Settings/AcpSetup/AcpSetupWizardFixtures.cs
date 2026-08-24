using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

/// <summary>
/// Catalog stub serving descriptors the test declares, so wizard behaviour is exercised against
/// deliberately shaped agents rather than the shipping catalog's real contents.
/// </summary>
internal sealed class StubAgentCatalog : IAcpAgentCatalog
{
    public StubAgentCatalog(params AcpAgentDescriptor[] agents)
    {
        Agents = agents;
    }

    public IReadOnlyList<AcpAgentDescriptor> Agents { get; }

    public AcpAgentDescriptor? FindAgent(string? agentId)
    {
        foreach (var agent in Agents)
        {
            if (string.Equals(agent.Id, agentId, StringComparison.Ordinal))
            {
                return agent;
            }
        }

        return null;
    }
}

/// <summary>
/// Probe stub answering from per-command values, including the tri-state package answers, so the
/// wizard's handling of "unknown" can be tested apart from "absent".
/// </summary>
internal sealed class StubExecutableProbe : IAcpExecutableProbe
{
    private readonly Dictionary<string, string?> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool?> _nodePackages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool?> _uvTools = new(StringComparer.Ordinal);

    public bool SupportsProcessProbing { get; set; } = true;

    public void SetExecutable(string command, string? path, string? version = null)
    {
        _paths[command] = path;
        _versions[command] = version;
    }

    public void SetNodePackage(string packageId, bool? installed) => _nodePackages[packageId] = installed;

    public void SetUvTool(string packageId, bool? installed) => _uvTools[packageId] = installed;

    public Task<string?> ResolveExecutablePathAsync(string command, CancellationToken cancellationToken = default)
        => Task.FromResult(_paths.TryGetValue(command, out var path) ? path : null);

    public Task<string?> ReadVersionAsync(
        string command,
        IReadOnlyList<string> versionArguments,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_versions.TryGetValue(command, out var version) ? version : null);

    public Task<bool?> IsGlobalNodePackageInstalledAsync(string packageId, CancellationToken cancellationToken = default)
        => Task.FromResult(_nodePackages.TryGetValue(packageId, out var installed) ? installed : null);

    public Task<bool?> IsGlobalUvToolInstalledAsync(string packageId, CancellationToken cancellationToken = default)
        => Task.FromResult(_uvTools.TryGetValue(packageId, out var installed) ? installed : null);
}

/// <summary>
/// Installer stub that records attempts and can emit output lines, so the wizard's progress plumbing
/// and its post-install re-probe can both be observed.
/// </summary>
internal sealed class StubComponentInstaller : IAcpComponentInstaller
{
    private readonly Func<AcpComponentDescriptor, AcpComponentInstallResult> _resultFactory;

    public StubComponentInstaller(
        Func<AcpComponentDescriptor, AcpComponentInstallResult>? resultFactory = null,
        bool supportsAutomaticInstall = true)
    {
        _resultFactory = resultFactory
            ?? (component => AcpComponentInstallResult.Success(component.Id, output: null));
        SupportsAutomaticInstall = supportsAutomaticInstall;
    }

    public bool SupportsAutomaticInstall { get; }

    /// <summary>
    /// Invoked before the canned result is produced, so a test can flip the probe's answer and the
    /// orchestrator's post-install re-probe sees the component as installed.
    /// </summary>
    public Action<AcpComponentDescriptor>? OnInstall { get; set; }

    public List<string> InstalledComponentIds { get; } = new();

    public List<string> OutputLines { get; } = new();

    public Task<AcpComponentInstallResult> InstallAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        InstalledComponentIds.Add(component.Id);
        foreach (var line in OutputLines)
        {
            onOutput?.Invoke(line);
        }

        OnInstall?.Invoke(component);
        return Task.FromResult(_resultFactory(component));
    }
}

/// <summary>Connectivity stub returning a canned result and recording the plan it was handed.</summary>
internal sealed class StubConnectivityTester : IAcpSetupConnectivityTester
{
    private Func<AcpLaunchPlan, AcpSetupTestResult> _resultFactory;

    public StubConnectivityTester(AcpSetupTestResult result)
        : this(_ => result)
    {
    }

    public StubConnectivityTester(Func<AcpLaunchPlan, AcpSetupTestResult> resultFactory)
    {
        _resultFactory = resultFactory;
    }

    /// <summary>Replaces the canned outcome so a test can model re-testing after a fix.</summary>
    public void SetResult(AcpSetupTestResult result) => _resultFactory = _ => result;

    /// <summary>The shared healthy-handshake answer most tests walk against.</summary>
    public static AcpSetupTestResult SuccessfulHandshake()
        => AcpSetupTestResult.Success(protocolVersion: 1, agentName: "test-agent");

    public int TestCount { get; private set; }

    public AcpLaunchPlan? LastPlan { get; private set; }

    public Task<AcpSetupTestResult> TestAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default)
    {
        TestCount++;
        LastPlan = launchPlan;
        return Task.FromResult(_resultFactory(launchPlan));
    }
}

/// <summary>Configuration stub capturing saves so the persisted shape can be asserted.</summary>
internal sealed class RecordingConfigurationService : IConfigurationService
{
    public List<ServerConfiguration> Saved { get; } = new();

    public Exception? SaveException { get; set; }

    public Task SaveConfigurationAsync(ServerConfiguration config)
    {
        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        Saved.Add(config);
        return Task.CompletedTask;
    }

    public Task<ServerConfiguration?> LoadConfigurationAsync(string id)
        => Task.FromResult<ServerConfiguration?>(null);

    public Task<IEnumerable<ServerConfiguration>> ListConfigurationsAsync()
        => Task.FromResult<IEnumerable<ServerConfiguration>>(Saved);

    public Task DeleteConfigurationAsync(string id, string? expectedRevision = null)
        => Task.CompletedTask;
}

/// <summary>Shared descriptor builders and a wizard factory for these tests.</summary>
internal static class AcpSetupWizardFixtures
{
    public const string RuntimeCommand = "test-agent";
    public const string AdapterPackage = "@scope/test-adapter";

    public static AcpComponentDescriptor Runtime(string id = "runtime.test")
        => new()
        {
            Id = id,
            DisplayName = "Test Agent CLI",
            Distribution = AcpDistributionKind.Npx,
            DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
            ProbeCommand = RuntimeCommand,
            ProbeVersionArguments = new[] { "--version" },
            PackageId = "@scope/test-agent"
        };

    public static AcpAdapterDescriptor BuiltInAdapter(
        string id = "adapter.builtin",
        params AcpSetupParameterDefinition[] parameters)
        => new()
        {
            Component = new AcpComponentDescriptor
            {
                Id = id,
                DisplayName = "Built-in ACP",
                Distribution = AcpDistributionKind.BuiltIn,
                DetectionMode = AcpComponentDetectionMode.None
            },
            LaunchTemplate = new AcpLaunchTemplate
            {
                Command = RuntimeCommand,
                FixedArguments = new[] { "--acp" },
                Parameters = parameters
            }
        };

    public static AcpAdapterDescriptor PackagedAdapter(
        string id = "adapter.packaged",
        params AcpSetupParameterDefinition[] parameters)
        => new()
        {
            Component = new AcpComponentDescriptor
            {
                Id = id,
                DisplayName = "Packaged ACP Adapter",
                Distribution = AcpDistributionKind.Npx,
                DetectionMode = AcpComponentDetectionMode.GlobalNodePackage,
                ProbeCommand = "npx",
                PackageId = AdapterPackage
            },
            LaunchTemplate = new AcpLaunchTemplate
            {
                Command = "npx",
                FixedArguments = new[] { AdapterPackage },
                Parameters = parameters
            }
        };

    public static AcpAgentDescriptor Agent(
        string id = "agent.test",
        AcpComponentDescriptor? runtime = null,
        params AcpAdapterDescriptor[] adapters)
        => new()
        {
            Id = id,
            DisplayName = "Test Agent",
            Description = "AcpSetup_Agent_Test_Description",
            Runtime = runtime ?? Runtime(),
            Adapters = adapters.Length > 0 ? adapters : new[] { BuiltInAdapter() },
            RecommendedAdapterId = adapters.Length > 0 ? adapters[0].Component.Id : "adapter.builtin"
        };

    public static AcpSetupParameterDefinition Parameter(
        string key = "--model",
        bool isRequired = false,
        string defaultValue = "",
        string description = "",
        params string[] allowedValues)
        => new()
        {
            Key = key,
            DisplayName = key,
            DefaultValue = defaultValue,
            Description = description,
            IsRequired = isRequired,
            AllowedValues = allowedValues
        };

    public static AcpSetupWizardViewModel CreateWizard(
        IAcpAgentCatalog catalog,
        IAcpExecutableProbe probe,
        IAcpComponentInstaller installer,
        IAcpSetupConnectivityTester connectivityTester,
        IConfigurationService configurationService,
        IStringLocalizer<CoreStrings>? localizer = null)
        => new(
            new AcpSetupWizardOrchestrator(
                catalog,
                probe,
                installer,
                connectivityTester,
                configurationService),
            new ImmediateUiDispatcher(),
            NullLogger<AcpSetupWizardViewModel>.Instance,
            localizer);

    /// <summary>The healthy-machine answer most tests walk against.</summary>
    public static class WellKnownResults
    {
        public static AcpSetupTestResult Success()
            => AcpSetupTestResult.Success(protocolVersion: 1, agentName: "test-agent");
    }
}
