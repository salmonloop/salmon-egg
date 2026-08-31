using System;
using System.Collections.Generic;
using SalmonEgg.Domain.Models;
namespace SalmonEgg.Infrastructure.Storage.YamlModels;

internal sealed class ServerConfigurationYaml
{
    public int SchemaVersion { get; set; } = 4;

    public string UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public string Revision { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Transport { get; set; } = "websocket";

    public string ServerUrl { get; set; } = string.Empty;

    public string StdioCommand { get; set; } = string.Empty;

    public List<string> StdioArguments { get; set; } = new();

    /// <summary>
    /// Stdio child-process environment overlay. Added in schema_version 3; absent in v2 files, which
    /// deserialize to null and therefore need no migration step.
    /// </summary>
    /// <remarks>
    /// Nullable and left null when there is nothing to write, so <c>OmitNull</c> drops the key entirely
    /// rather than emitting a flow-style <c>{}</c>. Configuration YAML stays block-style so it remains
    /// readable and mergeable, which is the shape the persistence spec requires.
    /// </remarks>
    public Dictionary<string, string>? StdioEnvironment { get; set; }

    public int ConnectionTimeoutSeconds { get; set; } = AcpConnectionTimeoutPolicy.DefaultSeconds;

    /// <summary>
    /// Whether this profile's launch configuration was proven to start and speak ACP. Added in
    /// schema_version 4; absent in v3 and earlier files, which deserialize to null and need no
    /// migration step.
    /// </summary>
    /// <remarks>
    /// Nullable, and left null for <see cref="ProfileVerificationState.Unknown"/> so <c>OmitNull</c>
    /// drops the key entirely rather than writing <c>unknown</c>. That keeps the no-verdict wire shape
    /// minimal and prevents a redundant default key from participating in the cloud-sync fingerprint.
    /// A schema_version 3 file saved by this build still changes to schema_version 4 by design.
    ///
    /// Written as a token rather than the enum's name so the on-disk vocabulary is owned here, the way
    /// <c>transport</c> and <c>proxy.mode</c> already are. An unrecognized token reads back as
    /// <see cref="ProfileVerificationState.Unknown"/>: the permissive direction, matching how
    /// <c>transport</c> falls back instead of refusing the file.
    /// </remarks>
    public string? Verification { get; set; }

    /// <summary>
    /// When the passing test ran, ISO-8601 round-trip in UTC. Non-null only alongside
    /// <c>verification: verified</c>.
    /// </summary>
    /// <remarks>
    /// A timestamp without a verified verdict is discarded on read rather than honoured, so a
    /// hand-edited file cannot produce a profile that claims evidence it does not have.
    /// </remarks>
    public string? VerifiedAtUtc { get; set; }

    public AuthenticationYamlV1 Authentication { get; set; } = new();

    public ProxyYamlV1 Proxy { get; set; } = new();
}

internal sealed class AuthenticationYamlV1
{
    public string Mode { get; set; } = "none";
}

internal sealed class ProxyYamlV1
{
    public string Mode { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string ProxyUrl { get; set; } = string.Empty;
}
