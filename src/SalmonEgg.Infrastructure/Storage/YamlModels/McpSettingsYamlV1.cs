using System;
using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Storage.YamlModels;

internal sealed class McpSettingsYamlV1
{
    public int SchemaVersion { get; set; } = 1;

    public string UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public List<McpServerYamlV1> Servers { get; set; } = new();
}
