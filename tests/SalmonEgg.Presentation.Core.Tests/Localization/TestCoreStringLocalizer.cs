using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

internal sealed class TestCoreStringLocalizer : IStringLocalizer<CoreStrings>
{
    private static readonly Dictionary<string, string> Strings = new()
    {
        ["Nav_Settings"] = "设置",
        ["AcpConnection_TransportStdio"] = "Stdio（子进程）",
        ["AcpConnection_TransportWebSocket"] = "WebSocket",
        ["AcpConnection_TransportHttpSse"] = "HTTP SSE",
        ["AcpConnection_HydrationStrictReplayName"] = "严格回放",
        ["AcpConnection_HydrationStrictReplayDescription"] = "回放投影进入稳定状态后完成 hydration。",
        ["AcpConnection_HydrationLoadResponseName"] = "加载响应",
        ["AcpConnection_HydrationLoadResponseDescription"] = "session/load 返回后立即完成 hydration，回放异步投影。",
        ["SettingsSection_General"] = "常规",
        ["SettingsSection_Appearance"] = "外观",
        ["SettingsSection_AgentAcp"] = "ACP / Agent",
        ["SettingsSection_DataStorage"] = "数据与存储",
        ["SettingsSection_Shortcuts"] = "快捷键",
        ["SettingsSection_Diagnostics"] = "诊断与日志",
        ["SettingsSection_About"] = "关于",
        ["AgentProfile_StatusConnecting"] = "连接中...",
        ["AgentProfile_StatusConnected"] = "已连接",
        ["AgentProfile_StatusDisconnected"] = "未连接",
        ["Shortcuts_InvalidGestureMessage"] = "存在无效快捷键格式，请修正后保存。",
        ["Shortcuts_ConflictMessage"] = "存在冲突：{0}",
        ["Shortcuts_ConflictSeparator"] = "，",
        ["Diagnostics_NoLogFileFound"] = "未找到日志文件。",
        ["LiveLog_StatusNotStarted"] = "未启动",
        ["LiveLog_StatusStreaming"] = "正在实时查看",
        ["LiveLog_StatusStopped"] = "已停止",
        ["LiveLog_StatusPaused"] = "已暂停",
        ["LiveLog_StatusReadFailed"] = "读取失败，请稍后重试",
        ["LiveLog_StatusNoLogFile"] = "未找到可用日志文件",
        ["LiveLog_StatusSwitchedToLatest"] = "已切换到最新日志文件",
        ["About_ReleaseNotesTitle"] = "更新日志",
        ["About_PrivacyPolicyTitle"] = "隐私政策",
        ["About_VersionInfoAppLabel"] = "应用",
        ["About_VersionInfoVersionLabel"] = "版本",
        ["About_VersionInfoProtocolLabel"] = "协议",
        ["About_VersionInfoCopied"] = "版本信息已复制到剪贴板。",
        ["About_ClipboardUnsupported"] = "当前平台暂不支持剪贴板复制。",
        ["About_AcknowledgementVersionFallback"] = "版本未列出",
        ["About_AcknowledgementLicenseFallback"] = "许可证未列出",
        ["About_AcknowledgementSourceFallback"] = "来源未列出",
        ["Platform_ExternalOpenUnsupported"] = "当前平台暂不支持打开本地文件或目录。",
        ["Platform_LocalFileExportUnsupported"] = "当前平台暂不支持导出本地文件。",
        ["About_MissingDocumentMessage"] = "未找到{0}文件。",
        ["About_MissingDocumentWithFolderMessage"] = "未找到{0}文件。\n请在以下目录创建对应的 Markdown 文件：\n{1}"
    };

    public LocalizedString this[string name] => new(name, Resolve(name));

    public LocalizedString this[string name, params object[] arguments]
        => new(name, string.Format(CultureInfo.InvariantCulture, Resolve(name), arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

    public IStringLocalizer WithCulture(CultureInfo culture) => this;

    private static string Resolve(string name) => Strings.GetValueOrDefault(name, name);
}
