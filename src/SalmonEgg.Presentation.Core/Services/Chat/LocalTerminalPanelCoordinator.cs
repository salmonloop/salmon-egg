using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed partial class LocalTerminalPanelCoordinator : ObservableObject, IAsyncDisposable
{
    private readonly ILocalTerminalSessionManager _sessionManager;
    private readonly ILocalTerminalCwdResolver _cwdResolver;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Dictionary<string, SessionRegistration> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public LocalTerminalPanelCoordinator(
        ILocalTerminalSessionManager sessionManager,
        ILocalTerminalCwdResolver cwdResolver,
        IUiDispatcher uiDispatcher)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _cwdResolver = cwdResolver ?? throw new ArgumentNullException(nameof(cwdResolver));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    [ObservableProperty]
    private LocalTerminalPanelSessionViewModel? _activeSession;

    public async Task<LocalTerminalPanelSessionViewModel> ActivateAsync(
        string conversationId,
        bool isLocalSession,
        string? sessionInfoCwd,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_sessions.TryGetValue(conversationId, out var existing))
            {
                await RefreshViewModelAsync(existing.ViewModel).ConfigureAwait(false);
                await PublishActiveSessionAsync(existing.ViewModel).ConfigureAwait(false);
                return existing.ViewModel;
            }

            var resolvedCwd = _cwdResolver.Resolve(isLocalSession, sessionInfoCwd);
            var session = await _sessionManager
                .GetOrCreateAsync(conversationId, resolvedCwd, cancellationToken)
                .ConfigureAwait(false);
            var viewModel = await CreateViewModelAsync(conversationId, session).ConfigureAwait(false);
            var registration = RegisterSession(session, viewModel);
            _sessions.Add(conversationId, registration);

            await PublishActiveSessionAsync(viewModel).ConfigureAwait(false);
            return viewModel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        SessionRegistration? registration = null;
        var clearsActiveSession = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(conversationId, out registration))
            {
                registration.Detach();
                _sessions.Remove(conversationId);
                clearsActiveSession = ActiveSession is not null
                    && string.Equals(ActiveSession.ConversationId, conversationId, StringComparison.Ordinal);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (registration is null)
        {
            return;
        }

        await _sessionManager
            .DisposeConversationAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (clearsActiveSession)
        {
            await PublishActiveSessionAsync(null).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<SessionRegistration> registrations;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            registrations = new List<SessionRegistration>(_sessions.Values);
            _sessions.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var registration in registrations)
        {
            registration.Detach();
        }

        await PublishDisposedActiveSessionAsync().ConfigureAwait(false);
        await _sessionManager.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private SessionRegistration RegisterSession(
        ILocalTerminalSession session,
        LocalTerminalPanelSessionViewModel viewModel)
    {
        SessionRegistration? registration = null;
        var conversationId = viewModel.ConversationId;

        void HandleOutputReceived(object? _, string output)
        {
            ProcessOutputReceived(conversationId, registration, viewModel, output);
        }

        void HandleStateChanged(object? _, EventArgs __)
        {
            ProcessStateChanged(conversationId, registration, viewModel);
        }

        session.OutputReceived += HandleOutputReceived;
        session.StateChanged += HandleStateChanged;
        registration = new SessionRegistration(
            session,
            viewModel,
            HandleOutputReceived,
            HandleStateChanged);
        return registration;
    }

    private async Task<LocalTerminalPanelSessionViewModel> CreateViewModelAsync(
        string conversationId,
        ILocalTerminalSession session)
    {
        LocalTerminalPanelSessionViewModel? viewModel = null;
        await ExecuteOnUiAsync(() =>
        {
            viewModel = new LocalTerminalPanelSessionViewModel(conversationId, session);
        }).ConfigureAwait(false);

        return viewModel!;
    }

    private Task RefreshViewModelAsync(LocalTerminalPanelSessionViewModel viewModel)
        => ExecuteOnUiAsync(viewModel.RefreshFromSession);

    private Task PublishActiveSessionAsync(LocalTerminalPanelSessionViewModel? viewModel)
        => ExecuteOnUiAsync(() => ActiveSession = viewModel);

    private Task PublishDisposedActiveSessionAsync()
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            ActiveSession = null;
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(() => ActiveSession = null);
    }

    private void ProcessOutputReceived(
        string conversationId,
        SessionRegistration? registration,
        LocalTerminalPanelSessionViewModel viewModel,
        string output)
    {
        if (!ShouldProcessRegistration(conversationId, registration) || string.IsNullOrEmpty(output))
        {
            return;
        }

        DispatchSessionEvent(conversationId, registration, () => viewModel.AppendOutput(output));
    }

    private void ProcessStateChanged(
        string conversationId,
        SessionRegistration? registration,
        LocalTerminalPanelSessionViewModel viewModel)
    {
        if (!ShouldProcessRegistration(conversationId, registration))
        {
            return;
        }

        DispatchSessionEvent(conversationId, registration, viewModel.RefreshFromSession);
    }

    private void DispatchSessionEvent(
        string conversationId,
        SessionRegistration? registration,
        Action action)
    {
        void GuardedAction()
        {
            if (!ShouldProcessRegistration(conversationId, registration))
            {
                return;
            }

            action();
        }

        if (_uiDispatcher.HasThreadAccess)
        {
            GuardedAction();
            return;
        }

        _uiDispatcher.Enqueue(GuardedAction);
    }

    private Task ExecuteOnUiAsync(Action action)
    {
        ThrowIfDisposed();
        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        return _uiDispatcher.EnqueueAsync(action);
    }

    private bool ShouldProcessRegistration(string conversationId, SessionRegistration? registration)
    {
        if (registration is null || registration.IsDetached || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        return string.Equals(registration.ViewModel.ConversationId, conversationId, StringComparison.Ordinal);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LocalTerminalPanelCoordinator));
        }
    }

    private sealed class SessionRegistration
    {
        public SessionRegistration(
            ILocalTerminalSession session,
            LocalTerminalPanelSessionViewModel viewModel,
            EventHandler<string> outputReceivedHandler,
            EventHandler stateChangedHandler)
        {
            Session = session;
            ViewModel = viewModel;
            OutputReceivedHandler = outputReceivedHandler;
            StateChangedHandler = stateChangedHandler;
        }

        public ILocalTerminalSession Session { get; }

        public LocalTerminalPanelSessionViewModel ViewModel { get; }

        public EventHandler<string> OutputReceivedHandler { get; }

        public EventHandler StateChangedHandler { get; }

        public bool IsDetached { get; private set; }

        public void Detach()
        {
            if (IsDetached)
            {
                return;
            }

            IsDetached = true;
            Session.OutputReceived -= OutputReceivedHandler;
            Session.StateChanged -= StateChangedHandler;
        }
    }
}
