using System;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct AcpSessionNewCwdResolution(
    bool IsSuccess,
    string? Cwd,
    string? ErrorMessage);

public static class AcpSessionNewCwdResolver
{
    public const string MissingRemoteCwdMessage =
        "Select a remote directory before creating a remote session.";

    public const string InvalidRemoteCwdMessage =
        "The remote working directory must be an absolute path.";

    public static AcpSessionNewCwdResolution Resolve(
        string? requestedCwd,
        ServerConfiguration? profile)
    {
        var trimmedCwd = TrimOrNull(requestedCwd);
        if (profile?.Transport == TransportType.Stdio)
        {
            return new AcpSessionNewCwdResolution(
                true,
                trimmedCwd ?? GetDefaultStdioUserProfileDirectory(),
                null);
        }

        if (trimmedCwd is null)
        {
            return new AcpSessionNewCwdResolution(false, null, MissingRemoteCwdMessage);
        }

        if (!ProtocolPathRules.IsAbsolutePath(trimmedCwd))
        {
            return new AcpSessionNewCwdResolution(false, null, InvalidRemoteCwdMessage);
        }

        return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetDefaultStdioUserProfileDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
