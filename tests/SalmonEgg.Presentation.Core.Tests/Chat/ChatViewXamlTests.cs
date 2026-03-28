using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatViewXamlTests
{
    [Fact]
    public void ChatViewXaml_ExposesProjectAffinityCorrectionAutomationIds()
    {
        var xaml = LoadChatViewXaml();

        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionProjectSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionApplyButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ProjectAffinityCorrectionClearButton\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_ProjectAffinityCorrection_BindsToViewModelStateAndCommands()
    {
        var xaml = LoadChatViewXaml();

        Assert.Contains("Visibility=\"{x:Bind ViewModel.IsProjectAffinityCorrectionVisible, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.ProjectAffinityOverrideOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{x:Bind ViewModel.SelectedProjectAffinityOverrideProjectId, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.ApplyProjectAffinityOverrideCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.ClearProjectAffinityOverrideCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_ProjectAffinityCorrection_DoesNotReintroduceAgentSelector()
    {
        var xaml = LoadChatViewXaml();

        Assert.DoesNotContain("SelectedItem=\"{x:Bind ViewModel.SelectedAcpProfile, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    private static string LoadChatViewXaml()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "ChatView.xaml"));
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
