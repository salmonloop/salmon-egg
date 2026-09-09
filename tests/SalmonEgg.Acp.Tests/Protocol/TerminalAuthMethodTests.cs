using System.Text.Json;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class TerminalAuthMethodTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TerminalMethod_InvocationFields_RoundTripsAndEditsOwnWire(int version)
    {
        // Arrange
        var id = version == 1 ? "id" : "methodId";
        var env = version == 1 ? """{"LOGIN":"yes"}""" : """[{"name":"LOGIN","value":"yes","future":1.20e+02,"_meta":{"source":"auth"}}]""";
        var json = $$$"""{"{{{id}}}":"login","name":"Sign in","type":"terminal","args":["login","--interactive"],"env":{{{env}}},"future":{"keep":true}}""";

        // Act
        var method = JsonSerializer.Deserialize(json, Wire.Of<AuthMethodDefinition>(version))!;
        var changed = method with { Args = ["signin"], Env = new Dictionary<string, string> { ["LOGIN"] = "new" } };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(changed, Wire.Of<AuthMethodDefinition>(version)));

        // Assert
        Assert.Equal(new[] { "login", "--interactive" }, method.Args);
        Assert.Equal("yes", method.Env!["LOGIN"]);
        Assert.False(method.SupportsAuthenticateRequest);
        Assert.Equal("signin", document.RootElement.GetProperty("args")[0].GetString());
        Assert.True(document.RootElement.GetProperty("future").GetProperty("keep").GetBoolean());
        var variables = document.RootElement.GetProperty("env");
        Assert.Equal("new", version == 1 ? variables.GetProperty("LOGIN").GetString() : variables[0].GetProperty("value").GetString());
        if (version == 2)
        {
            Assert.Equal("1.20e+02", variables[0].GetProperty("future").GetRawText());
            Assert.Equal("auth", variables[0].GetProperty("_meta").GetProperty("source").GetString());
        }
    }

    [Theory]
    [InlineData(1, "null")]
    [InlineData(1, "42")]
    [InlineData(2, "{}")]
    [InlineData(2, "false")]
    public void TerminalMethod_MalformedArguments_DefaultsField(int version, string args)
    {
        // Arrange / Act
        var id = version == 1 ? "id" : "methodId";
        var method = JsonSerializer.Deserialize($$"""{"{{id}}":"login","name":"Login","type":"terminal","args":{{args}}}""", Wire.Of<AuthMethodDefinition>(version))!;

        // Assert
        Assert.Empty(method.Args!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TerminalMethod_MalformedArgumentItems_SkipsOnlyInvalid(int version)
    {
        // Arrange / Act
        var id = version == 1 ? "id" : "methodId";
        var method = JsonSerializer.Deserialize($$"""{"{{id}}":"login","name":"Login","type":"terminal","args":["login",42,null,{},"--device"]}""", Wire.Of<AuthMethodDefinition>(version))!;

        // Assert
        Assert.Equal(new[] { "login", "--device" }, method.Args);
    }

    [Fact]
    public void TerminalMethodV1_MalformedEnvironmentValue_DefaultsWholeField()
    {
        // Arrange / Act
        var method = JsonSerializer.Deserialize("""{"id":"login","name":"Login","type":"terminal","env":{"KEEP":"one","bad":42}}""", Wire.Of<AuthMethodDefinition>(1))!;

        // Assert
        Assert.Empty(method.Env!);
    }

    [Fact]
    public void TerminalMethodV2_MalformedEnvironmentItems_SkipsInvalidAndEmitsUniqueNames()
    {
        // Arrange / Act
        var method = JsonSerializer.Deserialize("""{"methodId":"login","name":"Login","type":"terminal","env":[{"name":"KEEP","value":"one"},42,{}, {"name":"bad","value":42},{"name":"KEEP","value":"two"}]}""", Wire.Of<AuthMethodDefinition>(2))!;

        // Assert
        Assert.Equal("one", Assert.Single(method.Env!).Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TerminalMethod_AbsentInvocationFields_StaysAbsent(int version)
    {
        // Arrange / Act
        var id = version == 1 ? "id" : "methodId";
        var method = JsonSerializer.Deserialize($$"""{"{{id}}":"login","name":"Login","type":"terminal"}""", Wire.Of<AuthMethodDefinition>(version))!;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(method, Wire.Of<AuthMethodDefinition>(version)));

        // Assert
        Assert.Null(method.Args);
        Assert.Null(method.Env);
        Assert.False(document.RootElement.TryGetProperty("args", out _));
        Assert.False(document.RootElement.TryGetProperty("env", out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnknownMethod_InvocationLikeFields_RemainUninterpreted(int version)
    {
        // Arrange
        var id = version == 1 ? "id" : "methodId";
        const string raw = """{"token":1.20e+02}""";
        var json = $$"""{"{{id}}":"future","name":"Future","type":"_vendor","args":{{raw}},"env":{{raw}}}""";

        // Act
        var method = JsonSerializer.Deserialize(json, Wire.Of<AuthMethodDefinition>(version))!;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(method, Wire.Of<AuthMethodDefinition>(version)));

        // Assert
        Assert.Null(method.Args);
        Assert.Null(method.Env);
        Assert.Equal(raw, document.RootElement.GetProperty("args").GetRawText());
        Assert.Equal(raw, document.RootElement.GetProperty("env").GetRawText());
    }
}
