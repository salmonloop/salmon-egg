using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Curated snapshot of the ACP agent registry (<c>cdn.agentclientprotocol.com/registry/v1</c>),
/// limited to agents whose ACP entry point is a documented stdio command.
///
/// Installation packages are intentionally unpinned so the wizard does not go stale when the registry
/// advances. Pinning would freeze users to whatever version happened to ship with the app.
/// </summary>
internal static class AcpAgentCatalogEntries
{
    internal static IReadOnlyList<AcpAgentDescriptor> Create()
        => new[]
        {
            CreateClaudeCode(),
            CreateGeminiCli(),
            CreateCodex(),
            CreateGitHubCopilot(),
            CreateQwenCode(),
            CreateCline(),
            CreateAuggie(),
            CreateGoose()
        };

    /// <summary>
    /// Agent fronted by a separately packaged adapter: the runtime CLI and the adapter entry point are
    /// detected independently, while the package coordinate is used only when the wizard installs the
    /// adapter.
    ///
    /// The executable is the stable interoperability boundary. The same command can be supplied by a
    /// renamed package, a third-party distribution, or a standalone install, and all are usable when the
    /// later ACP handshake succeeds. Treating one publisher's package coordinate as adapter identity
    /// rejects those valid installations before the protocol can verify them.
    /// </summary>
    private static AcpAgentDescriptor CreateAdapterFrontedAgent(
        string agentId,
        string displayName,
        string descriptionKey,
        string runtimeProbeCommand,
        string runtimePackageId,
        Uri runtimeDocumentation,
        string adapterId,
        string adapterDisplayName,
        string adapterProbeCommand,
        string adapterPackageId,
        Uri adapterDocumentation,
        IReadOnlyList<AcpSetupParameterDefinition> parameters)
        => new()
        {
            Id = agentId,
            DisplayName = displayName,
            Description = descriptionKey,
            RecommendedAdapterId = adapterId,
            Runtime = new AcpComponentDescriptor
            {
                Id = agentId + "-cli",
                DisplayName = displayName,
                Distribution = AcpDistributionKind.Npx,
                DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
                ProbeCommand = runtimeProbeCommand,
                ProbeVersionArguments = new[] { "--version" },
                PackageId = runtimePackageId,
                InstallDocumentation = runtimeDocumentation
            },
            Adapters = new[]
            {
                new AcpAdapterDescriptor
                {
                    Component = new AcpComponentDescriptor
                    {
                        Id = adapterId,
                        DisplayName = adapterDisplayName,
                        Distribution = AcpDistributionKind.Npx,
                        DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
                        ProbeCommand = adapterProbeCommand,
                        PackageId = adapterPackageId,
                        InstallDocumentation = adapterDocumentation
                    },
                    LaunchTemplate = new AcpLaunchTemplate
                    {
                        Command = adapterProbeCommand,
                        Parameters = parameters
                    }
                }
            }
        };

    /// <summary>
    /// Agent that speaks ACP itself: the runtime CLI is both the thing to install and the adapter, so
    /// the adapter is reported as built in.
    /// </summary>
    /// <remarks>
    /// The launch command is the same executable the runtime probe resolves, so the wizard starts what it
    /// verified. Routing the launch through <c>npx &lt;package&gt;</c> instead would re-resolve the
    /// package at every start: a second resolution path that can disagree with the probe, an extra
    /// dependency on the Node launcher being reachable, and — with npx's auto-install behaviour — a
    /// silent network fetch inside a transport the agent speaks over stdio. Every package in this
    /// catalog publishes its CLI under exactly the name probed, so the two are equivalent when the
    /// package is installed and only the direct form is honest when it is not.
    /// </remarks>
    private static AcpAgentDescriptor CreateSelfHostedAgent(
        string agentId,
        string displayName,
        string descriptionKey,
        string probeCommand,
        string packageId,
        Uri documentation,
        IReadOnlyList<string> launchArguments,
        IReadOnlyList<AcpSetupParameterDefinition> parameters,
        IReadOnlyDictionary<string, string>? fixedEnvironment = null)
    {
        var adapterId = agentId + "-builtin-acp";
        return new AcpAgentDescriptor
        {
            Id = agentId,
            DisplayName = displayName,
            Description = descriptionKey,
            RecommendedAdapterId = adapterId,
            Runtime = new AcpComponentDescriptor
            {
                Id = agentId + "-cli",
                DisplayName = displayName,
                Distribution = AcpDistributionKind.Npx,
                DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
                ProbeCommand = probeCommand,
                ProbeVersionArguments = new[] { "--version" },
                PackageId = packageId,
                InstallDocumentation = documentation
            },
            Adapters = new[]
            {
                new AcpAdapterDescriptor
                {
                    Component = new AcpComponentDescriptor
                    {
                        Id = adapterId,
                        DisplayName = displayName + " (ACP)",
                        Distribution = AcpDistributionKind.BuiltIn,
                        DetectionMode = AcpComponentDetectionMode.None,
                        InstallDocumentation = documentation
                    },
                    LaunchTemplate = new AcpLaunchTemplate
                    {
                        Command = probeCommand,
                        FixedArguments = launchArguments,
                        FixedEnvironment = fixedEnvironment
                            ?? new Dictionary<string, string>(StringComparer.Ordinal),
                        Parameters = parameters
                    }
                }
            }
        };
    }

