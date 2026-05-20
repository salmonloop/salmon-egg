using System;
using System.Collections.Generic;

namespace SalmonEgg.Infrastructure.Storage.YamlModels;

internal sealed class ServerConfigurationYamlV1
{
    public int SchemaVersion { get; set; } = 1;

    public string UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Transport { get; set; } = "websocket";

    public string ServerUrl { get; set; } = string.Empty;

    public string StdioCommand { get; set; } = string.Empty;

    public string StdioArgs { get; set; } = string.Empty;

    public int ConnectionTimeoutSeconds { get; set; } = 10;

    public AuthenticationYamlV1 Authentication { get; set; } = new();

    public ProxyYamlV1 Proxy { get; set; } = new();

    public List<McpServerYamlV1> McpServers { get; set; } = new();
}

internal sealed class AuthenticationYamlV1
{
    public string Mode { get; set; } = "none";
}

internal sealed class ProxyYamlV1
{
    public bool Enabled { get; set; }

    public string ProxyUrl { get; set; } = string.Empty;
}

internal sealed class McpServerYamlV1
{
    public string Transport { get; set; } = "stdio";

    public string Name { get; set; } = string.Empty;

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
