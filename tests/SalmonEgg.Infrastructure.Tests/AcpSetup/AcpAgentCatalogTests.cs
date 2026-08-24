using System;
using System.Linq;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.AcpSetup;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Contracts the shipping agent catalog has to hold, as opposed to how it is written.
/// </summary>
/// <remarks>
/// The wizard's promise is that a configuration it verified is a configuration that starts. That only
/// holds when the executable a component is probed for is the executable its launch plan runs — the
/// catalog previously probed each self-hosted agent's own CLI and then launched it through
/// <c>npx &lt;package&gt;</c>, so the verified artifact and the started artifact were resolved by
/// different mechanisms and could disagree.
/// </remarks>
public sealed class AcpAgentCatalogTests
{
    [Fact]
    public void EveryAgent_DeclaresARecommendedAdapterThatExists()
    {
        foreach (var agent in new AcpAgentCatalog().Agents)
        {
            var adapter = agent.ResolveRecommendedAdapter();
            Assert.NotNull(adapter);
            Assert.Equal(agent.RecommendedAdapterId, adapter!.Component.Id);
        }
    }

    /// <summary>
    /// A built-in adapter is the agent's own CLI speaking ACP, so its launch command must be the command
    /// the runtime probe resolves. Anything else re-resolves the agent at launch through a path the
    /// wizard never checked.
    /// </summary>
    [Fact]
    public void BuiltInAdapters_LaunchTheExecutableTheRuntimeProbeResolves()
    {
        var failures = new System.Collections.Generic.List<string>();

        foreach (var agent in new AcpAgentCatalog().Agents)
        {
            foreach (var adapter in agent.Adapters.Where(adapter => adapter.Component.IsBuiltIn))
            {
                var launched = adapter.LaunchTemplate.Command;
                var probed = agent.Runtime.ProbeCommand;
                if (!string.Equals(launched, probed, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{agent.Id}: probes '{probed}' but launches '{launched}'."
                        + " A built-in adapter must start the executable that was verified.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A built-in adapter's launch arguments must not carry a package coordinate. Passing one means the
    /// command is a package runner rather than the agent, which is the shape this catalog moved away
    /// from.
    /// </summary>
    [Fact]
    public void BuiltInAdapters_DoNotPassPackageCoordinatesAsArguments()
    {
        var failures = new System.Collections.Generic.List<string>();

        foreach (var agent in new AcpAgentCatalog().Agents)
        {
            foreach (var adapter in agent.Adapters.Where(adapter => adapter.Component.IsBuiltIn))
            {
                foreach (var argument in adapter.LaunchTemplate.FixedArguments)
                {
                    if (argument.StartsWith('@') || string.Equals(argument, "-y", StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{agent.Id}: launch argument '{argument}' is a package-runner argument,"
                            + " so the launch is not starting the probed executable directly.");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A component the wizard offers to install must name the package to install, and one it cannot
    /// install must offer documentation instead. Otherwise a row shows a button that cannot work, or no
    /// route at all.
    /// </summary>
    [Fact]
    public void EveryComponent_OffersEitherAnInstallPackageOrDocumentation()
    {
        var failures = new System.Collections.Generic.List<string>();

        foreach (var agent in new AcpAgentCatalog().Agents)
        {
            Check(agent.Id + ".runtime", agent.Runtime);
            foreach (var adapter in agent.Adapters)
            {
                Check(agent.Id + "." + adapter.Component.Id, adapter.Component);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        void Check(string label, AcpComponentDescriptor component)
        {
            if (component.SupportsAutomaticInstall)
            {
                if (string.IsNullOrWhiteSpace(component.PackageId))
                {
                    failures.Add($"{label}: offers automatic install with no PackageId.");
                }

                return;
            }

            if (component.InstallDocumentation is null)
            {
                failures.Add($"{label}: cannot be installed automatically and offers no documentation.");
            }
        }
    }

    /// <summary>
    /// A component probed by resolving an executable must name one; otherwise detection resolves the
    /// empty string and reports every machine as missing it.
    /// </summary>
    [Fact]
    public void ExecutableProbedComponents_NameTheCommandToResolve()
    {
        foreach (var agent in new AcpAgentCatalog().Agents)
        {
            foreach (var component in Components(agent))
            {
                if (component.DetectionMode is AcpComponentDetectionMode.None
                    or AcpComponentDetectionMode.Manual)
                {
                    continue;
                }

                Assert.False(
                    string.IsNullOrWhiteSpace(component.ProbeCommand),
                    $"{agent.Id}/{component.Id} is probed as {component.DetectionMode} but names no command.");
            }
        }

        static System.Collections.Generic.IEnumerable<AcpComponentDescriptor> Components(
            AcpAgentDescriptor agent)
        {
            yield return agent.Runtime;
            foreach (var adapter in agent.Adapters)
            {
                yield return adapter.Component;
            }
        }
    }
}
