# 云配置同步「保存即回退」修复方案：内容寻址 3-way + LWW

## 1. 问题与根因（已确认）

### 症状
用户在设置中心改动并保存后，设置立刻被远端旧值覆盖回退。

### 触发闭环（全链路确认）
1. 保存设置 → `AppSettingsService.SaveAsync` → `FileSystemAppFileStore.WriteAllTextAsync` 发 `ConfigChangeSignal.NotifyChanged(Written)`（`FileSystemAppFileStore.cs:56`）。
2. `CloudConfigSyncCoordinator.OnConfigChanged` 响应，2s debounce 调度自动同步（`CloudConfigSyncCoordinator.cs:886-902`）。
3. debounce 后 `SyncNowCoreAsync` → `SynchronizeAsync`。
4. `SynchronizeAsync` 判定「远端赢」→ `RestorePackageAsync` 用旧远端覆盖本地 `app.yaml`，发 `Restored` 信号（`ConfigSyncPackageService.cs:153`）。
5. `ConfigProjectionReloadCoordinator` 收 `Restored` 重投影 VM（`ConfigProjectionReloadCoordinator.cs:53-86`）→ UI 回退。

### 根因：方向判定误用 ETag 相等性
`SynchronizeAsync`（`CloudConfigSyncCoordinator.cs:526-579`）只比较远端 ETag，没有本地变更事实源：
- `CloudConfigSyncState`（`CloudConfigSyncModels.cs:36-47`）只存 `RemoteETag`，不记录上次同步的内容基线，无法区分本地 dirty / clean。
- ETag 不一致一律判「远端领先」→ 无条件 restore。
- 最致命：`UploadLocalAsync` 执行 `state.RemoteETag = upload.ETag ?? string.Empty`（`cs:642`）。WebDAV PUT 按 RFC 4918 不强制返回 ETag，很多服务器不返回 → RemoteETag 变空 → 下一次同步进入「RemoteETag 为空 → RestoreRemote」分支，用旧远端覆盖刚上传的本地。**单设备也会稳定复现。**

## 2. 方案：内容寻址 3-way 方向判定，ETag 退位为并发保护

### 2.1 核心：三个内容指纹
每次同步计算三个规范化内容哈希（canonical hash）：
- `syncedFingerprint`：上次同步成功时的内容基线（存入 sync state）。
- `localFingerprint`：当前本地 config 的规范化哈希。
- `remoteFingerprint`：本次拉到的远端包的规范化哈希。

方向判定表：

| local vs synced | remote vs synced | 判定 |
|---|---|---|
| 相同 | 相同 | 无操作（no-op） |
| 不同 | 相同 | **上传**（当前被误判为回退的路径） |
| 相同 | 不同 | **restore** |
| 不同 | 不同，且 local==remote | 已收敛，仅刷新基线 |
| 不同 | 不同，且 local!=remote | **真冲突 → LWW** |

首次采用（`syncedFingerprint` 为空）：视为 baseline 未建立。若远端存在且与本地内容不同，按真冲突走 LWW（避免静默吞本地）。

### 2.2 冲突检测（严谨定义）
`local != synced && remote != synced && local != remote`。即两侧都相对上次同步基线变过且内容不一致。

### 2.3 冲突解决：fail-closed + 显式 first-adopt 策略（取代 LWW）
- **真冲突**（两侧相对基线都变且内容不同）：**永不静默覆盖**。`FailClosedConflict` → `CloudTransferPhase.Failed` + `CloudSyncFailureKind.RemoteConflict`，本地 config 与 `SyncedFingerprint` 保持不变；远端包 + 本地快照落入 `config-conflict-artifacts/`（路径写入 `CloudSyncFailure.ArtifactPath`）。
- **基线未知（首次采用）**：禁止时钟启发式。由调用方传入 `CloudSyncFirstAdoptPolicy`：
  - `SyncNow` / 自动同步 → `RequireManual`（与真冲突相同 fail-closed）。
  - `ApplyAndActivate` → `PreferRemote`（连接已有云配置时 restore）。
- **方向判定纯函数**：`CloudSyncContentDecisionMaker.Decide`（Domain，无 IO/时钟）；Coordinator 只取指纹并执行副作用。
- **废弃**：时间戳 LWW、`ConflictRemoteApplied` 成功路径（枚举值仅历史兼容保留）。

