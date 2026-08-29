using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.ViewModels.Navigation;

/// <summary>
/// 把 <see cref="IApplicationShutdownProgress"/> 的事实投影成"正在关闭"遮罩的可见性与文案。
/// </summary>
/// <remarks>
/// 与 <see cref="ShellSessionActivationOverlayViewModel"/> 同型：订阅事实源 → 映射 →
/// <see cref="ObservableObject.OnPropertyChanged(string?)"/>，跨线程更新经 <see cref="IUiDispatcher"/>
/// 封送回 UI 线程（teardown 在线程池上跑，事件不带 UI 线程上下文）。
///
/// 为什么有阈值：正常关闭几百毫秒就结束，立刻弹遮罩是"闪一下"的廉价感；
/// 只有清理真的超过 <see cref="RevealThresholdMilliseconds"/> 才浮出提示（issue #126 的交互决策）。
/// 阈值属于呈现策略，所以放在这里而不是 <see cref="IApplicationShutdownProgress"/>。
/// </remarks>
public sealed class ShellShutdownOverlayViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 清理进行多久之后才显示遮罩（毫秒）。低于该时长的关闭对用户完全无感。
    /// </summary>
    public const int RevealThresholdMilliseconds = 400;

    private readonly IApplicationShutdownProgress _progress;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private readonly object _revealGate = new();
    private CancellationTokenSource? _revealCts;
    private bool _isRevealed;

    public ShellShutdownOverlayViewModel(
        IApplicationShutdownProgress progress,
        IUiDispatcher uiDispatcher,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _localizer = localizer;

        _progress.PropertyChanged += OnProgressPropertyChanged;
    }

    /// <summary>遮罩当前是否可见。只有清理超过阈值后才会变 true。</summary>
    public bool IsOverlayVisible => _isRevealed;

    /// <summary>按阶段区分的提示文案；阶段没细分信息时退化为通用文案。</summary>
    public string StatusText => _progress.Phase switch
    {
        ApplicationShutdownPhase.PersistingState => Localize(
            "ShutdownOverlay_PersistingState",
            "Saving conversations..."),
        ApplicationShutdownPhase.ClosingChildProcesses => Localize(
            "ShutdownOverlay_ClosingChildProcesses",
            "Closing agent processes..."),
        _ => Localize(
            "ShutdownOverlay_Generic",
            "Shutting down...")
    };

    public void Dispose()
    {
        _progress.PropertyChanged -= OnProgressPropertyChanged;
        CancelRevealTimer();
    }

    private void OnProgressPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IApplicationShutdownProgress.IsShuttingDown)
            or nameof(IApplicationShutdownProgress.Phase)))
        {
            return;
        }

        RunOnUi(() =>
        {
            // Phase 变化只影响文案；可见性由"第一次进入关闭"这一跳变驱动。
            OnPropertyChanged(nameof(StatusText));
            if (_progress.Phase == ApplicationShutdownPhase.Completed)
            {
                // 清理已收尾：取消还挂在阈值里的显示计时，否则遮罩会在退出前一刻闪出。
                CancelRevealTimer();
                return;
            }

            if (_progress.IsShuttingDown)
            {
                StartRevealTimer();
            }
        });
    }

    private void StartRevealTimer()
    {
        lock (_revealGate)
        {
            if (_revealCts is not null)
            {
                return;
            }

            _revealCts = new CancellationTokenSource();
            var token = _revealCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(RevealThresholdMilliseconds, token).ConfigureAwait(false);
                    RunOnUi(() =>
                    {
                        _isRevealed = true;
                        OnPropertyChanged(nameof(IsOverlayVisible));
                        OnPropertyChanged(nameof(StatusText));
                    });
                }
                catch (OperationCanceledException)
                {
                    // 清理在阈值内完成：遮罩永不出场，这正是设计目标。
                }
            }, CancellationToken.None);
        }
    }

    private void CancelRevealTimer()
    {
        lock (_revealGate)
        {
            _revealCts?.Cancel();
            _revealCts = null;
        }
    }

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _uiDispatcher.Enqueue(action);
    }
}
