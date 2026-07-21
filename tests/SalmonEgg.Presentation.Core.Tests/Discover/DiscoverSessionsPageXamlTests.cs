using System.IO;

namespace SalmonEgg.Presentation.Core.Tests.Discover;

public sealed class DiscoverSessionsPageXamlTests
{
    [Fact]
    public void DiscoverSessionsPage_AffinityPreview_UsesViewModelDrivenBindings()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        // Assert
        Assert.Contains("Text=\"{x:Bind ProjectAffinityBadgeText, Mode=OneTime}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind AffinityStatusText, Mode=OneTime}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind NeedsUserAttention, Mode=OneTime, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_AffinityPreview_DoesNotHardcodeResolverFallbackText()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        // Assert
        Assert.DoesNotContain("Needs mapping", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Unclassified", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_ResponsivePaneVisibility_IsViewModelDriven()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        // Assert
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowProfilesPane, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowDetailsPane, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"800\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_AdaptiveLayout_UsesNativeStates_AndAvoidsOpacityHack()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");
        var codeBehind = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml.cs");

        // Assert
        Assert.Contains("VisualStateGroup x:Name=\"AdaptiveStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"768\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"DiscoverSessionsBackButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinActualWidthTrigger", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActualWidth", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionBtn.Opacity", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_LoadingAndCommands_UseNativeWinUiSurfaces()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");
        var codeBehind = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml.cs");

        Assert.Contains("<CommandBar", xaml, StringComparison.Ordinal);
        Assert.Contains("<AppBarButton", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"DiscoverSessionsRefreshButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowBusyStatus, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowSessionsSkeleton, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind LoadSessionCommand, Mode=OneTime}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{x:Bind}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RootPage.ViewModel.LoadSessionCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RootPage.ViewModel.AreSessionActionsEnabled", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{x:Bind RootPage.ViewModel.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SkeletonPulse", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SkeletonPulse", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource SubtleButtonStyle}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_SessionRowsExposeAccessibleMetadataBoundaries()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        Assert.Contains("AutomationProperties.Name=\"{x:Bind AutomationName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind HasFormattedDate, Mode=OneTime, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind HasSessionCwd, Mode=OneTime, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind SessionCwdDisplayText, Mode=OneTime}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_HeaderBindsToLocalSelectedProfileProjection()
    {
        // Arrange
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        // Assert
        Assert.Contains("Text=\"{x:Bind ViewModel.SelectedProfile.Name, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ViewModel.SelectedProfile.TransportDisplayName, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.ProfilesViewModel.SelectedProfile.Name", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.ProfilesViewModel.SelectedProfile.TransportDisplayName", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_ExposesStableAutomationIds_ForGuiSmoke()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"DiscoverSessions.ProfilesList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"DiscoverSessions.SessionsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"DiscoverSessions.DetailsPane\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"DiscoverSessions.ImportButton\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_CodeBehind_AvoidsDoubleDrivingProfileSelectionAndDetailOpen()
    {
        var codeBehind = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml.cs");

        Assert.Contains("var wasAlreadySelected = ReferenceEquals(ViewModel.SelectedProfile, profile);", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (wasAlreadySelected && ViewModel.OpenProfileDetailsCommand.CanExecute(null))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.SelectedProfile = profile;", codeBehind, StringComparison.Ordinal);
    }


    [Fact]
    public void DiscoverSessionsPage_SessionRowsDensifyOnShortWindowHeights()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        Assert.Contains("x:Name=\"SessionListHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionListHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionListHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DiscoverSessionItemStyleCompact", xaml, StringComparison.Ordinal);
        Assert.Contains("DiscoverSessionItemStyleComfortable", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"80\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"104\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource DiscoverSessionItemStyleCompact}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
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
