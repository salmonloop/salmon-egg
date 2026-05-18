using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Core.Services.Navigation;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Navigation;

public sealed class ContentFrameNavigationAdapter
{
    private readonly Frame _frame;
    private readonly ContentNavigationRequestTracker _requests = new();

    public ContentFrameNavigationAdapter(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _frame.Navigating += OnFrameNavigating;
        _frame.Navigated += OnFrameNavigated;
        _frame.NavigationFailed += OnFrameNavigationFailed;
    }

    public event EventHandler<ContentFrameNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<ContentFrameNavigationFailedEventArgs>? NavigationFailed;

    public bool IsDisplaying(Type pageType)
        => _frame.CurrentSourcePageType == pageType
           && _frame.Content is not null
           && pageType.IsInstanceOfType(_frame.Content);

    public ValueTask<ShellNavigationResult> NavigateAsync(Type pageType, object? parameter = null)
        => NavigateAsync(pageType, parameter, activationToken: null);

    public ValueTask<ShellNavigationResult> NavigateAsync(
        Type pageType,
        object? parameter,
        long? activationToken)
    {
        var request = _requests.BeginRequest(pageType, parameter, activationToken);

        if (IsDisplaying(pageType))
        {
            return ValueTask.FromResult(CompleteCurrentRequest(request));
        }

        var completion = new TaskCompletionSource<ShellNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        request.Completion = completion;

        var navigated = false;
        try
        {
            navigated = _frame.Navigate(pageType, parameter, UiMotionController.Current.CreateNavigationTransitionInfo());
        }
        catch (Exception ex)
        {
            _requests.ClearActive(request);
            request.Complete(ShellNavigationResult.Failed(ex.GetType().Name));
            return new ValueTask<ShellNavigationResult>(completion.Task);
        }

        if (!navigated)
        {
            _requests.ClearActive(request);
            request.Complete(ShellNavigationResult.Failed("NavigateReturnedFalse"));
        }

        return new ValueTask<ShellNavigationResult>(completion.Task);
    }

    private void OnFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        _ = _requests.TryResolveNavigating(e.SourcePageType, e.Parameter, out var cancel);
        e.Cancel = cancel;
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        var completion = _requests.ResolveNavigated(e.SourcePageType, e.Parameter);
        if (completion.Kind == ContentNavigationCompletionKind.Active && completion.Request is { } request)
        {
            CompleteCurrentRequest(request);
            return;
        }

        if (completion.Kind == ContentNavigationCompletionKind.RestoreLatest && completion.Request is { } latestRequest)
        {
            RestoreLatestRequest(latestRequest);
            return;
        }

        if (e.SourcePageType is not null)
        {
            NavigationCompleted?.Invoke(
                this,
                new ContentFrameNavigationCompletedEventArgs(
                    e.SourcePageType,
                    e.Parameter,
                    activationToken: null));
        }
    }

    private void OnFrameNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        var failure = _requests.ResolveNavigationFailed(e.SourcePageType);
        if (failure.Kind != ContentNavigationFailureKind.Active || failure.Request is not { } request)
        {
            return;
        }

        var reason = e.Exception?.GetType().Name ?? "NavigationFailed";
        request.Complete(ShellNavigationResult.Failed(reason));
        NavigationFailed?.Invoke(this, new ContentFrameNavigationFailedEventArgs(request.PageType, request.Parameter, reason));
    }

    private ShellNavigationResult CompleteCurrentRequest(ContentNavigationRequest request)
    {
        var result = _requests.CompleteActive(request, IsDisplaying(request.PageType));
        if (result.Succeeded)
        {
            NavigationCompleted?.Invoke(
                this,
                new ContentFrameNavigationCompletedEventArgs(
                    request.PageType,
                    request.Parameter,
                    request.ActivationToken));
        }
        return result;
    }

    private void RestoreLatestRequest(ContentNavigationRequest request)
    {
        if (_requests.IsStale(request.ActivationToken))
        {
            return;
        }

        if (IsDisplaying(request.PageType))
        {
            CompleteCurrentRequest(request);
            return;
        }

        var navigated = false;
        try
        {
            navigated = _frame.Navigate(
                request.PageType,
                request.Parameter,
                UiMotionController.Current.CreateNavigationTransitionInfo());
        }
        catch (Exception ex)
        {
            _requests.ClearActive(request);
            request.Complete(ShellNavigationResult.Failed(ex.GetType().Name));
            return;
        }

        if (!navigated)
        {
            _requests.ClearActive(request);
            request.Complete(ShellNavigationResult.Failed("NavigateReturnedFalse"));
        }
    }
}

public sealed class ContentFrameNavigationCompletedEventArgs : EventArgs
{
    public ContentFrameNavigationCompletedEventArgs(Type pageType, object? parameter, long? activationToken)
    {
        PageType = pageType;
        Parameter = parameter;
        ActivationToken = activationToken;
    }

    public Type PageType { get; }

    public object? Parameter { get; }

    public long? ActivationToken { get; }
}

public sealed class ContentFrameNavigationFailedEventArgs : EventArgs
{
    public ContentFrameNavigationFailedEventArgs(Type pageType, object? parameter, string reason)
    {
        PageType = pageType;
        Parameter = parameter;
        Reason = reason;
    }

    public Type PageType { get; }

    public object? Parameter { get; }

    public string Reason { get; }
}
