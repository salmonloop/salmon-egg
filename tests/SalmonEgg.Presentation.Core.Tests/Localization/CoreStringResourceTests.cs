using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

public sealed class CoreStringResourceTests
{
    [Theory]
    [InlineData("Nav_Settings")]
    [InlineData("Platform_ExternalOpenUnsupported")]
    [InlineData("Platform_LocalFileExportUnsupported")]
    [InlineData("SettingsSection_General")]
    [InlineData("SettingsSection_Appearance")]
    [InlineData("SettingsSection_AgentAcp")]
    [InlineData("SettingsSection_Mcp")]
    [InlineData("SettingsSection_DataStorage")]
    [InlineData("SettingsSection_Shortcuts")]
    [InlineData("SettingsSection_Diagnostics")]
    [InlineData("SettingsSection_About")]
    [InlineData("Search_Sessions")]
    [InlineData("Search_Projects")]
    [InlineData("Search_Settings")]
    [InlineData("Search_Commands")]
    [InlineData("SearchCommand_NewSessionTitle")]
    [InlineData("SearchCommand_NewSessionSubtitle")]
    [InlineData("SearchCommand_NewProjectTitle")]
    [InlineData("SearchCommand_NewProjectSubtitle")]
    [InlineData("SearchCommand_ToggleThemeTitle")]
    [InlineData("SearchCommand_ToggleThemeSubtitle")]
    [InlineData("SettingsSearchSubtitle_General")]
    [InlineData("SettingsSearchSubtitle_Shortcuts")]
    [InlineData("SettingsSearchSubtitle_Appearance")]
    [InlineData("SettingsSearchSubtitle_DataStorage")]
    [InlineData("SettingsSearchSubtitle_AgentAcp")]
    [InlineData("SettingsSearchSubtitle_Diagnostics")]
    [InlineData("SettingsSearchSubtitle_About")]
    [InlineData("McpSettings_LoadFailed")]
    [InlineData("McpSettings_Saved")]
    [InlineData("McpSettings_SaveFailed")]
    [InlineData("McpSettings_SaveValidationFailed")]
    [InlineData("McpSettings_SaveValidationNameRequired")]
    [InlineData("McpSettings_SaveValidationCommandRequired")]
    [InlineData("McpSettings_SaveValidationUrlRequired")]
    [InlineData("McpSettings_RowUnsaved")]
    [InlineData("McpSettings_RowSaved")]
    [InlineData("McpSettings_Removed")]
    [InlineData("McpSettings_ImportFailed")]
    [InlineData("McpSettings_ClipboardEmpty")]
    [InlineData("McpSettings_ClipboardFilled")]
    public void CoreMessages_ArePresentInAllCoreStringResources(string key)
    {
        foreach (var relativePath in CoreStringResourcePaths)
        {
            var document = XDocument.Load(Path.Combine(FindRepoRoot(), NormalizeRelativePath(relativePath)));
            var exists = document
                .Descendants("data")
                .Any(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal));

            Assert.True(exists, $"{key} must exist in {relativePath}.");
        }
    }

    [Fact]
    public void CoreStringResources_IncludeEveryCanonicalResourceLanguage()
    {
        var expectedFileNames = AppLanguageCatalog.SupportedResourceLanguageTags
            .Select(tag => $"CoreStrings.{tag}.resx")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var localizedFileNames = Directory
            .EnumerateFiles(CoreStringResourceDirectory(), "CoreStrings.*.resx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedFileNames, localizedFileNames);
    }

    [Fact]
    public void CoreStringResources_DoNotUseLegacyChineseAliasResourceFiles()
    {
        var resourceFileNames = Directory
            .EnumerateFiles(CoreStringResourceDirectory(), "CoreStrings.*.resx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToArray();

        foreach (var legacyAliasTag in AppLanguageCatalog.LegacyAliasTags)
        {
            Assert.DoesNotContain($"CoreStrings.{legacyAliasTag}.resx", resourceFileNames);
        }
    }

    private static readonly string[] CoreStringResourcePaths =
    [
        @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.resx",
        @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en.resx",
        @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en-US.resx",
        @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.zh-Hans.resx"
    ];

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);

    private static string CoreStringResourceDirectory()
        => Path.Combine(FindRepoRoot(), NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Resources"));

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "SalmonEgg.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
