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
    [InlineData("About_AcknowledgementVersionFallback")]
    [InlineData("About_AcknowledgementLicenseFallback")]
    [InlineData("About_AcknowledgementSourceFallback")]
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
    [InlineData("StartSuggestion_AnalyzeCodebaseTitle")]
    [InlineData("StartSuggestion_AnalyzeCodebaseSubtitle")]
    [InlineData("StartSuggestion_AnalyzeCodebasePrompt")]
    [InlineData("StartSuggestion_RecommendTasksTitle")]
    [InlineData("StartSuggestion_RecommendTasksSubtitle")]
    [InlineData("StartSuggestion_RecommendTasksPrompt")]
    [InlineData("StartSuggestion_ResolveErrorsTitle")]
    [InlineData("StartSuggestion_ResolveErrorsSubtitle")]
    [InlineData("StartSuggestion_ResolveErrorsPrompt")]
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
    [InlineData("AcpRemoteDirectories_SaveValidationRemotePathRequired")]
    [InlineData("GamepadDiagnostics_StatusNotStarted")]
    [InlineData("GamepadDiagnostics_StatusMonitoring")]
    [InlineData("GamepadDiagnostics_StatusStopped")]
    [InlineData("GamepadDiagnostics_StatusUnsupported")]
    [InlineData("GamepadDiagnostics_StatusFailed")]
    [InlineData("GamepadDiagnostics_InputSourceNone")]
    [InlineData("GamepadDiagnostics_InputSourceGamepad")]
    [InlineData("GamepadDiagnostics_InputSourceRawController")]
    [InlineData("GamepadDiagnostics_ActiveInputsNone")]
    [InlineData("GamepadDiagnostics_RawControllersNone")]
    [InlineData("GamepadDiagnostics_ConnectionWired")]
    [InlineData("GamepadDiagnostics_ConnectionWireless")]
    [InlineData("DataStorage_ClearAllLocalDataSuccess")]
    [InlineData("DataStorage_ClearAllLocalDataFailed")]
    [InlineData("DataStorage_ExportSessionFailed")]
    [InlineData("DataStorage_CreateDiagnosticsBundleFailed")]
    [InlineData("Diagnostics_LogSnippetCopied")]
    [InlineData("Diagnostics_CopyLogSnippetFailed")]
    [InlineData("ChatTurnFailure_CopyFailed")]
    [InlineData("ChatOperation_CreateSessionFailed")]
    [InlineData("ChatOperation_SwitchModeFailed")]
    [InlineData("ChatOperation_SwitchModelFailed")]
    [InlineData("ChatOperation_CancelSessionFailed")]
    [InlineData("ChatOperation_DisconnectFailed")]
    [InlineData("ChatOperation_LoadSessionNoActiveConversation")]
    [InlineData("ChatOperation_LoadSessionMissingActiveBinding")]
    [InlineData("ChatOperation_LoadSessionMissingProfileBinding")]
    [InlineData("ChatOperation_LoadSessionChatServiceNotReady")]
    [InlineData("ChatOperation_LoadSessionRecoveryCapabilityMissing")]
    [InlineData("ChatOperation_LoadSessionMissingBoundProfile")]
    [InlineData("ChatOperation_LoadSessionProfileNotResolved")]
    [InlineData("ChatOperation_LoadSessionRemoteConnectionNotReady")]
    [InlineData("ChatOperation_LoadSessionFailed")]
    [InlineData("ChatOperation_ReconnectSessionFailed")]
    [InlineData("ChatOperation_SwitchSessionFailed")]
    [InlineData("AgentProfileEditor_ValidationFailedFormat")]
    [InlineData("AgentProfileEditor_SaveFailedFormat")]
    [InlineData("AcpProfiles_RefreshFailed")]
    [InlineData("AcpProfiles_DeleteFailed")]
    [InlineData("AcpProfiles_SaveFailed")]
    [InlineData("AcpProfiles_ConnectFailed")]
    [InlineData("AcpProfiles_DisconnectFailed")]
    [InlineData("AcpProfiles_ReconnectFailed")]
    [InlineData("General_LaunchOnStartupFailed")]
    [InlineData("General_LanguageApplyFailed")]
    [InlineData("General_AppSettingsSaveFailed")]
    [InlineData("General_AppSettingsLoadFailed")]
    [InlineData("SessionActivation_FailedGeneric")]
    [InlineData("SessionActivation_ConversationSelectionFailed")]
    [InlineData("SessionActivation_ChatShellNavigationFailed")]
    [InlineData("Navigation_OpenSettingsFailed")]
    [InlineData("Navigation_OpenStartFailed")]
    [InlineData("Navigation_OpenDiscoverSessionsFailed")]
    [InlineData("Navigation_OpenSessionFailed")]
    [InlineData("Nav_CopySessionIdFailed")]
    [InlineData("Nav_ShowSessionsListFailed")]
    [InlineData("Start_SessionLaunchFailed")]
    [InlineData("AddProject_RemoteProjectMissing")]
    [InlineData("AddProject_InvalidSelection")]
    [InlineData("AgentProfileEditor_ProxyModeSystem")]
    [InlineData("AgentProfileEditor_ProxyModeNone")]
    [InlineData("AgentProfileEditor_ProxyModeCustom")]
    [InlineData("AcpConnection_TransportStdio")]
    [InlineData("AcpConnection_TransportWebSocket")]
    [InlineData("AcpConnection_TransportHttpSse")]
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

    [Fact]
    public void CoreStringResources_HaveSameKeysForCanonicalLanguages()
    {
        var keysByFile = CoreStringResourcePaths.ToDictionary(
            path => path,
            path => XDocument.Load(Path.Combine(FindRepoRoot(), NormalizeRelativePath(path)))
                .Descendants("data")
                .Select(data => (string?)data.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var allKeys = keysByFile.Values
            .SelectMany(static keys => keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var failures = keysByFile
            .Select(pair => new
            {
                File = pair.Key,
                Missing = allKeys.Except(pair.Value, StringComparer.Ordinal).ToArray()
            })
            .Where(result => result.Missing.Length > 0)
            .Select(result => $"{result.File} missing: {string.Join(", ", result.Missing)}")
            .ToArray();

        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RemoteProjectCopy_UsesProjectTerminologyForUserVisibleCoreMessages()
    {
        var zhHans = XDocument.Load(Path.Combine(FindRepoRoot(), NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.zh-Hans.resx")));
        var en = XDocument.Load(Path.Combine(FindRepoRoot(), NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en.resx")));

        Assert.Equal("请先选择远程项目", GetResourceValue(zhHans, "Selector_Mode_RemoteSelectionRequired"));
        Assert.Equal("请选择远程项目", GetResourceValue(zhHans, "Selector_Project_RemoteSelectionRequired"));
        Assert.Equal("已匹配已配置的远程项目。", GetResourceValue(zhHans, "Discover_AffinityStatusRemoteDirectory"));
        Assert.Equal("远程 ACP 工作路径需要指定项目。", GetResourceValue(zhHans, "Discover_AffinityStatusNeedsMapping"));
        Assert.Equal("远程元数据没有可用 ACP 工作路径。", GetResourceValue(zhHans, "Discover_AffinityStatusMissingCwd"));

        Assert.Equal("Select a remote project first", GetResourceValue(en, "Selector_Mode_RemoteSelectionRequired"));
        Assert.Equal("Select a remote project", GetResourceValue(en, "Selector_Project_RemoteSelectionRequired"));
        Assert.Equal("Matched a configured remote project.", GetResourceValue(en, "Discover_AffinityStatusRemoteDirectory"));
        Assert.Equal("Remote ACP working path needs a project assignment.", GetResourceValue(en, "Discover_AffinityStatusNeedsMapping"));
        Assert.Equal("Remote metadata has no usable ACP working path.", GetResourceValue(en, "Discover_AffinityStatusMissingCwd"));
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

    private static string GetResourceValue(XDocument resources, string key)
    {
        var value = resources
            .Descendants("data")
            .FirstOrDefault(data => string.Equals((string?)data.Attribute("name"), key, StringComparison.Ordinal))
            ?.Element("value")
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(value), $"{key} must define a non-empty value.");
        return value!;
    }

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
