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
    public void Deserializer_ServerConfigurationYaml_DoesNotRegress()
    {
        var yaml = @"schema_version: 2
id: test-id
name: test-server
transport: stdio
stdio_command: my_cmd
stdio_arguments:
- --serve
- --mode
- plan
connection_timeout_seconds: 15";
        var deserializer = YamlSerialization.CreateDeserializer();

        var result = deserializer.Deserialize<ServerConfigurationYaml>(yaml);

        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("test-server", result.Name);
        Assert.Equal("stdio", result.Transport);
        Assert.Equal("my_cmd", result.StdioCommand);
        Assert.Equal(["--serve", "--mode", "plan"], result.StdioArguments);
        Assert.Equal(15, result.ConnectionTimeoutSeconds);
        Assert.Null(typeof(ServerConfigurationYaml).GetProperty("McpServers"));
    }

    [Fact]
    public void Deserializer_McpSettingsYamlV1_DoesNotRegress()
    {
        var yaml = @"schema_version: 1
servers:
- transport: stdio
  name: filesystem
  enabled: false
  command: /usr/bin/mcp-filesystem";
        var deserializer = YamlSerialization.CreateDeserializer();

        var result = deserializer.Deserialize<McpSettingsYamlV1>(yaml);

        Assert.NotNull(result);
        var server = Assert.Single(result.Servers);
        Assert.Equal("stdio", server.Transport);
        Assert.Equal("filesystem", server.Name);
        Assert.False(server.Enabled);
        Assert.Equal("/usr/bin/mcp-filesystem", server.Command);
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
