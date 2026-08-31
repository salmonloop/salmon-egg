using OpenTelemetry.Exporter;
using SalmonEgg.Infrastructure.Observability;

namespace SalmonEgg.Infrastructure.Tests.Observability;

public sealed class OtlpExporterOptionsConfiguratorTests
{
    [Fact]
    public void Apply_HttpProtobuf_ResolvesSignalPathAndCopiesHeaders()
    {
        var options = new OtlpExporterOptions();
        var settings = CreateSettings(
            endpoint: "https://collector.example.com/otel",
            protocol: OtlpProtocol.HttpProtobuf,
            headers: "api-key=manual-key,tenant=test");

        OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Logs,
            grpcSupported: true);

        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
        Assert.Equal("https://collector.example.com/otel/v1/logs", options.Endpoint.AbsoluteUri);
        Assert.Equal("api-key=manual-key,tenant=test", options.Headers);
    }

    [Fact]
    public void Apply_GrpcSupported_PreservesBaseEndpoint()
    {
        var options = new OtlpExporterOptions();
        var settings = CreateSettings(
            endpoint: "https://collector.example.com:4317/custom",
            protocol: OtlpProtocol.Grpc);

        OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Traces,
            grpcSupported: true);

        Assert.Equal(OtlpExportProtocol.Grpc, options.Protocol);
        Assert.Equal("https://collector.example.com:4317/custom", options.Endpoint.AbsoluteUri);
    }

    [Fact]
    public void Apply_GrpcUnsupported_FallsBackToHttpAndResolvesSignalPath()
    {
        var options = new OtlpExporterOptions();
        var settings = CreateSettings(
            endpoint: "https://collector.example.com:4318",
            protocol: OtlpProtocol.Grpc);

        OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Metrics,
            grpcSupported: false);

        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
        Assert.Equal("https://collector.example.com:4318/v1/metrics", options.Endpoint.AbsoluteUri);
    }

    [Fact]
    public void Apply_BlankHeaders_DoesNotOverrideExistingOptions()
    {
        // OtlpExporterOptions reads OTEL_EXPORTER_OTLP_HEADERS in its constructor. Seed an explicit
        // value so this test remains deterministic even when the developer or CI environment sets it.
        var options = new OtlpExporterOptions { Headers = "existing=value" };
        var settings = CreateSettings(
            endpoint: "https://collector.example.com:4318",
            protocol: OtlpProtocol.HttpProtobuf,
            headers: "   ");

        OtlpExporterOptionsConfigurator.Apply(
            options,
            settings,
            OtlpSignal.Traces,
            grpcSupported: true);

        Assert.Equal("existing=value", options.Headers);
    }

    private static TelemetrySettings CreateSettings(
        string endpoint,
        OtlpProtocol protocol,
        string? headers = null)
        => new()
        {
            Enabled = true,
            OtlpEndpoint = endpoint,
            Protocol = protocol,
            OtlpHeaders = headers
        };
}
