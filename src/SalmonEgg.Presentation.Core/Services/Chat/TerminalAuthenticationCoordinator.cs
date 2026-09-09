using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>Owns one consent, PTY and reconnect sequence, independent of conversation terminals.</summary>
public sealed class TerminalAuthenticationCoordinator : IAsyncDisposable
{
    private readonly ITerminalAuthenticationSessionFactory _factory;
    private readonly ITerminalAuthenticationInteraction _interaction;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<TerminalAuthenticationCoordinator> _logger;
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TerminalAuthenticationCoordinator(
        ITerminalAuthenticationSessionFactory factory,
        ITerminalAuthenticationInteraction interaction,
        IUiDispatcher dispatcher,
        ILogger<TerminalAuthenticationCoordinator> logger,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localizer = localizer;
    }

    public bool CanAuthenticate(IChatService? chatService)
        => _factory.IsSupported && chatService is IStdioInvocationSource { StdioInvocation: not null };

    public async Task<bool> TryAuthenticateAsync(
        AuthMethodDefinition method,
        IAcpChatCoordinatorSink sink,
        Func<AcpConnectionContext, CancellationToken, Task<bool>> reconnectAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(reconnectAsync);
        if (method.ResolvedType != AuthMethodDefinition.TerminalType || string.IsNullOrWhiteSpace(method.Id)
            || !CanAuthenticate(sink.CurrentChatService)) return false;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        // A second auth_required from the same connection must not launch a second login process.
        if (!await _gate.WaitAsync(0, lifetime.Token).ConfigureAwait(false)) return false;
        try
        {
            return await AuthenticateCoreAsync(method, sink, reconnectAsync, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            // Exceptions from process creation can contain invocation/environment details.
            _logger.LogWarning("Interactive agent sign-in did not complete.");
            return false;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        if (_factory is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<bool> AuthenticateCoreAsync(
        AuthMethodDefinition method,
        IAcpChatCoordinatorSink sink,
        Func<AcpConnectionContext, CancellationToken, Task<bool>> reconnectAsync,
        CancellationToken cancellationToken)
    {
        using var intent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AuthenticationIdentity? identity = null;
        TerminalAuthenticationViewModel? viewModel = null;
        PropertyChangedEventHandler changed = (_, _) =>
        {
            if (identity is not null && !identity.Matches(sink)) intent.Cancel();
        };
        await _dispatcher.EnqueueAsync(() =>
        {
            if (sink.CurrentChatService is not IStdioInvocationSource { StdioInvocation: { } invocation }
                || !sink.IsInitialized || string.IsNullOrWhiteSpace(sink.SelectedProfileId)) return;
            identity = new AuthenticationIdentity(sink, invocation);
            viewModel = new TerminalAuthenticationViewModel(sink.AgentName ?? method.Name, method.Name, _localizer);
            sink.PropertyChanged += changed;
        }).ConfigureAwait(false);
        if (identity is null || viewModel is null) return false;

        try
        {
            if (!await ConfirmAsync(viewModel, intent.Token).ConfigureAwait(false)
                || !await IsCurrentAsync(identity, sink).ConfigureAwait(false)) return false;
            var invocation = identity.Invocation.WithAuthenticationMethod(method.Args, method.Env);
            if (!await RunSessionAsync(viewModel, invocation, intent.Token).ConfigureAwait(false)) return false;
            intent.Token.ThrowIfCancellationRequested();
            if (!await IsCurrentAsync(identity, sink).ConfigureAwait(false)) return false;
        }
        finally
        {
            await _dispatcher.EnqueueAsync(() => sink.PropertyChanged -= changed).ConfigureAwait(false);
        }

        // From here the connection coordinator owns latest-intent and candidate commit. The expected
        // identity prevents a consent completion from reconnecting over a newly selected profile.
        return await reconnectAsync(identity.ReconnectContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ConfirmAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken)
    {
        var confirmed = false;
        await _dispatcher.EnqueueAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            confirmed = await _interaction.ConfirmAsync(viewModel, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(false);
        return confirmed;
    }

    private async Task<bool> IsCurrentAsync(AuthenticationIdentity identity, IAcpChatCoordinatorSink sink)
    {
        var current = false;
        await _dispatcher.EnqueueAsync(() => current = identity.Matches(sink)).ConfigureAwait(false);
        return current;
    }

    private async Task<bool> RunSessionAsync(
        TerminalAuthenticationViewModel viewModel,
        StdioInvocationSnapshot invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await _factory.StartAsync(invocation, cancellationToken).ConfigureAwait(false);
        using var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _dispatcher.EnqueueAsync(() => viewModel.Session = session).ConfigureAwait(false);
        var dialog = _dispatcher.EnqueueAsync(() => _interaction.ShowSessionAsync(viewModel, dialogLifetime.Token));
        try
        {
            var exited = session.Completion.WaitAsync(cancellationToken);
            var completed = await Task.WhenAny(exited, dialog).ConfigureAwait(false);
            if (completed != exited || cancellationToken.IsCancellationRequested) return false;
            return await exited.ConfigureAwait(false) == 0;
        }
        finally
        {
            await dialogLifetime.CancelAsync().ConfigureAwait(false);
            try { await dialog.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            await _dispatcher.EnqueueAsync(() => viewModel.Session = null).ConfigureAwait(false);
        }
    }

    private sealed class AuthenticationIdentity
    {
        private readonly IChatService? _service;
        private readonly string? _profileId;
        private readonly string? _conversationId;
        private readonly string? _connectionId;

        public AuthenticationIdentity(IAcpChatCoordinatorSink sink, StdioInvocationSnapshot invocation)
        {
            _service = sink.CurrentChatService;
            _profileId = sink.SelectedProfileId;
            _conversationId = sink.CurrentSessionId;
            _connectionId = sink.ConnectionInstanceId;
            Invocation = invocation;
            ReconnectContext = new AcpConnectionContext(_conversationId, PreserveConversation: true)
            {
                ForceReconnect = true,
                ExpectedChatService = _service,
                ExpectedConnectionInstanceId = _connectionId,
                ExpectedProfileId = _profileId
            };
        }

        public StdioInvocationSnapshot Invocation { get; }

        public AcpConnectionContext ReconnectContext { get; }

        public bool Matches(IAcpChatCoordinatorSink sink)
            => ReferenceEquals(_service, sink.CurrentChatService) && _service is { IsConnected: true }
                && sink.IsInitialized && !sink.IsConnecting && !sink.IsInitializing
                && string.Equals(_profileId, sink.SelectedProfileId, StringComparison.Ordinal)
                && string.Equals(_conversationId, sink.CurrentSessionId, StringComparison.Ordinal)
                && string.Equals(_connectionId, sink.ConnectionInstanceId, StringComparison.Ordinal);
    }
}