### 2.4 关键陷阱：规范化指纹必须剔除易变字段
直接 hash 会每次都 dirty，必须剔除：
- `app.yaml` 的 `UpdatedAtUtc`（`AppSettingsService.cs:99`）。
- `manifest.json` 的 `CreatedAtUtc`（`CloudConfigSyncModels.cs:13`）——本就不进内容指纹。
- `mcp.yaml` / `server-*.yaml` 的 `UpdatedAtUtc`（`McpSettingsYamlV1.cs:10`、`ServerConfigurationYaml.cs:10`、`ConfigurationManager.cs:208`）。

**规范化策略**：指纹只覆盖 `files/config/` 下条目，对每个文件做「反序列化→剔除易变元数据→重新规范序列化→拼接 (相对路径, 规范内容) 有序列表→SHA-256」。secrets 是否纳入指纹由 `IncludeSecrets` 决定（避免仅密钥变更时的误判需一致处理）。指纹计算集中到一个新的 `ConfigContentFingerprint` 服务，作为唯一 owner，禁止在 coordinator 里散落 hash 逻辑。

## 3. 改动范围

### 3.1 状态模型 `CloudConfigSyncState`（`CloudConfigSyncModels.cs`）
新增字段（保持 YAML 向后兼容，缺省即「基线未知」）：
- `SyncedFingerprint`（string）：上次同步成功的规范化内容哈希。
- 保留 `RemoteETag`，语义降级为「乐观并发令牌」，不再参与方向判定。
- `SchemaVersion` 保持 1（新增字段可选，老状态读入即 baseline 未知，安全）。

### 3.2 新增 `ConfigContentFingerprint`（Infrastructure/Storage）
- `Task<string> ComputeLocalAsync(...)` / `string ComputeFromPackage(...)`：规范化内容哈希。
- 不暴露时间戳 API；方向判定不依赖时钟。
- 纯计算，无文件系统副作用触发（构造/DI 不落盘，遵守缓存与持久化边界规则）。

### 3.3 Domain：`CloudSyncContentDecisionMaker`
- 输入：local/remote/synced 指纹 + `BaselineKnown` + `FirstAdoptPolicy`。
- 输出：`RefreshBaseline` / `UploadLocal` / `RestoreRemote` / `FailClosedConflict`。

### 3.4 重写 `SynchronizeAsync`
按 2.1 表 + 2.3 策略执行。成功 outcome：`Uploaded` / `Restored` / `None`；冲突 → Failed + `RemoteConflict`。

### 3.5 `UploadLocalAsync` / `RestoreRemoteAsync` / `FailClosedConflictAsync`
- 成功路径写入 `SyncedFingerprint`。
- 冲突路径调用 `ConfigSyncPackageService.PersistConflictArtifactsAsync`，不改 config、不改 baseline。
- `PreconditionFailed` 后重新 3-way（最多一次）；若变成真冲突则 fail-closed。

## 4. 单一状态链路与边界（遵守 AGENTS.md 硬约束）
- 方向判定与基线写入全部收敛在 `CloudConfigSyncCoordinator` + `ConfigContentFingerprint`，不新增第二套状态 owner。
- `Revision` 语义保持不变（云连接设置自身修订号），不参与内容方向判定，避免作用域错配。
- 时间戳一律 UTC + "O"，进入判定前无需时区换算。

## 5. 测试计划（Core，跨平台可运行）
扩展 `CloudConfigSyncCoordinatorTests`：
1. **保存后仅本地 dirty → 上传，不回退**（回归当前 bug 的核心用例）。
2. **上传后远端不返回 ETag，二次同步不回退**（根治第 4 类缺陷）。
3. **仅远端变化 → restore**。
4. **两侧收敛（内容相同）→ no-op，不重复上传**。
5. **真冲突 → fail-closed，本地与 baseline 不变，工件落盘**。
6. **基线未知 + SyncNow → fail-closed**；**基线未知 + ApplyAndActivate → PreferRemote restore**。
7. **易变字段单独变化不触发 dirty 误判**。
8. **老 sync state（无 SyncedFingerprint）→ 不静默吞本地**。
新增 `CloudSyncContentDecisionMakerTests`（纯函数全分支）与 `ConfigContentFingerprintTests`。

## 6. 验证
- `dotnet test`（Infrastructure.Tests、相关 Core 测试）全绿。
- 构建通过。
- 属于状态机 + 数据处理实质改动，非文档 only，必须跑测试门禁。

## 7. 交付说明要点
- ETag 从方向判定退位；指纹 + 纯函数决策是唯一方向事实源。
- 真冲突 / 首次采用（SyncNow）fail-closed；Activate 显式 PreferRemote。
- 无时钟 LWW；残余工作：统一 content projection、typed canonical fingerprint、上传后 ETag reconcile、冲突 UI 决策入口。
