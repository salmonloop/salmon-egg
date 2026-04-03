using System;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public static class AcpErrorClassifier
{
    public static bool IsAuthenticationRequired(Exception ex) =>
        ex is AcpException acp && acp.ErrorCode == JsonRpcErrorCode.AuthenticationRequired;

    public static bool IsRemoteSessionNotFound(Exception ex) =>
        ex is AcpException acp
        && (acp.ErrorCode == JsonRpcErrorCode.ResourceNotFound
            || acp.ErrorCode == JsonRpcErrorCode.SessionNotFound
            || (acp.Message.Contains("session", StringComparison.OrdinalIgnoreCase)
                && acp.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)));
}
