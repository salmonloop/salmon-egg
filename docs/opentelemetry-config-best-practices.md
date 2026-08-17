# OpenTelemetry 配置与文案最佳实践

## 用户可见配置（设置界面）

### 1. 主开关：遥测数据分享

**推荐文案（参考 VS Code / Firefox / Chrome）：**

```
帮助改进 SalmonEgg

允许向已配置的 OTLP 收集器发送错误、性能与使用情况数据。
这些数据帮助我们发现并修复问题、改进性能和用户体验。

我们收集的内容：
• 错误和异常信息（不含聊天内容）
• 性能指标（响应时间、内存使用）
• 使用的功能和操作流程

我们不会收集：
• 您的聊天消息内容
• 文件内容

实现现状：应用不内置默认 collector。只有设置页填写了端点，或部署环境提供有效的
`OTEL_EXPORTER_OTLP_ENDPOINT` / 分信号端点时，管线才会激活。资源属性包含
`host.name`、`process.pid`、应用版本和运行时/操作系统信息；异常 stack trace 也可能
包含本地源码路径。因此 UI 不得把数据描述为“匿名”，只承诺不主动收集聊天内容和文件内容。

详细了解：隐私政策
```

**关键原则：**
- **默认状态**：偏好开关当前为默认开启（opt-out），但没有有效 OTLP 端点时运行态仍保持禁用。
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

没有配置端点时遥测不会启动。请确保您信任接收数据的服务提供商。
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

## 应用默认值（不暴露给用户）

应用只提供资源标识和平台采样率，不提供默认外部 collector：

```csharp
ServiceName = "SalmonEgg"
DefaultEnvironment = "production"

Desktop NormalRate = 0.10
WASM NormalRate = 0.05
Mobile NormalRate = 0.02
```

## 配置优先级

```
用户自定义端点 > 分信号环境变量 > 通用环境变量 > 不启用
  ↓
AppSettings.TelemetryCustomEndpoint (Settings UI)
  ↓
OTEL_EXPORTER_OTLP_{TRACES|METRICS|LOGS}_ENDPOINT
  ↓
OTEL_EXPORTER_OTLP_ENDPOINT (env var)
```

## 采样率说明

客户端使用 SDK 原生的 `ParentBasedSampler(TraceIdRatioBasedSampler)` 做确定性 head sampling：

- Desktop 10% / WASM 5% / Mobile 2%；
- 子 span 继承父级 sampled flag，同一 trace 不会出现相互矛盾的采样决策；
- 客户端不在 `SpanProcessor.OnEnd` 修改 sampled flag。OTel 规范规定 `OnEnd` 收到的已结束
  span 是只读对象，此时修改属于非法实现；
- 若部署要求“错误 100%”或按最终延迟采样，应在 collector/backend 配置 tail sampling。

## 隐私合规建议

1. **首次启动提示**：在欢迎向导中询问，而非预设启用
2. **随时退出**：设置界面一键关闭，立即生效
3. **数据透明**：提供"查看最近上报数据"功能（开发模式）
4. **保留期限**：由实际 collector/backend 策略决定并在隐私政策中明确
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
export OTEL_EXPORTER_OTLP_ENDPOINT=https://collector.example.com:4318
export OTEL_EXPORTER_OTLP_HEADERS=api-key=YOUR_INGEST_KEY
export OTEL_ENVIRONMENT=production
export OTEL_SERVICE_NAME=SalmonEgg
```

## 注意事项

⚠️ **禁止收集的数据**：
- 聊天消息内容
- API 密钥或认证令牌
- 用户文件路径或文件内容

- IP 地址或设备标识符（除非必要的 session 追踪）

✅ **允许收集的数据**：
- 错误堆栈（脱敏后的文件路径）
- 操作类型和耗时（如"创建会话：250ms"）
- 系统环境（OS 版本、.NET 版本、平台）
- 使用频率统计（匿名计数）
