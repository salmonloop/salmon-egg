using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IDiscoverSessionsConnectionFacade : INotifyPropertyChanged
{
    bool IsConnecting { get; }

    bool IsInitializing { get; }

    bool IsConnected { get; }

    string? ConnectionErrorMessage { get; }

    IChatService? CurrentChatService { get; }

    Task ConnectToProfileAsync(ServerConfiguration profile);

    Task<bool> HydrateActiveConversationAsync(CancellationToken cancellationToken = default);
}

public sealed class DiscoverSessionsConnectionFacade : IDiscoverSessionsConnectionFacade, IDisposable
{
    private readonly IAcpChatServiceFactory _chatServiceFactory;
    private readonly Func<CancellationToken, Task<bool>> _hydrateActiveConversationAsync;
    private readonly ILogger<DiscoverSessionsConnectionFacade> _logger;
    private readonly object _connectSync = new();
    private CancellationTokenSource? _connectCts;
    private long _connectVersion;
    private bool _disposed;
    private string? _connectedProfileId;
    private DiscoverConnectionTarget _connectedTarget;
    private bool _isConnecting;
    private bool _isInitializing;
    private bool _isConnected;
    private string? _connectionErrorMessage;
    private IChatService? _currentChatService;

    public DiscoverSessionsConnectionFacade(
        IAcpChatServiceFactory chatServiceFactory,
        Func<CancellationToken, Task<bool>> hydrateActiveConversationAsync,
        ILogger<DiscoverSessionsConnectionFacade> logger)
    {
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _hydrateActiveConversationAsync = hydrateActiveConversationAsync ?? throw new ArgumentNullException(nameof(hydrateActiveConversationAsync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsConnecting
    {
        get => _isConnecting;
        private set => SetProperty(ref _isConnecting, value, nameof(IsConnecting));
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set => SetProperty(ref _isInitializing, value, nameof(IsInitializing));
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value, nameof(IsConnected));
    }

    public string? ConnectionErrorMessage
    {
        get => _connectionErrorMessage;
        private set => SetProperty(ref _connectionErrorMessage, value, nameof(ConnectionErrorMessage));
    }

    public IChatService? CurrentChatService
    {
        get => _currentChatService;
        private set => SetProperty(ref _currentChatService, value, nameof(CurrentChatService));
    }

    public async Task ConnectToProfileAsync(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfDisposed();

        var target = DiscoverConnectionTarget.FromProfile(profile);
        if (TryReuseConnectedService(profile.Id, target))
        {
            return;
        }

        var (requestVersion, cancellationToken) = BeginConnectRequest();
        IChatService? previousService = null;
        IChatService? candidateService = null;

        try
        {
            previousService = DetachCurrentService();
            await DisposeServiceAsync(previousService).ConfigureAwait(false);

            UpdateConnectionState(isConnecting: true, isInitializing: false, isConnected: false, errorMessage: null, currentChatService: null);

            candidateService = _chatServiceFactory.CreateChatService(
                profile.Transport,
                profile.Transport == TransportType.Stdio ? profile.StdioCommand : null,
                profile.Transport == TransportType.Stdio ? profile.StdioArgs : null,
                profile.Transport == TransportType.Stdio ? null : profile.ServerUrl);

            UpdateConnectionState(isConnecting: false, isInitializing: true, isConnected: false, errorMessage: null, currentChatService: null);
            await candidateService.InitializeAsync(AcpInitializeRequestFactory.CreateDefault()).ConfigureAwait(false);

            if (!IsLatestConnectRequest(requestVersion, cancellationToken))
            {
                await DisposeServiceAsync(candidateService).ConfigureAwait(false);
                _logger.LogDebug(
                    "Discarding superseded Discover ACP browse connection before commit. profileId={ProfileId}",
                    profile.Id);
                return;
            }

            CommitConnectedService(profile.Id, target, candidateService);
            candidateService = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeServiceAsync(candidateService).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DisposeServiceAsync(candidateService).ConfigureAwait(false);

            if (!IsLatestConnectRequest(requestVersion, cancellationToken))
            {
                _logger.LogDebug(
                    ex,
                    "Discarding superseded Discover ACP browse connection failure. profileId={ProfileId}",
                    profile.Id);
                return;
            }

            UpdateConnectedTarget(null, DiscoverConnectionTarget.None);
            UpdateConnectionState(isConnecting: false, isInitializing: false, isConnected: false, errorMessage: ex.Message, currentChatService: null);
            _logger.LogError(ex, "Failed to connect Discover ACP browse service. profileId={ProfileId}", profile.Id);
            throw;
        }
    }

    public Task<bool> HydrateActiveConversationAsync(CancellationToken cancellationToken = default)
        => _hydrateActiveConversationAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancellationTokenSource? cts;
        IChatService? currentService;
        lock (_connectSync)
        {
            cts = _connectCts;
            _connectCts = null;
            currentService = _currentChatService;
            _currentChatService = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        if (currentService != null)
        {
            try
            {
                // Dispose is synchronous by IDisposable contract; fire-and-forget is acceptable here.
                _ = DisposeServiceAsync(currentService).AsTask();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose Discover ACP browse service");
            }
        }
    }

    private (long Version, CancellationToken Token) BeginConnectRequest()
    {
        lock (_connectSync)
        {
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();
            var version = ++_connectVersion;
            return (version, _connectCts.Token);
        }
    }

    private bool IsLatestConnectRequest(long version, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested && version == Interlocked.Read(ref _connectVersion);

    private bool TryReuseConnectedService(string profileId, DiscoverConnectionTarget target)
    {
        if (_currentChatService is not { IsConnected: true, IsInitialized: true } current)
        {
            return false;
        }

        if (!string.Equals(_connectedProfileId, profileId, StringComparison.Ordinal) || !_connectedTarget.Equals(target))
        {
            return false;
        }

        UpdateConnectionState(isConnecting: false, isInitializing: false, isConnected: true, errorMessage: null, currentChatService: current);
        return true;
    }

    private IChatService? DetachCurrentService()
    {
        var current = _currentChatService;
        CurrentChatService = null;
        UpdateConnectedTarget(null, DiscoverConnectionTarget.None);
        return current;
    }

    private void CommitConnectedService(string profileId, DiscoverConnectionTarget target, IChatService chatService)
    {
        CurrentChatService = chatService;
        UpdateConnectedTarget(profileId, target);
        UpdateConnectionState(isConnecting: false, isInitializing: false, isConnected: true, errorMessage: null, currentChatService: chatService);
    }

    private void UpdateConnectedTarget(string? profileId, DiscoverConnectionTarget target)
    {
        _connectedProfileId = profileId;
        _connectedTarget = target;
    }

    private void UpdateConnectionState(
        bool isConnecting,
        bool isInitializing,
        bool isConnected,
        string? errorMessage,
        IChatService? currentChatService)
    {
        CurrentChatService = currentChatService;
        ConnectionErrorMessage = errorMessage;
        IsConnected = isConnected;
        IsInitializing = isInitializing;
        IsConnecting = isConnecting;
    }

    private async ValueTask DisposeServiceAsync(IChatService? chatService)
    {
        if (chatService == null)
        {
            return;
        }

        try
        {
            await chatService.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to disconnect Discover ACP browse service cleanly");
        }

        if (chatService is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DiscoverSessionsConnectionFacade));
        }
    }

    private void SetProperty<T>(ref T field, T value, string propertyName)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly record struct DiscoverConnectionTarget(
        TransportType TransportType,
        string StdioCommand,
        string StdioArgs,
        string RemoteUrl)
    {
        public static DiscoverConnectionTarget None { get; } = new(TransportType.Stdio, string.Empty, string.Empty, string.Empty);

        public static DiscoverConnectionTarget FromProfile(ServerConfiguration profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return new DiscoverConnectionTarget(
                profile.Transport,
                (profile.StdioCommand ?? string.Empty).Trim(),
                (profile.StdioArgs ?? string.Empty).Trim(),
                (profile.ServerUrl ?? string.Empty).Trim());
        }
    }
}
