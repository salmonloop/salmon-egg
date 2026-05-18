using System;
using System.Collections.Generic;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Core.Services.Navigation;

public sealed class ContentNavigationRequestTracker
{
    private readonly List<ContentNavigationRequest> _pendingFrameRequests = new();
    private readonly List<ContentNavigationRequest> _supersededRequests = new();
    private ContentNavigationRequest? _activeRequest;
    private ContentNavigationRequest? _latestRequest;
    private long _latestNavigationToken;

    public ContentNavigationRequest BeginRequest(Type pageType, object? parameter, long? activationToken)
    {
        if (activationToken.HasValue)
        {
            _latestNavigationToken = activationToken.Value;
        }

        var request = new ContentNavigationRequest(pageType, parameter, activationToken);
        SupersedeActiveRequest(request);
        _latestRequest = request;
        return request;
    }

    public bool TryResolveNavigating(Type? pageType, object? parameter, out bool cancel)
    {
        cancel = false;
        if (_activeRequest is { } activeRequest
            && activeRequest.Matches(pageType, parameter))
        {
            RememberPendingFrameRequest(activeRequest);
            return true;
        }

        var supersededRequest = FindSupersededRequest(pageType, parameter);
        if (supersededRequest is null)
        {
            return false;
        }

        if (_latestRequest is { } latestRequest
            && latestRequest.Matches(pageType, parameter))
        {
            RemoveSupersededRequest(supersededRequest);
            RememberPendingFrameRequest(latestRequest);
            return true;
        }

        cancel = true;
        RemoveSupersededRequest(supersededRequest);
        return true;
    }

    public ContentNavigationCompletion ResolveNavigated(Type? pageType, object? parameter)
    {
        var request = _activeRequest;
        if (request is not null && request.Matches(pageType, parameter))
        {
            RemovePendingFrameRequest(pageType, parameter);
            return ContentNavigationCompletion.Active(request);
        }

        if (TryConsumeSupersededRequest(pageType, parameter))
        {
            RemovePendingFrameRequest(pageType, parameter);
            return _latestRequest is null
                ? ContentNavigationCompletion.None()
                : ContentNavigationCompletion.RestoreLatest(_latestRequest);
        }

        return request is null
            ? ContentNavigationCompletion.None()
            : ContentNavigationCompletion.RestoreLatest(_latestRequest ?? request);
    }

    public ContentNavigationFailure ResolveNavigationFailed(Type? pageType)
    {
        var request = ConsumePendingFrameRequest(pageType) ?? _activeRequest;
        if (request is null || request.PageType != pageType)
        {
            _ = TryConsumeSupersededRequest(pageType);
            return ContentNavigationFailure.None();
        }

        if (!ReferenceEquals(request, _activeRequest))
        {
            RemoveSupersededRequest(request);
            request.Complete(ShellNavigationResult.Failed("StaleNavigation"));
            return ContentNavigationFailure.Stale(request);
        }

        ClearActiveRequest(request);
        if (IsStale(request.ActivationToken))
        {
            request.Complete(ShellNavigationResult.Failed("StaleNavigation"));
            return ContentNavigationFailure.Stale(request);
        }

        return ContentNavigationFailure.Active(request);
    }

    public ShellNavigationResult CompleteActive(ContentNavigationRequest request, bool isDisplaying)
    {
        ClearActiveRequest(request);
        if (IsStale(request.ActivationToken))
        {
            return request.Complete(ShellNavigationResult.Failed("StaleNavigation"));
        }

        return request.Complete(
            isDisplaying
                ? ShellNavigationResult.Success()
                : ShellNavigationResult.Failed("ContentNotProjected"));
    }

    public void ClearActive(ContentNavigationRequest request) => ClearActiveRequest(request);

    public bool IsStale(long? activationToken)
        => activationToken.HasValue && _latestNavigationToken != activationToken.Value;

    private void SupersedeActiveRequest(ContentNavigationRequest nextRequest)
    {
        var activeRequest = _activeRequest;
        if (activeRequest is not null && !ReferenceEquals(activeRequest, nextRequest))
        {
            activeRequest.Complete(ShellNavigationResult.Failed("StaleNavigation"));
            RememberSupersededRequest(activeRequest, nextRequest);
        }

        _activeRequest = nextRequest;
    }

