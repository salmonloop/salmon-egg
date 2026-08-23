namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry Semantic Conventions
/// 严格遵守 https://opentelemetry.io/docs/specs/semconv/
/// </summary>
public static class SemanticConventions
{
    /// <summary>
    /// Resource Attributes
    /// https://opentelemetry.io/docs/specs/semconv/resource/
    /// </summary>
    public static class Resource
    {
        public const string ServiceName = "service.name";
        public const string ServiceVersion = "service.version";

        /// <summary>
        /// 注意：规范上只保证「同时存在的实例之间唯一」，SDK 默认每进程随机生成，
        /// 因此这是**进程/启动**判别符。<c>count(distinct)</c> 约等于启动次数而非设备数；
        /// 要数设备用 <see cref="AppInstallationId"/>，两者不可互相顶替。
        /// </summary>
        public const string ServiceInstanceId = "service.instance.id";

        /// <summary>
        /// 本次安装在本设备上的稳定标识，用于统计设备数 / DAU。
        /// </summary>
        /// <remarks>
        /// 规范要求跨启动与跨升级保持不变、卸载后改变，且**硬件 ID（序列号 / IMEI / MAC）
        /// MUST NOT 用作该值**。requirement level 是 Recommended（不是 Opt-In），故可默认
        /// 上报、不需要额外的用户开关；用户仍可通过关闭遥测总开关来停止上报。
        /// </remarks>
        public const string AppInstallationId = "app.installation.id";

        /// <summary>
        /// 注意：旧名 <c>deployment.environment</c> 已被规范弃用，
        /// 当前稳定名为 <c>deployment.environment.name</c>。
        /// </summary>
        public const string DeploymentEnvironmentName = "deployment.environment.name";

        public const string ProcessPid = "process.pid";
        public const string ProcessRuntimeName = "process.runtime.name";
        public const string ProcessRuntimeVersion = "process.runtime.version";

        public const string HostName = "host.name";
        public const string OsType = "os.type";

        public const string DeviceModelName = "device.model.name";
        public const string DeviceManufacturer = "device.manufacturer";

        public const string TelemetrySdkName = "telemetry.sdk.name";
        public const string TelemetrySdkLanguage = "telemetry.sdk.language";
        public const string TelemetrySdkVersion = "telemetry.sdk.version";
    }

    /// <summary>
    /// HTTP Attributes
    /// https://opentelemetry.io/docs/specs/semconv/http/
    /// </summary>
    public static class Http
    {
        public const string Method = "http.request.method";
        public const string Url = "url.full";
        public const string StatusCode = "http.response.status_code";
        public const string RequestBodySize = "http.request.body.size";
        public const string ResponseBodySize = "http.response.body.size";
        public const string UserAgent = "user_agent.original";
    }

    /// <summary>
    /// RPC Attributes (for ACP protocol)
    /// https://opentelemetry.io/docs/specs/semconv/rpc/
    /// </summary>
    public static class Rpc
    {
        public const string System = "rpc.system";
        public const string Service = "rpc.service";
        public const string Method = "rpc.method";
    }

    /// <summary>
    /// Exception Attributes
    /// https://opentelemetry.io/docs/specs/semconv/exceptions/
    /// </summary>
    public static class Exception
    {
        public const string Type = "exception.type";
        public const string Message = "exception.message";
        public const string Stacktrace = "exception.stacktrace";
    }

    /// <summary>
    /// 错误分类属性（span / metric 级）。
    ///
    /// 与 exception event 是两套不同约定：<c>error.type</c> 是低基数的“失败类别”，
    /// 用于聚合和告警；异常明细走 <c>exception.*</c>（见
    /// <c>SalmonEgg.Application.Observability.ActivityExtensions</c>）。
    /// 规范中 <c>error.message</c> 已弃用，<c>error.stack_trace</c> 非标准键，故不定义。
    /// </summary>
    public static class Error
    {
        public const string Type = "error.type";
    }

    /// <summary>
    /// 应用自定义 Attributes。
    ///
    /// 归属规则：只定义 Infrastructure 层埋点实际使用的键。Chat 相关键归 Application
    /// 层（ChatService 实现在那一层，见 <c>ApplicationSemanticConventions.Chat</c>），
    /// 此处不重复定义，避免同一语义在两层各有一份常量而漂移。
    ///
    /// 未定义 Database 组：本项目使用文件 / IndexedDB 存储，没有 SQL 数据库，
    /// 保留 <c>db.*</c> 常量只会诱导误用（且旧的 db.system / db.statement / db.name
    /// 在规范中均已弃用，分别被 db.system.name / db.query.text / db.namespace 取代）。
    /// </summary>
    public static class SalmonEgg
    {
        // Session
        public const string SessionId = "salmonegg.session.id";
        public const string SessionName = "salmonegg.session.name";
        public const string SessionAction = "salmonegg.session.action";

        // Transport
        public const string TransportType = "salmonegg.transport.type";
        public const string TransportConnectionId = "salmonegg.transport.connection_id";

        // Storage
        public const string StorageKeyPrefix = "salmonegg.storage.key_prefix";
        public const string StorageOperation = "salmonegg.storage.operation";
    }
}
