using FluentValidation;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Application.Validators
{
    /// <summary>
    /// 服务器配置验证器
    /// 使用 FluentValidation 验证 ServerConfiguration 对象
    /// </summary>
    public class ServerConfigurationValidator : AbstractValidator<ServerConfiguration>
    {
        public ServerConfigurationValidator()
        {
            // 验证 ID
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage("配置 ID 不能为空");

            // 验证名称
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("配置名称不能为空")
                .MaximumLength(100)
                .WithMessage("配置名称不能超过 100 个字符");

            // 验证服务器 URL
            RuleFor(x => x.ServerUrl)
                .NotEmpty()
                .WithMessage("服务器 URL 不能为空")
                .Must(BeValidUrl)
                .WithMessage("服务器 URL 格式无效，必须是有效的 WebSocket (ws:// 或 wss://) 或 HTTP (http:// 或 https://) URL");

            // 验证传输类型
            RuleFor(x => x.Transport)
                .IsInEnum()
                .WithMessage("传输类型无效");

            // 验证心跳间隔
            RuleFor(x => x.HeartbeatInterval)
                .GreaterThan(0)
                .WithMessage("心跳间隔必须大于 0 秒")
                .LessThanOrEqualTo(300)
                .WithMessage("心跳间隔不能超过 300 秒（5 分钟）");

            // 验证连接超时
            RuleFor(x => x.ConnectionTimeout)
                .GreaterThan(0)
                .WithMessage("连接超时必须大于 0 秒")
                .LessThanOrEqualTo(60)
                .WithMessage("连接超时不能超过 60 秒");

            // 验证认证配置（如果存在）
            When(x => x.Authentication != null, () =>
            {
                RuleFor(x => x.Authentication.Token)
                    .NotEmpty()
                    .When(x => string.IsNullOrEmpty(x.Authentication.ApiKey))
                    .WithMessage("必须提供 Token 或 ApiKey 之一");

                RuleFor(x => x.Authentication.ApiKey)
                    .NotEmpty()
                    .When(x => string.IsNullOrEmpty(x.Authentication.Token))
                    .WithMessage("必须提供 Token 或 ApiKey 之一");
            });

            // 验证代理配置（如果启用）
            When(x => x.Proxy != null && x.Proxy.Enabled, () =>
            {
                RuleFor(x => x.Proxy.ProxyUrl)
                    .NotEmpty()
                    .WithMessage("启用代理时必须提供代理 URL")
                    .Must(BeValidProxyUrl)
                    .WithMessage("代理 URL 格式无效");
            });
        }

        /// <summary>
        /// 验证 URL 是否有效
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
        /// 验证代理 URL 是否有效
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
