using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class LiveLogViewerViewModel : ObservableObject
{
    private const int DefaultMaxVisibleCharacters = 32768;

    private readonly ILiveLogStreamService _service;
    private readonly ILogger<LiveLogViewerViewModel> _logger;
    private readonly string _logsDirectoryPath;
    private readonly int _maxVisibleCharacters;
    private SynchronizationContext? _synchronizationContext;
    private CancellationTokenSource? _streamingCancellationTokenSource;
    private Task? _streamingTask;
    private bool _suppressExpansionSideEffects;

    public LiveLogViewerViewModel(
        ILiveLogStreamService service,
        string logsDirectoryPath,
        ILogger<LiveLogViewerViewModel> logger,
        int maxVisibleCharacters = DefaultMaxVisibleCharacters)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logsDirectoryPath = logsDirectoryPath ?? throw new ArgumentNullException(nameof(logsDirectoryPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxVisibleCharacters = maxVisibleCharacters > 0
            ? maxVisibleCharacters
            : throw new ArgumentOutOfRangeException(nameof(maxVisibleCharacters));
        _synchronizationContext = SynchronizationContext.Current;
        _visibleLogText = string.Empty;
        _statusText = "未启动";
        _isAutoFollowEnabled = true;
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
        _synchronizationContext = SynchronizationContext.Current ?? _synchronizationContext;
        EnsureExpandedState(true);
        if (IsStreaming)
        {
            return;
        }

        IsPaused = false;
        IsStreaming = true;
        StatusText = "正在实时查看";
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
        await StopStreamingCoreAsync("已停止").ConfigureAwait(false);
    }

    public async Task PauseStreamingAsync()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        await StopStreamingCoreAsync("已暂停").ConfigureAwait(false);
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

    private async Task StopStreamingCoreAsync(string stoppedStatusText)
    {
        var cancellationTokenSource = _streamingCancellationTokenSource;
        var streamingTask = _streamingTask;

        if (cancellationTokenSource is null)
        {
            IsStreaming = false;
            StatusText = stoppedStatusText;
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
            StatusText = stoppedStatusText;
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
                StatusText = "已停止";
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
                StatusText = "读取失败，请稍后重试";
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
            StatusText = string.IsNullOrWhiteSpace(update.CurrentLogFilePath)
                ? "未找到可用日志文件"
                : "已切换到最新日志文件";
        }

        if (string.IsNullOrEmpty(update.AppendedText))
        {
            return;
        }

        AppendVisibleText(update.AppendedText);

        if (!IsPaused)
        {
            StatusText = "正在实时查看";
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

    private Task RunOnCapturedContextAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(
            static state =>
            {
                var (callback, taskCompletionSource) = ((Action Callback, TaskCompletionSource<object?> TaskCompletionSource))state!;

                try
                {
                    callback();
                    taskCompletionSource.SetResult(null);
                }
                catch (Exception ex)
                {
                    taskCompletionSource.SetException(ex);
                }
            },
            (action, completion));

        return completion.Task;
    }
}
