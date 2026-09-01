using System;

namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// The toolchain a package-manager distribution needs before anything can be installed through it.
/// </summary>
/// <remarks>
/// Modelled because it is the one prerequisite the catalog never declares and the machine may not have.
/// A component's distribution says how it arrives, not whether the thing that carries it exists here — so
/// "installable" was previously answered from authoring data alone and could only be contradicted by a
/// failed install. Naming the requirement lets the wizard ask first.
///
/// Derived from the distribution rather than declared per component: every Node package needs the same
/// Node toolchain, and asking each catalog entry to repeat that would let entries disagree about a fact
/// that is not theirs to hold.
///
/// The launcher name is deliberately not repeated here. Which <em>executable</em> answers for a toolchain
/// depends on the user's overrides and on which launcher was resolved, which is
/// <see cref="AcpPackageManagerCommand"/>'s decision; this type only says which toolchain is needed and
/// where its documentation lives.
/// </remarks>
public sealed class AcpToolchainRequirement
{
    private AcpToolchainRequirement(string displayName, Uri documentation, bool hasAutomaticInstallPath)
    {
        DisplayName = displayName;
        Documentation = documentation;
        HasAutomaticInstallPath = hasAutomaticInstallPath;
    }

    /// <summary>Human-facing toolchain name. A vendor product name, so it is not localized.</summary>
    public string DisplayName { get; }

    /// <summary>Where the user is sent to install the toolchain themselves.</summary>
    public Uri Documentation { get; }

    /// <summary>
    /// True when the app publishes an automatic install path for this toolchain.
    /// </summary>
    /// <remarks>
    /// A statement about this app's own capability, not about the machine — deliberately the same
    /// distinction <see cref="AcpComponentDescriptor.HasAutomaticInstallPath"/> draws. Whether the toolchain
    /// is already here is a probe's answer, and whether this platform can run an installer at all is
    /// <see cref="Services.AcpSetup.IAcpToolchainInstaller.SupportsAutomaticInstall"/>'s; offering the user
    /// a button needs all three, so callers combine them rather than reading this alone.
    ///
    /// <see cref="Documentation"/> stays meaningful either way: it is the fallback when an install fails,
    /// and the only route for a toolchain with no automatic path.
    /// </remarks>
    public bool HasAutomaticInstallPath { get; }

    /// <summary>
    /// The toolchain <paramref name="distribution"/> needs, or null when it needs none.
    /// </summary>
    /// <remarks>
    /// Null for the distributions that carry no package manager at all — a built-in adapter installs
    /// nothing, and a prebuilt binary is fetched by the user out of band. Callers must read null as "no
    /// prerequisite to check" rather than as "prerequisite missing".
    /// </remarks>
    public static AcpToolchainRequirement? For(AcpDistributionKind distribution)
        => distribution switch
        {
            AcpDistributionKind.Npx => Node,
            AcpDistributionKind.Uvx => Uv,
            _ => null
        };

    /// <summary>
    /// The Node toolchain, which supplies both <c>node</c> and <c>npm</c>.
    /// </summary>
    /// <remarks>
    /// Documented at the download page rather than at a version-specific installer, and named without a
    /// minimum version: each package declares its own <c>engines</c> range, so a version asserted here
    /// would be a claim this type cannot keep true for every catalog entry.
    /// </remarks>
    public static AcpToolchainRequirement Node { get; } = new(
        "Node.js",
        new Uri("https://nodejs.org/en/download"),
        hasAutomaticInstallPath: true);

    /// <summary>The uv toolchain, which supplies both <c>uv</c> and <c>uvx</c>.</summary>
    /// <remarks>
    /// Named without a minimum version, for the same reason <see cref="Node"/> is: each tool declares its own
    /// <c>requires-python</c> and uv constraints, so a version asserted here could not stay true for every
    /// component. The install source pins which uv build the wizard fetches.
    /// </remarks>
    public static AcpToolchainRequirement Uv { get; } = new(
        "uv",
        new Uri("https://docs.astral.sh/uv/getting-started/installation/"),
        hasAutomaticInstallPath: true);
}
