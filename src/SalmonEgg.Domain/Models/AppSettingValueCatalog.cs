using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SalmonEgg.Domain.Models;

/// <summary>
/// AppSettings 各可编辑字段的取值目录：键名、允许值、解析与渲染。
/// </summary>
/// <remarks>
/// 单一事实源。GUI 的选项列表（主题/backdrop 等）与 CLI 的 <c>settings set</c> 校验都必须
/// 从这里取合法值；任何一处另抄一份枚举值，就会出现「GUI 能选、CLI 拒收」或反向的漂移。
/// 解析遵循与 <see cref="AppSettingsService"/> 相同的宽容读语义：未知值回退默认而非报错，
/// 但 CLI 写入路径用 <see cref="TryParse"/> 显式拒绝，避免把用户笔误静默吞成默认值。
/// </remarks>
public static class AppSettingValueCatalog
{
    private const string DefaultTheme = "System";
    private const string DefaultBackdrop = "System";
    private const string DefaultHydrationCompletionMode = "StrictReplay";

    public const string ThemeKey = "theme";
    public const string AnimationEnabledKey = "animation_enabled";
    public const string LanguageKey = "language";
    public const string BackdropKey = "backdrop";
    public const string SaveLocalHistoryKey = "save_local_history";
    public const string CacheRetentionDaysKey = "cache_retention_days";
    public const string TelemetrySharingEnabledKey = "telemetry_sharing_enabled";
    public const string KeyboardShortcutsEnabledKey = "keyboard_shortcuts_enabled";

    /// <summary>
    /// 全部可由 CLI 编辑的设置键，按帮助文本展示顺序排列。
    /// </summary>
    /// <remarks>
    /// 只收录「单标量、有明确合法值域」的字段。列表型字段（projects、key_bindings、
    /// agent_remote_directories）结构复杂且 GUI 已有专门编辑面，CLI 逐字段改写它们
    /// 需要另一套行级语法，超出本目录「单键单值」的契约。
    /// </remarks>
    public static IReadOnlyList<string> EditableKeys { get; } =
    [
        ThemeKey,
        AnimationEnabledKey,
        LanguageKey,
        BackdropKey,
        SaveLocalHistoryKey,
        CacheRetentionDaysKey,
        TelemetrySharingEnabledKey,
        KeyboardShortcutsEnabledKey
    ];

    /// <summary>主题合法值，与 GUI 设置页选项一致。</summary>
    public static IReadOnlyList<string> ThemeValues { get; } = ["System", "Light", "Dark"];

    /// <summary>背景材质合法值，与 GUI 设置页选项一致。</summary>
    public static IReadOnlyList<string> BackdropValues { get; } = ["System", "Mica", "Acrylic", "Solid"];

    /// <summary>水合完成模式合法值。</summary>
    public static IReadOnlyList<string> HydrationCompletionModeValues { get; } = ["StrictReplay", "LoadResponse"];

    /// <summary>
    /// 尝试把单个键值对应用到设置快照上。
    /// </summary>
    /// <returns>true 表示已应用；false 表示键不在 <see cref="EditableKeys"/> 内或值非法。</returns>
    public static bool TryApply(AppSettings settings, string key, string value)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(value)) return false;

        switch (key)
        {
            case ThemeKey:
                if (!Matches(ThemeValues, value)) return false;
                settings.Theme = value;
                return true;
            case BackdropKey:
                if (!Matches(BackdropValues, value)) return false;
                settings.Backdrop = value;
                return true;
            case LanguageKey:
                // 语言走目录自身的别名归一（zh-CN → zh-Hans 等），非法标签回退 System，
                // 与 LoadAsync 的宽容读语义一致。
                settings.Language = AppLanguageCatalog.NormalizeTag(value);
                return true;
            case AnimationEnabledKey:
                return TryParseBoolean(value, parsed => settings.IsAnimationEnabled = parsed);
            case SaveLocalHistoryKey:
                return TryParseBoolean(value, parsed => settings.SaveLocalHistory = parsed);
            case TelemetrySharingEnabledKey:
                return TryParseBoolean(value, parsed => settings.TelemetrySharingEnabled = parsed);
            case KeyboardShortcutsEnabledKey:
                return TryParseBoolean(value, parsed => settings.KeyboardShortcutsEnabled = parsed);
            case CacheRetentionDaysKey:
                return TryParseRetentionDays(value, parsed => settings.CacheRetentionDays = parsed);
            default:
                return false;
        }
    }

    /// <summary>
    /// 渲染单个键的当前值，供查看命令输出。
    /// </summary>
    /// <returns>当前值的文本形态；键未知时为 null。</returns>
    public static string? RenderValue(AppSettings settings, string key)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        return key switch
        {
            ThemeKey => settings.Theme,
            BackdropKey => settings.Backdrop,
            LanguageKey => settings.Language,
            AnimationEnabledKey => Render(settings.IsAnimationEnabled),
            SaveLocalHistoryKey => Render(settings.SaveLocalHistory),
            TelemetrySharingEnabledKey => Render(settings.TelemetrySharingEnabled),
            KeyboardShortcutsEnabledKey => Render(settings.KeyboardShortcutsEnabled),
            CacheRetentionDaysKey => settings.CacheRetentionDays.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    /// <summary>
    /// 键的合法取值说明，供错误信息与帮助文本使用；无封闭值域时为 null。
    /// </summary>
    public static IReadOnlyList<string>? AllowedValues(string key) => key switch
    {
        ThemeKey => ThemeValues,
        BackdropKey => BackdropValues,
        LanguageKey => AppLanguageCatalog.SupportedOptions.Select(option => option.Tag).ToArray(),
        _ => null
    };

    private static bool Matches(IReadOnlyList<string> allowed, string value) =>
        allowed.Contains(value, StringComparer.Ordinal);

    private static bool TryParseBoolean(string value, Action<bool> assign)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            return false;
        }

        assign(parsed);
        return true;
    }

    private static bool TryParseRetentionDays(string value, Action<int> assign)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days <= 0)
        {
            return false;
        }

        assign(days);
        return true;
    }

    private static string Render(bool value) => value ? "true" : "false";
}
