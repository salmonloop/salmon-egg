using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Tests;

public sealed class NavigationViewModelTests
{
    [Fact]
    public void SessionsHeader_CompactIcon_IsAddWhenPaneClosed()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);
        var vm = new SessionsHeaderNavItemViewModel(command);

        vm.IsPaneOpen = false;

        Assert.False(vm.IsPaneOpen);
        Assert.True(vm.IsPaneClosed);
        var icon = Assert.IsType<SymbolIcon>(vm.CompactIcon);
        Assert.Equal(Symbol.Add, icon.Symbol);
    }

    [Fact]
    public void SessionsHeader_CompactIcon_IsNullWhenPaneOpen()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);
        var vm = new SessionsHeaderNavItemViewModel(command);

        vm.IsPaneOpen = true;

        Assert.True(vm.IsPaneOpen);
        Assert.False(vm.IsPaneClosed);
        Assert.Null(vm.CompactIcon);
    }

    [Fact]
    public void NavTimeFormatter_ToRelativeText_UsesExpectedBuckets()
    {
        var now = DateTime.UtcNow;

        Assert.Equal("刚刚", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromSeconds(30)));
        Assert.Equal("2 分", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromMinutes(2)));
        Assert.Equal("3 小时", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromHours(3)));
        Assert.Equal("2 天", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromDays(2)));
    }

    [Fact]
    public void NavTimeFormatter_NormalizePathForPrefixMatch_AppendsSeparator()
    {
        var path = Path.Combine("C:", "Temp", "Demo");
        var normalized = NavTimeFormatter.NormalizePathForPrefixMatch(path);

        Assert.EndsWith(Path.DirectorySeparatorChar, normalized, StringComparison.Ordinal);
        Assert.Contains("Demo", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavItemTag_ProjectTag_RoundTrips()
    {
        var tag = NavItemTag.Project("abc123");

        Assert.True(NavItemTag.TryParseProject(tag, out var projectId));
        Assert.Equal("abc123", projectId);
    }

    [Fact]
    public void NavItemTag_MoreTag_RoundTrips()
    {
        var tag = NavItemTag.More("proj-9");

        Assert.True(NavItemTag.TryParseMore(tag, out var projectId));
        Assert.Equal("proj-9", projectId);
    }

    [Fact]
    public void NavItemTag_ParseRejectsInvalid()
    {
        Assert.False(NavItemTag.TryParseProject("Project:", out _));
        Assert.False(NavItemTag.TryParseMore("More:", out _));
        Assert.False(NavItemTag.TryParseProject("Other:123", out _));
        Assert.False(NavItemTag.TryParseMore("Other:123", out _));
    }
}
