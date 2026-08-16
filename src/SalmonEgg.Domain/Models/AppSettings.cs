namespace SalmonEgg.Domain.Models;

using System.Collections.Generic;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";

    public bool IsAnimationEnabled { get; set; } = true;

    public string? LastSelectedServerId { get; set; }

    // General
    public bool LaunchOnStartup { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Canonical UI language tag from <see cref="AppLanguageCatalog"/>, e.g. "System", "zh-Hans", "en-US".
    /// </summary>
    public string Language { get; set; } = "System";

    // Appearance
    /// <summary>
    /// Backdrop preference: "System", "Mica", "Acrylic", "Solid".
    /// </summary>
    public string Backdrop { get; set; } = "System";

    // Data & Storage / Privacy
    public bool SaveLocalHistory { get; set; } = true;

    public int CacheRetentionDays { get; set; } = 7;

    public CloudConfigSyncSettings CloudConfigSync { get; set; } = new();

    // Shortcuts
    public bool KeyboardShortcutsEnabled { get; set; } = true;

    public Dictionary<string, string> KeyBindings { get; set; } = new();

    // Projects (Navigation)
    public List<ProjectDefinition> Projects { get; set; } = new();

    public List<AgentRemoteDirectory> AgentRemoteDirectories { get; set; } = new();

    /// <summary>
    /// Stable <see cref="AgentRemoteDirectory.DirectoryId"/> values that the user has added to the
    /// navigation project list. This stores references only; the authoritative remote path
    /// configuration remains <see cref="AgentRemoteDirectories"/>.
    /// </summary>
    public List<string> NavigationRemoteDirectoryIds { get; set; } = new();

    public string? LastSelectedProjectId { get; set; }

    // ACP connection governance
    public bool AcpEnableConnectionEviction { get; set; }

    public int? AcpConnectionIdleTtlMinutes { get; set; }

    public int? AcpMaxWarmProfiles { get; set; }

    public int? AcpMaxPinnedProfiles { get; set; }

    public string AcpHydrationCompletionMode { get; set; } = "StrictReplay";

    // Telemetry & Error Reporting
    /// <summary>
    /// 用户是否同意分享匿名错误报告与性能数据。
    /// 默认 true（与 VS Code / Firefox / Chrome 一致的 opt-out 模式）；
    /// 用户可在「设置 → 数据与存储」随时关闭，关闭后整个 SDK 变为 no-op。
    /// </summary>
    public bool TelemetrySharingEnabled { get; set; } = true;

    /// <summary>
    /// 高级用户可自定义的 OpenTelemetry Collector 端点（覆盖默认值）。
    /// 格式：https://your-collector.example.com:4318
    /// </summary>
    public string? TelemetryCustomEndpoint { get; set; }

    /// <summary>
    /// Custom OTLP authentication header. This value is held by the secure-storage boundary and
    /// must not be serialized into app.yaml or configuration sync payloads.
    /// </summary>
    public string? TelemetryAuthHeader { get; set; }
}
