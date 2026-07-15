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

    public int CacheRetentionDays { get; set; } = 7;

    public CloudConfigSyncYamlV1 CloudConfigSync { get; set; } = new();

    // Shortcuts
    public bool KeyboardShortcutsEnabled { get; set; } = true;

    public Dictionary<string, string> KeyBindings { get; set; } = new();

    // Projects (Navigation)
    public List<ProjectDefinition> Projects { get; set; } = new();

    public List<AgentRemoteDirectory> AgentRemoteDirectories { get; set; } = new();

    // Semantic ids of the remote directories the user added to the navigation project list.
    // Reference-only: the authoritative remote-directory config remains AgentRemoteDirectories.
    public List<string> NavigationRemoteDirectoryIds { get; set; } = new();

    public string LastSelectedProjectId { get; set; } = string.Empty;

    // ACP connection governance
    public bool AcpEnableConnectionEviction { get; set; }

    public int? AcpConnectionIdleTtlMinutes { get; set; }

    public int? AcpMaxWarmProfiles { get; set; }

    public int? AcpMaxPinnedProfiles { get; set; }

    public string AcpHydrationCompletionMode { get; set; } = "StrictReplay";
}

internal sealed class CloudConfigSyncYamlV1
{
    public bool Enabled { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public long Revision { get; set; }

    public bool IncludeSecrets { get; set; } = true;

    public Dictionary<string, Dictionary<string, string>> ProviderOptions { get; set; } = new();
}
