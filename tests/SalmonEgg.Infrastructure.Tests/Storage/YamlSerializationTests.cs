using System;
using System.IO;
using Xunit;
using YamlDotNet.Core;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Infrastructure.Storage.YamlModels;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public class YamlSerializationTests
{
    [Fact]
    public void Deserializer_WithCustomTag_ThrowsYamlException()
    {
        var yaml = "some_object: !type:System.Diagnostics.Process { start_info: { file_name: calc } }";
        var deserializer = YamlSerialization.CreateDeserializer();

        var ex = Assert.Throws<YamlException>(() => deserializer.Deserialize<object>(yaml));
        Assert.Contains("Insecure deserialization blocked: Unrecognized tag '!type:System.Diagnostics.Process'", ex.Message);
    }

    [Fact]
    public void Deserializer_WithStandardTag_Succeeds()
    {
        var yaml = "some_object: !!str test";
        var deserializer = YamlSerialization.CreateDeserializer();

        var result = deserializer.Deserialize<dynamic>(yaml);
        Assert.NotNull(result);
    }

    [Fact]
    public void Deserializer_ServerConfigurationYamlV1_DoesNotRegress()
    {
        var yaml = @"schema_version: 1
id: test-id
name: test-server
transport: stdio
stdio_command: my_cmd
heartbeat_interval_seconds: 60";
        var deserializer = YamlSerialization.CreateDeserializer();

        var result = deserializer.Deserialize<ServerConfigurationYamlV1>(yaml);

        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("test-server", result.Name);
        Assert.Equal("stdio", result.Transport);
        Assert.Equal("my_cmd", result.StdioCommand);
        Assert.Equal(60, result.HeartbeatIntervalSeconds);
    }

    [Fact]
    public void Deserializer_AppSettingsYamlV1_DoesNotRegress()
    {
        var yaml = @"schema_version: 1
theme: Dark
is_animation_enabled: false
launch_on_startup: true";
        var deserializer = YamlSerialization.CreateDeserializer();

        var result = deserializer.Deserialize<AppSettingsYamlV1>(yaml);

        Assert.NotNull(result);
        Assert.Equal("Dark", result.Theme);
        Assert.False(result.IsAnimationEnabled);
        Assert.True(result.LaunchOnStartup);
    }
}
