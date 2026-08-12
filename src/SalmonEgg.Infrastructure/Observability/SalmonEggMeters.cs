using System.Diagnostics.Metrics;

namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Infrastructure 层拥有的 Meter 与具体指标。
/// 指标命名遵循 OTel 约定：全小写、点分层级、单位放在 unit 参数而非名称里。
/// </summary>
public static class SalmonEggMeters
{
    public const string SessionManagerMeterName = "SalmonEgg.Infrastructure.SessionManager";
    public const string AcpClientMeterName = "SalmonEgg.Infrastructure.AcpClient";
    public const string TransportMeterName = "SalmonEgg.Infrastructure.Transport";
    public const string StorageMeterName = "SalmonEgg.Infrastructure.Storage";

    private static readonly Meter SessionManagerMeter = new(SessionManagerMeterName, "1.0.0");
    private static readonly Meter AcpClientMeter = new(AcpClientMeterName, "1.0.0");
    private static readonly Meter TransportMeter = new(TransportMeterName, "1.0.0");
    private static readonly Meter StorageMeter = new(StorageMeterName, "1.0.0");

    // ===== SessionManager =====

    /// <summary>会话操作（load / create / save / delete）计数。维度：action（操作类型）。</summary>
    public static readonly Counter<long> SessionOperations = SessionManagerMeter.CreateCounter<long>(
        "salmonegg.session.operations",
        unit: "{operation}",
        description: "Number of session operations (load, create, save, delete).");

    /// <summary>会话操作错误。维度：action、error.type。</summary>
    public static readonly Counter<long> SessionErrors = SessionManagerMeter.CreateCounter<long>(
        "salmonegg.session.errors",
        unit: "{error}",
        description: "Number of errors during session operations.");

    // ===== AcpClient =====

    /// <summary>ACP 请求总数。维度：method（RPC 方法名）。</summary>
    public static readonly Counter<long> AcpRequests = AcpClientMeter.CreateCounter<long>(
        "salmonegg.acp.requests",
        unit: "{request}",
        description: "Total number of ACP JSON-RPC requests.");

    /// <summary>ACP 请求错误。维度：method、error.type。</summary>
    public static readonly Counter<long> AcpErrors = AcpClientMeter.CreateCounter<long>(
        "salmonegg.acp.errors",
        unit: "{error}",
        description: "Number of ACP request failures.");

    /// <summary>ACP 请求耗时直方图（毫秒）。维度：method。</summary>
    public static readonly Histogram<double> AcpRequestDuration = AcpClientMeter.CreateHistogram<double>(
        "salmonegg.acp.request.duration",
        unit: "ms",
        description: "Duration of ACP requests in milliseconds.");

    // ===== Transport =====

    /// <summary>传输层连接数。维度：transport_type（stdio / websocket / http）。</summary>
    public static readonly Counter<long> TransportConnections = TransportMeter.CreateCounter<long>(
        "salmonegg.transport.connections",
        unit: "{connection}",
        description: "Total number of transport connections established.");

    /// <summary>传输层发送字节数。维度：transport_type。</summary>
    public static readonly Counter<long> TransportBytesSent = TransportMeter.CreateCounter<long>(
        "salmonegg.transport.bytes.sent",
        unit: "By",
        description: "Total bytes sent via transport.");

    /// <summary>传输层接收字节数。维度：transport_type。</summary>
    public static readonly Counter<long> TransportBytesReceived = TransportMeter.CreateCounter<long>(
        "salmonegg.transport.bytes.received",
        unit: "By",
        description: "Total bytes received via transport.");

    /// <summary>传输层错误。维度：transport_type、error.type。</summary>
    public static readonly Counter<long> TransportErrors = TransportMeter.CreateCounter<long>(
        "salmonegg.transport.errors",
        unit: "{error}",
        description: "Number of transport layer errors.");

    // ===== Storage =====

    /// <summary>存储读操作计数。维度：key_prefix（存储键前缀，如 session / config）。</summary>
    public static readonly Counter<long> StorageReads = StorageMeter.CreateCounter<long>(
        "salmonegg.storage.reads",
        unit: "{read}",
        description: "Total number of storage read operations.");

    /// <summary>存储写操作计数。维度：key_prefix。</summary>
    public static readonly Counter<long> StorageWrites = StorageMeter.CreateCounter<long>(
        "salmonegg.storage.writes",
        unit: "{write}",
        description: "Total number of storage write operations.");

    /// <summary>存储删除操作计数。维度：key_prefix。</summary>
    public static readonly Counter<long> StorageDeletes = StorageMeter.CreateCounter<long>(
        "salmonegg.storage.deletes",
        unit: "{delete}",
        description: "Total number of storage delete operations.");

    /// <summary>存储错误。维度：operation（read / write / delete）、error.type。</summary>
    public static readonly Counter<long> StorageErrors = StorageMeter.CreateCounter<long>(
        "salmonegg.storage.errors",
        unit: "{error}",
        description: "Number of storage operation failures.");
}
