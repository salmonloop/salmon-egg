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
    /// Language tag, e.g. "System", "zh-CN", "en-US".
    /// </summary>
    public string Language { get; set; } = "System";

    // Appearance
    /// <summary>
    /// Backdrop preference: "System", "Mica", "Acrylic", "Solid".
    /// </summary>
    public string Backdrop { get; set; } = "System";

    // Data & Storage / Privacy
    public bool SaveLocalHistory { get; set; } = true;

    public int HistoryRetentionDays { get; set; } = 30;

    public bool RememberRecentProjectPaths { get; set; } = true;

    public int CacheRetentionDays { get; set; } = 7;

    // Shortcuts
    public Dictionary<string, string> KeyBindings { get; set; } = new();

    // Projects (Navigation)
    public List<ProjectDefinition> Projects { get; set; } = new();

    public string? LastSelectedProjectId { get; set; }
}
