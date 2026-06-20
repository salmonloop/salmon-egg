using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Settings;

/// <summary>
/// Wraps a single <see cref="ServerConfiguration"/> and exposes the per-profile
/// connection state + toggle command to the Settings page.
///
/// State flows from the bottom up:
///   AcpConnectionPoolManager.RecordSession / RemoveByProfile
///     → IAcpConnectionSessionEvents.ProfileConnectionChanged
///       → AgentProfileItemViewModel.IsConnected / IsConnecting
///
/// Connect/Disconnect flows from the top down (Plan B: Settings delegates to ChatViewModel
/// via ISettingsAcpConnectionCommands, keeping a single IAcpChatCoordinatorSink).
/// </summary>
public sealed partial class AgentProfileItemViewModel : ObservableObject, IDisposable
{
    private readonly IAcpConnectionSessionRegistry _registry;
    private readonly IAcpConnectionSessionEvents _events;
    private readonly ISettingsAcpConnectionCommands _commands;
    private readonly ILogger<AgentProfileItemViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private bool _disposed;

    // ── Observable state ────────────────────────────────────────────────────

    /// <summary>
    /// True when the pool has a live, connected session for this profile.
    /// Driven by <see cref="IAcpConnectionSessionEvents.ProfileConnectionChanged"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsNotConnected))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    private bool _isConnected;

    public bool IsNotConnected => !IsConnected;

    /// <summary>
    /// True while a connect/disconnect operation is in flight for this profile.
    /// Set optimistically before delegating to <see cref="_commands"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    private bool _isConnecting;

    // ── Display data (read-only, derived from ServerConfiguration) ──────────

    public string ProfileId { get; }

    private string _name;

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Human-readable endpoint hint (host:port or process path).
    /// </summary>
    private string _endpointDescription;

    public string EndpointDescription
    {
        get => _endpointDescription;
        private set => SetProperty(ref _endpointDescription, value);
    }

    /// <summary>
    /// The transport-specific FontIcon glyph code extracted from the server configuration.
    /// </summary>
    public string TransportGlyph => _profile.TransportGlyph;

    /// <summary>
    /// Short status string suitable for a badge or subtitle.
    /// </summary>
    public string StatusLabel => IsConnecting
        ? _localizer["AgentProfile_StatusConnecting"]
        : IsConnected
            ? _localizer["AgentProfile_StatusConnected"]
            : _localizer["AgentProfile_StatusDisconnected"];

    // ── Underlying config (needed to invoke connect) ─────────────────────────

    private ServerConfiguration _profile;

    // ── Constructor ──────────────────────────────────────────────────────────

    public AgentProfileItemViewModel(
        ServerConfiguration profile,
        IAcpConnectionSessionRegistry registry,
        IAcpConnectionSessionEvents events,
        ISettingsAcpConnectionCommands commands,
        ILogger<AgentProfileItemViewModel> logger,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(logger);

        _profile = profile;
        _registry = registry;
        _events = events;
        _commands = commands;
        _logger = logger;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

        ProfileId = profile.Id;
        _name = profile.Name;
        _endpointDescription = BuildEndpointDescription(profile);

        // Seed state from the current registry snapshot so the card looks right
        // immediately without waiting for the next event.
        RefreshConnectionStateFromRegistry();

        // Subscribe for future changes.
        _events.ProfileConnectionChanged += OnProfileConnectionChanged;
    }

    internal void UpdateProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(profile.Id, ProfileId, StringComparison.Ordinal))
        {
            return;
        }

        _profile = profile;
        Name = profile.Name;
        EndpointDescription = BuildEndpointDescription(profile);
        OnPropertyChanged(nameof(TransportGlyph));
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanToggleConnection))]
    private async Task ToggleConnectionAsync(CancellationToken cancellationToken)
    {
        if (IsConnecting)
        {
            return;
        }

        if (IsConnected)
        {
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool CanToggleConnection() => !IsConnecting;

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnecting = true;
        try
        {
            await _commands.ConnectProfileInPoolAsync(_profile).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Swallow; UI state will be corrected by the next ProfileConnectionChanged event.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to profile {ProfileId}", ProfileId);
        }
        finally
        {
            // IsConnecting is cleared reactively when the registry event fires.
            // Guard to avoid leaving spinner indefinitely if an error occurred.
            if (!IsConnected)
            {
                PostToUi(() => IsConnecting = false);
            }
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnecting = true;
        try
        {
            await _commands.DisconnectProfileInPoolAsync(ProfileId).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Swallow.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect profile {ProfileId}", ProfileId);
        }
        finally
        {
            if (!IsConnected)
            {
                PostToUi(() => IsConnecting = false);
            }
        }
    }

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnProfileConnectionChanged(string profileId, bool isConnected)
    {
        if (!string.Equals(profileId, ProfileId, StringComparison.Ordinal))
        {
            return;
        }

        PostToUi(() =>
        {
            IsConnected = isConnected;
            // Clear the spinner once the registry confirms the new state.
            IsConnecting = false;
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshConnectionStateFromRegistry()
    {
        IsConnected = _registry.TryGetByProfile(ProfileId, out _);
    }

    private void PostToUi(Action action)
    {
        _uiDispatcher.Enqueue(action);
    }

    private static string BuildEndpointDescription(ServerConfiguration profile)
    {
        // ServerConfiguration.EndpointDisplay already handles Stdio (command + args) and HTTP (ServerUrl).
        var display = profile.EndpointDisplay;
        return string.IsNullOrWhiteSpace(display) ? profile.Name : display;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _events.ProfileConnectionChanged -= OnProfileConnectionChanged;
    }
}
