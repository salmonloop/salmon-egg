using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class SecureStorageRegistrationContractTests
{
    // §5.6 回归护栏:desktop composition root 的 native keychain 与 plaintext 回退链,
    // 以及移动端 composition root 的 native backend,都不得被移除。desktop 部分另由
    // DesktopConfigurationServiceCollectionExtensionsTests 作真实容器断言；这里仅确保
    // 平台专属实现仍在各自 owner 中，且不锁具体语法形态(§5.5)。
    [Fact]
    public void CompositionRoots_KeepAllSupportedSecureStorageBackends()
    {
        var desktopSource = LoadText("src/SalmonEgg.Infrastructure.Desktop/DependencyInjection/DesktopConfigurationServiceCollectionExtensions.cs");
        var applicationSource = LoadText("SalmonEgg/SalmonEgg/DependencyInjection.cs");

        Assert.Contains("PlainTextFileSecureStorage", desktopSource, StringComparison.Ordinal);
        Assert.Contains("OSPlatform.Windows", desktopSource, StringComparison.Ordinal);
        Assert.Contains("WindowsDpapiSecureStorage", desktopSource, StringComparison.Ordinal);
        Assert.Contains("OSPlatform.Linux", desktopSource, StringComparison.Ordinal);
        Assert.Contains("LinuxSecretServiceSecureStorage", desktopSource, StringComparison.Ordinal);
        Assert.Contains("OSPlatform.OSX", desktopSource, StringComparison.Ordinal);
        Assert.Contains("MacOSKeychainSecureStorage", desktopSource, StringComparison.Ordinal);
        Assert.Contains("FallbackSecureStorage", desktopSource, StringComparison.Ordinal);
        Assert.Contains("AndroidKeyStoreSecureStorage", applicationSource, StringComparison.Ordinal);
        Assert.Contains("IosKeychainSecureStorage", applicationSource, StringComparison.Ordinal);
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
