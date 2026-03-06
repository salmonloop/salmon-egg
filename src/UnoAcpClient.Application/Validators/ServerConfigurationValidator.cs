using FluentValidation;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Application.Validators
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

            // Validate server URL
            RuleFor(x => x.ServerUrl)
                .NotEmpty()
                .WithMessage("Server URL cannot be empty")
                .Must(BeValidUrl)
                .WithMessage("Server URL format is invalid, must be a valid WebSocket (ws:// or wss://) or HTTP (http:// or https://) URL");

            // Validate transport type
            RuleFor(x => x.Transport)
                .IsInEnum()
                .WithMessage("Invalid transport type");

            // Validate heartbeat interval
            RuleFor(x => x.HeartbeatInterval)
                .GreaterThan(0)
                .WithMessage("Heartbeat interval must be greater than 0 seconds")
                .LessThanOrEqualTo(300)
                .WithMessage("Heartbeat interval cannot exceed 300 seconds (5 minutes)");

            // Validate connection timeout
            RuleFor(x => x.ConnectionTimeout)
                .GreaterThan(0)
                .WithMessage("Connection timeout must be greater than 0 seconds")
                .LessThanOrEqualTo(60)
                .WithMessage("Connection timeout cannot exceed 60 seconds");

            // Validate authentication configuration (if present)
            When(x => x.Authentication != null, () =>
            {
                RuleFor(x => x.Authentication.Token)
                    .NotEmpty()
                    .When(x => string.IsNullOrEmpty(x.Authentication.ApiKey))
                    .WithMessage("Either Token or ApiKey must be provided");

                RuleFor(x => x.Authentication.ApiKey)
                    .NotEmpty()
                    .When(x => string.IsNullOrEmpty(x.Authentication.Token))
                    .WithMessage("Either Token or ApiKey must be provided");
            });

            // Validate proxy configuration (if enabled)
            When(x => x.Proxy != null && x.Proxy.Enabled, () =>
            {
                RuleFor(x => x.Proxy.ProxyUrl)
                    .NotEmpty()
                    .WithMessage("Proxy URL must be provided when proxy is enabled")
                    .Must(BeValidProxyUrl)
                    .WithMessage("Invalid proxy URL format");
            });
        }

        /// <summary>
        /// Validates if the URL is valid
        /// </summary>
        private bool BeValidUrl(string url)
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
        private bool BeValidProxyUrl(string url)
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
