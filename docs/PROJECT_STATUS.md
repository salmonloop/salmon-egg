# 项目状态

> 本文件仅作为里程碑记录参考。当前工作状态请查看 `git log` 和分支历史。

## 已完成的主要里程碑

### 基础架构
- 四层架构（Domain / Application / Infrastructure / Presentation）建立完成
- Uno Platform 单项目多 TFM 结构（MSIX / WASM / Desktop）
- 跨平台 ViewModel 层（`SalmonEgg.Presentation.Core`）独立为共享库
- 桌面专用基础设施（`SalmonEgg.Infrastructure.Desktop`）分层完成
- DI 容器按平台条件区分桌面 / WASM 服务注册

### ACP 协议
- WebSocket / HTTP SSE / Stdio 三种传输全部实现
- ACP 能力协商（`clientCapabilities`）按平台门控
- 会话生命周期状态机（`Selecting → Selected → RemoteConnectionReady → Hydrated | Faulted`）
- Warm Reuse 判定逻辑与全条件矩阵测试

### 配置持久化
- YAML 格式配置持久化（`docs/SPEC-CONFIG-PERSISTENCE-YAML.md`）
- 原子写入（temp → flush → rename）
- 安全存储（Windows Credential Manager / volatile WASM）
- ACP profile YAML 在 WASM 通过 Uno IDBFS 持久化

### WASM
- Uno IDBFS 文件系统持久化（`/local/SalmonEgg`）
- WASM smoke gate（`scripts/gates/run-wasm-smoke-gates.sh`）
- Vercel 部署配置（`vercel.json`），输出目录 `publish/vercel-wasm/wwwroot`
- 静态资源验证 gate（`scripts/gates/verify-wasm-static-assets.sh`）

### 导航与会话
- 导航 SSOT：`INavigationCoordinator → IConversationSessionSwitcher`
- 全局搜索状态机（`Idle / Loading / Results / Empty / Error`，latest-wins）
- 项目/远端目录 ID 构造与解析统一到 `ProjectSelectionCwdResolver`
- `INavigationProjectPreferences.TryGetProjectCwd` 统一 CWD 解析

### 测试
- `SalmonEgg.Presentation.Core.Tests`：1800+ 测试
- `SalmonEgg.Infrastructure.Tests`：300+ 测试
- `SalmonEgg.GuiTests.Windows`：FlaUI GUI smoke（Windows）
- WASM 全链路 smoke gate（构建 → 启动 → ACP 会话 → 发送消息）

### 手柄输入
- `SalmonEgg.GamepadBridge.Windows`：标准 `Gamepad` + `RawGameController` 双通道
- Axis 归一化与八向 switch 映射按官方 enum 离散值
- 合同测试禁止重新引入 Raw fallback 跳过和 flags 解析

## 当前状态（2026-06-25）

- 分支：`develop`
- .NET SDK：10.0.202
- Uno SDK：6.5+
- 所有测试通过，WASM smoke gate 通过

详细变更历史见 `git log`。
