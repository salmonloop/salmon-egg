using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;
using Xunit;

namespace SalmonEgg.Domain.Tests.Protocol;

public sealed class AcpSchemaContractTests
{
    [Fact]
    public void SourceGeneratedContext_RoundTripsMetaAcrossRepresentativeOfficialTypes()
    {
        var authMethod = RoundTripWithMeta(
            new AuthMethodDefinition
            {
                Id = "agent",
                Name = "Agent login",
                Meta = CreateMeta("auth")
            },
            AcpJsonContext.Default.AuthMethodDefinition,
            "auth");
        AssertMeta(authMethod, "auth");

        var sessionList = RoundTripWithMeta(
            new SessionListResponse
            {
                Meta = CreateMeta("list"),
                Sessions =
                [
                    new AgentSessionInfo
                    {
                        SessionId = "remote-1",
                        Cwd = "/remote/repo",
                        AdditionalDirectories = ["/remote/shared"],
                        Meta = CreateMeta("session-info")
                    }
                ]
            },
            AcpJsonContext.Default.SessionListResponse,
            "list");
        AssertMeta(Assert.Single(sessionList.Sessions), "session-info");

        var updateParams = RoundTripWithMeta(
            new SessionUpdateParams
            {
                SessionId = "remote-1",
                Meta = CreateMeta("update-params"),
                Update = new UsageUpdate
                {
                    Used = 10,
                    Size = 100,
                    Meta = CreateMeta("usage"),
                    Cost = new UsageCost
                    {
                        Amount = 1.25,
                        Currency = "USD",
                        Meta = CreateMeta("cost")
                    }
                }
            },
            AcpJsonContext.Default.SessionUpdateParams,
            "update-params");
        var usage = Assert.IsType<UsageUpdate>(updateParams.Update);
        AssertMeta(usage, "usage");
        AssertMeta(Assert.IsType<UsageCost>(usage.Cost), "cost");

        var content = RoundTripWithMeta<ContentBlock>(
            new TextContentBlock("hello")
            {
                Meta = CreateMeta("content"),
                Annotations = new Annotations
                {
                    Audience = ["user"],
                    Priority = 0.5,
                    Meta = CreateMeta("annotations")
                }
            },
            AcpJsonContext.Default.ContentBlock,
            "content");
        var text = Assert.IsType<TextContentBlock>(content);
        AssertMeta(Assert.IsType<Annotations>(text.Annotations), "annotations");

        var location = RoundTripWithMeta(
            new ToolCallLocation("/remote/repo/file.cs", 42)
            {
                Meta = CreateMeta("tool-location")
            },
            AcpJsonContext.Default.ToolCallLocation,
            "tool-location");
        Assert.Equal((uint)42, location.Line);

        var modes = RoundTripWithMeta(
            new SessionModesState
            {
                CurrentModeId = "code",
                Meta = CreateMeta("modes"),
                AvailableModes =
                [
                    new SalmonEgg.Acp.Protocol.SessionMode
                    {
                        Id = "code",
                        Name = "Code",
                        Meta = CreateMeta("mode")
                    }
                ]
            },
            AcpJsonContext.Default.SessionModesState,
            "modes");
        AssertMeta(Assert.Single(modes.AvailableModes), "mode");

        var terminalOutput = RoundTripWithMeta(
            new TerminalOutputResponse
            {
                Output = "done",
                Truncated = false,
                Meta = CreateMeta("terminal-output"),
                ExitStatus = new TerminalExitStatus
                {
                    ExitCode = 0,
                    Meta = CreateMeta("terminal-exit")
                }
            },
            AcpJsonContext.Default.TerminalOutputResponse,
            "terminal-output");
        AssertMeta(Assert.IsType<TerminalExitStatus>(terminalOutput.ExitStatus), "terminal-exit");

        var configOption = RoundTripWithMeta(
            CreateGroupedConfigOption(),
            AcpJsonContext.Default.ConfigOption,
            "config");
        var group = Assert.Single(configOption.OptionGroups);
        AssertMeta(group, "config-group");
        AssertMeta(Assert.Single(group.Options), "config-value");

        var permissionOption = RoundTripWithMeta(
            new PermissionOption("allow", "Allow", "allow_once")
            {
                Meta = CreateMeta("permission")
            },
            AcpJsonContext.Default.PermissionOption,
            "permission");
        Assert.Equal("allow_once", permissionOption.Kind);
    }

