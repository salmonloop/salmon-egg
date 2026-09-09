using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Client;

/// <summary>Opt-in access to ACP v2 permission requests without enabling live v2 negotiation.</summary>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public static class AcpPermissionDraftExtensions
{
    /// <summary>Returns v2 prompt data carried by the existing permission event, or null for v1.</summary>
    public static AcpPermissionRequestSnapshot? GetDraftRequest(this PermissionRequestEventArgs request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.DraftRequest;
    }

    /// <summary>Reads recorded v2 permission params using the same contract as the client handler.</summary>
    /// <remarks>This offline helper never sends a response, executes a command, or changes session state.</remarks>
    /// <exception cref="JsonException">A required field or provided subject violates its v2 contract.</exception>
    public static AcpPermissionRequestSnapshot ReadRequest(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("ACP permission params must be an object.");
        }
        var sessionId = ReadRequiredString(parameters, "sessionId");
        var title = ReadRequiredString(parameters, "title");
        if (!parameters.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array
            || options.GetArrayLength() == 0)
        {
            throw new JsonException("ACP permission params require at least one option.");
        }
        var wire = AcpWireFormat.For(AcpProtocolVersion.V2);
        foreach (var option in options.EnumerateArray())
        {
            if (option.Deserialize(wire.TypeInfo<PermissionOption>()) is null)
            {
                throw new JsonException("ACP permission options must be objects.");
            }
        }
        if (parameters.TryGetProperty("subject", out var subject) && subject.ValueKind != JsonValueKind.Null)
        {
            _ = subject.Deserialize(wire.TypeInfo<RequestPermissionSubject>())
                ?? throw new JsonException("ACP permission subject must be an object.");
        }

        return new AcpPermissionRequestSnapshot(sessionId, title, parameters.Clone());
    }

    private static string ReadRequiredString(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()!
            : throw new JsonException($"ACP permission params require string '{name}'.");
}

/// <summary>Immutable prompt data for one ACP v2 permission request.</summary>
/// <remarks>
/// Title and description belong only to this permission prompt. A subject provides context and does
/// not update the transcript, tool-call projection, or terminal state. Mutable DTO getters return
/// detached copies; raw parameters preserve unknown request and subject fields for host forwarding.
/// </remarks>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public sealed class AcpPermissionRequestSnapshot
{
    internal AcpPermissionRequestSnapshot(string sessionId, string title, JsonElement parameters)
    {
        SessionId = sessionId;
        Title = title;
        RawParameters = parameters;
    }

    /// <summary>The session this permission request belongs to.</summary>
    public string SessionId { get; }
    /// <summary>The permission-specific title, separate from any subject title.</summary>
    public string Title { get; }
    /// <summary>The optional explanation, defaulted only as allowed by the v2 schema.</summary>
    public string? Description => RawParameters.TryGetProperty("description", out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    /// <summary>The optional structured subject. Unknown variants retain their complete raw payload.</summary>
    public RequestPermissionSubject? Subject => RawParameters.TryGetProperty("subject", out var value)
        && value.ValueKind != JsonValueKind.Null
            ? value.Deserialize(AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<RequestPermissionSubject>())
            : null;
    /// <summary>Detached permission options, including unknown option-kind strings.</summary>
    public ImmutableArray<PermissionOption> Options
    {
        get
        {
            var options = RawParameters.GetProperty("options");
            var result = ImmutableArray.CreateBuilder<PermissionOption>(options.GetArrayLength());
            foreach (var option in options.EnumerateArray())
            {
                result.Add(option.Deserialize(AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<PermissionOption>())!);
            }
            return result.MoveToImmutable();
        }
    }
    /// <summary>Request metadata, or null when absent, null, or defaulted after a type error.</summary>
    public JsonElement? Meta => RawParameters.TryGetProperty("_meta", out var value)
        && value.ValueKind == JsonValueKind.Object ? value : null;
    /// <summary>The complete received params object, retained verbatim for host recording or forwarding.</summary>
    public JsonElement RawParameters { get; }
}