    private void RememberSupersededRequest(
        ContentNavigationRequest activeRequest,
        ContentNavigationRequest nextRequest)
    {
        if (activeRequest.Matches(nextRequest.PageType, nextRequest.Parameter)
            || FindSupersededRequest(activeRequest.PageType, activeRequest.Parameter) is not null)
        {
            return;
        }

        _supersededRequests.Add(activeRequest);
        TrimOldest(_supersededRequests);
    }

    private void RememberPendingFrameRequest(ContentNavigationRequest request)
    {
        if (_pendingFrameRequests.Contains(request))
        {
            return;
        }

        _pendingFrameRequests.Add(request);
        TrimOldest(_pendingFrameRequests);
    }

    private ContentNavigationRequest? ConsumePendingFrameRequest(Type? pageType)
    {
        for (var i = 0; i < _pendingFrameRequests.Count; i++)
        {
            var request = _pendingFrameRequests[i];
            if (request.PageType == pageType)
            {
                _pendingFrameRequests.RemoveAt(i);
                return request;
            }
        }

        return null;
    }

    private void RemovePendingFrameRequest(Type? pageType, object? parameter)
    {
        for (var i = 0; i < _pendingFrameRequests.Count; i++)
        {
            if (_pendingFrameRequests[i].Matches(pageType, parameter))
            {
                _pendingFrameRequests.RemoveAt(i);
                return;
            }
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

    private ContentNavigationRequest? FindSupersededRequest(Type? pageType, object? parameter)
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

    private ContentNavigationRequest? FindSupersededRequest(Type? pageType)
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

    private void RemoveSupersededRequest(ContentNavigationRequest request)
        => _supersededRequests.Remove(request);

    private void ClearActiveRequest(ContentNavigationRequest request)
    {
        if (ReferenceEquals(_activeRequest, request))
        {
            _activeRequest = null;
        }
    }

    private static void TrimOldest(List<ContentNavigationRequest> requests)
    {
        if (requests.Count > 8)
        {
            requests.RemoveAt(0);
        }
    }
}

public sealed class ContentNavigationRequest
{
    public ContentNavigationRequest(Type pageType, object? parameter, long? activationToken)
    {
        PageType = pageType;
        Parameter = parameter;
        ActivationToken = activationToken;
    }

    public Type PageType { get; }

    public object? Parameter { get; }

    public long? ActivationToken { get; }

    public TaskCompletionSource<ShellNavigationResult>? Completion { get; set; }

    public ShellNavigationResult Complete(ShellNavigationResult result)
    {
        Completion?.TrySetResult(result);
        return result;
    }

    public bool Matches(Type? sourcePageType, object? parameter)
        => sourcePageType == PageType
           && Equals(parameter, Parameter);
}

public readonly record struct ContentNavigationCompletion(
    ContentNavigationCompletionKind Kind,
    ContentNavigationRequest? Request)
{
    public static ContentNavigationCompletion Active(ContentNavigationRequest request)
        => new(ContentNavigationCompletionKind.Active, request);

    public static ContentNavigationCompletion RestoreLatest(ContentNavigationRequest request)
        => new(ContentNavigationCompletionKind.RestoreLatest, request);

    public static ContentNavigationCompletion None()
        => new(ContentNavigationCompletionKind.None, null);
}

public enum ContentNavigationCompletionKind
{
    None,
    Active,
    RestoreLatest
}

public readonly record struct ContentNavigationFailure(
    ContentNavigationFailureKind Kind,
    ContentNavigationRequest? Request)
{
    public static ContentNavigationFailure Active(ContentNavigationRequest request)
        => new(ContentNavigationFailureKind.Active, request);

    public static ContentNavigationFailure Stale(ContentNavigationRequest request)
        => new(ContentNavigationFailureKind.Stale, request);

    public static ContentNavigationFailure None()
        => new(ContentNavigationFailureKind.None, null);
}

public enum ContentNavigationFailureKind
{
    None,
    Active,
    Stale
}
