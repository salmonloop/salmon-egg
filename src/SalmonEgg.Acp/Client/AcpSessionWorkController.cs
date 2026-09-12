using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Client;

// Foreground work belongs to a session, not to a prompt RPC. In v2 several accepted prompts may
// contribute to the same work, and unsolicited work may start before this client sends any prompt.
internal sealed class AcpSessionWorkController
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionWork> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closedSessions = new(StringComparer.Ordinal);
    private CancellationToken _connectionToken;

    internal void BeginConnection(CancellationToken connectionToken)
    {
        lock (_gate)
        {
            _connectionToken = connectionToken;
            _sessions.Clear();
            _closedSessions.Clear();
        }
    }

    internal void EndConnection(Exception failure)
    {
        lock (_gate)
        {
            foreach (var session in _sessions.Values)
            {
                foreach (var prompt in session.Prompts)
                {
                    prompt.Fail(failure);
                }
                session.CancellationCompletion?.TrySetResult(new SessionPromptCompletion(null, failure));
            }

            _sessions.Clear();
            _closedSessions.Clear();
            _connectionToken = default;
        }
    }

    internal void RegisterSession(string sessionId, CancellationToken connectionToken)
    {
        lock (_gate)
        {
            if (IsCurrent(connectionToken))
            {
                _closedSessions.Remove(sessionId);
                GetOrCreateSession(sessionId);
            }
        }
    }

    internal void RemoveSession(string sessionId, CancellationToken connectionToken)
    {
        lock (_gate)
        {
            if (!IsCurrent(connectionToken))
            {
                return;
            }

            _closedSessions.Add(sessionId);
            if (_sessions.Remove(sessionId, out var session))
            {
                foreach (var prompt in session.Prompts)
                {
                    prompt.Fail(new OperationCanceledException("The ACP session was closed."));
                }
                session.CancellationCompletion?.TrySetResult(new SessionPromptCompletion(
                    null, new OperationCanceledException("The ACP session was closed.")));
            }
        }
    }

    internal SessionPromptOperation BeginPrompt(
        string sessionId,
        AcpWireFormat wire,
        CancellationToken connectionToken)
    {
        lock (_gate)
        {
            if (!IsCurrent(connectionToken) || _closedSessions.Contains(sessionId))
            {
                throw new OperationCanceledException("The ACP connection or session changed before the prompt was sent.");
            }

            var prompt = new SessionPromptOperation(sessionId, wire, connectionToken);
            var session = GetOrCreateSession(sessionId);
            session.Prompts.Add(prompt);
            return prompt;
        }
    }

    internal void ReceivePromptResponse(SessionPromptOperation prompt, JsonRpcResponse response)
    {
        lock (_gate)
        {
            if (!TryGetCurrentPrompt(prompt, out var session))
            {
                return;
            }

            try
            {
                if (response.IsError)
                {
                    throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
                }

                if (prompt.Wire.Version == AcpProtocolVersion.V1)
                {
                    var completed = response.Result?.Deserialize(prompt.Wire.TypeInfo<SessionPromptResponse>())
                        ?? throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/prompt response");
                    prompt.Accept();
                    prompt.Complete(completed);
                    session.Prompts.Remove(prompt);
                    return;
                }

                if (response.Result?.ValueKind != JsonValueKind.Object)
                {
                    throw new AcpException(JsonRpcErrorCode.ParseError, "ACP v2 session/prompt acknowledgement must be an object.");
                }

                // The acknowledgement's metadata belongs to acceptance, not to the later idle.
                // PromptResponse._meta explicitly defaults on error in the schema.
                prompt.Accept(AcpMetaJson.ReadOrDefault(response.Result.Value));
                if (prompt.CompletionBeforeAcceptance is { } completion)
                {
                    prompt.Complete(completion);
                    session.Prompts.Remove(prompt);
                }
            }
            catch (Exception error)
            {
                prompt.Fail(error);
                session.Prompts.Remove(prompt);
            }
        }
    }

    internal void FailPrompt(SessionPromptOperation prompt, Exception error)
    {
        lock (_gate)
        {
            if (TryGetCurrentPrompt(prompt, out var session))
            {
                session.Prompts.Remove(prompt);
                prompt.Fail(error);
            }
        }
    }

    internal bool ReceiveUpdate(
        string sessionId,
        SessionUpdate update,
        CancellationToken connectionToken,
        JsonElement? draftPayload = null)
    {
        lock (_gate)
        {
            if (!IsCurrent(connectionToken) || _closedSessions.Contains(sessionId))
            {
                return false;
            }

            var session = GetOrCreateSession(sessionId);
            if (draftPayload is { } payload)
            {
                session.Projection ??= new AcpSessionProjection();
                session.Projection.Apply(update, payload);
            }

            if (update is not StateSessionUpdate workUpdate)
            {
                return true;
            }

            session.State = CloneState(workUpdate.State);
            if (workUpdate.State is IdleSessionWorkState idle)
            {
                CompleteIdle(session, idle);
            }
            else if (workUpdate.State is RunningSessionWorkState or RequiresActionSessionWorkState)
            {
                foreach (var prompt in session.Prompts)
                {
                    prompt.WorkObserved = true;
                    prompt.CompletionBeforeAcceptance = null;
                }
            }

            return true;
        }
    }

    internal Task<SessionPromptCompletion>? RequestCancellation(string sessionId, CancellationToken connectionToken)
    {
        lock (_gate)
        {
            if (IsCurrent(connectionToken) && _sessions.TryGetValue(sessionId, out var session)
                && (session.Prompts.Count > 0 || session.State is RunningSessionWorkState or RequiresActionSessionWorkState))
            {
                session.CancellationRequested = true;
                session.CancellationCompletion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                return session.CancellationCompletion.Task;
            }

            return null;
        }
    }

    internal SessionWorkSnapshot? GetSnapshot(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out var session)
                ? new SessionWorkSnapshot(
                    CloneState(session.State),
                    session.CancellationRequested,
                    session.Prompts.Count,
                    session.Prompts.Count(static prompt => prompt.Accepted is not null))
                : null;
        }
    }

    internal AcpSessionSnapshot? GetProjectionSnapshot(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out var session)
                ? (session.Projection ?? new AcpSessionProjection()).Snapshot(sessionId, session.State)
                : null;
        }
    }

    internal void ReceiveConfigOptions(
        string sessionId,
        IReadOnlyList<ConfigOption> configOptions,
        CancellationToken connectionToken,
        bool registerSession)
    {
        lock (_gate)
        {
            if (!IsCurrent(connectionToken) || (!registerSession && _closedSessions.Contains(sessionId)))
            {
                return;
            }
            if (registerSession) _closedSessions.Remove(sessionId);
            var session = GetOrCreateSession(sessionId);
            session.Projection ??= new AcpSessionProjection();
            session.Projection.SetConfigOptions(configOptions);
        }
    }

    internal AcpSessionProjection BeginReplay(string sessionId, CancellationToken connectionToken)
    {
        lock (_gate)
        {
            if (!IsCurrent(connectionToken))
            {
                throw new OperationCanceledException("The ACP connection changed before session replay.");
            }
            var session = GetOrCreateSession(sessionId);
            if (session.ReplayInProgress)
            {
                // session/update has no request id: overlapping full replays cannot be separated.
                throw new InvalidOperationException("A full history replay is already in progress for this session.");
            }
            _closedSessions.Remove(sessionId);
            session.ReplayInProgress = true;
            session.Projection = new AcpSessionProjection();
            return session.Projection;
        }
    }

    internal void EndReplay(string sessionId, AcpSessionProjection? projection, CancellationToken connectionToken)
    {
        lock (_gate)
        {
            if (IsCurrent(connectionToken) && _sessions.TryGetValue(sessionId, out var session)
                && ReferenceEquals(session.Projection, projection))
            {
                session.ReplayInProgress = false;
            }
        }
    }

    private static SessionWorkState? CloneState(SessionWorkState? state)
        => state is null ? null : state with { Meta = AcpMetaJson.Clone(state.Meta) };

    private static void CompleteIdle(SessionWork session, IdleSessionWorkState idle)
    {
        session.CancellationRequested = false;
        // The pinned schema makes stopReason nullable/default-on-error (SHOULD for an ending
        // transition); the prose lifecycle says MUST. Preserve the schema's unknown reason and
        // still finish on idle: inventing end_turn or waiting forever would both misreport the peer.
        var completion = new SessionPromptResponse
        {
            StopReason = idle.StopReason ?? default,
            HasStopReason = idle.StopReason is not null,
            Meta = AcpMetaJson.Clone(idle.Meta)
        };
        session.CancellationCompletion?.TrySetResult(new SessionPromptCompletion(completion, null));
        session.CancellationCompletion = null;

        foreach (var prompt in session.Prompts.ToArray())
        {
            if (prompt.Wire.Version != AcpProtocolVersion.V2)
            {
                continue;
            }

            if (prompt.Accepted is not null)
            {
                prompt.Complete(completion);
                session.Prompts.Remove(prompt);
            }
            else if (prompt.WorkObserved)
            {
                // A response can be delayed behind already emitted work. An idle from setup
                // alone cannot finish a prompt; only work observed since this submission can.
                prompt.CompletionBeforeAcceptance = completion;
            }
        }
    }

    private bool IsCurrent(CancellationToken token)
        => token.CanBeCanceled && !token.IsCancellationRequested && _connectionToken == token;

    private bool TryGetCurrentPrompt(SessionPromptOperation prompt, out SessionWork session)
    {
        session = null!;
        return IsCurrent(prompt.ConnectionToken)
            && _sessions.TryGetValue(prompt.SessionId, out session!)
            && session.Prompts.Contains(prompt);
    }

    private SessionWork GetOrCreateSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            session = new SessionWork();
            _sessions.Add(sessionId, session);
        }

        return session;
    }

    private sealed class SessionWork
    {
        internal List<SessionPromptOperation> Prompts { get; } = [];
        internal SessionWorkState? State { get; set; }
        internal bool CancellationRequested { get; set; }
        internal TaskCompletionSource<SessionPromptCompletion>? CancellationCompletion { get; set; }
        internal AcpSessionProjection? Projection { get; set; }
        internal bool ReplayInProgress { get; set; }
    }
}

