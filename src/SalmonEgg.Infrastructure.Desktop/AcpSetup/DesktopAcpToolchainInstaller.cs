using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Installs a missing toolchain from its vendor's official archive: download, verify the published
/// SHA-256, unpack into per-user application data, prove the result runs, and put it on the user's PATH.
/// </summary>
/// <remarks>
/// This exists because the wizard's advice used to end at a documentation link. A user whose machine had no
/// Node was told to leave the app, find an installer, and come back — and the step most likely to be got
/// wrong (installing somewhere the app cannot see) was entirely theirs to get right.
///
/// The install is per-user and unprivileged throughout. A system location would need elevation, which makes
/// a declined prompt indistinguishable from a broken feature, and would leave files behind that uninstalling
/// the app cannot remove.
///
/// Verification is fail-closed and not optional. The payload is executable code fetched over the network, so
/// a digest that cannot be located is treated exactly like one that does not match: nothing is installed.
/// The alternative — install and hope — is the one outcome worth avoiding absolutely.
/// </remarks>
public sealed class DesktopAcpToolchainInstaller : IAcpToolchainInstaller
{
    /// <summary>Advice key for a platform with no published automatic install.</summary>
    internal const string UnsupportedPlatformRemediationKey = "AcpSetup_Toolchain_UnsupportedPlatform";

    /// <summary>Advice key for a download or verification that did not complete.</summary>
    internal const string DownloadFailedRemediationKey = "AcpSetup_Toolchain_DownloadFailed";

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Func<string> _installRootFactory;
    private readonly Func<AcpToolchainRequirement, AcpToolchainDownload?> _resolveDownload;

    /// <param name="httpClientFactory">
    /// Supplies the client used for both the archive and its checksum. Injectable so the download path can
    /// be tested against a local server rather than the vendor's.
    /// </param>
    /// <param name="installRootFactory">
    /// Yields the directory toolchains are installed under. Must agree with what
    /// <see cref="ToolchainScanSearchPathSource"/> scans, or an install would succeed and stay
    /// undiscoverable; both default to the same app-data location.
    /// </param>
    /// <param name="resolveDownload">
    /// Selects the archive to install for a requirement. Injectable so the download, verification, and
    /// unpack path can be exercised against a local server: the shipped resolver names vendor URLs, and a
    /// test bound to those would either reach the network or never reach this code at all — leaving the
    /// checksum refusal, the traversal guard, and the post-extract probe unverified.
    /// </param>
    public DesktopAcpToolchainInstaller(
        Func<HttpClient>? httpClientFactory = null,
        Func<string>? installRootFactory = null,
        Func<AcpToolchainRequirement, AcpToolchainDownload?>? resolveDownload = null)
    {
        _httpClientFactory = httpClientFactory ?? CreateDefaultHttpClient;
        _installRootFactory = installRootFactory ?? DefaultInstallRoot;
        _resolveDownload = resolveDownload ?? ResolvePublishedDownload;
    }

    public bool SupportsAutomaticInstall => true;

    /// <summary>The directory toolchains are installed under by default.</summary>
    internal static string DefaultInstallRoot()
        => Path.Combine(
            SalmonEggPaths.GetAppDataRootPath(),
            ToolchainScanSearchPathSource.ToolchainsDirectoryName);

