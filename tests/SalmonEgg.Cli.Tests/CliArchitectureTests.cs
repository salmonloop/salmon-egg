using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SalmonEgg.Cli.Output;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests;

public sealed class CliArchitectureTests
{
    [Fact]
    public void CliProject_DoesNotReferenceUnoOrPresentationProjects()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "SalmonEgg.Cli", "SalmonEgg.Cli.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("SalmonEgg\\SalmonEgg\\SalmonEgg.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("SalmonEgg.Presentation.Core", project, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialHandler_DoesNotBypassTheCredentialServiceBoundary()
    {
        var root = FindRepositoryRoot();
        var handlerPath = Path.Combine(
            root,
            "src",
            "SalmonEgg.Cli",
            "Commands",
            "Credentials",
            "CredentialsHandler.cs");
        var source = File.ReadAllText(handlerPath);

        Assert.Contains("IServerCredentialService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISecureStorage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigurationSecretKeys", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateServiceProvider_ResolvesSharedDesktopConfigurationStack()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        using var provider = CliApplication.CreateServiceProvider(new TextCliOutput(stdout, stderr));

        Assert.IsType<ConfigurationManager>(provider.GetRequiredService<IConfigurationService>());
        Assert.IsType<AppSettingsService>(provider.GetRequiredService<IAppSettingsService>());
        Assert.IsType<ServerCredentialService>(provider.GetRequiredService<IServerCredentialService>());
        Assert.IsType<ConfigSyncPackageService>(provider.GetRequiredService<ConfigSyncPackageService>());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
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
