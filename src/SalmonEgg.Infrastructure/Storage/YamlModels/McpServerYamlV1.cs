using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Storage.YamlModels;

internal sealed class McpServerYamlV1
{
    public string Transport { get; set; } = "stdio";

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Dictionary<string, object?>? Meta { get; set; }

    public string Command { get; set; } = string.Empty;

    public List<string> Args { get; set; } = new();

    public List<McpNameValueYamlV1> Env { get; set; } = new();

    public string Url { get; set; } = string.Empty;

    public List<McpNameValueYamlV1> Headers { get; set; } = new();
}

internal sealed class McpNameValueYamlV1
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public Dictionary<string, object?>? Meta { get; set; }
}