    [Fact]
    public void GroupedSelectConfigOption_RoundTripsOfficialUnionShapeAndOrderedGroups()
    {
        var original = CreateGroupedConfigOption();

        var json = JsonSerializer.Serialize(original, AcpJsonContext.Default.ConfigOption);
        var parsed = JsonSerializer.Deserialize(json, AcpJsonContext.Default.ConfigOption);
        var replay = JsonSerializer.Serialize(parsed, AcpJsonContext.Default.ConfigOption);
        using var document = JsonDocument.Parse(replay);

        Assert.NotNull(parsed);
        Assert.Equal("select", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("code", document.RootElement.GetProperty("currentValue").GetString());
        var options = document.RootElement.GetProperty("options");
        var group = Assert.Single(options.EnumerateArray());
        Assert.Equal("workflow", group.GetProperty("group").GetString());
        Assert.Equal("Workflow", group.GetProperty("name").GetString());
        Assert.False(group.TryGetProperty("value", out _));
        Assert.Equal("code", group.GetProperty("options")[0].GetProperty("value").GetString());
        Assert.Empty(parsed!.Options);
        Assert.Equal("workflow", Assert.Single(parsed.OptionGroups).Group);
    }

    [Fact]
    public void SessionInfoAndPermissionOption_SerializeOnlyOfficialFieldsAndDoNotReplayUnknownRootFields()
    {
        const string sessionJson = """
        {
          "sessionId": "remote-1",
          "cwd": "/remote/repo",
          "additionalDirectories": ["/remote/shared"],
          "title": "Remote",
          "updatedAt": "2026-07-16T00:00:00Z",
          "description": "legacy nonstandard field",
          "futureField": 42,
          "_meta": { "marker": "session" }
        }
        """;
        var session = JsonSerializer.Deserialize(sessionJson, AcpJsonContext.Default.AgentSessionInfo);
        var sessionReplay = JsonSerializer.Serialize(session, AcpJsonContext.Default.AgentSessionInfo);
        using var sessionDocument = JsonDocument.Parse(sessionReplay);

        Assert.NotNull(session);
        AssertPropertySet(
            sessionDocument.RootElement,
            "sessionId",
            "cwd",
            "additionalDirectories",
            "title",
            "updatedAt",
            "_meta");
        Assert.False(sessionDocument.RootElement.TryGetProperty("description", out _));
        Assert.False(sessionDocument.RootElement.TryGetProperty("futureField", out _));

        const string permissionJson = """
        {
          "optionId": "allow",
          "name": "Allow",
          "kind": "allow_once",
          "description": "legacy nonstandard field",
          "_meta": { "marker": "permission" }
        }
        """;
        var permission = JsonSerializer.Deserialize(permissionJson, AcpJsonContext.Default.PermissionOption);
        var permissionReplay = JsonSerializer.Serialize(permission, AcpJsonContext.Default.PermissionOption);
        using var permissionDocument = JsonDocument.Parse(permissionReplay);

        Assert.NotNull(permission);
        AssertPropertySet(permissionDocument.RootElement, "optionId", "name", "kind", "_meta");
        Assert.False(permissionDocument.RootElement.TryGetProperty("description", out _));
    }

    [Fact]
    public void OfficialUnsignedNumericFields_RoundTripAtSchemaMaximums()
    {
        var terminalRequest = new TerminalCreateRequest
        {
            SessionId = "remote-1",
            Command = "agent",
            OutputByteLimit = ulong.MaxValue
        };
        var terminalRequestJson = JsonSerializer.Serialize(
            terminalRequest,
            AcpJsonContext.Default.TerminalCreateRequest);
        using var terminalRequestDocument = JsonDocument.Parse(terminalRequestJson);
        Assert.Equal(
            ulong.MaxValue,
            terminalRequestDocument.RootElement.GetProperty("outputByteLimit").GetUInt64());
        Assert.Equal(
            ulong.MaxValue,
            JsonSerializer.Deserialize(
                terminalRequestJson,
                AcpJsonContext.Default.TerminalCreateRequest)!.OutputByteLimit);

        var usage = new UsageUpdate
        {
            Used = ulong.MaxValue,
            Size = ulong.MaxValue
        };
        var usageJson = JsonSerializer.Serialize(usage, AcpJsonContext.Default.UsageUpdate);
        using var usageDocument = JsonDocument.Parse(usageJson);
        Assert.Equal(ulong.MaxValue, usageDocument.RootElement.GetProperty("used").GetUInt64());
        Assert.Equal(ulong.MaxValue, usageDocument.RootElement.GetProperty("size").GetUInt64());
        var parsedUsage = JsonSerializer.Deserialize(usageJson, AcpJsonContext.Default.UsageUpdate);
        Assert.Equal(ulong.MaxValue, parsedUsage!.Used);
        Assert.Equal(ulong.MaxValue, parsedUsage.Size);

        var exitStatus = new TerminalExitStatus { ExitCode = uint.MaxValue };
        var exitStatusJson = JsonSerializer.Serialize(exitStatus, AcpJsonContext.Default.TerminalExitStatus);
        Assert.Equal(
            uint.MaxValue,
            JsonSerializer.Deserialize(exitStatusJson, AcpJsonContext.Default.TerminalExitStatus)!.ExitCode);

        var location = new ToolCallLocation("/remote/repo/file.cs", uint.MaxValue);
        var locationJson = JsonSerializer.Serialize(location, AcpJsonContext.Default.ToolCallLocation);
        Assert.Equal(
            uint.MaxValue,
            JsonSerializer.Deserialize(locationJson, AcpJsonContext.Default.ToolCallLocation)!.Line);
    }

    private static ConfigOption CreateGroupedConfigOption()
        => new()
        {
            Id = "mode",
            Name = "Mode",
            Type = "select",
            CurrentValue = "code",
            Meta = CreateMeta("config"),
            OptionGroups =
            [
                new ConfigOptionGroup
                {
                    Group = "workflow",
                    Name = "Workflow",
                    Meta = CreateMeta("config-group"),
                    Options =
                    [
                        new ConfigOptionValue
                        {
                            Value = "code",
                            Name = "Code",
                            Meta = CreateMeta("config-value")
                        }
                    ]
                }
            ]
        };

    private static T RoundTripWithMeta<T>(
        T value,
        JsonTypeInfo<T> typeInfo,
        string expectedMarker)
        where T : AcpProtocolObject
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        var parsed = JsonSerializer.Deserialize(json, typeInfo);
        Assert.NotNull(parsed);

        var replay = JsonSerializer.Serialize(parsed, typeInfo);
        using var document = JsonDocument.Parse(replay);
        Assert.Equal(
            expectedMarker,
            document.RootElement.GetProperty("_meta").GetProperty("marker").GetString());
        return parsed!;
    }

    private static Dictionary<string, object?> CreateMeta(string marker)
        => new(StringComparer.Ordinal)
        {
            ["marker"] = marker
        };

    private static void AssertMeta(AcpProtocolObject value, string expectedMarker)
    {
        Assert.NotNull(value.Meta);
        Assert.True(value.Meta!.TryGetValue("marker", out var marker));
        Assert.Equal(expectedMarker, marker switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null
        });
    }

    private static void AssertPropertySet(JsonElement element, params string[] expectedNames)
    {
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        var actual = element.EnumerateObject().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            expected.SetEquals(actual),
            $"Expected properties [{string.Join(", ", expected.Order())}], actual [{string.Join(", ", actual.Order())}].");
    }
}
