using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <remarks>
/// 继承 <see cref="IAsyncDisposable"/> 而非只依赖 <see cref="IDisposable"/>：本 facade 持有的
/// 是<b>不在会话注册表里</b>的 ACP 连接（它从不 <c>RecordSession</c>），因此基于注册表的
/// 关闭 drain 抓不到它，只能由 teardown owner 直接释放。而释放要 <c>DisconnectAsync</c> 后
/// 再 <c>Dispose</c>——同步的 <see cref="IDisposable.Dispose"/> 只能 fire-and-forget 这一段，
/// 在进程退出路径上等于和进程赛跑，agent 子进程可能来不及被终止（issue #126）。
/// </remarks>
public interface IDiscoverSessionsConnectionFacade : INotifyPropertyChanged, IAsyncDisposable
{
    bool IsConnecting { get; }

    bool IsInitializing { get; }

    bool IsConnected { get; }

    string? ConnectionErrorMessage { get; }

    IChatService? CurrentChatService { get; }

    Task ConnectToProfileAsync(ServerConfiguration profile);
}

public sealed class DiscoverSessionsConnectionFacade : IDiscoverSessionsConnectionFacade, IDisposable
{
    private readonly IAcpChatServiceFactory _chatServiceFactory;
    private readonly ITransportSupportPolicy _transportSupportPolicy;
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
        ITransportSupportPolicy transportSupportPolicy,
        ILogger<DiscoverSessionsConnectionFacade> logger)
    {
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _transportSupportPolicy = transportSupportPolicy ?? throw new ArgumentNullException(nameof(transportSupportPolicy));
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

        if (!_transportSupportPolicy.IsSupported(profile.Transport))
        {
            var message = _transportSupportPolicy.GetUnsupportedReason(profile.Transport)
                ?? $"Unsupported transport type: {profile.Transport}.";
            UpdateConnectedTarget(null, DiscoverConnectionTarget.None);
            UpdateConnectionState(
                isConnecting: false,
                isInitializing: false,
                isConnected: false,
                errorMessage: message,
                currentChatService: null);
            throw new NotSupportedException(message);
        }

        var (requestVersion, cancellationToken) = BeginConnectRequest();
        IChatService? previousService = null;
        IChatService? candidateService = null;

        try
        {
            previousService = DetachCurrentService();
            await DisposeServiceAsync(previousService).ConfigureAwait(false);

            UpdateConnectionState(isConnecting: true, isInitializing: false, isConnected: false, errorMessage: null, currentChatService: null);

            candidateService = _chatServiceFactory.CreateChatService(profile);

            UpdateConnectionState(isConnecting: false, isInitializing: true, isConnected: false, errorMessage: null, currentChatService: null);
            await AcpInitializeTimeout.WaitForInitializeAsync(
                    candidateService,
                    profile.Transport,
                    profile.Id,
                    conversationId: null,
                    AcpInitializeTimeout.Resolve(profile),
                    cancellationToken)
                .ConfigureAwait(false);

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

    public void Dispose()
    {
        if (!TryClaimDisposal(out var cts, out var currentService))
        {
            return;
        }

        cts?.Cancel();
        cts?.Dispose();
        if (currentService != null)
        {
            try
            {
                // Dispose is synchronous by IDisposable contract; fire-and-forget is acceptable here.
                // 关闭路径必须走 DisposeAsync 而不是这里——见 IDiscoverSessionsConnectionFacade 注释。
                _ = DisposeServiceAsync(currentService).AsTask();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose Discover ACP browse service");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!TryClaimDisposal(out var cts, out var currentService))
        {
            return;
        }

        cts?.Cancel();
        cts?.Dispose();

        // 与 Dispose 的唯一区别：等待服务真正断开并释放。DisposeServiceAsync 自吞异常，
        // 所以关闭路径不会因为这一步抛出。
        await DisposeServiceAsync(currentService).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子地认领这一次释放：胜者拿走 CTS 与当前服务，其余调用直接返回。
    /// </summary>
    private bool TryClaimDisposal(out CancellationTokenSource? cts, out IChatService? currentService)
    {
        lock (_connectSync)
        {
            if (_disposed)
            {
                cts = null;
                currentService = null;
                return false;
            }

            _disposed = true;
            cts = _connectCts;
            _connectCts = null;
            currentService = _currentChatService;
            _currentChatService = null;
            return true;
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

        try
        {
            chatService.Dispose();
        }
        catch (Exception ex)
        {
            // 清理路径的释放失败不得逃逸:在 catch 分支里 rethrow 会顶替真正的连接异常。
            _logger.LogDebug(ex, "Failed to dispose Discover ACP browse service cleanly");
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
        string StdioArgumentsCanonical,
        string RemoteUrl)
    {
        public static DiscoverConnectionTarget None { get; } = new(TransportType.Stdio, string.Empty, string.Empty, string.Empty);

        public static DiscoverConnectionTarget FromProfile(ServerConfiguration profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return new DiscoverConnectionTarget(
                profile.Transport,
                (profile.StdioCommand ?? string.Empty).Trim(),
                StdioCommandLine.CanonicalizeArguments(profile.StdioArguments),
                (profile.ServerUrl ?? string.Empty).Trim());
        }
    }
}
