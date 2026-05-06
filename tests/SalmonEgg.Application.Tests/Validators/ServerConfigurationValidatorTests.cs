using FluentValidation.TestHelper;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Application.Tests.Validators;

public sealed class ServerConfigurationValidatorTests
{
    private readonly ServerConfigurationValidator _validator = new();

    [Fact]
    public void Validate_WhenValidConfiguration_ShouldNotHaveAnyErrors()
    {
        var configuration = new ServerConfiguration
        {
            Id = "ssh-bridge",
            Name = "SSH Bridge",
            Transport = TransportType.Stdio,
            StdioCommand = "ssh",
            ConnectionTimeout = 10
        };

        var result = _validator.TestValidate(configuration);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Id = "" };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Configuration ID cannot be empty");
    }

    [Fact]
    public void Validate_WhenNameIsEmpty_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Name = "" };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Configuration name cannot be empty");
    }

    [Fact]
    public void Validate_WhenNameExceeds100Characters_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Name = new string('A', 101) };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Configuration name cannot exceed 100 characters");
    }

    [Fact]
    public void Validate_WhenTransportIsInvalidEnum_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Transport = (TransportType)999 };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Transport)
            .WithErrorMessage("Invalid transport type");
    }

    [Fact]
    public void Validate_WhenStdioCommandMissing_ShouldMentionLauncherSupport()
    {
        var configuration = new ServerConfiguration
        {
            Id = "ssh-bridge",
            Name = "SSH Bridge",
            Transport = TransportType.Stdio
        };

        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.StdioCommand)
            .WithErrorMessage("Stdio transport requires a command or launcher");
    }

    [Theory]
    [InlineData(TransportType.WebSocket)]
    [InlineData(TransportType.HttpSse)]
    public void Validate_WhenServerUrlMissingForNetworkTransport_ShouldHaveError(TransportType transport)
    {
        var configuration = new ServerConfiguration { Transport = transport, ServerUrl = "" };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.ServerUrl)
            .WithErrorMessage("Server URL cannot be empty");
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("invalid-url")]
    public void Validate_WhenServerUrlIsInvalid_ShouldHaveError(string url)
    {
        var configuration = new ServerConfiguration { Transport = TransportType.WebSocket, ServerUrl = url };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.ServerUrl)
            .WithErrorMessage("Server URL format is invalid, must be a valid WebSocket (ws:// or wss://) or HTTP (http:// or https://) URL");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenConnectionTimeoutIsZeroOrLess_ShouldHaveError(int timeout)
    {
        var configuration = new ServerConfiguration { ConnectionTimeout = timeout };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.ConnectionTimeout)
            .WithErrorMessage("Connection timeout must be greater than 0 seconds");
    }

    [Fact]
    public void Validate_WhenConnectionTimeoutIsGreaterThan60_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { ConnectionTimeout = 61 };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.ConnectionTimeout)
            .WithErrorMessage("Connection timeout cannot exceed 60 seconds");
    }

    [Fact]
    public void Validate_WhenAuthenticationMissingBoth_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Authentication = new AuthenticationConfig() };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Authentication)
            .WithErrorMessage("Authentication must provide exactly one of Token or ApiKey");
    }

    [Fact]
    public void Validate_WhenAuthenticationHasBoth_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Authentication = new AuthenticationConfig { Token = "token", ApiKey = "apikey" } };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Authentication)
            .WithErrorMessage("Authentication must provide exactly one of Token or ApiKey");
    }

    [Fact]
    public void Validate_WhenAuthenticationHasTokenOnly_ShouldNotHaveError()
    {
        var configuration = new ServerConfiguration
        {
            Id = "valid",
            Name = "valid",
            Transport = TransportType.Stdio,
            StdioCommand = "cmd",
            Authentication = new AuthenticationConfig { Token = "token" }
        };
        var result = _validator.TestValidate(configuration);
        result.ShouldNotHaveValidationErrorFor(x => x.Authentication);
    }

    [Fact]
    public void Validate_WhenAuthenticationHasApiKeyOnly_ShouldNotHaveError()
    {
        var configuration = new ServerConfiguration
        {
            Id = "valid",
            Name = "valid",
            Transport = TransportType.Stdio,
            StdioCommand = "cmd",
            Authentication = new AuthenticationConfig { ApiKey = "key" }
        };
        var result = _validator.TestValidate(configuration);
        result.ShouldNotHaveValidationErrorFor(x => x.Authentication);
    }

    [Fact]
    public void Validate_WhenProxyEnabledWithEmptyUrl_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Proxy = new ProxyConfig { Enabled = true, ProxyUrl = "" } };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Proxy!.ProxyUrl)
            .WithErrorMessage("Proxy URL must be provided when proxy is enabled");
    }

    [Fact]
    public void Validate_WhenProxyEnabledWithInvalidScheme_ShouldHaveError()
    {
        var configuration = new ServerConfiguration { Proxy = new ProxyConfig { Enabled = true, ProxyUrl = "ftp://proxy.com" } };
        var result = _validator.TestValidate(configuration);
        result.ShouldHaveValidationErrorFor(x => x.Proxy!.ProxyUrl)
            .WithErrorMessage("Invalid proxy URL format");
    }

    [Fact]
    public void Validate_WhenProxyEnabledWithValidUrl_ShouldNotHaveError()
    {
        var configuration = new ServerConfiguration
        {
            Id = "valid",
            Name = "valid",
            Transport = TransportType.Stdio,
            StdioCommand = "cmd",
            Proxy = new ProxyConfig { Enabled = true, ProxyUrl = "http://proxy.com:8080" }
        };
        var result = _validator.TestValidate(configuration);
        result.ShouldNotHaveValidationErrorFor(x => x.Proxy!.ProxyUrl);
    }

    [Fact]
    public void Validate_WhenProxyDisabledWithEmptyUrl_ShouldNotHaveError()
    {
        var configuration = new ServerConfiguration
        {
            Id = "valid",
            Name = "valid",
            Transport = TransportType.Stdio,
            StdioCommand = "cmd",
            Proxy = new ProxyConfig { Enabled = false, ProxyUrl = "" }
        };
        var result = _validator.TestValidate(configuration);
        result.ShouldNotHaveValidationErrorFor(x => x.Proxy!.ProxyUrl);
    }
}