internal sealed record SessionWorkSnapshot(
    SessionWorkState? State,
    bool CancellationRequested,
    int PendingPrompts,
    int AcceptedPrompts);

internal sealed record SessionPromptAcceptance(Dictionary<string, object?>? Meta);

internal sealed record SessionPromptCompletion(SessionPromptResponse? Response, Exception? Error)
{
    internal SessionPromptResponse GetResponse()
    {
        if (Error is not null)
        {
            ExceptionDispatchInfo.Capture(Error).Throw();
        }

        return Response!;
    }
}

internal sealed class SessionPromptOperation
{
    private readonly TaskCompletionSource<SessionPromptCompletion> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal SessionPromptOperation(string sessionId, AcpWireFormat wire, CancellationToken connectionToken)
    {
        SessionId = sessionId;
        Wire = wire;
        ConnectionToken = connectionToken;
    }

    internal string SessionId { get; }
    internal AcpWireFormat Wire { get; }
    internal CancellationToken ConnectionToken { get; }
    internal SessionPromptAcceptance? Accepted { get; private set; }
    internal bool WorkObserved { get; set; }
    internal SessionPromptResponse? CompletionBeforeAcceptance { get; set; }
    internal Task<SessionPromptCompletion> Completion => _completion.Task;

    internal void Accept(Dictionary<string, object?>? meta = null)
        => Accepted = new SessionPromptAcceptance(meta);

    internal void Complete(SessionPromptResponse response)
        => _completion.TrySetResult(new SessionPromptCompletion(response, null));

    // The caller can abandon its await while the RPC/work remains correlated. Carry failures as
    // outcomes so a later disconnect does not create an unobserved fault on that abandoned task.
    internal void Fail(Exception error)
        => _completion.TrySetResult(new SessionPromptCompletion(null, error));
}
