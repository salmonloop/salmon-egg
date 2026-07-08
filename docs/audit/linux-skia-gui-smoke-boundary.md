# Linux Skia GUI Smoke Boundary

## 结论

Linux Skia Desktop GUI smoke 当前覆盖真实 `net10.0-desktop` 产物在 X11 GUI host 中的启动、窗口映射、非空像素、host-window focus 和 XTest 键盘输入边界。它不覆盖 AT-SPI 语义树、AutomationId 查询或控件级行为自动化。

## AT-SPI 探测结果

本机审查使用 `dbus-run-session`、Xvfb 和 `org.a11y.Bus` 启动真实 Debug Skia Desktop 产物，并提前激活 AT-SPI bus。即使设置 `NO_AT_BRIDGE=0` 和 `GTK_MODULES=atk-bridge`，SalmonEgg 进程只出现在 session bus，没有注册到 AT-SPI bus。

进程 native maps 显示运行时加载的是 Uno Skia X11 host 和 `libX11.so.6`，未加载 GTK/ATK/AT-SPI bridge。仓库入口 [`Program.cs`](../../SalmonEgg/SalmonEgg/Platforms/Desktop/Program.cs) 使用 `.UseX11()` 和 `.UseLinuxFrameBuffer()`，当前没有可切换的 Linux GTK/AT-SPI host。

## 规则

1. Linux Skia GUI smoke 只声明 host-window 层覆盖，不声明 AT-SPI、AutomationId 或控件语义树覆盖。
2. 不允许用 X11 window 属性、截图文本识别或应用内 test hook 冒充 Linux 语义 GUI 自动化。
3. 若后续 Uno/Skia host 暴露稳定 AT-SPI provider，应新增独立 Linux semantic GUI gate，并继续与 Windows FlaUI/UIA3、WASM Playwright gate 分离。
4. 跨平台行为一致性仍由共享 ViewModel/Core 行为测试和平台专属 GUI gate 共同保证。
