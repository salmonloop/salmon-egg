using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class AcpConnectionSettingsXamlTests
{
    [Fact]
    public void McpSettingsPage_UsesNativeSettingsLayoutAndViewModelBindings()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml");

        Assert.Contains("x:Class=\"SalmonEgg.Presentation.Views.Settings.McpSettingsPage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Mcp_PageTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Mcp_PageSummary\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.IsEnabled", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"Mcp.Global.Enabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"Mcp_EnableSwitch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOn=\"{x:Bind Enabled, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Mcp_ServerEnabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.Server.Enabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind EditCommand, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.EditServer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.IsEditorOpen, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.Editor.Panel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{x:Bind ViewModel.EditingServer, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.CloseEditorCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.Editor.Close\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"Name\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Transport\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{x:Bind Transport, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{x:Bind Transport, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsControl", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.Servers, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView ItemsSource=\"{x:Bind ViewModel.Servers", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.AddServerCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ViewModel.SaveCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"Mcp.SaveConfig\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind SaveCommand, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.SaveServer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ViewModel.OpenImportPanelCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ViewModel.ImportJsonCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility=\"{x:Bind ViewModel.IsImportPanelOpen", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.FillEditorFromClipboardCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.FillFromClipboardJson\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ViewModel.ImportStatusMessage, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.Import.Status\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.AddServer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.RemoveServer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"删除\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Button.Flyout>", xaml, StringComparison.Ordinal);
        Assert.Contains("<Flyout>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Mcp_RemoveServerConfirm\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.RemoveServer.Confirm\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind RemoveCommand, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Mcp_ServerAdvanced\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"Mcp_ServerDetails\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{x:Bind IsDetailsExpanded, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind StatusMessage, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.Server.Status\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{x:Bind ViewModel.ImportJsonText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Mcp.Servers.List\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void McpSettingsRows_KeepStableOwnCommandsForVirtualizedListItems()
    {
        var rowViewModel = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\McpServerRowViewModel.cs");
        var settingsViewModel = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\McpSettingsViewModel.cs");

        Assert.Contains("public IRelayCommand RemoveCommand { get; }", rowViewModel, StringComparison.Ordinal);
        Assert.Contains("RemoveCommand = new RelayCommand(Remove);", rowViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveCommand { get; set; }", rowViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("row.RemoveCommand =", settingsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void McpSettingsAddCommand_UsesViewModelCanExecuteAsEnabledStateOwner()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml");
        var document = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var addButton = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Button"
            && string.Equals((string?)element.Attribute(x + "Uid"), "Mcp_AddServer", StringComparison.Ordinal)));
        Assert.Equal("{x:Bind ViewModel.AddServerCommand}", (string?)addButton.Attribute("Command"));
        Assert.Null(addButton.Attribute("IsEnabled"));
    }

    [Fact]
    public void McpSettingsPage_HasLocalizedVisibleTextResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "Mcp_PageTitle.Text",
            "Mcp_PageSummary.Text",
            "Mcp_ServersTitle.Text",
            "Mcp_Reload.Content",
            "Mcp_AddServer.Content",
            "Mcp_FillFromClipboardJson.Content",
            "Mcp_ServerCatalogDescription.Text",
            "Mcp_SaveServer.Content",
            "Mcp_ServerName.Header",
            "Mcp_ServerName.PlaceholderText",
            "Mcp_ServerTransport.Header",
            "Mcp_ServerEnabled.OnContent",
            "Mcp_ServerEnabled.OffContent",
            "Mcp_EditServer.Content",
            "Mcp_RemoveServer.ToolTipService.ToolTip",
            "Mcp_RemoveServer.AutomationProperties.Name",
            "Mcp_RemoveServerConfirmTitle.Text",
            "Mcp_RemoveServerConfirmDescription.Text",
            "Mcp_RemoveServerConfirm.Content",
            "Mcp_EditorTitle.Text",
            "Mcp_EditorDescription.Text",
            "Mcp_EditorClose.Content",
            "Mcp_EditorSave.Content",
            "Mcp_ServerAdvanced.Header",
            "Mcp_ServerCommand.Header",
            "Mcp_ServerCommand.PlaceholderText",
            "Mcp_ServerArguments.Header",
            "Mcp_ServerArguments.PlaceholderText",
            "Mcp_ServerArgumentsHelp.Text",
            "Mcp_ServerEnvironment.Header",
            "Mcp_ServerEnvironment.PlaceholderText",
            "Mcp_ServerEnvironmentHelp.Text",
            "Mcp_ServerUrl.Header",
            "Mcp_ServerUrl.PlaceholderText",
            "Mcp_ServerHeaders.Header",
            "Mcp_ServerHeaders.PlaceholderText",
            "Mcp_ServerHeadersHelp.Text"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadFile(resourceFile));

            foreach (var resourceName in requiredResources)
            {
                Assert.True(
                    resources.Descendants("data")
                        .Any(data => string.Equals((string?)data.Attribute("name"), resourceName, StringComparison.Ordinal)),
                    $"{resourceFile} must define {resourceName}.");
            }
        }
    }

    [Fact]
    public void AcpConnectionSettingsPage_ExposesPageTitleSummaryAndAdvancedHydrationDisclosure()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        Assert.Contains("x:Uid=\"Acp_PageTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_PageSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_GlobalTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_GlobalEnabledTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_GlobalEnabledDescription\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOn=\"{x:Bind ViewModel.IsAcpEnabled, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.Global.Enabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOn=\"{x:Bind ViewModel.Profiles", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsPageTitleTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsPageSummaryTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_AdvancedExpander\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpConnectionSettingsPage_HasLocalizedGlobalAcpResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "Acp_GlobalTitle.Text",
            "Acp_GlobalEnabledTitle.Text",
            "Acp_GlobalEnabledDescription.Text",
            "Acp_GlobalEnabledSwitch.OnContent",
            "Acp_GlobalEnabledSwitch.OffContent"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadFile(resourceFile));

            foreach (var resourceName in requiredResources)
            {
                Assert.True(
                    resources.Descendants("data")
                        .Any(data => string.Equals((string?)data.Attribute("name"), resourceName, StringComparison.Ordinal)),
                    $"{resourceFile} must define {resourceName}.");
            }
        }
    }

    [Fact]
    public void AcpConnectionSettingsPage_ProfileCommandsStayInSectionHeader()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        Assert.Contains("x:Uid=\"Acp_ProfilesTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.Profiles.RefreshCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAddProfileClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.Profiles.ProfileItems, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpConnectionSettingsPage_ProfileList_PreservesNativeSelectionAndActions()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        Assert.Contains("<ListView ItemsSource=\"{x:Bind ViewModel.Profiles.ProfileItems, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.Profiles.SelectedProfileItem, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Single\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ToggleSwitch", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOn=\"{x:Bind IsConnected, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Toggled=\"OnProfileConnectionToggleToggled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Button.Flyout>", xaml, StringComparison.Ordinal);
        Assert.Contains("<MenuFlyout>", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnEditProfileMenuClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnDeleteProfileMenuClick\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpConnectionSettingsPage_RemoteDirectoriesEditor_UsesViewModelDrivenBindings()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.RemoteDirectoryRows, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.AddRemoteDirectoryCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind DisplayName, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind RemotePath, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PathMappingRows", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalRootPath", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpConnectionSettingsPage_RemoteDirectoriesEditor_ExposesStableAutomationIds()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.Section\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.List\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.Add\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Acp.PathMappings", xaml, StringComparison.Ordinal);
    }

    private static string LoadFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, NormalizeRelativePath(relativePath)));
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

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);
}
