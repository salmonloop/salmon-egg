using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// 传输配置 ViewModel，用于在 UI 中管理不同的传输方式（Stdio, WebSocket, HTTP SSE）。
/// </summary>
public partial class TransportConfigViewModel : ObservableObject
{
    /// <summary>
    /// 当前选择的传输类型
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStdio))]
    [NotifyPropertyChangedFor(nameof(IsWebSocket))]
    [NotifyPropertyChangedFor(nameof(IsHttpSse))]
    private TransportType _selectedTransportType = TransportType.Stdio;

    /// <summary>
    /// 是否为 Stdio 传输
    /// </summary>
    public bool IsStdio => SelectedTransportType == TransportType.Stdio;

    /// <summary>
    /// 是否为 WebSocket 传输
    /// </summary>
    public bool IsWebSocket => SelectedTransportType == TransportType.WebSocket;

    /// <summary>
    /// 是否为 HTTP SSE 传输
    /// </summary>
    public bool IsHttpSse => SelectedTransportType == TransportType.HttpSse;

    /// <summary>
    /// Stdio 命令 (例如：agent-command)
    /// </summary>
    [ObservableProperty]
    private string _stdioCommand = string.Empty;

    /// <summary>
    /// Stdio 命令行参数 (例如：--port 8080)
    /// </summary>
    [ObservableProperty]
    private string _stdioArgs = string.Empty;

    /// <summary>
    /// WebSocket 或 HTTP SSE 的 URL
    /// </summary>
    [ObservableProperty]
    private string _remoteUrl = string.Empty;

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    /// <returns>验证结果和错误消息</returns>
    public (bool IsValid, string? ErrorMessage) Validate()
    {
        switch (SelectedTransportType)
        {
            case TransportType.Stdio:
                if (string.IsNullOrWhiteSpace(StdioCommand))
                {
                    return (false, "Stdio 传输必须指定命令");
                }
                return (true, null);

            case TransportType.WebSocket:
            case TransportType.HttpSse:
                if (string.IsNullOrWhiteSpace(RemoteUrl))
                {
                    return (false, "远程传输必须指定 URL");
                }

                if (!Uri.TryCreate(RemoteUrl, UriKind.Absolute, out _))
                {
                    return (false, "URL 格式无效");
                }

                // 简单验证协议
                if (SelectedTransportType == TransportType.WebSocket &&
                    !RemoteUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
                    !RemoteUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "WebSocket URL 必须以 ws:// 或 wss:// 开头");
                }

                if (SelectedTransportType == TransportType.HttpSse &&
                    !RemoteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !RemoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "HTTP SSE URL 必须以 http:// 或 https:// 开头");
                }

                return (true, null);

            default:
                return (false, "不支持的传输类型");
        }
    }

    /// <summary>
    /// 重置为默认配置
    /// </summary>
    public void Reset()
    {
        SelectedTransportType = TransportType.Stdio;
        StdioCommand = string.Empty;
        StdioArgs = string.Empty;
        RemoteUrl = string.Empty;
    }
}
