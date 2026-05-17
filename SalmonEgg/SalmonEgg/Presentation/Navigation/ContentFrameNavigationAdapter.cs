using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Navigation;

public sealed class ContentFrameNavigationAdapter
{
    private readonly Frame _frame;

    public ContentFrameNavigationAdapter(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public bool IsDisplaying(Type pageType)
        => _frame.CurrentSourcePageType == pageType
           && _frame.Content is not null
           && pageType.IsInstanceOfType(_frame.Content);

    public ValueTask<ShellNavigationResult> NavigateAsync(Type pageType, object? parameter = null)
    {
        if (IsDisplaying(pageType))
        {
            return ValueTask.FromResult(ShellNavigationResult.Success());
        }

        var completion = new TaskCompletionSource<ShellNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void DetachHandlers()
        {
            _frame.Navigated -= OnNavigated;
            _frame.NavigationFailed -= OnNavigationFailed;
        }

        void OnNavigated(object sender, NavigationEventArgs e)
        {
            if (e.SourcePageType != pageType)
            {
                return;
            }

            DetachHandlers();
            var result = IsDisplaying(pageType)
                ? ShellNavigationResult.Success()
                : ShellNavigationResult.Failed("ContentNotProjected");
            completion.TrySetResult(result);
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            if (e.SourcePageType != pageType)
            {
                return;
            }

            DetachHandlers();
            completion.TrySetResult(ShellNavigationResult.Failed(e.Exception?.GetType().Name ?? "NavigationFailed"));
        }

        _frame.Navigated += OnNavigated;
        _frame.NavigationFailed += OnNavigationFailed;

        var navigated = false;
        try
        {
            navigated = _frame.Navigate(pageType, parameter, UiMotionController.Current.CreateNavigationTransitionInfo());
        }
        catch (Exception ex)
        {
            DetachHandlers();
            completion.TrySetResult(ShellNavigationResult.Failed(ex.GetType().Name));
        }

        if (!navigated)
        {
            DetachHandlers();
            completion.TrySetResult(ShellNavigationResult.Failed("NavigateReturnedFalse"));
        }

        return new ValueTask<ShellNavigationResult>(completion.Task);
    }
}
