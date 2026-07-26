using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class SecureStorageRegistrationContractTests
{
    // §5.6 回归护栏:各平台 secure storage 及 plaintext 回退链不得从组合根被移除。
    // Infrastructure.Tests 无法引用应用头程序集做容器级断言,故保留源文件检查,
    // 但只断言名称级存在(格式不敏感),不锁具体语法形态(§5.5)。
    [Fact]
    public void DependencyInjection_RegistersPlainTextFallbackSecureStorage()
    {
        var source = LoadText("SalmonEgg/SalmonEgg/DependencyInjection.cs");

        Assert.Contains("PlainTextFileSecureStorage", source, StringComparison.Ordinal);
        Assert.Contains("OSPlatform.Linux", source, StringComparison.Ordinal);
        Assert.Contains("LinuxSecretServiceSecureStorage", source, StringComparison.Ordinal);
        Assert.Contains("OSPlatform.OSX", source, StringComparison.Ordinal);
        Assert.Contains("MacOSKeychainSecureStorage", source, StringComparison.Ordinal);
        Assert.Contains("FallbackSecureStorage", source, StringComparison.Ordinal);
        Assert.Contains("AndroidKeyStoreSecureStorage", source, StringComparison.Ordinal);
        Assert.Contains("IosKeychainSecureStorage", source, StringComparison.Ordinal);
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
