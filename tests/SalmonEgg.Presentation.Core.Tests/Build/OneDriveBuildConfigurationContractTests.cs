using System;
using System.IO;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class OneDriveBuildConfigurationContractTests
{
    private static readonly string[] OneDriveVariableNames =
    [
        "SALMONEGG_ONEDRIVE_CLIENT_ID",
        "SALMONEGG_ONEDRIVE_TENANT_ID",
        "SALMONEGG_ONEDRIVE_REDIRECT_URI",
        "SALMONEGG_ONEDRIVE_SCOPES"
    ];

    private static readonly string[] OneDrivePropertyNames =
    [
        "SalmonEggOneDriveClientId",
        "SalmonEggOneDriveTenantId",
        "SalmonEggOneDriveRedirectUri",
        "SalmonEggOneDriveScopes"
    ];

    private static readonly string[] OneDriveMetadataKeys =
    [
        "SalmonEgg.OneDrive.ClientId",
        "SalmonEgg.OneDrive.TenantId",
        "SalmonEgg.OneDrive.RedirectUri",
        "SalmonEgg.OneDrive.Scopes"
    ];

    [Fact]
    public void AppProject_MapsOneDriveBuildEnvironmentIntoAssemblyMetadata()
    {
        var project = TestSourceFiles.ReadAllText(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        for (var index = 0; index < OneDriveVariableNames.Length; index++)
        {
            Assert.Contains(
                $"<{OneDrivePropertyNames[index]} Condition=\"'$({OneDrivePropertyNames[index]})' == ''\">$({OneDriveVariableNames[index]})</{OneDrivePropertyNames[index]}>",
                project,
                StringComparison.Ordinal);
            Assert.Contains($"<AssemblyMetadata Include=\"{OneDriveMetadataKeys[index]}\" Value=\"$({OneDrivePropertyNames[index]})\" />", project, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OneDriveProvider_ReadsBuildMetadataNotRuntimeEnvironment()
    {
        var provider = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Cloud\OneDriveCloudConfigStorageProvider.cs");

        Assert.Contains("OneDriveCloudConfigOptions.FromAssembly", provider, StringComparison.Ordinal);
        Assert.Contains("AssemblyMetadataAttribute", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("SALMONEGG_ONEDRIVE_", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubActions_InjectOneDriveBuildEnvironmentFromSecretsOrVariables()
    {
        var workflowPaths = Directory.EnumerateFiles(
                TestSourceFiles.GetPath(@".github\workflows"),
                "*.yml",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.NotEmpty(workflowPaths);

        foreach (var workflowPath in workflowPaths)
        {
            var workflow = File.ReadAllText(workflowPath);
            if (!BuildsApp(workflow))
            {
                continue;
            }

            foreach (var variableName in OneDriveVariableNames)
            {
                Assert.Contains(
                    $"{variableName}: ${{{{ secrets.{variableName} || vars.{variableName} }}}}",
                    workflow,
                    StringComparison.Ordinal);
            }
        }
    }

    private static bool BuildsApp(string workflow) =>
        workflow.Contains("dotnet build SalmonEgg.sln", StringComparison.Ordinal) ||
        workflow.Contains("dotnet publish", StringComparison.Ordinal) ||
        workflow.Contains("dotnet msbuild", StringComparison.Ordinal) ||
        workflow.Contains("run-wasm-smoke-gates", StringComparison.Ordinal) ||
        workflow.Contains("run-gui-smoke-gates", StringComparison.Ordinal);
}
