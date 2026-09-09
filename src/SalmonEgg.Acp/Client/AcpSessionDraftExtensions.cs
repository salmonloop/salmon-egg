using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Client;

/// <summary>Opt-in access to ACP v2 session projections and offline history replay.</summary>
/// <remarks>
/// These helpers do not enable live v2 negotiation. The public client continues to reject v2 until
/// its complete lifecycle and feature gates are delivered. Replay consumes recorded update objects
/// without connecting to an Agent or modifying a client's state.
/// </remarks>
[Experimental(AcpDraftProtocol.DiagnosticId, Message = AcpDraftProtocol.Message, UrlFormat = AcpDraftProtocol.UrlFormat)]
public static class AcpSessionDraftExtensions
{
    /// <summary>
    /// Returns a point-in-time snapshot from the client's normal session/update handler, or null
    /// when no draft session is currently tracked. Closed and disconnected sessions are not exposed.
    /// </summary>
    public static AcpSessionSnapshot? GetSessionSnapshot(this AcpClient client, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(sessionId);
        return client.GetDraftSessionSnapshot(sessionId);
    }

    /// <summary>Replays a recorded ACP v2 session history using the same projection as the live handler.</summary>
    /// <param name="sessionId">The identity of the recorded session.</param>
    /// <param name="updates">
    /// Update objects, each containing the protocol's sessionUpdate discriminator. The sequence must
    /// be in receive order and scoped to this session. Input objects are not mutated or retained.
    /// </param>
    /// <returns>
    /// An immutable current-view snapshot, including bounded recent unprojected updates and their
    /// omission count. Keep the input history when a complete archive is required.
    /// </returns>
    /// <exception cref="JsonException">A required update field violates its ACP v2 contract.</exception>
    public static AcpSessionSnapshot ReplaySession(string sessionId, IEnumerable<JsonElement> updates)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(updates);
        var projection = new AcpSessionProjection();
        SessionWorkState? workState = null;
        foreach (var payload in updates)
        {
            var update = ReadUpdate(sessionId, payload);
            projection.Apply(update, payload);
            if (update is StateSessionUpdate state)
            {
                workState = state.State;
            }
        }
        return projection.Snapshot(sessionId, workState);
    }

    private static SessionUpdate ReadUpdate(string sessionId, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Recorded session updates must be JSON objects.");
        }

        // Use the existing params converter, including flattened state and negotiated variant gates.
        // Building the envelope avoids a second discriminator parser for the offline host API.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", sessionId);
            writer.WritePropertyName("update");
            payload.WriteTo(writer);
            writer.WriteEndObject();
        }

        var value = JsonSerializer.Deserialize(
            stream.GetBuffer().AsSpan(0, checked((int)stream.Length)),
            AcpWireFormat.For(AcpProtocolVersion.V2).TypeInfo<SessionUpdateParams>());
        return value?.Update ?? throw new JsonException("Recorded history requires session/update content.");
    }
}
