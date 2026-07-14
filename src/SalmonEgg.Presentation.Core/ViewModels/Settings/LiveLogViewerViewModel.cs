using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class LiveLogViewerViewModel : ObservableObject, IDisposable
{
    private enum LiveLogStatusKind
    {
        NotStarted,
        Streaming,
        Stopped,
        Paused,
        ReadFailed,
        NoLogFile,
        SwitchedToLatest
    }

    private const int DefaultMaxVisibleCharacters = 32768;

    private readonly ILiveLogStreamService _service;
    private readonly ILogger<LiveLogViewerViewModel> _logger;
    private readonly string _logsDirectoryPath;
    private readonly int _maxVisibleCharacters;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings> _localizer;
    private readonly IAppLanguageService? _languageService;
    private CancellationTokenSource? _streamingCancellationTokenSource;
    private Task? _streamingTask;
    private bool _suppressExpansionSideEffects;
    private LiveLogStatusKind _statusKind;
    private bool _disposed;

    public LiveLogViewerViewModel(
        ILiveLogStreamService service,
        string logsDirectoryPath,
        ILogger<LiveLogViewerViewModel> logger,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings> localizer,
        int maxVisibleCharacters = DefaultMaxVisibleCharacters,
        IAppLanguageService? languageService = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logsDirectoryPath = logsDirectoryPath ?? throw new ArgumentNullException(nameof(logsDirectoryPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _languageService = languageService;
        _maxVisibleCharacters = maxVisibleCharacters > 0
            ? maxVisibleCharacters
            : throw new ArgumentOutOfRangeException(nameof(maxVisibleCharacters));
        _visibleLogText = string.Empty;
        _statusKind = LiveLogStatusKind.NotStarted;
        _statusText = ResolveStatusText();
        _isAutoFollowEnabled = true;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isAutoFollowEnabled;

    [ObservableProperty]
    private string? _currentLogFilePath;

    [ObservableProperty]
    private string _visibleLogText;

    [ObservableProperty]
    private string _statusText;

    public bool CanStartStreaming => !IsStreaming && !IsPaused;

    public bool CanPauseStreaming => IsStreaming;

    public bool CanResumeStreaming => IsPaused;

    public async Task StartStreamingAsync()
    {
        EnsureExpandedState(true);
        if (IsStreaming)
        {
            return;
        }

        IsPaused = false;
        IsStreaming = true;
        SetStatus(LiveLogStatusKind.Streaming);
        NotifyStreamingStateChanged();

        var cancellationTokenSource = new CancellationTokenSource();
        _streamingCancellationTokenSource = cancellationTokenSource;
        var serviceTask = _service.StartAsync(_logsDirectoryPath, HandleUpdateAsync, cancellationTokenSource.Token);
        _streamingTask = ObserveStreamingAsync(serviceTask, cancellationTokenSource);
        await Task.CompletedTask;
    }

    public async Task StopStreamingAsync()
    {
        IsPaused = false;
        await StopStreamingCoreAsync(LiveLogStatusKind.Stopped).ConfigureAwait(false);
    }

    public async Task PauseStreamingAsync()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        await StopStreamingCoreAsync(LiveLogStatusKind.Paused).ConfigureAwait(false);
    }

    public async Task ResumeStreamingAsync()
    {
        if (!IsPaused)
        {
            await StartStreamingAsync().ConfigureAwait(false);
            return;
        }

        IsPaused = false;
        await StartStreamingAsync().ConfigureAwait(false);
    }

    public async Task HandlePageUnloadedAsync()
    {
        await StopStreamingAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenViewerAsync()
    {
        await StartStreamingAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task CollapseAsync()
    {
        EnsureExpandedState(false);
        await StopStreamingAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (IsPaused)
        {
            await ResumeStreamingAsync().ConfigureAwait(false);
            return;
        }

        await PauseStreamingAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ClearVisibleLog()
    {
        VisibleLogText = string.Empty;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (_suppressExpansionSideEffects)
        {
            return;
        }

        if (!value)
        {
            _ = StopStreamingAsync();
        }
    }

    private async Task StopStreamingCoreAsync(LiveLogStatusKind stoppedStatus)
    {
        var cancellationTokenSource = _streamingCancellationTokenSource;
        var streamingTask = _streamingTask;

        if (cancellationTokenSource is null)
        {
            IsStreaming = false;
            SetStatus(stoppedStatus);
            NotifyStreamingStateChanged();
            return;
        }

        _streamingCancellationTokenSource = null;
        _streamingTask = null;

        cancellationTokenSource.Cancel();

        try
        {
            if (streamingTask is not null)
            {
                await streamingTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }

        await RunOnCapturedContextAsync(() =>
        {
            IsStreaming = false;
            SetStatus(stoppedStatus);
            NotifyStreamingStateChanged();
        }).ConfigureAwait(false);
    }

    private async Task ObserveStreamingAsync(Task serviceTask, CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await serviceTask.ConfigureAwait(false);
            await RunOnCapturedContextAsync(() =>
            {
                if (!ReferenceEquals(_streamingCancellationTokenSource, cancellationTokenSource))
                {
                    return;
                }

                IsStreaming = false;
                SetStatus(LiveLogStatusKind.Stopped);
                NotifyStreamingStateChanged();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live log streaming failed");
            await RunOnCapturedContextAsync(() =>
            {
                if (!ReferenceEquals(_streamingCancellationTokenSource, cancellationTokenSource))
                {
                    return;
                }

                IsStreaming = false;
                SetStatus(LiveLogStatusKind.ReadFailed);
                NotifyStreamingStateChanged();
            }).ConfigureAwait(false);
        }
    }

    private Task HandleUpdateAsync(LiveLogStreamUpdate update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        return RunOnCapturedContextAsync(() => ApplyUpdate(update));
    }

    private void ApplyUpdate(LiveLogStreamUpdate update)
    {
        CurrentLogFilePath = update.CurrentLogFilePath;

        if (update.HasFileSwitched)
        {
            SetStatus(string.IsNullOrWhiteSpace(update.CurrentLogFilePath)
                ? LiveLogStatusKind.NoLogFile
                : LiveLogStatusKind.SwitchedToLatest);
        }

        if (string.IsNullOrEmpty(update.AppendedText))
        {
            return;
        }

        AppendVisibleText(update.AppendedText);

        if (!IsPaused)
        {
            SetStatus(LiveLogStatusKind.Streaming);
        }
    }

    private void AppendVisibleText(string appendedText)
    {
        var combined = string.Concat(VisibleLogText, appendedText);
        if (combined.Length > _maxVisibleCharacters)
        {
            combined = combined.Substring(combined.Length - _maxVisibleCharacters, _maxVisibleCharacters);
        }

        VisibleLogText = combined;
    }

    private void EnsureExpandedState(bool value)
    {
        if (IsExpanded == value)
        {
            return;
        }

        _suppressExpansionSideEffects = true;
        try
        {
            IsExpanded = value;
        }
        finally
        {
            _suppressExpansionSideEffects = false;
        }
    }

    private void NotifyStreamingStateChanged()
    {
        OnPropertyChanged(nameof(CanStartStreaming));
        OnPropertyChanged(nameof(CanPauseStreaming));
        OnPropertyChanged(nameof(CanResumeStreaming));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => _ = RunOnCapturedContextAsync(() => StatusText = ResolveStatusText());

    private void SetStatus(LiveLogStatusKind statusKind)
    {
        _statusKind = statusKind;
        StatusText = ResolveStatusText();
    }

    private string ResolveStatusText()
        => _statusKind switch
        {
            LiveLogStatusKind.Streaming => _localizer["LiveLog_StatusStreaming"],
            LiveLogStatusKind.Stopped => _localizer["LiveLog_StatusStopped"],
            LiveLogStatusKind.Paused => _localizer["LiveLog_StatusPaused"],
            LiveLogStatusKind.ReadFailed => _localizer["LiveLog_StatusReadFailed"],
            LiveLogStatusKind.NoLogFile => _localizer["LiveLog_StatusNoLogFile"],
            LiveLogStatusKind.SwitchedToLatest => _localizer["LiveLog_StatusSwitchedToLatest"],
            _ => _localizer["LiveLog_StatusNotStarted"]
        };

    private Task RunOnCapturedContextAsync(Action action)
    {
        return _uiDispatcher.EnqueueAsync(action);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }
}
