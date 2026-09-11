using System;
using System.Linq;
using System.Text.Json;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using Xunit;

namespace SalmonEgg.Acp.Tests.JsonRpc;

public sealed class JsonRpcBatchProperties
{
    [Fact]
    public void BatchRoundTrip_PreservesResponseIdsAndResultValues()
        => FsCheckPropertyRunner.Run(this, nameof(BatchRoundTripProperty));

    private void BatchRoundTripProperty(int numericId, string textId, int[] values)
    {
        // Arrange
        var parser = new MessageParser();
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(values ?? []));
        JsonRpcResponse[] responses =
        [
            new(numericId, value.RootElement.Clone()),
            new(textId ?? string.Empty, value.RootElement.Clone()),
            new(null, JsonRpcError.CreateInvalidRequest("invalid item"))
        ];

        // Act
        var frame = parser.ParseFrame(parser.SerializeResponses(responses), AcpProtocolVersion.V2);

        // Assert
        Assert.True(frame.IsBatch);
        Assert.True(frame.IsResponseBatch);
        Assert.Equal(responses.Length, frame.Items.Count);
        for (var index = 0; index < responses.Length; index++)
        {
            var actual = Assert.IsType<JsonRpcResponse>(frame.Items[index].Message);
            Assert.True(AcpRequestId.TryFromEnvelopeId(responses[index].Id, out var expectedId));
            Assert.True(AcpRequestId.TryFromEnvelopeId(actual.Id, out var actualId));
            Assert.Equal(expectedId, actualId);
            Assert.Equal(responses[index].Result?.GetRawText(), actual.Result?.GetRawText());
            Assert.Equal(responses[index].Error?.Code, actual.Error?.Code);
        }
    }
}
