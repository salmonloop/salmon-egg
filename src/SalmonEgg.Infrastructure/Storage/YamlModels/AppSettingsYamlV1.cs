using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Infrastructure.Storage.YamlModels;

internal sealed class AppSettingsYamlV1
{
    public int SchemaVersion { get; set; } = 1;

    public string UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public string Theme { get; set; } = "System";

    public bool IsAnimationEnabled { get; set; } = true;

    public string LastSelectedServerId { get; set; } = string.Empty;

    // General
    public bool LaunchOnStartup { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public string Language { get; set; } = "System";

    // Appearance
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

    public string LastSelectedProjectId { get; set; } = string.Empty;
}
