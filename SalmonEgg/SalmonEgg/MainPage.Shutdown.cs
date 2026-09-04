using System.Threading.Tasks;
using Microsoft.UI.Windowing;

namespace SalmonEgg;

/// <summary>
/// 窗口关闭路径：拦住原生关闭 → 跑完 teardown → 再真正关闭。
/// </summary>
/// <remarks>
/// 这段逻辑是<b>跨平台共享</b>的，不是 Windows 专属。反编译 Uno 6.6.166 实际发货的
/// <c>uno-runtime/&lt;tfm&gt;/skia/Uno.UI.dll</c> 确认：X11 / Win32 / macOS 三个 Skia host 都会
/// raise <c>AppWindow.Closing</c>，且各自的 <c>*NativeWindowFactoryExtension</c>
/// <c>SupportsClosingCancellation</c> 均为 <c>true</c>——汇聚点 <c>BaseWindowImplementation</c>
/// 只在 <c>Cancel &amp;&amp; SupportsClosingCancellation</c> 时才真的中止关闭。
/// （注意：<c>lib/&lt;tfm&gt;/Uno.UI.dll</c> 是 facade，那里该属性硬编码 false，据它会得出
/// "平台不支持取消"的相反结论。）
/// 该反编译结论取自 6.6.166；升级到 6.7.103 后只确认了符号仍在，未重新反编译逐个 host 的取值。
///
/// 为什么必须在这里 teardown、而不能只依赖宿主返回后再做：desktop 头原先只在
/// <c>Platforms/Desktop/Program.cs</c> 里等 <c>host.RunAsync()</c> 返回后才清理，
/// 那时窗口早已销毁——实测窗口 1.1s 就消失而进程活到 10.3s，用户看到的是"关掉了但还在跑"。
/// 先取消这一轮关闭、清理完再关，窗口在整个 teardown 期间保持可见，
/// 关闭提示 overlay 才有地方显示（<c>ShellShutdownOverlayViewModel</c>）。
/// </remarks>
public sealed partial class MainPage
{
    /// <summary>
    /// 重入闩锁：teardown 完成后我们自己调用 <c>Close()</c>，那一轮必须放行。
    /// </summary>
    private bool _allowClose;

    private void AttachAppWindowClosing()
    {
        var window = App.MainWindowInstance;
        if (window?.AppWindow is not { } appWindow)
        {
            return;
        }

        // 先减后加：shell 可被重载（如切换语言会替换根 Frame），重复订阅会让一次关闭
        // 触发多轮 teardown。workflow 幂等，但重复订阅仍是状态泄漏。
        appWindow.Closing -= OnAppWindowClosing;
        appWindow.Closing += OnAppWindowClosing;
    }

    private void DetachAppWindowClosing()
    {
        if (App.MainWindowInstance?.AppWindow is { } appWindow)
        {
            appWindow.Closing -= OnAppWindowClosing;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        // 托盘由能力位驱动而非 #if：IsMinimizeToTraySupported 已是跨平台事实源
        // （PlatformCapabilityService.SupportsTray），不支持托盘的平台恒为 false，
        // 于是这里天然退化为"直接关闭"。但"把窗口藏起来"这个动作本身是 Windows 托盘
        // 专属能力（Uno 对 AppWindow.Hide 未实现），必须留在平台 partial 里，
        // 否则共享代码在 desktop 目标上会撞 Uno0001。
        if (Preferences.IsMinimizeToTraySupported && Preferences.MinimizeToTray)
        {
            args.Cancel = true;
            HideMainWindowToTray();
            return;
        }

        // 这是进程边界。teardown 是异步的，而 Closing 事件无法被 await，
        // 且窗口一旦消失就再也无法持久化任何东西——所以取消这一轮，清理完再真关。
        args.Cancel = true;
        _ = FlushRuntimeThenCloseAsync();
    }

    private async Task FlushRuntimeThenCloseAsync()
    {
        try
        {
            await App.ShutdownRuntimeAsync().ConfigureAwait(true);
        }
        finally
        {
            // finally：teardown 失败也必须让窗口关掉，否则用户会卡在一个关不掉的窗口里。
            _allowClose = true;
            App.MainWindowInstance?.Close();
        }
    }
}