    private static AcpAgentDescriptor CreateClaudeCode()
        => CreateAdapterFrontedAgent(
            agentId: "claude-code",
            displayName: "Claude Code",
            descriptionKey: "AcpSetup_Agent_ClaudeCode_Description",
            runtimeProbeCommand: "claude",
            runtimePackageId: "@anthropic-ai/claude-code",
            runtimeDocumentation: new Uri("https://docs.claude.com/en/docs/claude-code/setup"),
            adapterId: "claude-agent-acp",
            adapterDisplayName: "Claude Agent ACP",
            adapterProbeCommand: "claude-agent-acp",
            adapterPackageId: "@agentclientprotocol/claude-agent-acp",
            adapterDocumentation: new Uri("https://github.com/agentclientprotocol/claude-agent-acp"),
            parameters: Array.Empty<AcpSetupParameterDefinition>());

    private static AcpAgentDescriptor CreateCodex()
        => CreateAdapterFrontedAgent(
            agentId: "codex",
            displayName: "Codex CLI",
            descriptionKey: "AcpSetup_Agent_Codex_Description",
            runtimeProbeCommand: "codex",
            runtimePackageId: "@openai/codex",
            runtimeDocumentation: new Uri("https://developers.openai.com/codex/cli/"),
            adapterId: "codex-acp",
            adapterDisplayName: "Codex ACP",
            adapterProbeCommand: "codex-acp",
            adapterPackageId: "@agentclientprotocol/codex-acp",
            adapterDocumentation: new Uri("https://github.com/agentclientprotocol/codex-acp"),
            parameters: Array.Empty<AcpSetupParameterDefinition>());

    private static AcpAgentDescriptor CreateGeminiCli()
        => CreateSelfHostedAgent(
            agentId: "gemini",
            displayName: "Gemini CLI",
            descriptionKey: "AcpSetup_Agent_Gemini_Description",
            probeCommand: "gemini",
            packageId: "@google/gemini-cli",
            documentation: new Uri("https://geminicli.com"),
            launchArguments: new[] { "--acp" },
            parameters: new[]
            {
                AcpSetupParameters.ModelEnvironment("GEMINI_MODEL", "gemini-2.5-pro")
            });

    private static AcpAgentDescriptor CreateGitHubCopilot()
        => CreateSelfHostedAgent(
            agentId: "github-copilot",
            displayName: "GitHub Copilot CLI",
            descriptionKey: "AcpSetup_Agent_GitHubCopilot_Description",
            probeCommand: "copilot",
            packageId: "@github/copilot",
            documentation: new Uri("https://github.com/features/copilot/cli/"),
            launchArguments: new[] { "--acp" },
            parameters: Array.Empty<AcpSetupParameterDefinition>());

    private static AcpAgentDescriptor CreateQwenCode()
        => CreateSelfHostedAgent(
            agentId: "qwen-code",
            displayName: "Qwen Code",
            descriptionKey: "AcpSetup_Agent_QwenCode_Description",
            probeCommand: "qwen",
            packageId: "@qwen-code/qwen-code",
            documentation: new Uri("https://qwenlm.github.io/qwen-code-docs/en/users/overview"),
            launchArguments: new[] { "--acp" },
            parameters: Array.Empty<AcpSetupParameterDefinition>());

    private static AcpAgentDescriptor CreateCline()
        => CreateSelfHostedAgent(
            agentId: "cline",
            displayName: "Cline",
            descriptionKey: "AcpSetup_Agent_Cline_Description",
            probeCommand: "cline",
            packageId: "cline",
            documentation: new Uri("https://cline.bot/cli"),
            launchArguments: new[] { "--acp" },
            parameters: Array.Empty<AcpSetupParameterDefinition>());

    private static AcpAgentDescriptor CreateAuggie()
        => CreateSelfHostedAgent(
            agentId: "auggie",
            displayName: "Auggie CLI",
            descriptionKey: "AcpSetup_Agent_Auggie_Description",
            probeCommand: "auggie",
            packageId: "@augmentcode/auggie",
            documentation: new Uri("https://www.augmentcode.com/"),
            launchArguments: new[] { "--acp" },
            parameters: Array.Empty<AcpSetupParameterDefinition>(),
            // Registry declares this so the adapter does not self-update mid-session, which would
            // restart the process the ACP transport is attached to.
            fixedEnvironment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AUGMENT_DISABLE_AUTO_UPDATE"] = "1"
            });

    /// <summary>
    /// Distributed as a prebuilt binary, so the wizard detects the CLI on PATH and points at the
    /// vendor installer instead of offering one-click installation.
    /// </summary>
    private static AcpAgentDescriptor CreateGoose()
    {
        const string adapterId = "goose-builtin-acp";
        var documentation = new Uri("https://block.github.io/goose/docs/getting-started/installation");
        return new AcpAgentDescriptor
        {
            Id = "goose",
            DisplayName = "goose",
            Description = "AcpSetup_Agent_Goose_Description",
            RecommendedAdapterId = adapterId,
            Runtime = new AcpComponentDescriptor
            {
                Id = "goose-cli",
                DisplayName = "goose",
                Distribution = AcpDistributionKind.Binary,
                DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
                ProbeCommand = "goose",
                ProbeVersionArguments = new[] { "--version" },
                InstallDocumentation = documentation
            },
            Adapters = new[]
            {
                new AcpAdapterDescriptor
                {
                    Component = new AcpComponentDescriptor
                    {
                        Id = adapterId,
                        DisplayName = "goose (ACP)",
                        Distribution = AcpDistributionKind.BuiltIn,
                        DetectionMode = AcpComponentDetectionMode.None,
                        InstallDocumentation = documentation
                    },
                    LaunchTemplate = new AcpLaunchTemplate
                    {
                        Command = "goose",
                        FixedArguments = new[] { "acp" },
                        Parameters = Array.Empty<AcpSetupParameterDefinition>()
                    }
                }
            }
        };
    }
}
