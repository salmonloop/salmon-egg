using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Content;

/// <summary>Opt-in access to ACP v2 resource-link icon metadata.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public static class ResourceLinkDraftExtensions
{
    /// <summary>Returns supported icon entries, applying the schema's default-on-error and skip-invalid-items rules.</summary>
    public static IReadOnlyList<Icon> GetIcons(this ResourceLinkContentBlock resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var result = new List<Icon>();
        if (resource.RawIcons is not { ValueKind: JsonValueKind.Array } icons)
        {
            return result;
        }

        var typeInfo = AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<Icon>();
        foreach (var item in icons.EnumerateArray())
        {
            try
            {
                if (item.Deserialize(typeInfo) is { } icon)
                {
                    result.Add(icon);
                }
            }
            catch (JsonException)
            {
                // ResourceLink.icons alone grants per-item recovery; Icon.src remains required.
            }
        }

        return result;
    }

    /// <summary>Creates a resource link with v2 icon metadata. Writing the result requires a v2 wire context.</summary>
    public static ResourceLinkContentBlock WithIcons(this ResourceLinkContentBlock resource, IReadOnlyList<Icon> icons)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(icons);
        var typeInfo = AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<Icon>();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var icon in icons)
            {
                JsonSerializer.Serialize(writer, icon, typeInfo);
            }

            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return resource with { RawIcons = document.RootElement.Clone(), HasDraftIcons = true };
    }
}
