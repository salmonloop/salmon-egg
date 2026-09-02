using System.Collections.Generic;
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class ElicitationTypesTests
{
    [Fact]
    public void FormRequest_RoundTripsSchemaCoveringEveryPrimitivePropertyType()
    {
        const string Json = """
        {
          "sessionId": "sess_abc123",
          "mode": "form",
          "message": "How should I approach this refactoring?",
          "requestedSchema": {
            "type": "object",
            "title": "Refactoring plan",
            "description": "Choose how to proceed",
            "properties": {
              "strategy": {
                "type": "string",
                "title": "Strategy",
                "enum": ["conservative", "balanced", "aggressive"],
                "default": "balanced"
              },
              "contact": {
                "type": "string",
                "format": "email",
                "minLength": 3,
                "maxLength": 254,
                "pattern": "^.+@.+$"
              },
              "reviewer": {
                "type": "string",
                "oneOf": [
                  { "const": "alice", "title": "Alice", "description": "Owns the module" },
                  { "const": "bob", "title": "Bob" }
                ]
              },
              "batchSize": { "type": "integer", "minimum": 1, "maximum": 500, "default": 20 },
              "threshold": { "type": "number", "minimum": 0.5, "maximum": 9.75, "default": 1.25 },
              "dryRun": { "type": "boolean", "default": true },
              "targets": {
                "type": "array",
                "items": { "type": "string", "enum": ["api", "ui", "docs"] },
                "minItems": 1,
                "maxItems": 3,
                "default": ["api"]
              },
              "owners": {
                "type": "array",
                "items": {
                  "anyOf": [
                    { "const": "core", "title": "Core team" },
                    { "const": "infra", "title": "Infra team" }
                  ]
                }
              }
            },
            "required": ["strategy"]
          }
        }
        """;

        var request = JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest);

        var form = Assert.IsType<FormElicitationRequest>(request);
        Assert.Equal("sess_abc123", form.Scope.SessionId);
        Assert.Null(form.Scope.ToolCallId);
        Assert.True(form.Scope.IsSessionScoped);
        Assert.Equal("How should I approach this refactoring?", form.Message);
        Assert.Equal(ElicitationModes.Form, form.Mode);
        Assert.Equal(["strategy"], form.RequestedSchema.Required);
        Assert.Equal("Refactoring plan", form.RequestedSchema.Title);

        var strategy = Assert.IsType<StringPropertySchema>(form.RequestedSchema.Properties["strategy"]);
        Assert.Equal(["conservative", "balanced", "aggressive"], strategy.Enum);
        Assert.Equal("balanced", strategy.Default);
        Assert.Null(strategy.OneOf);

        var contact = Assert.IsType<StringPropertySchema>(form.RequestedSchema.Properties["contact"]);
        Assert.Equal(StringFormat.Email, contact.Format);
        Assert.Equal(3u, contact.MinLength);
        Assert.Equal(254u, contact.MaxLength);
        Assert.Equal("^.+@.+$", contact.Pattern);

        var reviewer = Assert.IsType<StringPropertySchema>(form.RequestedSchema.Properties["reviewer"]);
        Assert.Equal(2, reviewer.OneOf!.Count);
        Assert.Equal("alice", reviewer.OneOf[0].Const);
        Assert.Equal("Owns the module", reviewer.OneOf[0].Description);
        Assert.Null(reviewer.OneOf[1].Description);

        var batchSize = Assert.IsType<IntegerPropertySchema>(form.RequestedSchema.Properties["batchSize"]);
        Assert.Equal(1L, batchSize.Minimum);
        Assert.Equal(500L, batchSize.Maximum);
        Assert.Equal(20L, batchSize.Default);

        var threshold = Assert.IsType<NumberPropertySchema>(form.RequestedSchema.Properties["threshold"]);
        Assert.Equal(1.25d, threshold.Default);

        var dryRun = Assert.IsType<BooleanPropertySchema>(form.RequestedSchema.Properties["dryRun"]);
        Assert.True(dryRun.Default);

        var targets = Assert.IsType<MultiSelectPropertySchema>(form.RequestedSchema.Properties["targets"]);
        Assert.Equal(["api", "ui", "docs"], Assert.IsType<StringMultiSelectItems>(targets.Items).Enum);
        Assert.Equal(1u, targets.MinItems);
        Assert.Equal(3u, targets.MaxItems);
        Assert.Equal(["api"], targets.Default);

        var owners = Assert.IsType<MultiSelectPropertySchema>(form.RequestedSchema.Properties["owners"]);
        var titled = Assert.IsType<TitledMultiSelectItems>(owners.Items);
        Assert.Equal(["core", "infra"], titled.AnyOf.Select(static option => option.Const));

        // A parse -> write -> parse -> write cycle must be a fixed point: any field the converter drops or
        // reshapes would make the second write differ, which is what breaks replaying a stored request.
        // Record equality cannot express this because the DTOs hold dictionaries and lists, which records
        // compare by reference.
        var written = JsonSerializer.Serialize(form, AcpJsonContext.Default.CreateElicitationRequest);
        var rewritten = JsonSerializer.Serialize(
            JsonSerializer.Deserialize(written, AcpJsonContext.Default.CreateElicitationRequest),
            AcpJsonContext.Default.CreateElicitationRequest);
        Assert.Equal(written, rewritten);

        // The fixed point above would also hold if the converter dropped a constraint on both passes, so
        // the constraints most at risk of being silently lost are re-read from the written form. They are
        // checked as parsed values rather than as substrings because the writer escapes characters such as
        // '+' to \u002B, which a text match would miss.
        var reparsed = Assert.IsType<FormElicitationRequest>(
            JsonSerializer.Deserialize(written, AcpJsonContext.Default.CreateElicitationRequest));
        var reparsedContact = Assert.IsType<StringPropertySchema>(reparsed.RequestedSchema.Properties["contact"]);
        Assert.Equal("^.+@.+$", reparsedContact.Pattern);
        Assert.Equal(StringFormat.Email, reparsedContact.Format);
        Assert.Equal(254u, reparsedContact.MaxLength);
        Assert.Equal(
            1u,
            Assert.IsType<MultiSelectPropertySchema>(reparsed.RequestedSchema.Properties["targets"]).MinItems);
        Assert.Equal(
            ["core", "infra"],
            Assert.IsType<TitledMultiSelectItems>(
                    Assert.IsType<MultiSelectPropertySchema>(reparsed.RequestedSchema.Properties["owners"]).Items)
                .AnyOf.Select(static option => option.Const));
        Assert.Equal(["strategy"], reparsed.RequestedSchema.Required);
        Assert.Equal(
            20L,
            Assert.IsType<IntegerPropertySchema>(reparsed.RequestedSchema.Properties["batchSize"]).Default);
    }

    [Fact]
    public void UrlRequest_RoundTripsRequestScopeAndKeepsRequestIdTokenShape()
    {
        const string Json = """
        {
          "requestId": 12,
          "mode": "url",
          "elicitationId": "github-oauth-001",
          "url": "https://agent.example.com/connect?elicitationId=github-oauth-001",
          "message": "Please authorize access to your repositories."
        }
        """;

        var request = JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest);

        var url = Assert.IsType<UrlElicitationRequest>(request);
        Assert.Equal("github-oauth-001", url.ElicitationId);
        Assert.Equal("https://agent.example.com/connect?elicitationId=github-oauth-001", url.Url);
        Assert.False(url.Scope.IsSessionScoped);
        Assert.Null(url.Scope.SessionId);
        Assert.Equal("12", url.Scope.RequestId!.Value.GetRawText());

        var written = JsonSerializer.Serialize(url, AcpJsonContext.Default.CreateElicitationRequest);

        // requestId must stay a JSON number: coercing it to a string would break the agent's correlation.
        Assert.Contains("\"requestId\":12", written);
        Assert.DoesNotContain("\"requestId\":\"12\"", written);
    }

    [Fact]
    public void SessionScope_PreservesToolCallId()
    {
        const string Json = """
        {
          "sessionId": "sess_1",
          "toolCallId": "call_9",
          "mode": "form",
          "message": "Confirm the arguments",
          "requestedSchema": { "type": "object", "properties": {} }
        }
        """;

        var form = Assert.IsType<FormElicitationRequest>(
            JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest));

        Assert.Equal("sess_1", form.Scope.SessionId);
        Assert.Equal("call_9", form.Scope.ToolCallId);
        Assert.Empty(form.RequestedSchema.Properties);
        Assert.Contains(
            "\"toolCallId\":\"call_9\"",
            JsonSerializer.Serialize(form, AcpJsonContext.Default.CreateElicitationRequest));
    }

    [Theory]
    [InlineData("_vendorWizard")]
    [InlineData("futureMode")]
    public void UnknownMode_IsNotRenderedAsAKnownModeAndReplaysPayloadVerbatim(string mode)
    {
        var json = $$$"""
        {"sessionId":"sess_1","mode":"{{{mode}}}","message":"Do the thing","vendorField":{"nested":[1,2,{"deep":true}]},"unknownScalar":1.2300e+02,"_meta":{"trace":"abc"}}
        """.Trim();

        var request = JsonSerializer.Deserialize(json, AcpJsonContext.Default.CreateElicitationRequest);

        var custom = Assert.IsType<CustomElicitationRequest>(request);
        Assert.IsNotType<FormElicitationRequest>(request);
        Assert.IsNotType<UrlElicitationRequest>(request);
        Assert.Equal(mode, custom.RawMode);
        Assert.Equal(mode, custom.Mode);
        Assert.Equal("sess_1", custom.Scope.SessionId);
        Assert.Equal("Do the thing", custom.Message);

        // Byte-for-byte replay, including the unknown vendor field and the numeric token's original form:
        // the spec requires the raw payload to survive storing, replaying, proxying, and forwarding.
        Assert.Equal(
            json,
            JsonSerializer.Serialize(custom, AcpJsonContext.Default.CreateElicitationRequest));
    }

    [Fact]
    public void OmittedMode_IsNotPromotedToFormDefault()
    {
        // ACP requires mode explicitly and deliberately does not apply MCP's omitted-mode form default,
        // so an absent mode must not be rendered as a form.
        const string Json = """
        {"sessionId":"sess_1","message":"No mode here","requestedSchema":{"type":"object","properties":{}}}
        """;

        var request = JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest);

        var custom = Assert.IsType<CustomElicitationRequest>(request);
        Assert.Equal(string.Empty, custom.RawMode);
    }

    [Fact]
    public void UnknownPropertySchemaType_PreservesRawSchemaWithoutRenderingAKnownControl()
    {
        const string Json = """
        {"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"widget":{"type":"_vendorSlider","min":1,"max":9,"unknown":{"a":[true,null]}},"plain":{"type":"string"}}}}
        """;

        var form = Assert.IsType<FormElicitationRequest>(
            JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest));

        var widget = Assert.IsType<CustomPropertySchema>(form.RequestedSchema.Properties["widget"]);
        Assert.Equal("_vendorSlider", widget.SchemaType);
        Assert.IsType<StringPropertySchema>(form.RequestedSchema.Properties["plain"]);

        var written = JsonSerializer.Serialize(form, AcpJsonContext.Default.CreateElicitationRequest);
        Assert.Contains(
            """{"type":"_vendorSlider","min":1,"max":9,"unknown":{"a":[true,null]}}""",
            written);
    }

    [Fact]
    public void UnknownMultiSelectItemsType_PreservesRawItems()
    {
        const string Json = """
        {"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"picks":{"type":"array","items":{"type":"_vendorTree","roots":["a"]}}}}}
        """;

        var form = Assert.IsType<FormElicitationRequest>(
            JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest));

        var picks = Assert.IsType<MultiSelectPropertySchema>(form.RequestedSchema.Properties["picks"]);
        var items = Assert.IsType<CustomMultiSelectItems>(picks.Items);
        Assert.Equal("_vendorTree", items.ItemsType);
        Assert.Contains(
            """
            "items":{"type":"_vendorTree","roots":["a"]}
            """.Trim(),
            JsonSerializer.Serialize(form, AcpJsonContext.Default.CreateElicitationRequest));
    }

    [Fact]
    public void UnknownStringFormat_RoundTripsInsteadOfBeingRejected()
    {
        const string Json = """
        {"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"f":{"type":"string","format":"_vendorColor"}}}}
        """;

        var form = Assert.IsType<FormElicitationRequest>(
            JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest));

        var field = Assert.IsType<StringPropertySchema>(form.RequestedSchema.Properties["f"]);
        Assert.Equal("_vendorColor", field.Format!.Value.Value);
        Assert.Contains(
            """
            "format":"_vendorColor"
            """.Trim(),
            JsonSerializer.Serialize(form, AcpJsonContext.Default.CreateElicitationRequest));
    }

    [Fact]
    public void OptionalFieldsOmittedOrNull_ReadBackAsNotProvided()
    {
        const string Json = """
        {"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"string","default":null,"enum":null,"format":null,"maxLength":null,"pattern":null,"title":null,"description":null},"b":{"type":"integer"}},"required":null,"title":null}}
        """;

        var form = Assert.IsType<FormElicitationRequest>(
            JsonSerializer.Deserialize(Json, AcpJsonContext.Default.CreateElicitationRequest));

        var a = Assert.IsType<StringPropertySchema>(form.RequestedSchema.Properties["a"]);
        Assert.Null(a.Default);
        Assert.Null(a.Enum);
        Assert.Null(a.Format);
        Assert.Null(a.MaxLength);
        Assert.Null(a.Pattern);
        Assert.Null(a.Title);
        Assert.Null(a.Description);
        Assert.Null(form.RequestedSchema.Required);
        Assert.Null(form.RequestedSchema.Title);

        var b = Assert.IsType<IntegerPropertySchema>(form.RequestedSchema.Properties["b"]);
        Assert.Null(b.Minimum);
        Assert.Null(b.Maximum);
    }

    [Theory]
    // Optional-but-present fields with the wrong JSON type still throw: protocol looseness covers absent
    // fields and unknown discriminators, not broken type contracts.
    [InlineData("""{"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"string","enum":"notAnArray"}}}}""")]
    [InlineData("""{"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"string","maxLength":-1}}}}""")]
    [InlineData("""{"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"integer","minimum":"low"}}}}""")]
    [InlineData("""{"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"boolean","default":"yes"}}}}""")]
    [InlineData("""{"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"array"}}}}""")]
    [InlineData("""{"sessionId":"s","mode":"form","message":"m","requestedSchema":{"type":"object","properties":{"a":{"type":"array","items":{"type":"string"}}}}}""")]
    [InlineData("""{"sessionId":"s","mode":"form","message":"m"}""")]
    [InlineData("""{"sessionId":"s","mode":"url","elicitationId":"e"}""")]
    [InlineData("""{"sessionId":"s","mode":"form","requestedSchema":{"type":"object","properties":{}}}""")]
    public void MalformedRequest_StillThrows(string json)
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(json, AcpJsonContext.Default.CreateElicitationRequest));
    }
}
