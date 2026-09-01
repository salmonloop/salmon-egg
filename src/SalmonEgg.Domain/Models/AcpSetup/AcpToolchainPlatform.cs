namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>The operating system a published toolchain build targets.</summary>
/// <remarks>
/// Named here rather than reusing <c>System.Runtime.InteropServices.OSPlatform</c> so selecting a build
/// stays a pure data decision. That type belongs to the family the domain must not touch: asking it
/// anything means asking the host it is running on, and this layer answers about a target instead —
/// which is what lets one process resolve every platform's archive under test. The host is inspected in
/// the desktop layer and mapped onto these values.
/// </remarks>
public enum AcpToolchainOperatingSystem
{
    Linux,

    MacOS,

    Windows
}

/// <summary>The CPU architecture a published toolchain build targets.</summary>
/// <remarks>
/// A closed set covering only what the vendors publish and this app supports. Anything outside it
/// resolves to no download, which is the honest answer for a platform with no published build.
/// </remarks>
public enum AcpToolchainArchitecture
{
    X64,

    Arm64,

    X86,

    Arm
}
