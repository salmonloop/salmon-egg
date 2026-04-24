using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SalmonEgg.GuiTests.Windows;

internal static class GuiTestGate
{
    private const string EnableEnvVar = "SALMONEGG_GUI";
    private const string CurrentInstallMarkerRelativePath = "artifacts\\msix\\current-install.json";

    public static void RequireEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable(EnableEnvVar);
        Skip.IfNot(
            string.Equals(enabled, "1", StringComparison.Ordinal),
            $"Windows GUI smoke tests are opt-in. Set {EnableEnvVar}=1 after installing/running the MSIX build.");

        var install = GetRequiredCurrentInstall();
        Skip.IfNot(
            install.IsCurrentInstall,
            install.FailureMessage
            ?? $"Windows GUI smoke tests require the current repo MSIX install marker. Run .tools/run-winui3-msix.ps1 -Configuration Debug first, then set {EnableEnvVar}=1.");
    }

    public static CurrentInstallValidationResult GetRequiredCurrentInstall()
    {
        var markerPath = GetCurrentInstallMarkerPath();
        if (!File.Exists(markerPath))
        {
            return CurrentInstallValidationResult.Fail(
                $"Windows GUI smoke tests require '{markerPath}'. Run .tools/run-winui3-msix.ps1 -Configuration Debug to install the current repo MSIX before setting {EnableEnvVar}=1.");
        }

        MsixInstallMarker? marker;
        try
        {
            marker = LoadMarker(markerPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CurrentInstallValidationResult.Fail(
                $"Windows GUI smoke tests could not read install provenance marker '{markerPath}'. {ex.Message}");
        }

        if (marker is null)
        {
            return CurrentInstallValidationResult.Fail(
                $"Windows GUI smoke tests could not read install provenance marker '{markerPath}'.");
        }

        var actualPackageIdentity = marker.PackageIdentity;
        var markerInstalledExecutablePath = marker.InstalledExecutablePath;
        if (WindowsGuiAppSession.TryResolveInstalledExecutablePath(marker.PackageIdentity, out var resolvedInstalledExecutablePath, out _))
        {
            return ValidateCurrentInstall(markerPath, actualPackageIdentity, resolvedInstalledExecutablePath);
        }

        // Some GUI test hosts cannot query Get-AppxPackage for the current user even when
        // the provenance marker still points at the active Debug MSIX install. In that case,
        // accept the marker path only if the executable and hashes still match exactly.
        return ValidateCurrentInstall(markerPath, actualPackageIdentity, markerInstalledExecutablePath);
    }

    internal static CurrentInstallValidationResult ValidateCurrentInstall(
        string markerPath,
        string actualPackageIdentity,
        string actualInstalledExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualPackageIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualInstalledExecutablePath);

        var marker = LoadMarker(markerPath)
            ?? throw new InvalidOperationException($"Install provenance marker '{markerPath}' is empty.");

        if (!File.Exists(actualInstalledExecutablePath))
        {
            return CurrentInstallValidationResult.Fail(
                $"Installed SalmonEgg executable '{actualInstalledExecutablePath}' from package '{actualPackageIdentity}' was not found.");
        }

        if (!string.Equals(marker.Configuration, "Debug", StringComparison.Ordinal))
        {
            return CurrentInstallValidationResult.Fail(
                $"Current install marker configuration '{marker.Configuration}' is not supported for GUI smoke. Reinstall with .tools/run-winui3-msix.ps1 -Configuration Debug.");
        }

        if (!string.Equals(marker.PackageIdentity, actualPackageIdentity, StringComparison.Ordinal))
        {
            return CurrentInstallValidationResult.Fail(
                $"Current installed package identity '{actualPackageIdentity}' does not match marker package identity '{marker.PackageIdentity}'.");
        }

        if (!string.Equals(marker.InstalledExecutablePath, actualInstalledExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return CurrentInstallValidationResult.Fail(
                $"Current installed executable path '{actualInstalledExecutablePath}' does not match marker path '{marker.InstalledExecutablePath}'.");
        }

        var actualInstalledExecutableSha256 = ComputeSha256(actualInstalledExecutablePath);
        if (!string.Equals(marker.InstalledExecutableSha256, actualInstalledExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            return CurrentInstallValidationResult.Fail(
                $"Current installed executable SHA256 '{actualInstalledExecutableSha256}' does not match marker SHA256 '{marker.InstalledExecutableSha256}'.");
        }

        if (string.IsNullOrWhiteSpace(marker.MsixPath) || !File.Exists(marker.MsixPath))
        {
            return CurrentInstallValidationResult.Fail(
                $"Current install marker MSIX path '{marker.MsixPath}' was not found.");
        }

        var actualMsixSha256 = ComputeSha256(marker.MsixPath);
        if (!string.Equals(marker.MsixSha256, actualMsixSha256, StringComparison.OrdinalIgnoreCase))
        {
            return CurrentInstallValidationResult.Fail(
                $"Current install marker MSIX SHA256 '{actualMsixSha256}' does not match marker MSIX SHA256 '{marker.MsixSha256}'.");
        }

        return CurrentInstallValidationResult.Success(actualPackageIdentity, actualInstalledExecutablePath, actualInstalledExecutableSha256);
    }

    private static MsixInstallMarker? LoadMarker(string markerPath)
    {
        using var stream = File.OpenRead(markerPath);
        return JsonSerializer.Deserialize<MsixInstallMarker>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static string GetCurrentInstallMarkerPath()
    {
        return Path.Combine(FindRepoRoot(), CurrentInstallMarkerRelativePath);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    internal sealed record CurrentInstallValidationResult(
        bool IsCurrentInstall,
        string? FailureMessage,
        string? PackageIdentity,
        string? InstalledExecutablePath,
        string? InstalledExecutableSha256)
    {
        public static CurrentInstallValidationResult Fail(string failureMessage)
        {
            return new(false, failureMessage, null, null, null);
        }

        public static CurrentInstallValidationResult Success(
            string packageIdentity,
            string installedExecutablePath,
            string installedExecutableSha256)
        {
            return new(true, null, packageIdentity, installedExecutablePath, installedExecutableSha256);
        }
    }

    internal sealed record MsixInstallMarker
    {
        public string RepoRoot { get; init; } = string.Empty;

        public string Configuration { get; init; } = string.Empty;

        public string PackageIdentity { get; init; } = string.Empty;

        public string InstalledExecutablePath { get; init; } = string.Empty;

        public string InstalledExecutableSha256 { get; init; } = string.Empty;

        public string WrittenAtUtc { get; init; } = string.Empty;

        public string? MsixPath { get; init; }

        public string? MsixSha256 { get; init; }
    }
}

public sealed class GuiTestGateTests : IDisposable
{
    private readonly string _tempRoot;

    public GuiTestGateTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "salmonegg-gui-gate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void ValidateCurrentInstall_MismatchedExecutableHash_ReturnsFailure()
    {
        var installedExecutablePath = CreateFile("installed", "SalmonEgg.exe", "installed-binary-v1");
        var msixPath = CreateFile("artifacts", "SalmonEgg.msix", "msix-binary-v1");
        var markerPath = CreateMarker(
            configuration: "Debug",
            packageIdentity: "SalmonEgg.Package",
            installedExecutablePath: installedExecutablePath,
            installedExecutableSha256: ComputeSha256(installedExecutablePath),
            msixPath: msixPath,
            msixSha256: ComputeSha256(msixPath));
        File.WriteAllText(installedExecutablePath, "installed-binary-v2", Encoding.UTF8);

        var result = GuiTestGate.ValidateCurrentInstall(
            markerPath,
            actualPackageIdentity: "SalmonEgg.Package",
            actualInstalledExecutablePath: installedExecutablePath);

        Assert.False(result.IsCurrentInstall);
        Assert.Contains("SHA256", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCurrentInstall_MatchingIdentityPathAndHash_ReturnsSuccess()
    {
        var installedExecutablePath = CreateFile("installed", "SalmonEgg.exe", "installed-binary-v1");
        var msixPath = CreateFile("artifacts", "SalmonEgg.msix", "msix-binary-v1");
        var markerPath = CreateMarker(
            configuration: "Debug",
            packageIdentity: "SalmonEgg.Package",
            installedExecutablePath: installedExecutablePath,
            installedExecutableSha256: ComputeSha256(installedExecutablePath),
            msixPath: msixPath,
            msixSha256: ComputeSha256(msixPath));

        var result = GuiTestGate.ValidateCurrentInstall(
            markerPath,
            actualPackageIdentity: "SalmonEgg.Package",
            actualInstalledExecutablePath: installedExecutablePath);

        Assert.True(result.IsCurrentInstall);
        Assert.Null(result.FailureMessage);
        Assert.Equal(installedExecutablePath, result.InstalledExecutablePath);
    }

    [Fact]
    public void ValidateCurrentInstall_WhenAppxResolutionIsUnavailableButMarkerPathStillMatches_ReturnsSuccess()
    {
        var installedExecutablePath = CreateFile("installed", "SalmonEgg.exe", "installed-binary-v1");
        var msixPath = CreateFile("artifacts", "SalmonEgg.msix", "msix-binary-v1");
        var markerPath = CreateMarker(
            configuration: "Debug",
            packageIdentity: "SalmonEgg.Package",
            installedExecutablePath: installedExecutablePath,
            installedExecutableSha256: ComputeSha256(installedExecutablePath),
            msixPath: msixPath,
            msixSha256: ComputeSha256(msixPath));

        var result = GuiTestGate.ValidateCurrentInstall(
            markerPath,
            actualPackageIdentity: "SalmonEgg.Package",
            actualInstalledExecutablePath: installedExecutablePath);

        Assert.True(result.IsCurrentInstall);
        Assert.Equal(installedExecutablePath, result.InstalledExecutablePath);
    }

    [Fact]
    public void ValidateCurrentInstall_WhenMarkerConfigurationIsRelease_ReturnsFailure()
    {
        var installedExecutablePath = CreateFile("installed", "SalmonEgg.exe", "installed-binary-v1");
        var msixPath = CreateFile("artifacts", "SalmonEgg.msix", "msix-binary-v1");
        var markerPath = CreateMarker(
            configuration: "Release",
            packageIdentity: "SalmonEgg.Package",
            installedExecutablePath: installedExecutablePath,
            installedExecutableSha256: ComputeSha256(installedExecutablePath),
            msixPath: msixPath,
            msixSha256: ComputeSha256(msixPath));

        var result = GuiTestGate.ValidateCurrentInstall(
            markerPath,
            actualPackageIdentity: "SalmonEgg.Package",
            actualInstalledExecutablePath: installedExecutablePath);

        Assert.False(result.IsCurrentInstall);
        Assert.Contains("Configuration Debug", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCurrentInstall_WhenMarkerMsixHashMismatches_ReturnsFailure()
    {
        var installedExecutablePath = CreateFile("installed", "SalmonEgg.exe", "installed-binary-v1");
        var msixPath = CreateFile("artifacts", "SalmonEgg.msix", "msix-binary-v1");
        var markerPath = CreateMarker(
            configuration: "Debug",
            packageIdentity: "SalmonEgg.Package",
            installedExecutablePath: installedExecutablePath,
            installedExecutableSha256: ComputeSha256(installedExecutablePath),
            msixPath: msixPath,
            msixSha256: ComputeSha256(msixPath));
        File.WriteAllText(msixPath, "msix-binary-v2", Encoding.UTF8);

        var result = GuiTestGate.ValidateCurrentInstall(
            markerPath,
            actualPackageIdentity: "SalmonEgg.Package",
            actualInstalledExecutablePath: installedExecutablePath);

        Assert.False(result.IsCurrentInstall);
        Assert.Contains("MSIX SHA256", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string CreateMarker(
        string configuration,
        string packageIdentity,
        string installedExecutablePath,
        string installedExecutableSha256,
        string msixPath,
        string msixSha256)
    {
        var markerDirectory = Path.Combine(_tempRoot, "artifacts", "msix");
        Directory.CreateDirectory(markerDirectory);

        var markerPath = Path.Combine(markerDirectory, "current-install.json");
        var payload = $$"""
        {
          "repoRoot": "{{NormalizePath(_tempRoot)}}",
          "configuration": "{{configuration}}",
          "packageIdentity": "{{packageIdentity}}",
          "installedExecutablePath": "{{NormalizePath(installedExecutablePath)}}",
          "installedExecutableSha256": "{{installedExecutableSha256}}",
          "writtenAtUtc": "2026-04-24T00:00:00.0000000Z",
          "msixPath": "{{NormalizePath(msixPath)}}",
          "msixSha256": "{{msixSha256}}"
        }
        """;

        File.WriteAllText(markerPath, payload, Encoding.UTF8);
        return markerPath;
    }

    private string CreateFile(string relativeDirectory, string fileName, string content)
    {
        var directory = Path.Combine(_tempRoot, relativeDirectory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }
}
