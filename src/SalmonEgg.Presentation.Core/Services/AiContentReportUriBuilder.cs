using System;
using System.Collections.Generic;
using System.Globalization;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Single owner for the mailto URI shape used to report inappropriate AI content.
/// About and Chat both build through this helper so subject/body layout stay identical.
/// </summary>
public static class AiContentReportUriBuilder
{
    private const int MaximumContentExcerptTextElements = 1000;

    public static Uri? TryCreate(
        string email,
        string subject,
        string appLabel,
        string appName,
        string versionLabel,
        string appVersion,
        string protocolLabel,
        string protocolVersion,
        string bodyPrompt,
        string? contentExcerptLabel = null,
        string? contentExcerpt = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var bodyLines = new List<string>
        {
            $"{appLabel}: {appName}",
            $"{versionLabel}: {appVersion}",
            $"{protocolLabel}: {protocolVersion}",
            string.Empty,
            bodyPrompt ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(contentExcerpt))
        {
            bodyLines.Add(string.Empty);
            if (!string.IsNullOrWhiteSpace(contentExcerptLabel))
            {
                bodyLines.Add(contentExcerptLabel.Trim());
            }

            bodyLines.Add(CreateBoundedExcerpt(contentExcerpt));
        }

        var subjectValue = Uri.EscapeDataString(subject ?? string.Empty);
        var bodyValue = Uri.EscapeDataString(string.Join(Environment.NewLine, bodyLines));
        return Uri.TryCreate(
            $"mailto:{email.Trim()}?subject={subjectValue}&body={bodyValue}",
            UriKind.Absolute,
            out var uri)
            ? uri
            : null;
    }

    private static string CreateBoundedExcerpt(string contentExcerpt)
    {
        var normalized = contentExcerpt.Trim();
        var contentInfo = new StringInfo(normalized);
        if (contentInfo.LengthInTextElements <= MaximumContentExcerptTextElements)
        {
            return normalized;
        }

        return contentInfo.SubstringByTextElements(0, MaximumContentExcerptTextElements - 3) + "...";
    }
}
