using SalmonEgg.Infrastructure.Observability;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public sealed class OtlpEndpointResolverTests
{
    [Theory]
    [InlineData(OtlpSignal.Traces, "http://localhost:4318/v1/traces")]
    [InlineData(OtlpSignal.Metrics, "http://localhost:4318/v1/metrics")]
    [InlineData(OtlpSignal.Logs, "http://localhost:4318/v1/logs")]
    public void Resolve_HttpBaseEndpoint_AppendsSignalPath(OtlpSignal signal, string expected)
    {
        var actual = OtlpEndpointResolver.Resolve(
            "http://localhost:4318",
            OtlpProtocol.HttpProtobuf,
            signal);

        Assert.Equal(expected, actual.AbsoluteUri);
    }

    [Fact]
    public void Resolve_HttpEndpointWithPrefix_PreservesPrefix()
    {
        var actual = OtlpEndpointResolver.Resolve(
            "https://collector.example.com/tenant/otel/",
            OtlpProtocol.HttpProtobuf,
            OtlpSignal.Logs);

        Assert.Equal("https://collector.example.com/tenant/otel/v1/logs", actual.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://collector.example.com/v1/traces")]
    [InlineData("https://collector.example.com/v1/traces/")]
    [InlineData("https://collector.example.com/v1/metrics")]
    [InlineData("https://collector.example.com/v1/metrics/")]
    [InlineData("https://collector.example.com/v1/logs")]
    [InlineData("https://collector.example.com/v1/logs/")]
    public void Resolve_HttpEndpointWithKnownSignalPath_ReplacesPath(string endpoint)
    {
        var actual = OtlpEndpointResolver.Resolve(
            endpoint,
            OtlpProtocol.HttpProtobuf,
            OtlpSignal.Metrics);

        Assert.Equal("https://collector.example.com/v1/metrics", actual.AbsoluteUri);
    }

    [Fact]
    public void Resolve_GrpcEndpoint_DoesNotAppendSignalPath()
    {
        var actual = OtlpEndpointResolver.Resolve(
            "https://collector.example.com:4317/custom",
            OtlpProtocol.Grpc,
            OtlpSignal.Traces);

        Assert.Equal("https://collector.example.com:4317/custom", actual.AbsoluteUri);
    }
}
