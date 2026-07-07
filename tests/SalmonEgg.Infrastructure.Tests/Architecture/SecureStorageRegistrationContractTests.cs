using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class SecureStorageRegistrationContractTests
{
    [Fact]
    public void DependencyInjection_RegistersPlainTextFallbackSecureStorage()
    {
        var source = LoadText("SalmonEgg/SalmonEgg/DependencyInjection.cs");

        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "src/SalmonEgg.Infrastructure/Storage/PlainTextFileSecureStorage.cs")));
        Assert.Contains("services.AddSingleton<PlainTextFileSecureStorage>();", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Linux)", source, StringComparison.Ordinal);
        Assert.Contains("new LinuxSecretServiceSecureStorage()", source, StringComparison.Ordinal);
        Assert.Contains("new FallbackSecureStorage(new LinuxSecretServiceSecureStorage(), fallback)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.OSX)", source, StringComparison.Ordinal);
        Assert.Contains("new MacOSKeychainSecureStorage()", source, StringComparison.Ordinal);
        Assert.Contains("new FallbackSecureStorage(new MacOSKeychainSecureStorage(), fallback)", source, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISecureStorage, AndroidKeyStoreSecureStorage>();", source, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISecureStorage, IosKeychainSecureStorage>();", source, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ISecureStorage>(sp => sp.GetRequiredService<PlainTextFileSecureStorage>());", source, StringComparison.Ordinal);
    }

    private static string LoadText(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
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
}
