using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

/// <summary>
/// Locks the <c>$/cancel_request</c> payload against the ACP <c>CancelRequestNotification</c>
/// schema, whose only required field is a <c>RequestId</c> — itself a union of <c>null</c>, a
/// number, and a string. JSON-RPC 2.0 requires the responder to echo the same id back, so every
/// form has to survive the round trip unchanged.
/// </summary>
public sealed class CancelRequestTypesTests
{
    [Fact]
    public void NumericRequestId_RoundTripsAsANumber()
    {
        var json = Serialize(new CancelRequestParams(AcpRequestId.FromNumber(42)));

        using var document = JsonDocument.Parse(json);
        var requestId = document.RootElement.GetProperty("requestId");
        Assert.Equal(JsonValueKind.Number, requestId.ValueKind);
        Assert.Equal(42, requestId.GetInt64());

        var roundTripped = Deserialize(json);
        Assert.Equal(AcpRequestIdKind.Number, roundTripped.RequestId.Kind);
        Assert.True(roundTripped.RequestId.TryGetNumber(out var value));
        Assert.Equal(42, value);
        Assert.Equal(AcpRequestId.FromNumber(42), roundTripped.RequestId);
    }

    [Fact]
    public void StringRequestId_RoundTripsAsAStringAndIsNotConfusedWithANumber()
    {
        // A string id is legal, and an agent that issues "7" must get "7" back — not 7.
        var json = Serialize(new CancelRequestParams(AcpRequestId.FromString("7")));

        using var document = JsonDocument.Parse(json);
        var requestId = document.RootElement.GetProperty("requestId");
        Assert.Equal(JsonValueKind.String, requestId.ValueKind);
        Assert.Equal("7", requestId.GetString());

        var roundTripped = Deserialize(json);
        Assert.Equal(AcpRequestIdKind.String, roundTripped.RequestId.Kind);
        Assert.True(roundTripped.RequestId.TryGetString(out var text));
        Assert.Equal("7", text);
        Assert.NotEqual(AcpRequestId.FromNumber(7), roundTripped.RequestId);
        Assert.False(roundTripped.RequestId.TryGetNumber(out _));
    }

    [Fact]
    public void NullRequestId_RoundTripsAsExplicitNull()
    {
        var json = Serialize(new CancelRequestParams(AcpRequestId.Null));

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("requestId", out var requestId));
        Assert.Equal(JsonValueKind.Null, requestId.ValueKind);

        Assert.Equal(AcpRequestIdKind.Null, Deserialize(json).RequestId.Kind);
    }

    [Fact]
    public void NumericRequestId_BeyondInt64_KeepsItsRawTokenSoTheEchoStillCorrelates()
    {
        // Re-encoding through a CLR numeric type would rewrite the token; the peer would then be
        // unable to match our cancellation against the request it issued.
        const string Raw = "123456789012345678901234567890";
        var roundTripped = Deserialize($"{{\"requestId\":{Raw}}}");

        Assert.Equal(AcpRequestIdKind.Number, roundTripped.RequestId.Kind);
        Assert.False(roundTripped.RequestId.TryGetNumber(out _));

        using var document = JsonDocument.Parse(Serialize(roundTripped));
        Assert.Equal(Raw, document.RootElement.GetProperty("requestId").GetRawText());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[1]")]
    public void NonUnionRequestId_RemainsATypeError(string wire)
    {
        // Protocol looseness is not extended past what the specification allows: JSON-RPC 2.0
        // permits exactly null, a number, or a string as an id.
        Assert.ThrowsAny<JsonException>(() => Deserialize($"{{\"requestId\":{wire}}}"));
    }

    [Fact]
    public void Meta_RoundTripsAlongsideTheRequestId()
    {
        var json = Serialize(new CancelRequestParams(AcpRequestId.FromNumber(9))
        {
            Meta = new Dictionary<string, object?> { ["traceparent"] = "00-abc-def-01" }
        });

        var roundTripped = Deserialize(json);
        Assert.NotNull(roundTripped.Meta);
        Assert.True(roundTripped.Meta!.TryGetValue("traceparent", out var traceparent));
        Assert.Equal("00-abc-def-01", Assert.IsType<JsonElement>(traceparent).GetString());
        Assert.Equal(AcpRequestId.FromNumber(9), roundTripped.RequestId);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData((int)2)]
    [InlineData((short)3)]
    public void TryFromEnvelopeId_ProjectsLocallyIssuedIntegerIdsOntoTheNumberForm(object envelopeId)
    {
        // Locally issued ids are CLR integers while parsed ones are JsonElement; both must land on
        // the same wire form or the echoed id would not correlate.
        Assert.True(AcpRequestId.TryFromEnvelopeId(envelopeId, out var requestId));
        Assert.Equal(AcpRequestIdKind.Number, requestId.Kind);
        Assert.True(requestId.TryGetNumber(out var value));
        Assert.Equal(Convert.ToInt64(envelopeId), value);
    }

    [Fact]
    public void TryFromEnvelopeId_ProjectsParsedElementsOntoTheSameFormsAsLocalValues()
    {
        Assert.True(AcpRequestId.TryFromEnvelopeId(Element("11"), out var number));
        Assert.Equal(AcpRequestId.FromNumber(11), number);

        Assert.True(AcpRequestId.TryFromEnvelopeId(Element("\"abc\""), out var text));
        Assert.Equal(AcpRequestId.FromString("abc"), text);

        Assert.True(AcpRequestId.TryFromEnvelopeId(Element("null"), out var missing));
        Assert.Equal(AcpRequestId.Null, missing);

        Assert.True(AcpRequestId.TryFromEnvelopeId(null, out var nullReference));
        Assert.Equal(AcpRequestId.Null, nullReference);
    }

    [Fact]
    public void TryFromEnvelopeId_RejectsFormsJsonRpcDoesNotAllow()
    {
        Assert.False(AcpRequestId.TryFromEnvelopeId(true, out _));
        Assert.False(AcpRequestId.TryFromEnvelopeId(Element("true"), out _));
        Assert.False(AcpRequestId.TryFromEnvelopeId(Element("{}"), out _));
    }

    [Fact]
    public void Method_IsTheProtocolLevelNotificationName()
    {
        // The '$/' prefix is what marks the notification implementation dependent, so the receiver
        // is free to ignore it; losing the prefix would turn it into an ordinary ACP method.
        Assert.Equal("$/cancel_request", CancelRequestParams.Method);
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Serialize(CancelRequestParams @params)
        => JsonSerializer.Serialize(@params, AcpJsonContext.Default.CancelRequestParams);

    private static CancelRequestParams Deserialize(string json)
        => JsonSerializer.Deserialize(json, AcpJsonContext.Default.CancelRequestParams)!;
}
