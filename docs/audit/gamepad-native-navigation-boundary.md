# 手柄原生导航边界审计

## 结论

手柄输入事实源与诊断能力保留；应用 shell 不再把物理 `Gamepad` / `RawGameController` 读数合成为全局焦点移动或控件激活。

`NavigationView`、按钮、列表等标准控件的焦点、DPad、层级导航和激活语义继续由 WinUI/Uno 原生控件拥有。应用层只处理两类输入：

- 已聚焦控件祖先链中明确 opt-in 的 `INavigationIntentConsumer` 语义消费。
- 应用级返回意图 `GamepadNavigationIntent.Back`。

## 根因

GUI 复现显示，同一 `NavigationView` 层级路径中：

- 键盘 `Down` 可以从 project item 进入 session child。
- 虚拟 gamepad DPad / 注入 gamepad intent 曾跳到 footer item。

这说明左侧导航的数据投影和 `NavigationView` 层级键盘语义本身可用；问题出在 shell 层把手柄读数翻译成全局 `FocusManager.TryMoveFocus` 或 AutomationPeer 激活，绕开了 `NavigationView` 自己的树形导航行为。

## 当前边界

- `WindowsGamepadInputService` 继续读取 `Gamepad` 与 `RawGameController`，Raw fallback 不被标准 gamepad 连接状态遮蔽。
- `WindowsGamepadDiagnosticsService` 暴露标准 gamepad 数量、Raw controller 数量、active input source、active intents、thumbstick、VID/PID、button labels、switch 与 axis values。
- `MainShellGamepadNavigationDispatcher` 不再调用 `FocusManager.TryMoveFocus`、AutomationPeer invoke/toggle/expand、`SelectedItem` 回写、AutomationId/Tag 匹配或具体控件导航。
- `MainPage` 不实现 `INavigationIntentConsumer`，也不拦截 `NavigationView` activation。
- GUI 自动化不再提供合成 `gamepad-intent` 输入服务；`SALMONEGG_GUI_CONTROL_FILE` 仅保留给现有后台消息 smoke 使用，不能替代物理手柄兼容性验证。

## 验证

代码提交：

- `9f5397bd fix: stop synthesizing gamepad focus navigation`
- `7b1485db docs: document native gamepad navigation boundary`
- `320d24ec refactor: remove synthetic gui gamepad input service`

已运行：

- `dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -m:1 -nr:false -v:minimal`
- `$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ShellFocusedActivationSmokeTests" -m:1 -nr:false -v:minimal`
- `$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName=SalmonEgg.GuiTests.Windows.DiagnosticsSettingsSmokeTests.GamepadDiagnosticsMonitor_CanRefreshAndStartFromDiagnosticsSettings" -m:1 -nr:false -v:minimal`
- `dotnet build SalmonEgg\SalmonEgg\SalmonEgg.csproj -f net10.0-desktop -p:SalmonEggTargetFrameworks=net10.0-desktop -p:SalmonEggAllTargetFrameworks=net10.0-desktop -m:1 -nr:false -v:minimal`
- `.tools\run-winui3-msix.ps1 -Configuration Debug`

最新安装证据：

- commit: `320d24ec5b89e49285d88cda95b5fc375836cd10`
- MSIX SHA256: `3B48DCC8839E2E87678CF22E24D118A731C91FB7D7462EA38D426CD0F7831120`
- installed executable SHA256: `C8885B67BCAD0D7BFA71A73E913E75491D183D73898267F0F1556845A699F94C`

## 真机收口 Checklist

在已安装的本次 MSIX 中，用物理手柄测试前先打开设置页的“诊断与日志 -> 手柄输入”：

1. 点击“刷新一次”或“开始监测”。
2. 按 DPad、左摇杆、确认键、返回键。
3. 记录以下字段：
   - `Gamepad 数量`
   - `Raw 控制器数量`
   - `输入来源`
   - `当前输入`
   - `左摇杆`
   - `Raw 明细`
4. 如果 `Gamepad 数量` 或 `Raw 控制器数量` 为 0，问题在 Windows/设备识别层，不应修改 shell 焦点逻辑。
5. 如果诊断页能看到输入，但 `NavigationView` 不能移动焦点，先确认 Windows 是否向 WinUI 控件投递原生 DPad/键盘焦点事件；不要重新引入 shell 级全局焦点合成。
6. 如果只有 Chat 输入框、Transcript 等 opt-in 区域需要特殊语义，再在对应控件实现 `INavigationIntentConsumer`，不要在 shell 或 `NavigationView` 外部补偿。

## 禁止回归

- 禁止按设备名、VID/PID 或经验按钮 index 猜测 PlayStation / Xbox / Generic 映射。
- 禁止在 `MainPage`、shell dispatcher 或导航 adapter 中按 `AutomationId`、Tag、DataContext 类型手动跳转下一个 `NavigationViewItem`。
- 禁止用 `SelectedItem = ...`、AutomationPeer pattern、延迟回写或清空/重选来补偿手柄导航。
- 禁止把 GUI 注入 gamepad intent 的通过结果当成真实物理手柄兼容性证明。
