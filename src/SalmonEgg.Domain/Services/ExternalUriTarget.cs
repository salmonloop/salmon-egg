using System;
using System.Globalization;
using System.Linq;

namespace SalmonEgg.Domain.Services;

// A navigation intent is deliberately not a record: generated ToString must never print a URL/token.
public sealed class ExternalUriTarget
{
    private ExternalUriTarget(string fullUrl, Uri uri)
    {
        FullUrl = fullUrl;
        Uri = uri;
        Host = uri.IdnHost;
        HasAmbiguousHost = Host.Split('.').Any(label => label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
            || uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            || !uri.IsDefaultPort || Host.EndsWith(".", StringComparison.Ordinal);
    }

    public string FullUrl { get; }

    public string Host { get; }

    public bool HasAmbiguousHost { get; }

    public Uri Uri { get; }

    public static bool TryCreate(string? value, out ExternalUriTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Any(character => char.IsControl(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IsDevelopmentHost(uri.Host))))
        {
            return false;
        }

        try
        {
            // IdnHost forces the platform IDNA validation before anything becomes an openable target.
            target = new ExternalUriTarget(value, uri);
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public override string ToString() => nameof(ExternalUriTarget);

    private static bool IsDevelopmentHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1" || host == "[::1]";
}