    public async Task<AcpToolchainInstallResult> InstallAsync(
        AcpToolchainRequirement requirement,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (!requirement.HasAutomaticInstallPath)
        {
            return AcpToolchainInstallResult.Failure(
                requirement,
                $"'{requirement.DisplayName}' has no automatic install path.",
                remediationKey: UnsupportedPlatformRemediationKey);
        }

        if (_resolveDownload(requirement) is not { } download)
        {
            return AcpToolchainInstallResult.Failure(
                requirement,
                $"No published {requirement.DisplayName} build for "
                + $"{RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}.",
                remediationKey: UnsupportedPlatformRemediationKey);
        }

        var version = ResolveVersionDirectoryName(requirement);
        var destination = Path.Combine(_installRootFactory(), ResolveToolchainDirectoryName(requirement), version);
        var staging = destination + ".incoming-" + Guid.NewGuid().ToString("N")[..8];
        var archivePath = Path.Combine(Path.GetTempPath(), "salmonegg-toolchain-" + Guid.NewGuid().ToString("N"));

        try
        {
            onOutput?.Invoke($"Downloading {requirement.DisplayName} {version}…");
            var actualDigest = await DownloadAsync(download, archivePath, onOutput, cancellationToken)
                .ConfigureAwait(false);

            onOutput?.Invoke("Verifying checksum…");
            var expectedDigest = await ReadExpectedDigestAsync(download, cancellationToken).ConfigureAwait(false);
            if (expectedDigest is null)
            {
                return AcpToolchainInstallResult.Failure(
                    requirement,
                    $"No published checksum for '{ResolveArchiveFileName(download)}'.",
                    remediationKey: DownloadFailedRemediationKey);
            }

            if (!string.Equals(expectedDigest, actualDigest, StringComparison.OrdinalIgnoreCase))
            {
                // Refused rather than reported-and-installed. Anything else would put unverified executable
                // code on the user's PATH.
                return AcpToolchainInstallResult.Failure(
                    requirement,
                    $"Checksum mismatch: expected {expectedDigest}, downloaded {actualDigest}.",
                    remediationKey: DownloadFailedRemediationKey);
            }

            onOutput?.Invoke("Extracting…");
            // Unpacked to a staging directory and moved into place, so an interrupted extraction cannot
            // leave a half-populated version directory that the scan would then offer as a candidate.
            await AcpToolchainArchive.ExtractAsync(archivePath, staging, download, cancellationToken)
                .ConfigureAwait(false);

            var stagedBin = ResolveBinDirectory(staging, download);
            if (VerifyContents(stagedBin, download) is { } missing)
            {
                return AcpToolchainInstallResult.Failure(
                    requirement,
                    $"The extracted archive has no '{missing}'.",
                    remediationKey: DownloadFailedRemediationKey);
            }

            onOutput?.Invoke("Verifying the installed toolchain…");
            var probe = await AcpSetupProcessRunner
                .RunAsync(
                    Path.Combine(stagedBin, download.VerifyCommand),
                    download.VerifyArguments,
                    VerifyTimeout,
                    onOutput,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!probe.Succeeded)
            {
                // Present on disk is not the same as runnable: an archive for the wrong architecture
                // unpacks cleanly and fails here. Reporting success would hand the wizard a toolchain that
                // cannot execute.
                return AcpToolchainInstallResult.Failure(
                    requirement,
                    probe.FailureDetail
                        ?? $"'{download.VerifyCommand}' did not run after extraction.",
                    probe.CombinedOutput,
                    DownloadFailedRemediationKey);
            }

            ReplaceDirectory(staging, destination);
            var binDirectory = ResolveBinDirectory(destination, download);

            var registration = UserPathRegistration.Register(binDirectory, onOutput);
            onOutput?.Invoke($"Installed to {binDirectory}");
            return AcpToolchainInstallResult.Success(
                requirement,
                binDirectory,
                registration,
                probe.CombinedOutput);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return AcpToolchainInstallResult.Failure(
                requirement,
                exception.Message,
                remediationKey: DownloadFailedRemediationKey);
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>The published build for this machine, or null when there is none.</summary>
    private static AcpToolchainDownload? ResolvePublishedDownload(AcpToolchainRequirement requirement)
    {
        var platform = ResolveCurrentPlatform();
        if (ResolveCurrentArchitecture() is not { } architecture)
        {
            return null;
        }

        if (ReferenceEquals(requirement, AcpToolchainRequirement.Node))
        {
            return AcpToolchainInstallSource.ResolveNode(platform, architecture);
        }

        if (ReferenceEquals(requirement, AcpToolchainRequirement.Uv))
        {
            return AcpToolchainInstallSource.ResolveUv(platform, architecture, PrefersMuslBuild());
        }

        // A requirement this layer has no source for. Null rather than a guess: the caller turns it into
        // "no automatic install on this platform" and keeps the vendor's documentation on screen.
        return null;
    }

    /// <summary>True when this host's C library is musl rather than glibc.</summary>
    /// <remarks>
    /// Read from the runtime identifier, which spells the libc out (<c>linux-musl-arm64</c> versus
    /// <c>linux-arm64</c>). It cannot be inferred from <see cref="Architecture"/>, and the distinction is not
    /// cosmetic: a glibc archive extracts cleanly on Alpine and then fails to exec, which would surface as a
    /// corrupt-download error rather than as the wrong build being chosen.
    /// </remarks>
    private static bool PrefersMuslBuild()
        => RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// This host's operating system, as the toolchain sources name it.
    /// </summary>
    /// <remarks>
    /// Inspecting the host belongs here rather than in the domain, which selects a build for a target it is
    /// told about. Keeping the question on this side is what lets the resolvers be exercised for every
    /// platform from one test process.
    /// </remarks>
    private static AcpToolchainOperatingSystem ResolveCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return AcpToolchainOperatingSystem.Windows;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? AcpToolchainOperatingSystem.MacOS
            : AcpToolchainOperatingSystem.Linux;
    }

    /// <summary>
    /// This host's architecture, or null when no supported toolchain targets it.
    /// </summary>
    /// <remarks>
    /// Null rather than a nearest guess: a build for the wrong architecture extracts cleanly and then fails
    /// to exec, which would surface as a corrupt download instead of as an unsupported platform.
    /// </remarks>
    private static AcpToolchainArchitecture? ResolveCurrentArchitecture()
        => RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => AcpToolchainArchitecture.X64,
            Architecture.Arm64 => AcpToolchainArchitecture.Arm64,
            Architecture.X86 => AcpToolchainArchitecture.X86,
            Architecture.Arm => AcpToolchainArchitecture.Arm,
            _ => null
        };

