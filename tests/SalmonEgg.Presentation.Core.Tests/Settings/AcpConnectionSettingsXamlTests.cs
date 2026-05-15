using System.IO;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed class AcpConnectionSettingsXamlTests
{
    [Fact]
    public void AcpConnectionSettingsPage_ExposesPageTitleSummaryAndAdvancedHydrationDisclosure()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        Assert.Contains("x:Uid=\"Acp_PageTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_PageSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsPageTitleTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsPageSummaryTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Acp_AdvancedExpander\"", xaml, StringComparison.Ordinal);
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
    public void AcpConnectionSettingsPage_PathMappingsEditor_UsesViewModelDrivenBindings()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        // Assert
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.PathMappingRows, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.AddPathMappingCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind RemoveCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind RemoteRootPath, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind LocalRootPath, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpConnectionSettingsPage_PathMappingsEditor_ExposesStableAutomationIds()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        // Assert
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.PathMappings.Section\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.PathMappings.List\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.PathMappings.Add\"", xaml, StringComparison.Ordinal);
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
