# 激活流程改造方案（修正版）：前台取消 + 后台不取消

## 1. 产品语义

用户依次点击会话 A → B → C，三个都是真实意图：
- 屏幕焦点立即跟随最后一次点击（C），不闪烁
- A、B 的后台工作（远端连接、加载历史、缓存数据）继续完成
- A、B 完成后数据落入 workspace snapshot 缓存，但不能改屏幕状态

## 2. 核心设计决策

### 前台阶段：保留取消（保证不闪烁）
- 新激活仍然 cancel 旧激活的 context token
- gate 内的旧激活在 `ThrowIfCancellationRequested` 处快速退出
- 用户看不到 A 闪一下再切到 B

### 后台阶段：不取消（保证数据加载完成）
- 后台阶段使用独立 token（绑定 VM dispose，不被新激活 cancel）
- 远端连接、loadSession、回放历史全部跑完
- 完成后通过版本号判断：
  - 仍是最新 → commit 屏幕状态
  - 已过期 → 数据写入 snapshot 缓存，不动屏幕

## 3. 改动范围

### 3.1 ConversationActivationOrchestrator.BeginActivation
- **保留** `previousCts?.Cancel()`（前台取消不变）
- 保留版本号递增

### 3.2 ChatViewModel.ExecuteActivationAsync — gate 释放后的分支
当前代码（gate 释放后）：
```csharp
context.ReleaseForegroundGate();
if (!request.AwaitRemoteHydration)
{
    _ = ContinueConversationActivationAsync(request, context, ...);
    return BackgroundOwnedSuccess();
}
var result = await CompleteConversationRemoteActivationAsync(
    sessionId, context.ActivationVersion, context.CancellationToken, ...);
```

改为：
```csharp
context.ReleaseForegroundGate();
// 后台阶段使用 dispose token，不被新激活取消
var backgroundToken = _disposeCts.Token;
if (!request.AwaitRemoteHydration)
{
    _ = ContinueConversationActivationAsync(request, context, backgroundToken, ...);
    return BackgroundOwnedSuccess();
}
var result = await CompleteConversationRemoteActivationAsync(
    sessionId, context.ActivationVersion, backgroundToken, ...);
```

### 3.3 CompleteConversationRemoteActivationAsync — 版本守卫语义调整
当前行为：`IsActivationContextStale` → return false（激活失败）
新行为：`IsActivationContextStale` → 继续远端加载，但标记为"后台 only"模式
- 远端连接、loadSession、回放正常完成
- 数据写入 workspace snapshot（通过现有 session update 路由）
- 不调用：
  - `CommitActivatedConversationStateAsync`
  - runtime phase 设置为 Warm（改为 BackgroundCompleted 或不设置）
  - overlay 状态变更
  - `SetSelectedProfileIntentAction`

### 3.4 ContinueConversationActivationAsync — 同样的版本守卫
- 接收 `backgroundToken` 替代 `context.CancellationToken`
- 完成后的 `CompleteDeferredActivationAsync` 在 orchestrator 内已有版本检查：
  - 版本匹配 → 正常 finalize
  - 版本不匹配 → `TryCompleteActivationCore` 返回 false → 不调用 `OnActivationCompletedAsync`

### 3.5 Session update 路由（已有能力）
- `_sessionUpdateRouter` 已经按 conversationId 分发
- 旧激活的回放数据写入对应 conversationId 的 snapshot
- 只要不调用 `SelectConversationAction` 切换当前显示的会话，数据就只在后台积累

## 4. 不变的部分

- `BeginActivation` 的 cancel（前台取消）
- `CommitActivatedConversationStateAsync` 的版本门控
- Workspace 的 prepare/commit 拆分
- NavigationCoordinator 的 latest-intent 逻辑
- Store reducer / action
- Dispose 路径

## 5. 测试计划

### 5.1 新增测试
1. **后台不中断**：A 激活到后台阶段 → B 激活 → A 的 loadSession 继续完成 → A 数据落入 snapshot
2. **屏幕不闪烁**：A 前台阶段被 B 取消 → 屏幕从未显示 A（无中间态投影）
3. **后台完成不覆写屏幕**：A 后台完成 → `HydratedConversationId` 仍是 B
4. **后台完成不改 profile**：A（profile-x）后台完成 → `SelectedProfileIntentId` 仍是 B 的 profile-y
5. **三连击**：A → B → C，A/B 后台都完成，屏幕始终是 C

### 5.2 现有测试
- 前台取消相关测试保持不变（行为未改）
- 后台阶段依赖 `OperationCanceledException` 的测试需要改为检查版本守卫跳过

## 6. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 多个并发远端连接消耗资源 | 远端连接池已有上限；ACP 协议支持多 session |
| 旧回放数据和新回放数据时间交错 | session update 按 conversationId 路由，隔离 |
| 用户快速切换 10+ 次 | gate 保证前台串行，后台各自独立完成后版本检查 |

## 7. 实施步骤

1. 写失败测试（5.1 的第 1 个）
2. 修改后台阶段 token 来源（3.2）
3. 修改版本守卫语义（3.3）
4. 修改 deferred 路径（3.4）
5. 跑全量测试 + 修复
6. 写剩余测试（5.1 的 2-5）
7. 全量验证
