using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class SecureStorageRegistrationContractTests
{
    [Fact]
    public void DependencyInjection_DoesNotRegisterFileBackedSecureStorageForProductionPlatforms()
    {
        var source = LoadText("SalmonEgg/SalmonEgg/DependencyInjection.cs");

        Assert.DoesNotContain("AddSingleton<ISecureStorage>(sp =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppFileStoreSecureStorage", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Linux)", source, StringComparison.Ordinal);
        Assert.Contains("new LinuxSecretServiceSecureStorage()", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.OSX)", source, StringComparison.Ordinal);
        Assert.Contains("new MacOSKeychainSecureStorage()", source, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ISecureStorage, VolatileSecureStorage>();", source, StringComparison.Ordinal);
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
