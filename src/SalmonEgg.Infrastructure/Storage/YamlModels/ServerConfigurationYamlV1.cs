using System;

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

