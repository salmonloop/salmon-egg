using FluentValidation;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Application.Validators
{
    /// <summary>
    /// Server configuration validator
    /// Validates ServerConfiguration objects using FluentValidation
    /// </summary>
    public class ServerConfigurationValidator : AbstractValidator<ServerConfiguration>
    {
        public ServerConfigurationValidator()
        {
            // Validate ID
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage("Configuration ID cannot be empty");

            // Validate name
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("Configuration name cannot be empty")
                .MaximumLength(100)
                .WithMessage("Configuration name cannot exceed 100 characters");

            // Validate transport type
            RuleFor(x => x.Transport)
                .IsInEnum()
                .WithMessage("Invalid transport type");

            // Validate endpoint based on transport
            When(x => x.Transport == TransportType.Stdio, () =>
            {
                RuleFor(x => x.StdioCommand)
                    .NotEmpty()
                    .WithMessage("Stdio transport requires a command or launcher");
            });

            When(x => x.Transport == TransportType.WebSocket || x.Transport == TransportType.HttpSse, () =>
            {
                RuleFor(x => x.ServerUrl)
                    .NotEmpty()
                    .WithMessage("Server URL cannot be empty")
                    .Must(BeValidUrl)
                    .WithMessage("Server URL format is invalid, must be a valid WebSocket (ws:// or wss://) or HTTP (http:// or https://) URL");
            });

            // Validate connection timeout
            RuleFor(x => x.ConnectionTimeout)
                .GreaterThan(0)
                .WithMessage("Connection timeout must be greater than 0 seconds")
                .LessThanOrEqualTo(60)
                .WithMessage("Connection timeout cannot exceed 60 seconds");

            // Validate authentication configuration (if present)
            When(x => x.Authentication != null, () =>
            {
                RuleFor(x => x.Authentication!)
                    .Must(auth =>
                    {
                        var hasToken = !string.IsNullOrWhiteSpace(auth.Token);
                        var hasApiKey = !string.IsNullOrWhiteSpace(auth.ApiKey);
                        return hasToken ^ hasApiKey;
                    })
                    .WithMessage("Authentication must provide exactly one of Token or ApiKey");
            });

            // Validate proxy configuration (if enabled)
            When(x => x.Proxy != null && x.Proxy.Enabled, () =>
            {
                RuleFor(x => x.Proxy!.ProxyUrl)
                    .NotEmpty()
                    .WithMessage("Proxy URL must be provided when proxy is enabled")
                    .Must(BeValidProxyUrl)
                    .WithMessage("Invalid proxy URL format");
            });
        }

        /// <summary>
        /// Validates if the URL is valid
        /// </summary>
        private bool BeValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
                return false;

            // 验证协议
            var scheme = uri.Scheme.ToLowerInvariant();
            return scheme == "ws" || scheme == "wss" || scheme == "http" || scheme == "https";
        }

        /// <summary>
        /// Validates if the proxy URL is valid
        /// </summary>
        private bool BeValidProxyUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
                return false;

            // 代理通常使用 HTTP 或 HTTPS
            var scheme = uri.Scheme.ToLowerInvariant();
            return scheme == "http" || scheme == "https";
        }
    }
}
