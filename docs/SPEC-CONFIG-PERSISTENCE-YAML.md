# 配置持久化方案 SPEC（YAML + 安全存储 + 版本化）

更新时间：2026-03-08（Asia/Shanghai）

此次本项目正式命名为 Salmon Egg。

本文档钉死 UnoAcpClient 的**配置持久化**最佳实践方案：使用 **YAML** 存储可读/可审计的非敏感配置，敏感字段进入**平台安全存储**（Windows Credential Manager / macOS Keychain / Linux Secret Service / Web 的替代实现），并提供可演进的版本化与迁移策略。

> 目标：可读、可合并、可迁移、跨平台一致、不会因为新增字段/枚举值/格式差异导致客户端崩溃。

---

## 1. 范围与原则

### 1.1 范围
- 覆盖 `ServerConfiguration`（预设服务器、传输、超时、心跳、认证方式等）。
- 覆盖 UI 偏好设置（主题/动画开关/布局密度等）。
- 覆盖“连接与会话”相关的**默认项**（但不持久化临时 sessionId）。

### 1.2 原则
- **明文 YAML 只保存非敏感信息**（如 URL、超时、选项），密钥/Token/证书等永不落盘。
- 文件必须**向前兼容**：读取到未知字段必须忽略；枚举未知值必须回退到默认值并记录 warning。
- 写入必须**原子性**：写临时文件 → fsync/flush → rename 替换，避免断电/崩溃导致半文件。文件操作使用统一包装工具。
- 多配置必须**可扩展**：允许用户手动编辑/版本控制，但不要求必须手动编辑。

---

## 2. 存储位置（跨平台一致）

### 2.1 根目录（app data）
使用平台 AppData（与现有 `GetAppDataPath()` 一致）：
- Windows：`%LOCALAPPDATA%\\SalmonEgg\\`
- macOS：`~/Library/Application Support/SalmonEgg/`
- Linux：`~/.local/share/SalmonEgg/`（或对应 XDG）
- WASM：浏览器存储（逻辑等价的 key-value）

### 2.2 目录结构（钉死）
```
SalmonEgg/
  config/
    app.yaml
    servers/
      <id>.yaml
  config-migrations/
    migrations.log
  logs/
    ...
```

- `app.yaml`：全局设置（UI 偏好、默认选项、最近使用的 server id 等）。
- `servers/<id>.yaml`：每个 ServerConfiguration 一个文件，便于 diff/merge。
- `config-migrations/migrations.log`：记录迁移过程（便于诊断）。

---

## 3. YAML 格式规范（钉死）

### 3.1 顶层字段
每个 YAML 文件必须包含：
- `schemaVersion`：整数，默认从 `1` 开始。
- `updatedAtUtc`：UTC 时间（ISO 8601）。

### 3.2 ServerConfiguration 文件示例
`config/servers/agent-local.yaml`：
```yaml
schema_version: 1
updated_at_utc: "2026-03-08T13:20:00Z"

id: "agent-local"
name: "Local Agent"
transport: "websocket"   # websocket | stdio | http_sse（全部使用 snake_case）
server_url: "ws://127.0.0.1:8080"

heartbeat_interval_seconds: 30
connection_timeout_seconds: 10

authentication:
  mode: "none"           # none | bearer_token | api_key
  # token/apiKey 不允许出现在 YAML 中（见 4.1）
```

### 3.3 命名约束
- 键名统一 `snake_case` （如文档有冲突以此为准）。
- enum 值统一 **snake_case**（如 `in_progress`），保持与 ACP 事件一致。

---

## 4. 敏感信息策略（钉死）

### 4.1 绝不落盘字段
暂无

### 4.2 SecureStorage Key 规则
统一 key 前缀，便于清理与迁移：
- `unoacpclient/config/<serverId>/token`
- `unoacpclient/config/<serverId>/apiKey`

删除 server 配置时必须同时删除对应 secure keys。

---

## 5. 读写与合并策略（钉死）

### 5.1 读取优先级
1) 运行时（UI）修改但尚未保存的 in-memory 状态
2) `app.yaml` / `servers/<id>.yaml`
3) 默认值（代码内）

### 5.2 写入时机
- UI 明确保存动作（推荐）
- 或者节流自动保存（例如 1s debounce），但必须避免频繁 IO

### 5.3 原子写入
写入必须采用：
1) 写到 `<file>.tmp`
2) `Flush(true)`（或等价）
3) rename/replace 覆盖原文件

并在写入期间持有进程内互斥锁（避免并发写）。

---

## 6. 版本化与迁移（钉死）

### 6.1 schema_version
- 客户端必须支持读取 `schema_version <= 当前版本`
- 高版本：必须拒绝写回（防止降级破坏），但允许只读并提示用户升级

### 6.2 迁移机制
提供 `IConfigMigration`：
- `FromVersion` / `ToVersion`
- `Task MigrateAsync(...)`

启动时检测版本并按序迁移，记录到 `config-migrations/migrations.log`。

---

## 7. 建议的代码结构（不要求一次性落地，但方向钉死）

### 7.1 抽象层
- `IConfigurationStore`（Infrastructure）
  - `Task SaveServerAsync(ServerConfiguration config)`
  - `Task<ServerConfiguration?> LoadServerAsync(string id)`
  - `Task<IReadOnlyList<ServerConfiguration>> ListServersAsync()`
  - `Task DeleteServerAsync(string id)`

### 7.2 实现
- `YamlConfigurationStore`：YAML 文件读写（依赖 `YamlDotNet`）
- `SecureConfigurationSecretStore`：封装 `ISecureStorage` 的 secret 读写
- `ConfigurationService`：组合 store + secret store，对外暴露当前的 `IConfigurationService`

### 7.3 测试（必须）
- 解析兼容：未知字段/未知 enum 不崩溃
- 原子写：写入中断不会生成半文件（可用临时文件模拟）
- round-trip：保存→加载一致（除 secret 字段）

---

## 8. 验收标准（Definition of Done）

- 用户可在磁盘上看到可读 YAML 配置，并能手动编辑后被程序读取。
- 新增字段不破坏旧版本读取（至少忽略并继续运行）。
- Windows / macOS / Linux / Skia Desktop 行为一致。
