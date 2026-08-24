# 配置持久化方案 SPEC（YAML + 安全存储 + 版本化）

本文档钉死 Salmon Egg 的**配置持久化**最佳实践方案：使用 **YAML** 存储可读/可审计的普通配置，敏感字段进入 `ISecureStorage` 抽象（Windows DPAPI / macOS Keychain / Linux Secret Service / AndroidKeyStore / iOS Keychain / 受限平台 plaintext file 替代实现），并提供可演进的版本化与迁移策略。

> 目标：可读、可合并、可迁移、跨平台一致、不会因为新增字段/枚举值/格式差异导致客户端崩溃。

---

## 1. 范围与原则

### 1.1 范围
- 覆盖 `ServerConfiguration`（预设服务器、传输、超时、心跳、认证方式等）。
- 覆盖 UI 偏好设置（主题/动画开关/布局密度等）。
- 覆盖“连接与会话”相关的**默认项**（但不持久化临时 sessionId）。

### 1.2 原则
- **明文 YAML 只保存普通配置**（如 URL、超时、选项），密钥/Token/证书等不写入 YAML；受限平台的 `ISecureStorage` 可使用应用数据目录下的 plaintext file fallback。
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
  logs/
    ...
```

- `app.yaml`：全局设置（UI 偏好、默认选项、最近使用的 server id 等）。
- `servers/<id>.yaml`：每个 ServerConfiguration 一个文件，便于 diff/merge。

---

## 3. YAML 格式规范（钉死）

### 3.1 顶层字段
每个 YAML 文件必须包含：
- `schemaVersion`：整数，当前版本为 `2`。
- `updatedAtUtc`：UTC 时间（ISO 8601）。

### 3.2 ServerConfiguration 文件示例
`config/servers/agent-local.yaml`：
```yaml
schema_version: 2
updated_at_utc: "2026-03-08T13:20:00Z"

id: "agent-local"
name: "Local Agent"
transport: "websocket"   # websocket | stdio | streamable_http（全部使用 snake_case;旧值 http_sse 兼容读取,写出一律 canonical）
server_url: "ws://127.0.0.1:8080"

connection_timeout_seconds: 120

authentication:
  mode: "none"           # none | bearer_token | api_key
  # token/apiKey 不允许出现在 YAML 中（见 4.1）
```

SSH bridge 仍然必须使用同一个 `stdio` transport token，不允许新增 `ssh` transport：

```yaml
schema_version: 2
updated_at_utc: "2026-04-17T09:20:00Z"

id: "agent-ssh"
name: "Remote Agent via SSH"
transport: "stdio"
stdio_command: "ssh"
stdio_arguments:
  - "-T"
  - "-o"
  - "BatchMode=yes"
  - "-o"
  - "RequestTTY=no"
  - "-o"
  - "LogLevel=ERROR"
  - "user@host"
  - "/opt/acp/bin/agent"
  - "stdio"
connection_timeout_seconds: 120
authentication:
  mode: "none"
```

- `transport: "ssh"` 是禁止配置；SSH 只是 `stdio` 的启动器/桥接进程。
- 推荐使用 `ssh -T` 或 `-o RequestTTY=no`；禁止 `ssh -t`，否则 PTY 可能污染 ACP 帧流。

### 3.3 命名约束
- 键名统一 `snake_case` （如文档有冲突以此为准）。
- enum 值统一 **snake_case**（如 `in_progress`），保持与 ACP 事件一致。

---

## 4. 敏感信息策略（钉死）

### 4.1 持久化边界
- 敏感字段不得写入 `app.yaml` 或 `servers/<id>.yaml`。
- 敏感字段通过 `ISecureStorage` 保存；平台实现可为 OS-backed secure store，也可为受限平台的 plaintext file fallback。
- 配置云同步启用且 `IncludeSecrets=true` 时，同步包中的 `secrets.json` 会明文包含已登记的配置相关凭据。
- `cloud_config_sync.provider_id` 是唯一活动 provider；OneDrive、WebDAV 与 S3-compatible object storage 不能同时启用。WebDAV 的远端文件夹 URL 和用户名可写入 `cloud_config_sync.provider_options.webdav`，同步包默认文件名为 `salmonegg-config.zip`，保存时会在服务端允许的情况下创建缺失 collection，密码只写入 `ISecureStorage`；S3 的 endpoint/bucket/region/object key/path-style 选项可写入 `cloud_config_sync.provider_options.s3`，access key ID 和 secret access key 只写入 `ISecureStorage`。
- OneDrive 应用注册的 `client_id` / tenant / redirect URI / scopes 不是用户配置，必须由 GitHub Actions 在构建阶段从 repository `secrets` 或 `variables` 注入到程序集元数据；客户端运行时不得读取环境变量或把这些值写入 `config/`。

### 4.2 SecureStorage Key 规则
统一 key 前缀，便于清理与迁移：
- `salmonegg/config/<serverId>/token`
- `salmonegg/config/<serverId>/apiKey`

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

### 6.2 附加式升级与写入防护（无迁移机制）

版本演进采用**附加式（additive-only）契约**：每次升 `schema_version` 只新增字段或枚举值，
不重命名、不删除、不改语义。旧版本写入的文件天然是当前版本的合法子集，因此**不需要迁移
机制**——`IConfigMigration` 曾在早期设计中规划但从未实现，现已放弃；仓库中不保留迁移目录
或迁移日志。

真正的保护在**写入路径**而非读取路径：

- **读取**宽松：`schema_version <= 0` 视为"不是我们的文件"予以忽略（返回默认值/空），
  `schema_version > 当前版本` 的文件**仍可读**（附加式契约保证未知字段被忽略、枚举未知值
  回退默认），`ConfigurationDiagnosticsService` 会对其报告 `SchemaTooNew` 诊断提示用户升级。
- **写入**严格：发现磁盘上文件的 `schema_version > 当前版本` 时直接拒绝写回
  （`SchemaVersionTooNew`），防止旧版本构建覆盖新版本文件而丢字段。这是云同步场景的关键
  防线——两台版本不同的机器可能拉到对方写的更高版本文件，写入防护确保旧版本不会静默降级它。

不保留迁移机制的理由：无历史遗留的破坏性 schema 变更，且旧版本 APP 几乎无存量用户，
"装新版 → 回滚旧版"场景不构成需要支持的前向兼容路径。若未来确需破坏性变更，再以此节
为锚点重新引入显式迁移。

---
