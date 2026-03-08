using System;

namespace SalmonEgg.Infrastructure.Storage.YamlModels;

internal sealed class AppSettingsYamlV1
{
    public int SchemaVersion { get; set; } = 1;

    public string UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public string Theme { get; set; } = "System";

    public bool IsAnimationEnabled { get; set; } = true;

    public string LastSelectedServerId { get; set; } = string.Empty;
}

