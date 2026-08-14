# OpenTelemetry 配置与文案最佳实践

## 用户可见配置（设置界面）

### 1. 主开关：遥测数据分享

**推荐文案（参考 VS Code / Firefox / Chrome）：**

```
✅ 帮助改进 SalmonEgg（推荐）

允许发送匿名使用数据和崩溃报告给开发者。
这些数据帮助我们发现并修复问题、改进性能和用户体验。

我们收集的内容：
• 错误和异常信息（不含聊天内容）
• 性能指标（响应时间、内存使用）
• 使用的功能和操作流程

我们不会收集：
• 您的聊天消息内容
• 文件内容

⚠️ 实现现状（与上面的理想文案有差距，勿直接照抄进 UI）：
当前 span 会带上 `host.name`（机器名，常含人名）、`process.pid`，
以及 stdio 传输的 `chat.command`（本地 agent 命令行，通常含用户名与路径）、
`chat.url`（远端 URL，query 里可能带 token）。
因此设置页文案只承诺"不收集聊天内容与文件内容"，并明确告知会包含设备名等技术信息。
若要兑现"不含路径/个人信息"，需要先对这些属性做脱敏，那是独立于本次配置生效链路的改动。

详细了解：隐私政策
```

**关键原则：**
- **默认状态**：当前实现为默认开启（opt-out，与 VS Code / Firefox 一致），
  用户可在「设置 → 数据与存储」随时关闭。若目标市场需要 GDPR 式的显式同意，
  应改为默认关闭并加入首次启动询问——这是产品决策，不要仅凭本文档默认值推断
- **透明**：明确列出收集什么、不收集什么
- **正向语言**："帮助改进" 而非 "报告错误"
- **简洁**：核心信息一屏内

### 2. 高级配置：自定义端点（可折叠）

```
🔧 高级选项（仅供开发者）

自定义遥测端点：
[文本框] https://your-collector.example.com:4318

OTLP 请求头（可选）：
[密码框] api-key=YOUR_KEY

⚠️ 警告：自定义端点会绕过官方收集器，请确保您信任该服务提供商。
```

**实现建议：**
- 放在可折叠的 Expander 内
- 普通用户无需看到这部分
- 请求头格式遵循 OTLP 规范的 `OTEL_EXPORTER_OTLP_HEADERS`：逗号分隔的 `key=value`。
  **不是** `Bearer xxx`——那样解析不出任何 header，导出会静默 401。
  需要 Authorization 语义时写 `Authorization=Bearer YOUR_TOKEN`。
- 端口：HTTP/Protobuf 用 4318，gRPC 用 4317。默认协议是 HTTP/Protobuf。
- 变更在保存成功后立即重建管线生效，无需重启（见 `ITelemetryRuntime`）

## 业界文案参考

### Visual Studio Code
```
✓ Send usage data to Microsoft

Help improve VS Code by allowing Microsoft to collect usage data.
Read our privacy statement to learn more.
```

### Firefox
```
✓ Allow Firefox to send technical and interaction data to Mozilla

This helps us improve Firefox. Mozilla handles your data as described
in our Privacy Notice.
```

### Chrome
```
✓ Help improve Chrome's features and performance

Automatically sends usage statistics and crash reports to Google.
```

### JetBrains IDEs
```
✓ Send usage statistics

Help JetBrains improve its products by sending anonymous data about
features and plugins used, hardware and software configuration, and
statistics on types of files, number of files per project, etc.
```

## 默认配置（配置文件，不暴露给用户）

这些配置应硬编码在 `TelemetryDefaults.cs` 中，普通用户无法修改：

```csharp
// 默认 OTLP 基础端点（HTTP/Protobuf；应用按信号解析为 /v1/{traces,metrics,logs}）
DefaultOtlpEndpoint = "https://otlp.shangxin.me"

// 平台差异化采样率
DesktopBaseSamplingRate = 0.10  // 10%（开发调试友好）
MobileBaseSamplingRate = 0.01   // 1%（省电省流量）
WasmBaseSamplingRate = 0.05     // 5%（防控制台洪泛）

// 错误/慢请求提升采样率
DesktopSlowSpanSamplingRate = 1.0   // 100%（完整捕获）
MobileSlowSpanSamplingRate = 0.10   // 10%（平衡性能）
WasmSlowSpanSamplingRate = 0.20     // 20%

// 慢请求阈值
SlowSpanThresholdMs = 1000  // 超过 1 秒视为慢请求
```

## 配置优先级

```
用户自定义端点 > 环境变量 > 默认值
  ↓
AppSettings.TelemetryCustomEndpoint (Settings UI)
  ↓
OTEL_EXPORTER_OTLP_ENDPOINT (env var)
  ↓
TelemetryDefaults.DefaultOtlpEndpoint (hardcoded)
```

## 采样率说明

### 基础采样率（Base Sampling）
- **用途**：正常流量的随机抽样，控制数据量
- **建议值**：Desktop 10% / Mobile 1% / WASM 5%
- **原因**：Desktop 调试需求高；Mobile 省资源；WASM 避免控制台卡顿

### 慢请求采样率（Slow Span Sampling）
- **用途**：超过阈值的慢操作提升采样率
- **建议值**：Desktop 100% / Mobile 10% / WASM 20%
- **原因**：性能问题必须完整捕获（Desktop）；Mobile 仍需节制

### 异常采样率（Error Sampling）
- **固定值**：100%（所有平台）
- **实现**：通过 `ErrorSpanProcessor` 在 span 结束时检测异常并强制导出
- **原因**：错误是最高优先级信号，绝不能丢失

## 隐私合规建议

1. **首次启动提示**：在欢迎向导中询问，而非预设启用
2. **随时退出**：设置界面一键关闭，立即生效
3. **数据透明**：提供"查看最近上报数据"功能（开发模式）
4. **保留期限**：明确说明数据保留 30 天后自动删除
5. **地区适配**：EU 用户默认禁用（GDPR）

## UI 布局建议

```
设置 > 数据与隐私
  ├─ 本地历史记录 [Toggle]
  ├─ 缓存保留天数 [数字输入]
  ├─ 云端配置同步 [Section]
  └─ 遥测与改进 [Section] ← 新增
      ├─ [Toggle] 帮助改进 SalmonEgg
      ├─ [说明文本] 我们收集什么...
      ├─ [链接] 隐私政策
      └─ [Expander] 高级选项
          ├─ 自定义端点 [TextBox]
          └─ 认证头 [PasswordBox]
```

## 开发者端点配置示例

```bash
# 本地测试（OTLP Collector）
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
export OTEL_SDK_DISABLED=false

# 生产环境（通过 CI/CD 注入）
export OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp.shangxin.me
export OTEL_EXPORTER_OTLP_HEADERS=api-key=YOUR_INGEST_KEY
export OTEL_ENVIRONMENT=production
export OTEL_SERVICE_NAME=SalmonEgg
```

## 注意事项

⚠️ **禁止收集的数据**：
- 聊天消息内容
- API 密钥或认证令牌
- 用户文件路径或文件内容

  ⚠️ 上面第 3 条尚未在代码中兑现，见本文档开头「实现现状」。
- IP 地址或设备标识符（除非必要的 session 追踪）

✅ **允许收集的数据**：
- 错误堆栈（脱敏后的文件路径）
- 操作类型和耗时（如"创建会话：250ms"）
- 系统环境（OS 版本、.NET 版本、平台）
- 使用频率统计（匿名计数）
