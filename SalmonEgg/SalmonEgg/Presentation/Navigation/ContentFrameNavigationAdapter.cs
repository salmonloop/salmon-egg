using System;
using System.Collections.Generic;
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
    private readonly List<ContentFrameNavigationRequest> _supersededRequests = new();
    private ContentFrameNavigationRequest? _activeRequest;
    private ContentFrameNavigationRequest? _latestRequest;
    private long _latestNavigationToken;

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
        if (activationToken.HasValue)
        {
            _latestNavigationToken = activationToken.Value;
        }

        var request = new ContentFrameNavigationRequest(pageType, parameter, activationToken);
        SupersedeActiveRequest(request);
        _latestRequest = request;

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
            ClearActiveRequest(request);
            completion.TrySetResult(ShellNavigationResult.Failed(ex.GetType().Name));
            return new ValueTask<ShellNavigationResult>(completion.Task);
        }

        if (!navigated)
        {
            ClearActiveRequest(request);
            completion.TrySetResult(ShellNavigationResult.Failed("NavigateReturnedFalse"));
        }

        return new ValueTask<ShellNavigationResult>(completion.Task);
    }

    private void OnFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_activeRequest is { } activeRequest
            && activeRequest.Matches(e.SourcePageType, e.Parameter))
        {
            return;
        }

        var supersededRequest = FindSupersededRequest(e.SourcePageType, e.Parameter);
        if (supersededRequest is null)
        {
            return;
        }

        if (_latestRequest is { } latestRequest
            && latestRequest.Matches(e.SourcePageType, e.Parameter))
        {
            RemoveSupersededRequest(supersededRequest);
            return;
        }

        e.Cancel = true;
        RemoveSupersededRequest(supersededRequest);
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        var request = _activeRequest;
        if (request is not null && request.Matches(e.SourcePageType, e.Parameter))
        {
            CompleteCurrentRequest(request);
            return;
        }

        if (TryConsumeSupersededRequest(e.SourcePageType, e.Parameter))
        {
            if (_latestRequest is { } latestRequest)
            {
                RestoreLatestRequest(latestRequest);
            }

            return;
        }

        if (request is not null)
        {
            RestoreLatestRequest(_latestRequest ?? request);
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
        var request = _activeRequest;
        if (request is null || e.SourcePageType != request.PageType)
        {
            TryConsumeSupersededRequest(e.SourcePageType);
            return;
        }

        ClearActiveRequest(request);
        if (IsStaleNavigation(request.ActivationToken))
        {
            request.Completion?.TrySetResult(ShellNavigationResult.Failed("StaleNavigation"));
            return;
        }

        var reason = e.Exception?.GetType().Name ?? "NavigationFailed";
        request.Completion?.TrySetResult(ShellNavigationResult.Failed(reason));
        NavigationFailed?.Invoke(this, new ContentFrameNavigationFailedEventArgs(request.PageType, request.Parameter, reason));
    }

    private ShellNavigationResult CompleteCurrentRequest(ContentFrameNavigationRequest request)
    {
        ClearActiveRequest(request);
        if (IsStaleNavigation(request.ActivationToken))
        {
            request.Completion?.TrySetResult(ShellNavigationResult.Failed("StaleNavigation"));
            return ShellNavigationResult.Failed("StaleNavigation");
        }

        var result = IsDisplaying(request.PageType)
            ? ShellNavigationResult.Success()
            : ShellNavigationResult.Failed("ContentNotProjected");
        request.Completion?.TrySetResult(result);
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

    private void SupersedeActiveRequest(ContentFrameNavigationRequest nextRequest)
    {
        var activeRequest = _activeRequest;
        if (activeRequest is not null && !ReferenceEquals(activeRequest, nextRequest))
        {
            activeRequest.Completion?.TrySetResult(ShellNavigationResult.Failed("StaleNavigation"));
            RememberSupersededRequest(activeRequest, nextRequest);
        }

        _activeRequest = nextRequest;
    }

    private void RestoreLatestRequest(ContentFrameNavigationRequest request)
    {
        if (IsStaleNavigation(request.ActivationToken))
        {
            return;
        }

        if (!ReferenceEquals(_activeRequest, request))
        {
            _activeRequest = request;
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
            ClearActiveRequest(request);
            request.Completion?.TrySetResult(ShellNavigationResult.Failed(ex.GetType().Name));
            return;
        }

        if (!navigated)
        {
            ClearActiveRequest(request);
            request.Completion?.TrySetResult(ShellNavigationResult.Failed("NavigateReturnedFalse"));
        }
    }

    private void RememberSupersededRequest(
        ContentFrameNavigationRequest activeRequest,
        ContentFrameNavigationRequest nextRequest)
    {
        if (activeRequest.Matches(nextRequest.PageType, nextRequest.Parameter)
            || FindSupersededRequest(activeRequest.PageType, activeRequest.Parameter) is not null)
        {
            return;
        }

        _supersededRequests.Add(activeRequest);
        if (_supersededRequests.Count > 8)
        {
            _supersededRequests.RemoveAt(0);
        }
    }

    private bool TryConsumeSupersededRequest(Type? pageType, object? parameter)
    {
        var request = FindSupersededRequest(pageType, parameter);
        if (request is null)
        {
            return false;
        }

        RemoveSupersededRequest(request);
        return true;
    }

    private bool TryConsumeSupersededRequest(Type? pageType)
    {
        var request = FindSupersededRequest(pageType);
        if (request is null)
        {
            return false;
        }

        RemoveSupersededRequest(request);
        return true;
    }

    private ContentFrameNavigationRequest? FindSupersededRequest(Type? pageType, object? parameter)
    {
        foreach (var request in _supersededRequests)
        {
            if (request.Matches(pageType, parameter))
            {
                return request;
            }
        }

        return null;
    }

    private ContentFrameNavigationRequest? FindSupersededRequest(Type? pageType)
    {
        foreach (var request in _supersededRequests)
        {
            if (request.PageType == pageType)
            {
                return request;
            }
        }

        return null;
    }

    private void RemoveSupersededRequest(ContentFrameNavigationRequest request)
        => _supersededRequests.Remove(request);

    private void ClearActiveRequest(ContentFrameNavigationRequest request)
    {
        if (ReferenceEquals(_activeRequest, request))
        {
            _activeRequest = null;
        }
    }

    private bool IsStaleNavigation(long? activationToken)
        => activationToken.HasValue && _latestNavigationToken != activationToken.Value;

    private sealed class ContentFrameNavigationRequest
    {
        public ContentFrameNavigationRequest(Type pageType, object? parameter, long? activationToken)
        {
            PageType = pageType;
            Parameter = parameter;
            ActivationToken = activationToken;
        }

        public Type PageType { get; }

        public object? Parameter { get; }

        public long? ActivationToken { get; }

        public TaskCompletionSource<ShellNavigationResult>? Completion { get; set; }

        public bool Matches(Type? sourcePageType, object? parameter)
            => sourcePageType == PageType
               && Equals(parameter, Parameter);
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