    private static string ResolveToolchainDirectoryName(AcpToolchainRequirement requirement)
        => ReferenceEquals(requirement, AcpToolchainRequirement.Node) ? "node" : "uv";

    /// <summary>
    /// The version directory an install lands in.
    /// </summary>
    /// <remarks>
    /// A real version rather than a fixed name like "current", because the scan finds these through a
    /// wildcard version segment: a non-versioned directory would sit outside the layout and stay
    /// undiscoverable. It also keeps an upgrade from overwriting a toolchain that something may still be
    /// running from.
    /// </remarks>
    private static string ResolveVersionDirectoryName(AcpToolchainRequirement requirement)
    {
        if (ReferenceEquals(requirement, AcpToolchainRequirement.Node))
        {
            return AcpToolchainInstallSource.NodeVersion;
        }

        return ReferenceEquals(requirement, AcpToolchainRequirement.Uv)
            ? AcpToolchainInstallSource.UvVersion
            : "current";
    }

    /// <summary>
    /// Streams the archive to disk and returns its SHA-256, computed as the bytes arrive.
    /// </summary>
    /// <remarks>
    /// Hashed while streaming rather than by re-reading the file: these archives are tens of megabytes, and
    /// buffering one in memory to hash it would be a needless allocation on a desktop that may be short of
    /// it.
    /// </remarks>
    private async Task<string> DownloadAsync(
        AcpToolchainDownload download,
        string archivePath,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory();
        client.Timeout = DownloadTimeout;

        using var response = await client
            .GetAsync(download.Archive, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(archivePath))
        {
            var buffer = new byte[81920];
            long copied = 0;
            var lastReportedPercent = -1;

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;

                if (total is > 0 && onOutput is not null)
                {
                    // Reported per whole percent so a long download shows progress without flooding the
                    // wizard's bounded output log.
                    var percent = (int)(copied * 100 / total.Value);
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        onOutput($"Downloaded {percent}%");
                    }
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<string?> ReadExpectedDigestAsync(
        AcpToolchainDownload download,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory();
        var document = await client.GetStringAsync(download.Checksum, cancellationToken).ConfigureAwait(false);
        return AcpToolchainChecksum.Parse(
            document,
            download.ChecksumFormat,
            ResolveArchiveFileName(download));
    }

    private static string ResolveArchiveFileName(AcpToolchainDownload download)
        => download.Archive.Segments[^1];

    private static string ResolveBinDirectory(string installRoot, AcpToolchainDownload download)
        => string.IsNullOrEmpty(download.BinSubdirectory)
            ? installRoot
            : Path.Combine(installRoot, download.BinSubdirectory);

    /// <summary>The first declared executable that is absent, or null when all are present.</summary>
    private static string? VerifyContents(string binDirectory, AcpToolchainDownload download)
    {
        foreach (var executable in download.VerifyExecutables)
        {
            var path = Path.Combine(binDirectory, executable);

            // Existence is checked through the link rather than its target, because npm and npx in Node's
            // POSIX archive are symlinks and File.Exists follows them — which is the right test: a link
            // whose target is missing is a broken install.
            if (!File.Exists(path))
            {
                return executable;
            }
        }

        return null;
    }

    /// <summary>Moves a verified staging directory into its final location, replacing any prior install.</summary>
    private static void ReplaceDirectory(string staging, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (Directory.Exists(destination))
        {
            // A reinstall of the same version replaces it. Deleted immediately before the move so the
            // window in which neither copy exists is as short as the filesystem allows.
            Directory.Delete(destination, recursive: true);
        }

        Directory.Move(staging, destination);
    }

    private static HttpClient CreateDefaultHttpClient()
        => new(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover file in the temp directory is the operating system's to reclaim.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Same reasoning: a failed cleanup must not mask the install's own outcome.
        }
    }
}
