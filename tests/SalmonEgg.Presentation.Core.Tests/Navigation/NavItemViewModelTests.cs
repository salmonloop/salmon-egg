using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class NavItemViewModelTests
{
    [Fact]
    public void IsPaneClosed_Tracks_IsPaneOpen_And_Notifies()
    {
        var item = new DummyNavItem();
        var notified = false;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainNavItemViewModel.IsPaneClosed))
            {
                notified = true;
            }
        };

        item.IsPaneOpen = false;

        Assert.True(notified);
        Assert.False(item.IsPaneOpen);
        Assert.True(item.IsPaneClosed);
    }

    [Fact]
    public void SessionsHeader_Exposes_Title_And_Command()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);
        var header = new SessionsHeaderNavItemViewModel(command);

        Assert.Equal("会话", header.Title);
        Assert.Same(command, header.AddProjectCommand);
    }

    [Fact]
    public void SessionsCompactAdd_Exposes_Command()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);
        var compactAdd = new SessionsCompactAddNavItemViewModel(command);

        Assert.Same(command, compactAdd.AddProjectCommand);
    }

    private sealed class DummyNavItem : MainNavItemViewModel
    {
    }
}
