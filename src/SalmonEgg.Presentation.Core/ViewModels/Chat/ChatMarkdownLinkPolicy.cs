namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Defines which markdown links may be opened from chat content.
/// </summary>
public static class ChatMarkdownLinkPolicy
{
    /// <summary>
    /// Attempts to resolve a raw markdown link into a launchable URI.
    /// </summary>
    /// <param name="rawLink">The raw markdown link target.</param>
    /// <param name="uri">The resolved launchable URI when allowed.</param>
    /// <returns><see langword="true" /> when the link may be opened; otherwise <see langword="false" />.</returns>
    public static bool TryResolveLaunchUri(string? rawLink, out Uri? uri)
    {
        if (!Uri.TryCreate(rawLink, UriKind.Absolute, out uri) || uri is null)
        {
            uri = null;
            return false;
        }

        return TryResolveLaunchUri(uri, out uri);
    }

    /// <summary>
    /// Attempts to validate an existing URI for markdown link launching.
    /// </summary>
    /// <param name="candidate">The URI produced by the markdown control.</param>
    /// <param name="uri">The validated launchable URI when allowed.</param>
    /// <returns><see langword="true" /> when the URI may be opened; otherwise <see langword="false" />.</returns>
    public static bool TryResolveLaunchUri(Uri? candidate, out Uri? uri)
    {
        if (candidate is null || !candidate.IsAbsoluteUri)
        {
            uri = null;
            return false;
        }

        if (!IsAllowedScheme(candidate.Scheme))
        {
            uri = null;
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsAllowedScheme(string? scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
