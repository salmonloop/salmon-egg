using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Observability;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;
namespace SalmonEgg.Acp.Client
{
    /// <summary>
    /// Core ACP client implementation.
    /// Combines the message, protocol, transport, and security layers into a complete ACP client.
    /// </summary>
    public sealed class AcpClient : IAcpClient, IDisposable
    {
        private const string StableV1RuntimeOnlyMessage =
            "ACP live client support is limited to stable protocolVersion 1 while newer modeled versions remain draft or incomplete.";
        private const string DisconnectIncompleteMessage =
            "ACP client disconnect has not completed successfully. Wait for it or retry DisconnectAsync before initializing.";

        // A best-effort notification must never turn a user cancellation into an unbounded wait.
        // Shared with behavioral tests so they validate the lifecycle, not a second timeout value.
        internal static readonly TimeSpan CancellationNotificationTimeout = TimeSpan.FromSeconds(2);

        private sealed record PendingOutboundRequest(
            TaskCompletionSource<JsonRpcResponse> Completion,
            Action<JsonRpcResponse>? ResponseObserver);

        private sealed record InboundResponseRoute(
            CancellationToken ConnectionToken,
            InboundResponseBatch? Batch = null,
            int Index = -1);

        private sealed class PendingInboundRequest
        {
            public PendingInboundRequest(
                string method,
                object? messageId,
                CancellationToken connectionToken,
                string? sessionId = null,
                AskUserRequest? askUserRequest = null,
                CreateElicitationRequest? elicitationRequest = null)
            {
                Method = method;
                MessageId = messageId;
                ConnectionToken = connectionToken;
                SessionId = sessionId;
                AskUserRequest = askUserRequest;
                ElicitationRequest = elicitationRequest;
            }

            public string Method { get; }

            public object? MessageId { get; }

            public CancellationToken ConnectionToken { get; }

            public string? SessionId { get; }

            public AskUserRequest? AskUserRequest { get; }

            public CreateElicitationRequest? ElicitationRequest { get; }

            public bool IsElicitationResponseInFlight { get; set; }

            public bool IsElicitationCancellationRequested { get; set; }

            public HashSet<string>? PermissionOptionIds { get; set; }

            public bool IsPermissionResponseInFlight { get; set; }

            public bool IsPermissionCancellationRequested { get; set; }

            public JsonRpcResponse? PreparedPermissionResponse { get; set; }

            public PermissionRequestEventArgs? PermissionEvent { get; set; }

            public CreateElicitationResponse? PreparedElicitationResponse { get; set; }

            public bool HasPreparedElicitationFailure { get; set; }

            public bool IsAskUserResponseInFlight { get; set; }

            public JsonRpcResponse? PreparedAskUserResponse { get; set; }

            public JsonRpcResponse? AskUserCancellationResponse { get; set; }

            public JsonRpcResponse? PreparedCancellationResponse { get; set; }

            public PendingInboundRequest WithSessionId(string sessionId)
                => new(Method, MessageId, ConnectionToken, sessionId, AskUserRequest, ElicitationRequest);

            public PendingInboundRequest WithAskUserRequest(AskUserRequest request)
                => new(
                    string.IsNullOrWhiteSpace(Method) ? ClientCapabilityMetadata.AskUserExtensionMethod : Method,
                    MessageId,
                    ConnectionToken,
                    request.SessionId,
                    request,
                    ElicitationRequest);
        }
        private readonly IAcpTransport _transport;
        private readonly MessageParser _parser;
        private readonly MessageValidator _validator;
        private readonly IAcpClientSessionStore _sessionStore;
        private readonly IAcpTerminalSessionManager _terminalSessionManager;
        private readonly IAcpClientLogger _logger;
        private readonly AcpSessionWorkController _sessionWork = new();
        private readonly ConcurrentDictionary<AcpRequestId, PendingOutboundRequest> _pendingRequests = new();
        // Inbound tool requests (agent -> client) are correlated by request id so we can format responses correctly.
        private readonly ConcurrentDictionary<AcpRequestId, PendingInboundRequest> _pendingInboundRequests = new();
        // Routes belong to the parsed id object, not just its value: an old callback must never
        // answer a new request that reuses the id. Weak keys retain that protection after teardown
        // without retaining every completed request for the lifetime of the client.
        private readonly ConditionalWeakTable<object, InboundResponseRoute> _responseRoutes = new();
        private readonly HashSet<InboundResponseBatch> _responseBatches = [];
        // URL completion outlives the JSON-RPC response. Both indexes refer to the same request identity;
        // the lock also protects response claims and connection teardown from stale callbacks.
        private readonly Dictionary<string, PendingInboundRequest> _pendingUrlElicitations = new(StringComparer.Ordinal);

        private readonly object _lock = new();
        private bool _disposed;
        private CancellationTokenSource? _messageLoopCts;
        private EventHandler<AcpTransportMessageReceivedEventArgs>? _connectionMessageHandler;
        private Task<bool>? _disconnectTask;
        private string? _lastTransportErrorMessage;

        private bool _isInitialized;

        // The serialization contract of this connection. ACP negotiates one major version per
        // connection and each side then serves that version's surface, so the contract is connection
        // state - not a per-call argument and not ambient state. Starts on the stable surface because
        // that is what an un-negotiated client may legitimately write (the initialize request itself).
        private AcpWireFormat _wire = AcpWireFormat.For(AcpProtocolVersion.V1);

        // Projection rather than a second field: a copy could drift from the contract actually in use,
        // and the two disagreeing is exactly the class of defect this refactor exists to remove.
        private int ProtocolVersion => _wire.Version;
        private AgentInfo? _agentInfo;
        private AgentCapabilities? _agentCapabilities;
        private IReadOnlyList<AuthMethodDefinition>? _authMethods;
        private ClientCapabilities? _clientCapabilities;
        private long _nextMessageId;
        private bool SupportsSessionList => _agentCapabilities?.SupportsSessionList == true;
        private bool SupportsSessionLoad => _agentCapabilities?.SupportsSessionLoading == true;
        private bool SupportsSessionResume => _agentCapabilities?.SupportsSessionResume == true;
        private bool SupportsSessionClose => _agentCapabilities?.SupportsSessionClose == true;
        private bool SupportsSessionDelete => _agentCapabilities?.SupportsSessionDelete == true;
        private bool SupportsSessionAdditionalDirectories => _agentCapabilities?.SupportsSessionAdditionalDirectories == true;
        private bool SupportsAuthenticationSurface => _authMethods is { Count: > 0 };
        private bool SupportsAdvertisedTerminalExecution =>
            ProtocolVersion == AcpProtocolVersion.V1
                && _clientCapabilities?.Terminal == true;
        private bool SupportsLogout =>
            ProtocolVersion == AcpProtocolVersion.V2
                ? SupportsAuthenticationSurface
                : _agentCapabilities?.SupportsLogout == true;

        /// <summary>
        /// Raised when initialization completes.
        /// </summary>
        public event EventHandler<InitializeResponse>? Initialized;

        /// <summary>
        /// Raised when a session update is received.
        /// </summary>
        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        /// <summary>
        /// Raised when a permission request is received.
        /// </summary>
        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;

        /// <summary>
        /// Raised when a file system request is received.
        /// </summary>
        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;

        /// <summary>
        /// Raised when a terminal request is received.
        /// </summary>
        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived;

        /// <summary>
        /// Raised when terminal state changes.
        /// </summary>
        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived;

        /// <summary>
        /// Raised when an ask-user request is received.
        /// </summary>
        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived;

        /// <inheritdoc />
        public event EventHandler<ElicitationRequestEventArgs>? ElicitationRequestReceived;

        /// <inheritdoc />
        public event EventHandler<ElicitationCompletedEventArgs>? ElicitationCompleted;

        /// <summary>
        /// Raised when a connection error occurs.
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Gets a value indicating whether the client has been initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Gets a value indicating whether the client is connected to the agent.
        /// </summary>
        public bool IsConnected => _transport.IsConnected;

        /// <summary>
        /// Gets the current agent information.
        /// </summary>
        public AgentInfo? AgentInfo => _agentInfo;

        /// <summary>
        /// Gets the current agent capabilities.
        /// </summary>
        public AgentCapabilities? AgentCapabilities => _agentCapabilities;

        /// <summary>
        /// Creates a new <see cref="AcpClient"/> instance.
        /// </summary>
        /// <param name="transport">The transport used to exchange messages with the agent.</param>
        /// <param name="logger">Optional logger for client diagnostics.</param>
        /// <param name="sessionStore">Optional store consulted for session state.</param>
        /// <param name="terminalSessionManager">Optional manager that services terminal requests.</param>
        public AcpClient(
            IAcpTransport transport,
            IAcpClientLogger? logger = null,
            IAcpClientSessionStore? sessionStore = null,
            IAcpTerminalSessionManager? terminalSessionManager = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _parser = new MessageParser();
            _validator = new MessageValidator();
            _sessionStore = sessionStore ?? new InMemoryAcpClientSessionStore();
            _terminalSessionManager = terminalSessionManager ?? new UnsupportedAcpTerminalSessionManager();
            _logger = logger ?? new NullAcpClientLogger();

            // Subscribe to transport events.
            _transport.MessageReceived += OnMessageReceived;
            _transport.ErrorOccurred += OnTransportError;
        }

        /// <summary>
        /// Initializes the connection to the agent.
        /// </summary>
        public Task<InitializeResponse> InitializeAsync(InitializeParams @params, CancellationToken cancellationToken = default)
            => InitializeCoreAsync(@params, allowDraftRuntime: false, cancellationToken);

        // Assembly-internal staging seam: deterministic protocol peers exercise the actual parser,
        // dispatch and lifecycle before the full draft can be enabled by a future feature gate.
        internal Task<InitializeResponse> InitializeDraftAsync(InitializeParams @params, CancellationToken cancellationToken = default)
            => InitializeCoreAsync(@params, allowDraftRuntime: true, cancellationToken);

        internal SessionWorkSnapshot? GetSessionWorkSnapshot(string sessionId)
            => _sessionWork.GetSnapshot(sessionId);

        internal AcpSessionSnapshot? GetDraftSessionSnapshot(string sessionId)
        {
            lock (_lock)
            {
                return _isInitialized && _wire.Version == AcpProtocolVersion.V2
                    ? _sessionWork.GetProjectionSnapshot(sessionId)
                    : null;
            }
        }

        private async Task<InitializeResponse> InitializeCoreAsync(
            InitializeParams @params,
            bool allowDraftRuntime,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(@params);
            // A modeled version is not a served version: the SDK carries draft v2 wire contracts so they
            // can be developed and asserted, while this runtime runs exactly one version end to end.
            // Ask the single authority rather than spelling out which version that is here.
            if (AcpProtocolVersion.IsSupported(@params.ProtocolVersion)
                && !AcpProtocolVersion.IsRuntimeServed(@params.ProtocolVersion)
                && !allowDraftRuntime)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    StableV1RuntimeOnlyMessage);
            }

            InitializeClientProtocolPolicy.Validate(@params.ProtocolVersion, @params.ClientCapabilities);

            Task<bool>? previousDisconnect;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                previousDisconnect = _disconnectTask;
                if (previousDisconnect is { IsCompletedSuccessfully: false })
                {
                    throw new InvalidOperationException(DisconnectIncompleteMessage);
                }
            }

            if (previousDisconnect is not null && !await previousDisconnect.ConfigureAwait(false))
            {
                throw new InvalidOperationException(DisconnectIncompleteMessage);
            }

            CancellationToken connectionToken;
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!ReferenceEquals(_disconnectTask, previousDisconnect))
                {
                    throw new InvalidOperationException(DisconnectIncompleteMessage);
                }

                if (_isInitialized)
                {
                    throw new InvalidOperationException("ACP client is already initialized.");
                }
                if (_messageLoopCts is not null)
                {
                    throw new InvalidOperationException("ACP client initialization is already in progress.");
                }
                // The initialize request belongs to this connection too. Keep this same owner
                // through the successful handshake instead of introducing it only afterwards.
                _messageLoopCts = new CancellationTokenSource();
                connectionToken = _messageLoopCts.Token;
                _sessionWork.BeginConnection(connectionToken);
                AttachConnectionMessageHandler(connectionToken);
            }

            InitializeResponse initializeResponse;
            try
            {
                initializeResponse = await InitializeConnectionAsync(@params, allowDraftRuntime, connectionToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                lock (_lock)
                {
                    if (_messageLoopCts?.Token == connectionToken)
                    {
                        ResetConnectionState();
                        CancelPendingRequests();
                    }
                }
                throw;
            }

            Initialized?.Invoke(this, initializeResponse);
            return initializeResponse;
        }

        private async Task<InitializeResponse> InitializeConnectionAsync(
            InitializeParams @params,
            bool allowDraftRuntime,
            CancellationToken connectionToken,
            CancellationToken cancellationToken)
        {
            // Make sure the transport is connected.
            if (!_transport.IsConnected)
            {
                ClearLastTransportError();
                var connected = await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
                if (!connected)
                {
                    throw new InvalidOperationException(CreateTransportConnectFailureMessage());
                }
            }

            // Send the initialize request.
            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "initialize",
                ToElement<InitializeParams>(@params));
            var response = await SendRequestAsync(request, cancellationToken, connectionToken).ConfigureAwait(false);

            // Validate the response.
            var validationResult = _validator.ValidateResponse(response);
            if (!validationResult.IsValid)
            {
                throw new AcpException(JsonRpcErrorCode.InvalidRequest, $"Response validation failed: {string.Join("; ", validationResult.Errors)}");
            }

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // Parse the response.
            var initializeResponse = FromElement<InitializeResponse>(response.Result!.Value);
            if (initializeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse initialize response");
            }

            var serverVersion = initializeResponse.ProtocolVersion;
            var clientVersion = @params.ProtocolVersion;

            // ACP lets the Agent answer with an older version than requested, and requires the Client to
            // close the connection when it does not support what the Agent chose. Being able to parse a
            // version is not the same as being able to run it, so the downgrade target must be one this
            // runtime actually serves; otherwise the connection would proceed under a version whose
            // lifecycle is unimplemented. serverVersion > clientVersion stays rejected separately: an
            // Agent must never answer above what the Client asked for.
            if ((!AcpProtocolVersion.IsRuntimeServed(serverVersion)
                    && !(allowDraftRuntime && serverVersion == AcpProtocolVersion.V2))
                || serverVersion > clientVersion)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    $"Protocol version mismatch. Expected by client: {clientVersion}, Server: {serverVersion}");
            }

            lock (_lock)
            {
                connectionToken.ThrowIfCancellationRequested();
                // An old handshake may finish after a disconnect and a newer initialize. Its
                // result cannot replace the newer connection's capabilities or cancellation owner.
                if (_messageLoopCts?.Token != connectionToken)
                {
                    throw new OperationCanceledException("The ACP connection changed during initialization.");
                }
                _wire = AcpWireFormat.For(serverVersion);
                _agentInfo = initializeResponse.AgentInfo;
                _agentCapabilities = initializeResponse.AgentCapabilities;
                _authMethods = initializeResponse.AuthMethods;
                _clientCapabilities = @params.ClientCapabilities;
                _isInitialized = true;
                _ = MonitorTransportConnectionAsync(connectionToken);
            }

            return initializeResponse;
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        public async Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            var connectionToken = GetConnectionToken();
            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/new");
            ValidateAdditionalDirectories(@params.AdditionalDirectories, "session/new");
            EnsureMcpServersSupported(@params.McpServers, "session/new");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/new",
                ToElement<SessionNewParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var sessionNewResponse = FromElement<SessionNewResponse>(response.Result!.Value);
            if (sessionNewResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/new response");
            }

            // A session/update notification can arrive before the session/new response and
            // create the local tracking entry first. The response is authoritative, but the
            // local cache write must remain idempotent.
            if (!_sessionStore.ContainsSession(sessionNewResponse.SessionId))
            {
                await _sessionStore.CreateSessionAsync(sessionNewResponse.SessionId, @params.Cwd).ConfigureAwait(false);
            }
            RegisterSessionWork(sessionNewResponse.SessionId, connectionToken);

            return sessionNewResponse;
        }

        /// <summary>
        /// Loads an existing session.
        /// </summary>
        public async Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            var connectionToken = GetConnectionToken();
            if (!SupportsSessionLoad)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "SESSION_LOAD_UNSUPPORTED",
                    "Agent does not support session/load capability",
                    nameof(LoadSessionAsync));

                return SessionLoadResponse.Completed;
            }

            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/load");
            ValidateAdditionalDirectories(@params.AdditionalDirectories, "session/load");
            EnsureMcpServersSupported(@params.McpServers, "session/load");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/load",
                ToElement<SessionLoadParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // A successful session/load means the agent has acknowledged the session, so register it in
            // the local store. Otherwise the existence fast-fail in session/prompt would locally reject
            // the official load -> prompt flow as SessionNotFound.
            await RegisterSessionAsync(@params.SessionId, @params.Cwd, connectionToken).ConfigureAwait(false);

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SessionLoadResponse.Completed;
            }

            var sessionLoadResponse = FromElement<SessionLoadResponse>(response.Result.Value);

            return sessionLoadResponse ?? SessionLoadResponse.Completed;
        }

        /// <summary>
        /// Resumes an existing session. Omitting <see cref="SessionResumeParams.ReplayFrom"/> requests no
        /// history replay; setting <c>replayFrom: { type: "start" }</c> requests a full history replay (V2).
        /// </summary>
        public async Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            var connectionToken = GetConnectionToken();
            ArgumentNullException.ThrowIfNull(@params);
            if (ProtocolVersion == AcpProtocolVersion.V1 && @params.ReplayFrom is not null)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    "session/resume replayFrom is only available in ACP v2.");
            }

            if (!SupportsSessionResume)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "SESSION_RESUME_UNSUPPORTED",
                    "Agent does not support session/resume capability",
                    nameof(ResumeSessionAsync));

                return SessionResumeResponse.Completed;
            }

            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/resume");
            ValidateAdditionalDirectories(@params.AdditionalDirectories, "session/resume");
            EnsureMcpServersSupported(@params.McpServers, "session/resume");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/resume",
                ToElement<SessionResumeParams>(@params));

            var response = await SendSessionResumeRequestAsync(
                request, @params, connectionToken, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // The agent has acknowledged the resumed session, so register the local tracking entry. This
            // keeps the existence fast-fail gate in SendPromptAsync from misreporting the official
            // resume -> prompt flow as SessionNotFound.
            await RegisterSessionAsync(@params.SessionId, @params.Cwd, connectionToken).ConfigureAwait(false);

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SessionResumeResponse.Completed;
            }

            var sessionResumeResponse = FromElement<SessionResumeResponse>(response.Result.Value);

            return sessionResumeResponse ?? SessionResumeResponse.Completed;
        }

        private async Task<JsonRpcResponse> SendSessionResumeRequestAsync(
            JsonRpcRequest request,
            SessionResumeParams @params,
            CancellationToken connectionToken,
            CancellationToken cancellationToken)
        {
            if (ProtocolVersion != AcpProtocolVersion.V2 || !RequestsFullReplay(request))
            {
                return await SendRequestAsync(request, cancellationToken, connectionToken).ConfigureAwait(false);
            }

            AcpSessionProjection? replay = null;
            try
            {
                return await SendRequestAsync(
                    request,
                    cancellationToken,
                    connectionToken,
                    responseObserver: _ => _sessionWork.EndReplay(@params.SessionId, replay, connectionToken),
                    beforeSend: () => replay = _sessionWork.BeginReplay(@params.SessionId, connectionToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && replay is not null)
            {
                // Cancelling the wait cannot retract streamed replay. Its response or disconnect
                // releases the claim; another full replay must not interleave uncorrelated updates.
                throw;
            }
            catch
            {
                _sessionWork.EndReplay(@params.SessionId, replay, connectionToken);
                throw;
            }
        }

        private static bool RequestsFullReplay(JsonRpcRequest request)
            => request.Params is { } parameters
                && parameters.TryGetProperty("replayFrom", out var cursor)
                && cursor.ValueKind == JsonValueKind.Object
                && cursor.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.ValueEquals("start");

        /// <summary>
        /// Closes an existing session and releases the resources held on the agent side.
        /// </summary>
        public async Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            var connectionToken = GetConnectionToken();

            if (!SupportsSessionClose)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "SESSION_CLOSE_UNSUPPORTED",
                    "Agent does not support session/close capability",
                    nameof(CloseSessionAsync));

                RemoveSession(@params.SessionId, connectionToken);
                return SessionCloseResponse.Completed;
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/close",
                ToElement<SessionCloseParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                RemoveSession(@params.SessionId, connectionToken);
                return SessionCloseResponse.Completed;
            }

            var sessionCloseResponse = FromElement<SessionCloseResponse>(response.Result.Value);

            RemoveSession(@params.SessionId, connectionToken);
            return sessionCloseResponse ?? SessionCloseResponse.Completed;
        }

        /// <summary>
        /// Deletes a session on the remote agent.
        /// </summary>
        public async Task<SessionDeleteResponse> DeleteSessionAsync(SessionDeleteParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            var connectionToken = GetConnectionToken();

            if (!SupportsSessionDelete)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "SESSION_DELETE_UNSUPPORTED",
                    "Agent does not support session/delete capability",
                    nameof(DeleteSessionAsync));

                return SessionDeleteResponse.Completed;
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/delete",
                ToElement<SessionDeleteParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                RemoveSession(@params.SessionId, connectionToken);
                return SessionDeleteResponse.Completed;
            }

            var sessionDeleteResponse = FromElement<SessionDeleteResponse>(response.Result.Value);

            RemoveSession(@params.SessionId, connectionToken);
            return sessionDeleteResponse ?? SessionDeleteResponse.Completed;
        }

        /// <summary>
        /// Lists the sessions reported by the remote agent.
        /// </summary>
        public async Task<SessionListResponse> ListSessionsAsync(SessionListParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            ValidateOptionalAbsolutePath(@params.Cwd, "cwd", "session/list");

            if (!SupportsSessionList)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "SESSION_LIST_UNSUPPORTED",
                    "Agent does not support session/list capability",
                    nameof(ListSessionsAsync));

                return new SessionListResponse();
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/list",
                ToElement<SessionListParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var listResponse = FromElement<SessionListResponse>(response.Result!.Value);
            if (listResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/list response");
            }

            ValidateSessionListResponse(listResponse);
            return listResponse;
        }

        /// <summary>
        /// Sends a prompt and waits for the session's foreground work to finish.
        /// </summary>
        public async Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            ArgumentNullException.ThrowIfNull(@params);
            cancellationToken.ThrowIfCancellationRequested();

            // Check whether the session exists.
            if (!_sessionStore.ContainsSession(@params.SessionId))
            {
                throw new AcpException(JsonRpcErrorCode.SessionNotFound, $"Session '{@params.SessionId}' not found");
            }

            EnsurePromptContentAllowed(@params);

            SessionPromptOperation prompt;
            JsonRpcRequest request;
            lock (_lock)
            {
                EnsureInitialized();
                var connectionToken = _messageLoopCts!.Token;
                request = new JsonRpcRequest(
                    Interlocked.Increment(ref _nextMessageId),
                    "session/prompt",
                    ToElement<SessionPromptParams>(@params));
                prompt = _sessionWork.BeginPrompt(@params.SessionId, _wire, connectionToken);
            }

            // Record acceptance synchronously in response dispatch. An asynchronous continuation
            // could otherwise run after the very next state_update and lose an immediate idle.
            await SendRequestAsync(request, cancellationToken, prompt.ConnectionToken,
                response => _sessionWork.ReceivePromptResponse(prompt, response),
                error => _sessionWork.FailPrompt(prompt, error)).ConfigureAwait(false);
            var completion = await prompt.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            return completion.GetResponse();
        }

        // spec MUST: the client must restrict the content types it sends to the promptCapabilities
        // negotiated during initialize (image -> SupportsImage, audio -> SupportsAudio,
        // resource -> SupportsEmbeddedContext). text and resource_link are the unconditional baseline and
        // are always allowed; unknown discriminator values pass through for the agent to decide and are
        // not tightened here. Failing fast before sending avoids putting content the agent explicitly
        // does not support on the wire.
        private void EnsurePromptContentAllowed(SessionPromptParams @params)
        {
            var prompt = @params.Prompt;
            if (prompt == null)
            {
                return;
            }

            foreach (var block in prompt)
            {
                switch (block)
                {
                    case ImageContentBlock when !(_agentCapabilities?.SupportsImage ?? false):
                        throw new AcpException(
                            JsonRpcErrorCode.InvalidParams,
                            "Agent did not advertise the image prompt capability; image content cannot be sent.");
                    case AudioContentBlock when !(_agentCapabilities?.SupportsAudio ?? false):
                        throw new AcpException(
                            JsonRpcErrorCode.InvalidParams,
                            "Agent did not advertise the audio prompt capability; audio content cannot be sent.");
                    case ResourceContentBlock when !(_agentCapabilities?.SupportsEmbeddedContext ?? false):
                        throw new AcpException(
                            JsonRpcErrorCode.InvalidParams,
                            "Agent did not advertise the embeddedContext prompt capability; embedded resource content cannot be sent.");
                }
            }
        }

        /// <summary>
        /// Sets the session mode.
        /// </summary>
        public async Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/set_mode",
                ToElement<SessionSetModeParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var setModeResponse = FromElement<SessionSetModeResponse>(response.Result!.Value);
            if (setModeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/set_mode response");
            }

            // Update the cached session mode.
            _sessionStore.UpdateCurrentMode(@params.SessionId, @params.ModeId);

            return setModeResponse;
        }

        /// <summary>
        /// Sets a session configuration option.
        /// </summary>
        public async Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/set_config_option",
                ToElement<SessionSetConfigOptionParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var configResponse = FromElement<SessionSetConfigOptionResponse>(response.Result!.Value);
            if (configResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/set_config_option response");
            }

            return configResponse;
        }

        /// <summary>
        /// Cancels a session.
        /// </summary>
        public async Task CancelSessionAsync(SessionCancelParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            if (@params == null)
            {
                throw new ArgumentNullException(nameof(@params));
            }

            if (string.IsNullOrWhiteSpace(@params.SessionId))
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    "session/cancel requires 'sessionId'.");
            }

            var connectionToken = GetConnectionToken();
            using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionToken);
            Task<SessionPromptCompletion>? cancellationCompletion;
            Task<bool> sendTask;
            lock (_lock)
            {
                connectionToken.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();
                if (_messageLoopCts?.Token != connectionToken)
                {
                    throw new OperationCanceledException("The ACP connection changed before cancelling the session.");
                }
                EnsureInitialized();
                var notification = new JsonRpcNotification(
                    "session/cancel",
                    ToElement<SessionCancelParams>(@params));

                cancellationCompletion = ProtocolVersion == AcpProtocolVersion.V2
                    ? _sessionWork.RequestCancellation(@params.SessionId, connectionToken)
                    : null;
                // Claim the send before a disconnect can replace the connection. The linked token
                // also prevents a queued physical write from reaching that replacement connection.
                sendTask = _transport.SendMessageAsync(_parser.SerializeMessage(notification), sendCancellation.Token);
            }
            var sent = await sendTask.ConfigureAwait(false);
            connectionToken.ThrowIfCancellationRequested();
            if (!sent)
            {
                throw new InvalidOperationException(CreateTransportSendFailureMessage("session/cancel"));
            }

            await CancelPendingInboundRequestsForSessionAsync(@params.SessionId, connectionToken).ConfigureAwait(false);
            if (cancellationCompletion is not null)
            {
                var completion = await cancellationCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);
                completion.GetResponse();
            }
            Task<bool> cancelSessionTask;
            lock (_lock)
            {
                connectionToken.ThrowIfCancellationRequested();
                if (_messageLoopCts?.Token != connectionToken)
                {
                    throw new OperationCanceledException("The ACP connection changed while cancelling the session.");
                }
                cancelSessionTask = _sessionStore.CancelSessionAsync(@params.SessionId);
            }
            await cancelSessionTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Performs authentication.
        /// </summary>
        public async Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (!SupportsAuthenticationSurface)
            {
                throw new AcpException(
                    JsonRpcErrorCode.MethodNotAllowed,
                    "Agent does not advertise authentication methods");
            }

            // The ACP schema requires methodId to name a method advertised during initialize, and forbids
            // passing an AuthMethodTerminal to authenticate. Enforce both here so a non-compliant
            // advertisement or a caller that skipped discrimination cannot put a forbidden id on the wire.
            var advertised = _authMethods?
                .FirstOrDefault(method => string.Equals(method.Id, @params.MethodId, StringComparison.Ordinal));
            if (advertised is null)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    $"Authentication method '{@params.MethodId}' was not advertised by the agent");
            }

            if (!advertised.SupportsAuthenticateRequest)
            {
                throw new AcpException(
                    JsonRpcErrorCode.MethodNotAllowed,
                    $"Authentication method '{@params.MethodId}' has type '{advertised.ResolvedType}', which must not be passed to authenticate");
            }

            var methodName = ProtocolVersion == AcpProtocolVersion.V2 ? "auth/login" : "authenticate";

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                methodName,
                ToElement<AuthenticateParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var authResponse = FromElement<AuthenticateResponse>(response.Result!.Value);
            if (authResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse authenticate response");
            }

            return authResponse;
        }

        /// <summary>
        /// Logs out of the current authenticated state.
        /// </summary>
        public async Task<LogoutResponse> LogoutAsync(LogoutParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (!SupportsLogout)
            {
                throw new AcpException(
                    JsonRpcErrorCode.MethodNotAllowed,
                    "Agent does not support logout capability");
            }

            var methodName = ProtocolVersion == AcpProtocolVersion.V2 ? "auth/logout" : "logout";

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                methodName,
                ToElement<LogoutParams>(@params));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return LogoutResponse.Completed;
            }

            return FromElement<LogoutResponse>(response.Result.Value)
                ?? LogoutResponse.Completed;
        }

        /// <summary>
        /// Responds to a permission request.
        /// </summary>
        public async Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
        {
            return await TrySendPermissionOutcomeResponseAsync(messageId, outcome, optionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Responds to a file system request.
        /// </summary>
        public async Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
        {
            return await TrySendFileSystemResponseAsync(messageId, success, content, message).ConfigureAwait(false);
        }

        /// <summary>
        /// Responds to an ask-user request.
        /// </summary>
        public async Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        {
            if (answers == null)
            {
                throw new ArgumentNullException(nameof(answers));
            }

            return await TrySendAskUserResponseAsync(messageId, answers).ConfigureAwait(false);
        }

        /// <summary>
        /// Accepts an elicitation request, optionally submitting form content.
        /// </summary>
        public async Task<bool> RespondToElicitationRequestAsync(
            object messageId,
            ElicitationAcceptContent? content)
        {
            return await TrySendElicitationResponseAsync(
                messageId,
                new ElicitationAcceptResponse { Content = content?.ToWireContent() }).ConfigureAwait(false);
        }

        /// <summary>
        /// Declines an elicitation request on the user's behalf.
        /// </summary>
        public async Task<bool> DeclineElicitationRequestAsync(object messageId)
        {
            return await TrySendElicitationResponseAsync(
                messageId,
                new ElicitationDeclineResponse()).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels an elicitation request the user dismissed without choosing.
        /// </summary>
        public async Task<bool> CancelElicitationRequestAsync(object messageId)
        {
            return await TrySendElicitationResponseAsync(
                messageId,
                new ElicitationCancelResponse()).ConfigureAwait(false);
        }

        private Task<bool> TrySendElicitationResponseAsync(
            object messageId,
            CreateElicitationResponse response)
        {
            var idStr = RequestKey(messageId);
            return TryGetPendingInboundRequest(idStr, out var pending)
                ? TrySendElicitationResponseAsync(pending, response)
                : Task.FromResult(false);
        }

        private async Task<bool> TrySendElicitationResponseAsync(
            PendingInboundRequest pending,
            CreateElicitationResponse response,
            bool cancelForSession = false)
        {
            var idStr = RequestKey(pending.MessageId);
            CancellationToken connectionCancellation;
            lock (_lock)
            {
                if (_disposed || !_transport.IsConnected || pending.ElicitationRequest is null
                    || !TryGetPendingInboundRequest(idStr, out var current)
                    || !ReferenceEquals(current, pending)
                    || (pending.PreparedCancellationResponse is not null && response is not ElicitationCancelResponse))
                {
                    return false;
                }

                if (cancelForSession)
                {
                    pending.IsElicitationCancellationRequested = true;
                }

                if (pending.IsElicitationResponseInFlight
                    || (pending.IsElicitationCancellationRequested && response is not ElicitationCancelResponse))
                {
                    return false;
                }

                // Claim without removing: a failed send must remain retryable, while a second action
                // cannot race the first one onto the wire. The callback owns this exact request object,
                // so reusing its JSON-RPC id never lets an old form answer a later request.
                pending.IsElicitationResponseInFlight = true;
                pending.PreparedElicitationResponse = response;
                pending.HasPreparedElicitationFailure = false;
                connectionCancellation = _messageLoopCts?.Token ?? CancellationToken.None;
            }

            var sent = false;
            try
            {
                sent = await SendResponseAsync(new JsonRpcResponse(
                    pending.MessageId,
                    ToElement<CreateElicitationResponse>(response)), connectionCancellation).ConfigureAwait(false);
                return sent;
            }
            finally
            {
                if (CompleteElicitationResponse(pending, response, sent))
                {
                    await SendPendingElicitationCancellationAsync(pending, connectionCancellation).ConfigureAwait(false);
                }
            }
        }

        private bool CompleteElicitationResponse(PendingInboundRequest pending, CreateElicitationResponse? response, bool sent)
        {
            var idStr = RequestKey(pending.MessageId);
            lock (_lock)
            {
                if (sent)
                {
                    _pendingInboundRequests.TryRemove(new KeyValuePair<AcpRequestId, PendingInboundRequest>(idStr, pending));
                    if (response is not ElicitationAcceptResponse)
                    {
                        RemovePendingUrlElicitation(pending);
                    }
                }
                else if (pending.IsElicitationCancellationRequested && response is not ElicitationCancelResponse
                    && !_disposed && _transport.IsConnected
                    && TryGetPendingInboundRequest(idStr, out var current) && ReferenceEquals(current, pending))
                {
                    // Session cancellation must survive a failed in-flight answer. Keep the claim
                    // for exactly one cancel send; another user action cannot overtake this intent.
                    return true;
                }

                pending.IsElicitationResponseInFlight = false;
                return false;
            }
        }

        private async Task SendPendingElicitationCancellationAsync(PendingInboundRequest pending, CancellationToken cancellationToken)
        {
            var response = new ElicitationCancelResponse();
            var sent = false;
            try
            {
                sent = await SendResponseAsync(new JsonRpcResponse(
                    pending.MessageId,
                    ToElement<CreateElicitationResponse>(response)), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // A failed cancellation remains explicitly retryable; it never schedules another send.
                CompleteElicitationResponse(pending, response, sent);
            }
        }

        private async Task TrySendElicitationFailureResponseAsync(PendingInboundRequest pending)
        {
            Task<bool> responseTask;
            lock (_lock)
            {
                if (!TryGetPendingInboundRequest(RequestKey(pending.MessageId), out var current)
                    || !ReferenceEquals(current, pending) || !IsInboundRequestCurrent(pending)
                    || pending.IsElicitationResponseInFlight || pending.IsElicitationCancellationRequested
                    || pending.PreparedCancellationResponse is not null)
                {
                    return;
                }

                // Host failures share the original response claim. They cannot overwrite a reply
                // already sent, consume a replacement id, or escape onto a later connection.
                pending.IsElicitationResponseInFlight = true;
                pending.HasPreparedElicitationFailure = true;
                responseTask = SendResponseAsync(new JsonRpcResponse(pending.MessageId,
                    JsonRpcError.CreateInternalError("Failed to process elicitation/create request.")),
                    pending.ConnectionToken);
            }

            var sent = false;
            try
            {
                sent = await responseTask.ConfigureAwait(false);
            }
            finally
            {
                if (CompleteElicitationResponse(pending, response: null, sent))
                {
                    await SendPendingElicitationCancellationAsync(pending, pending.ConnectionToken).ConfigureAwait(false);
                }
            }
        }

        private void RemovePendingUrlElicitation(PendingInboundRequest pending)
        {
            if (pending.ElicitationRequest is UrlElicitationRequest url
                && _pendingUrlElicitations.TryGetValue(url.ElicitationId, out var current)
                && ReferenceEquals(current, pending))
            {
                _pendingUrlElicitations.Remove(url.ElicitationId);
            }
        }

        private async Task<bool> TrySendPermissionOutcomeResponseAsync(
            object? messageId,
            string outcome,
            string? optionId,
            PendingInboundRequest? expectedRequest = null,
            bool cancelForSession = false)
        {
            if (messageId == null)
            {
                return false;
            }

            // Only respond once per inbound request id. Unknown or stale ids are not a
            // protocol payload, so they should not fail ACP schema validation.
            var idStr = RequestKey(messageId);

            PendingInboundRequest pending;
            Task<bool> responseTask;
            lock (_lock)
            {
                if (!TryGetPendingInboundRequest(idStr, out pending)
                    || (expectedRequest is not null && !ReferenceEquals(pending, expectedRequest))
                    || pending.Method != "session/request_permission" || !IsInboundRequestCurrent(pending)
                    || (pending.PreparedCancellationResponse is not null && outcome != "cancelled"))
                {
                    return false;
                }
                if (cancelForSession || outcome == "cancelled")
                {
                    pending.IsPermissionCancellationRequested = true;
                    pending.PermissionEvent?.NotifyChanged();
                }
                if (outcome == "cancelled" && pending.MessageId is not null
                    && _responseRoutes.TryGetValue(pending.MessageId, out var route) && route.Batch is { } batch)
                {
                    // A prepared slot is still owned by this batch. Withdraw it before waiting for
                    // sibling inputs; the immutable in-flight array may already be the single reply.
                    pending.PreparedCancellationResponse ??= new JsonRpcResponse(pending.MessageId,
                        ToElement<PermissionOutcomeResult>(CreatePermissionOutcome(pending, "cancelled", null)));
                    pending.PreparedPermissionResponse = pending.PreparedCancellationResponse;
                    pending.IsPermissionResponseInFlight = true;
                    responseTask = batch.SubmitCancellation(route.Index, pending.PreparedCancellationResponse);
                }
                else
                {
                    if (pending.IsPermissionResponseInFlight
                        || (pending.IsPermissionCancellationRequested && outcome != "cancelled"))
                    {
                        return false;
                    }

                    // Invalid UI choices must not consume the request or enable a second response.
                    // Offered ids are captured before consumers can mutate their DTO lists.
                    pending.PreparedPermissionResponse = pending.PreparedCancellationResponse
                        ?? new JsonRpcResponse(pending.MessageId,
                            ToElement<PermissionOutcomeResult>(CreatePermissionOutcome(pending, outcome, optionId)));
                    pending.IsPermissionResponseInFlight = true;
                    responseTask = SendResponseAsync(pending.PreparedPermissionResponse, pending.ConnectionToken);
                }
                pending.PermissionEvent?.NotifyChanged();
            }

            return await AwaitPermissionResponseAsync(pending, responseTask, outcome == "cancelled").ConfigureAwait(false);
        }

        private async Task<bool> TrySendPermissionFailureResponseAsync(PendingInboundRequest pending)
        {
            Task<bool> responseTask;
            lock (_lock)
            {
                if (!TryGetPendingInboundRequest(RequestKey(pending.MessageId), out var current)
                    || !ReferenceEquals(current, pending) || !IsInboundRequestCurrent(pending)
                    || pending.IsPermissionResponseInFlight || pending.IsPermissionCancellationRequested
                    || pending.PreparedCancellationResponse is not null)
                {
                    return false;
                }
                pending.IsPermissionResponseInFlight = true;
                pending.PreparedPermissionResponse = new JsonRpcResponse(pending.MessageId,
                    JsonRpcError.CreateInternalError("Client failed to process inbound permission request."));
                responseTask = SendResponseAsync(pending.PreparedPermissionResponse, pending.ConnectionToken);
                pending.PermissionEvent?.NotifyChanged();
            }

            return await AwaitPermissionResponseAsync(pending, responseTask, isCancellation: false).ConfigureAwait(false);
        }

        private async Task<bool> AwaitPermissionResponseAsync(PendingInboundRequest pending, Task<bool> responseTask, bool isCancellation)
        {
            var sent = false;
            try
            {
                sent = await responseTask.ConfigureAwait(false);
                return sent;
            }
            finally
            {
                if (CompletePermissionResponse(pending, isCancellation, sent))
                {
                    await TrySendPermissionOutcomeResponseAsync(pending.MessageId, "cancelled", null,
                        pending, cancelForSession: true).ConfigureAwait(false);
                }
            }
        }

        private static PermissionOutcomeResult CreatePermissionOutcome(PendingInboundRequest pending, string outcome, string? optionId)
        {
            if (outcome == "selected")
            {
                if (optionId is null || pending.PermissionOptionIds?.Contains(optionId) != true)
                {
                    throw new AcpException(JsonRpcErrorCode.InvalidParams, "Permission outcome 'selected' requires an offered optionId.");
                }
            }
            else if (outcome != "cancelled")
            {
                throw new AcpException(JsonRpcErrorCode.InvalidParams, $"Unsupported permission outcome '{outcome}'.");
            }
            return new PermissionOutcomeResult
            {
                Outcome = new PermissionOutcome
                {
                    Outcome = outcome,
                    OptionId = outcome == "selected" ? optionId : null
                }
            };
        }

        private bool CompletePermissionResponse(PendingInboundRequest pending, bool isCancellation, bool sent)
        {
            lock (_lock)
            {
                pending.IsPermissionResponseInFlight = false;
                if (!TryGetPendingInboundRequest(RequestKey(pending.MessageId), out var current)
                    || !ReferenceEquals(current, pending))
                {
                    return false;
                }
                if (sent)
                {
                    _pendingInboundRequests.TryRemove(new KeyValuePair<AcpRequestId, PendingInboundRequest>(
                        RequestKey(pending.MessageId), pending));
                    pending.PermissionEvent?.NotifyChanged();
                    return false;
                }
                pending.PermissionEvent?.NotifyChanged();
                // A cancellation that arrived while the user's answer was in flight still owns the
                // next attempt. A failed cancellation stays retryable without a background retry loop.
                return !IsBatchedResponse(pending) && !isCancellation && pending.IsPermissionCancellationRequested
                    && IsInboundRequestCurrent(pending);
            }
        }

        private bool IsInboundRequestCurrent(PendingInboundRequest pending)
            => !_disposed && _transport.IsConnected && !pending.ConnectionToken.IsCancellationRequested
                && (pending.ConnectionToken.CanBeCanceled
                    ? _messageLoopCts?.Token == pending.ConnectionToken
                    : _messageLoopCts is null);

        private bool CanRespondToPermissionRequest(PendingInboundRequest pending)
        {
            lock (_lock)
            {
                return IsInboundRequestCurrent(pending)
                    && TryGetPendingInboundRequest(RequestKey(pending.MessageId), out var current)
                    && ReferenceEquals(current, pending);
            }
        }

        private bool IsPermissionResponsePrepared(PendingInboundRequest pending)
        {
            lock (_lock)
            {
                if (!CanRespondToPermissionRequest(pending)) return false;
                return pending.MessageId is not null && _responseRoutes.TryGetValue(pending.MessageId, out var route)
                    && route.Batch is { } batch
                    ? batch.IsResponsePrepared(route.Index)
                    : pending.IsPermissionResponseInFlight;
            }
        }

        private bool IsPermissionCancellationRequested(PendingInboundRequest pending)
        {
            lock (_lock)
            {
                return pending.IsPermissionCancellationRequested || pending.PreparedCancellationResponse is not null;
            }
        }

        private bool IsPermissionResponseSending(PendingInboundRequest pending)
        {
            lock (_lock)
            {
                if (!CanRespondToPermissionRequest(pending)) return false;
                return pending.MessageId is not null && _responseRoutes.TryGetValue(pending.MessageId, out var route)
                    && route.Batch is { } batch ? batch.IsSending : pending.IsPermissionResponseInFlight;
            }
        }

        private async Task<bool> TrySendFileSystemResponseAsync(object messageId, bool success, string? content, string? message)
        {
            var idStr = RequestKey(messageId);
            if (!TryTakePendingInboundRequest(idStr, out var pending))
            {
                return false;
            }

            if (!success)
            {
                // Use a JSON-RPC error instead of a success=false payload (ACP tools follow JSON-RPC semantics).
                var error = new JsonRpcError(
                    JsonRpcErrorCode.PermissionDenied,
                    string.IsNullOrWhiteSpace(message) ? "Permission denied" : message);
                return await SendResponseAsync(new JsonRpcResponse(pending.MessageId, error), pending.ConnectionToken).ConfigureAwait(false);
            }

            JsonElement result;
            if (string.Equals(pending.Method, "fs/read_text_file", StringComparison.Ordinal))
            {
                result = ToElement<ReadTextFileResult>(
                    new ReadTextFileResult { Content = content ?? string.Empty });
            }
            else
            {
                // fs/write_text_file returns null on success.
                result = NullJsonElement();
            }

            return await SendResponseAsync(new JsonRpcResponse(pending.MessageId, result), pending.ConnectionToken).ConfigureAwait(false);
        }

        private Task<bool> TrySendAskUserResponseAsync(object messageId, IReadOnlyDictionary<string, string> answers,
            PendingInboundRequest? expectedRequest = null)
        {
            var idStr = RequestKey(messageId);
            PendingInboundRequest pending;
            JsonRpcResponse response;
            lock (_lock)
            {
                if (!TryGetPendingInboundRequest(idStr, out pending)
                    || (expectedRequest is not null && !ReferenceEquals(pending, expectedRequest))
                    || pending.AskUserRequest is null || !IsCurrentConnection(pending.ConnectionToken)
                    || pending.IsAskUserResponseInFlight || pending.AskUserCancellationResponse is not null
                    || pending.PreparedCancellationResponse is not null)
                {
                    return Task.FromResult(false);
                }

                // Invalid UI answers must leave the same interaction available for correction.
                AskUserContract.ValidateAnswers(pending.AskUserRequest, answers);
                response = new JsonRpcResponse(pending.MessageId,
                    ToElement(new AskUserResponse(pending.AskUserRequest.Questions, answers)));
                pending.IsAskUserResponseInFlight = true;
                pending.PreparedAskUserResponse = response;
            }
            return AwaitAskUserResponseAsync(pending, response);
        }

        private Task<bool> TrySendAskUserFailureResponseAsync(PendingInboundRequest pending, JsonRpcError error,
            bool cancelForSession = false)
        {
            JsonRpcResponse response;
            lock (_lock)
            {
                if (!TryGetPendingInboundRequest(RequestKey(pending.MessageId), out var current)
                    || !ReferenceEquals(current, pending) || !IsCurrentConnection(pending.ConnectionToken)
                    || pending.PreparedCancellationResponse is not null)
                {
                    return Task.FromResult(false);
                }

                response = new JsonRpcResponse(pending.MessageId, error);
                if (cancelForSession)
                {
                    pending.AskUserCancellationResponse = response;
                }
                if (pending.IsAskUserResponseInFlight
                    || (!cancelForSession && pending.PreparedAskUserResponse is not null))
                {
                    return Task.FromResult(false);
                }
                pending.IsAskUserResponseInFlight = true;
                pending.PreparedAskUserResponse = response;
            }
            return AwaitAskUserResponseAsync(pending, response);
        }

        private async Task<bool> AwaitAskUserResponseAsync(PendingInboundRequest pending, JsonRpcResponse response)
        {
            var sent = false;
            try
            {
                sent = await SendResponseAsync(response, pending.ConnectionToken).ConfigureAwait(false);
                return sent;
            }
            finally
            {
                var cancellation = CompleteAskUserResponse(pending, response, sent);
                if (cancellation is not null)
                {
                    // One bounded follow-up preserves a cancel that arrived while a failed write
                    // was in flight. The cancellation response cannot schedule itself again.
                    await AwaitAskUserResponseAsync(pending, cancellation).ConfigureAwait(false);
                }
            }
        }

        private JsonRpcResponse? CompleteAskUserResponse(PendingInboundRequest pending, JsonRpcResponse response, bool sent)
        {
            lock (_lock)
            {
                var key = RequestKey(pending.MessageId);
                if (sent)
                {
                    _pendingInboundRequests.TryRemove(new KeyValuePair<AcpRequestId, PendingInboundRequest>(key, pending));
                }
                else if (pending.AskUserCancellationResponse is { } cancellation && !ReferenceEquals(response, cancellation)
                    && IsCurrentConnection(pending.ConnectionToken)
                    && TryGetPendingInboundRequest(key, out var current) && ReferenceEquals(current, pending))
                {
                    pending.PreparedAskUserResponse = cancellation;
                    return cancellation;
                }
                pending.IsAskUserResponseInFlight = false;
                return null;
            }
        }

        /// <summary>
        /// Disconnects from the agent.
        /// </summary>
        public Task<bool> DisconnectAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (_lock)
            {
                if (_disconnectTask is { IsCompleted: false } pending)
                {
                    return pending;
                }

                if (_disposed)
                {
                    return Task.FromResult(true);
                }

                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                // Publish before cancelling the old connection. A reentrant initializer must not
                // share a transport whose physical teardown is still in flight.
                _disconnectTask = completion.Task;
            }

            _ = CompleteDisconnectAsync(completion);
            return completion.Task;
        }

        private async Task CompleteDisconnectAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                ResetConnectionState();
                CancelPendingRequests();
                var disconnected = await _transport.DisconnectAsync().ConfigureAwait(false);
                completion.TrySetResult(disconnected);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        /// <summary>
        /// Sends a request and awaits its response.
        /// </summary>

        private async Task<JsonRpcResponse> SendRequestAsync(
            JsonRpcRequest request,
            CancellationToken cancellationToken,
            CancellationToken connectionToken = default,
            Action<JsonRpcResponse>? responseObserver = null,
            Action<Exception>? requestNotSentObserver = null,
            Action? beforeSend = null)
        {
            Activity? activity = null;
            CancellationTokenSource? sendCancellation = null;
            var requestIdStr = RequestKey(request.Id);
            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestWriteStarted = false;
            var retainPendingRequest = false;
            Exception? failure = null;

            try
            {
                if (!connectionToken.CanBeCanceled)
                {
                    lock (_lock)
                    {
                        connectionToken = _messageLoopCts?.Token ?? CancellationToken.None;
                    }
                }
                // Host tracing callbacks can throw before the physical send is claimed. They
                // belong to the same failure scope as serialization and transport preparation.
                activity = AcpActivitySources.StartClientRequest(request.Method);
                sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionToken);
                var json = _parser.SerializeMessage(request);
                Task<bool> sendTask;
                lock (_lock)
                {
                    connectionToken.ThrowIfCancellationRequested();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_disposed || _messageLoopCts?.Token != connectionToken)
                    {
                        throw new OperationCanceledException("The ACP connection changed before sending the request.");
                    }
                    ClearLastTransportError();
                    // Lifecycle claims and pending registration must precede synchronous peer replies,
                    // under the same connection lock as starting transport I/O. The linked token also
                    // prevents a transport's delayed write from crossing a subsequent reconnect.
                    beforeSend?.Invoke();
                    _pendingRequests[requestIdStr] = new PendingOutboundRequest(tcs, responseObserver);
                    requestWriteStarted = true;
                    sendTask = _transport.SendMessageAsync(json, sendCancellation.Token);
                }
                var sent = await sendTask.ConfigureAwait(false);
                if (!sent)
                {
                    throw new InvalidOperationException(CreateTransportSendFailureMessage(request.Method));
                }

                using var cancellationRegistration = cancellationToken.Register(
                    static state => ((TaskCompletionSource<JsonRpcResponse>)state!).TrySetCanceled(),
                    tcs);
                var response = await tcs.Task.ConfigureAwait(false);
                if (response.IsError)
                {
                    AcpActivitySources.MarkProtocolError(activity, response.Error!.Code);
                }
                else if (!response.IsSuccess)
                {
                    AcpActivitySources.MarkInvalidResponse(activity);
                }
                else
                {
                    AcpActivitySources.MarkSuccess(activity);
                }

                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A transport may accept a complete peer response before its write await notices
                // cancellation. That terminal result already won the request; do not send a cancel
                // for completed work. Otherwise also cancel the waiter for writes interrupted before
                // the token registration, so a late peer response settles the retained correlation.
                if (!tcs.TrySetCanceled(cancellationToken) && tcs.Task.IsCompletedSuccessfully)
                {
                    var completed = await tcs.Task.ConfigureAwait(false);
                    if (completed.IsError)
                    {
                        AcpActivitySources.MarkProtocolError(activity, completed.Error!.Code);
                    }
                    else
                    {
                        AcpActivitySources.MarkSuccess(activity);
                    }
                    return completed;
                }
                // A disconnection may have faulted the retained waiter while its original write
                // was still in progress. Caller cancellation wins here, but observe that fault.
                if (tcs.Task.IsFaulted)
                {
                    _ = tcs.Task.Exception;
                }
                AcpActivitySources.MarkCancelled(activity);

                // A caller can abandon its own await immediately, but the matching response still
                // belongs in _pendingRequests: ACP requires the peer to send a terminal response
                // for the original request (possibly -32800), and OnMessageReceived owns removing
                // that correlation entry. Do not use the caller's already-cancelled token here —
                // cancelling a request must itself reach the peer.
                if (requestWriteStarted && AcpRequestId.TryFromEnvelopeId(request.Id, out var requestId))
                {
                    retainPendingRequest = true;
                    await SendCancelRequestNotificationAsync(requestId, connectionToken).ConfigureAwait(false);
                }

                failure = new OperationCanceledException(cancellationToken);
                throw failure;
            }
            catch (TaskCanceledException ex)
            {
                var exception = new OperationCanceledException(
                    "ACP request was canceled because the transport disconnected.",
                    ex);
                failure = exception;
                AcpActivitySources.RecordException(activity, exception);
                throw exception;
            }
            catch (Exception ex)
            {
                failure = ex;
                AcpActivitySources.RecordException(activity, ex);
                _logger.Log(
                    AcpClientLogLevel.Error,
                    "REQ_ERROR",
                    $"[AcpClient.SendRequestAsync] Request {requestIdStr} failed: {ex.Message}",
                    "SendRequestAsync",
                    ex);
                throw;
            }
            finally
            {
                if (failure is not null)
                {
                    if (!requestWriteStarted)
                    {
                        requestNotSentObserver?.Invoke(failure);
                    }
                    else if (responseObserver is not null)
                    {
                        // Once transport I/O starts, false or an exception cannot prove that no
                        // bytes reached the peer. Keep prompt acceptance correlated until its
                        // response or disconnect; only the send owner can declare it unsent.
                        retainPendingRequest = true;
                        _ = tcs.Task.ContinueWith(
                            static completed => _ = completed.Exception,
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                }
                if (!retainPendingRequest)
                {
                    _pendingRequests.TryRemove(requestIdStr, out _);
                    if (tcs.Task.IsFaulted)
                    {
                        _ = tcs.Task.Exception;
                    }
                }
                sendCancellation?.Dispose();
                activity?.Dispose();
            }
        }

        /// <summary>
        /// Sends the protocol-level <c>$/cancel_request</c> notification for an already dispatched
        /// outbound request.
        /// </summary>
        /// <remarks>
        /// This is deliberately best-effort: ACP permits a peer to ignore <c>$/</c> notifications,
        /// so a failed cancellation notification must not turn a caller's cancellation into a second
        /// user-facing error. The original request remains pending until its terminal response or a
        /// disconnect resolves it.
        /// </remarks>
        private async Task SendCancelRequestNotificationAsync(AcpRequestId requestId, CancellationToken connectionToken)
        {
            using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
            sendCancellation.CancelAfter(CancellationNotificationTimeout);
            Task<bool>? sendTask = null;
            try
            {
                var notification = new JsonRpcNotification(
                    CancelRequestParams.Method,
                    ToElement<CancelRequestParams>(
                        new CancelRequestParams(requestId)));
                var json = _parser.SerializeMessage(notification);
                lock (_lock)
                {
                    if (connectionToken.IsCancellationRequested || _messageLoopCts?.Token != connectionToken
                        || !_transport.IsConnected)
                    {
                        return;
                    }
                    // Validate ownership and begin the send before ResetConnectionState can hand
                    // this same transport to a new initialize. Its token always belongs to the
                    // original request's connection, never the connection current at catch time.
                    sendTask = _transport.SendMessageAsync(json, AcpTransportSendOptions.DiagnosticOnly,
                        sendCancellation.Token);
                }
                // The token bounds compliant transports; WaitAsync also bounds a legacy transport
                // that ignores it. We retain only a fault observer for that legacy operation below.
                var sent = await sendTask.WaitAsync(sendCancellation.Token).ConfigureAwait(false);
                if (!sent)
                {
                    _logger.Log(
                        AcpClientLogLevel.Warning,
                        "CANCEL_REQUEST_SEND_FAILED",
                        "Failed to send the best-effort $/cancel_request notification.",
                        nameof(SendCancelRequestNotificationAsync));
                }
            }
            catch (Exception ex)
            {
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "CANCEL_REQUEST_SEND_FAILED",
                    "Failed to send the best-effort $/cancel_request notification.",
                    nameof(SendCancelRequestNotificationAsync),
                    ex);
            }
            finally
            {
                if (sendTask is not null)
                {
                    // There is no second running loop or timer. A non-cooperative external
                    // transport owns finishing its operation; observe any eventual exception.
                    _ = sendTask.ContinueWith(
                        static task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                }
            }
        }

        /// <summary>
        /// Idempotently registers the local session tracking entry. It is recorded after a successful
        /// session/new, session/load, or session/resume so the existence fast-fail gate in
        /// SendPromptAsync does not reject the official load/resume -> prompt flow.
        /// The local entry is only an optional fast-fail optimization; the agent remains the source of
        /// truth for session existence, and nothing is tightened when a capability is not advertised.
        /// </summary>
        private async Task RegisterSessionAsync(string sessionId, string cwd, CancellationToken connectionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (!_sessionStore.ContainsSession(sessionId))
            {
                await _sessionStore.CreateSessionAsync(sessionId, cwd).ConfigureAwait(false);
            }
            RegisterSessionWork(sessionId, connectionToken);
        }

        private void RegisterSessionWork(string sessionId, CancellationToken connectionToken)
        {
            lock (_lock)
            {
                if (_messageLoopCts?.Token == connectionToken)
                {
                    _sessionWork.RegisterSession(sessionId, connectionToken);
                }
            }
        }

        private void RemoveSession(string sessionId, CancellationToken connectionToken)
        {
            lock (_lock)
            {
                if (_messageLoopCts?.Token == connectionToken)
                {
                    _sessionWork.RemoveSession(sessionId, connectionToken);
                    _sessionStore.RemoveSession(sessionId);
                }
            }
        }

        private CancellationToken GetConnectionToken()
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _messageLoopCts!.Token;
            }
        }

        private void CancelPendingRequests(string? transportErrorMessage = null)
        {
            _sessionWork.EndConnection(string.IsNullOrWhiteSpace(transportErrorMessage)
                ? new OperationCanceledException("The ACP client disconnected.")
                : new InvalidOperationException(CreateTransportDisconnectedMessage(transportErrorMessage)));
            foreach (var pendingRequest in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(pendingRequest.Key, out var pending))
                {
                    if (string.IsNullOrWhiteSpace(transportErrorMessage))
                    {
                        pending.Completion.TrySetCanceled();
                    }
                    else
                    {
                        pending.Completion.TrySetException(new InvalidOperationException(
                            CreateTransportDisconnectedMessage(transportErrorMessage)));
                    }
                }
            }
        }

        /// <summary>
        /// Sends a response (used to answer inbound requests).
        /// </summary>
        private Task<bool> SendResponseAsync(JsonRpcResponse response, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (response.Id is not null && _responseRoutes.TryGetValue(response.Id, out var route))
                {
                    if (!IsCurrentConnection(route.ConnectionToken))
                    {
                        return Task.FromResult(false);
                    }
                    if (route.Batch is not null)
                    {
                        return route.Batch.SubmitAsync(route.Index, response);
                    }
                    cancellationToken = route.ConnectionToken;
                }
                else if (!cancellationToken.CanBeCanceled)
                {
                    cancellationToken = _messageLoopCts?.Token ?? default;
                }
            }

            return SendResponseFrameAsync(_parser.SerializeMessage(response), cancellationToken);
        }

        private async Task<bool> SendResponseFrameAsync(string json, CancellationToken cancellationToken)
        {
            try
            {
                Task<bool> send;
                lock (_lock)
                {
                    if (!IsCurrentConnection(cancellationToken))
                    {
                        return false;
                    }
                    // Claim the physical write before teardown can replace the connection. The
                    // transport keeps the same token until that write has finished or cancelled.
                    send = _transport.SendMessageAsync(json, cancellationToken);
                }
                return await send.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to send response: {ex.Message}");
                return false;
            }
        }

        private bool IsCurrentConnection(CancellationToken connectionToken)
            => !_disposed && _transport.IsConnected && !connectionToken.IsCancellationRequested
                && (connectionToken.CanBeCanceled
                    ? _messageLoopCts?.Token == connectionToken
                    : _messageLoopCts is null);

        private static AcpRequestId RequestKey(object? messageId)
            => AcpRequestId.TryFromEnvelopeId(messageId, out var key)
                ? key
                : throw new AcpException(JsonRpcErrorCode.InvalidRequest, "Invalid JSON-RPC request id.");

        /// <summary>
        /// Handles the transport message-received event.
        /// </summary>
        private void OnMessageReceived(object? sender, AcpTransportMessageReceivedEventArgs e)
            => ProcessMessage(e, connectionToken: default);

        private void AttachConnectionMessageHandler(CancellationToken connectionToken)
        {
            _transport.MessageReceived -= OnMessageReceived;
            if (_connectionMessageHandler is not null)
            {
                _transport.MessageReceived -= _connectionMessageHandler;
            }

            // A queued callback retains its receiving connection even after the transport is reused.
            _connectionMessageHandler = (_, message) => ProcessMessage(message, connectionToken);
            _transport.MessageReceived += _connectionMessageHandler;
        }

        private void ProcessMessage(AcpTransportMessageReceivedEventArgs e, CancellationToken connectionToken)
        {
            lock (_lock)
            {
                if (_disposed || (connectionToken.CanBeCanceled
                    && (connectionToken.IsCancellationRequested || _messageLoopCts?.Token != connectionToken))
                    || (!connectionToken.CanBeCanceled && _messageLoopCts is not null))
                {
                    return;
                }
            }
            // Not every transport can pre-classify. Stdio does, because it alone sees a stderr to
            // contrast with, but a bridge that relays an agent's stdout over WebSocket/HTTP delivers
            // the same non-ACP line verbatim as a frame. Guarding here keeps the answer identical on
            // every transport: a line that was never an ACP message must not be parsed, must not be
            // reported as a client error, and must not draw a -32700 reply.
            if (AcpFrame.IsBlank(e.Message))
            {
                return;
            }

            if (!AcpFrame.LooksLikeFrame(e.Message))
            {
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "PEER_NON_ACP_MESSAGE",
                    $"Ignoring non-ACP message from the peer, which must not send this: {AcpFrame.Describe(e.Message)}");
                return;
            }

            try
            {
                lock (_lock)
                {
                    if (!IsCurrentConnection(connectionToken))
                    {
                        return;
                    }
                    var frame = _parser.ParseFrame(AcpFrame.StripByteOrderMark(e.Message), ProtocolVersion);
                    DispatchFrame(frame, connectionToken);
                }
            }
            catch (AcpException ex) when (ex.ErrorCode == JsonRpcErrorCode.ParseError)
            {
                OnErrorOccurred($"Failed to process message: {ex.Message}");
                _ = SendResponseFrameAsync(_parser.SerializeMessage(new JsonRpcResponse(
                    null, JsonRpcError.CreateParseError(ex.Message))), connectionToken);
            }
            catch (AcpException ex) when (ex.ErrorCode == JsonRpcErrorCode.InvalidRequest)
            {
                _ = SendResponseFrameAsync(_parser.SerializeMessage(new JsonRpcResponse(
                    null, JsonRpcError.CreateInvalidRequest(ex.Message))), connectionToken);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process message: {ex.Message}");
            }
        }

        private void DispatchFrame(ParsedMessageFrame frame, CancellationToken connectionToken)
        {
            var responseCount = frame.IsBatch && !frame.IsResponseBatch
                ? frame.Items.Count(item => item.Message is JsonRpcRequest || (item.Error is not null && !item.IsResponse))
                : 0;
            InboundResponseBatch? batch = null;
            if (responseCount > 0)
            {
                batch = new InboundResponseBatch(responseCount,
                    responses => SendResponseFrameAsync(_parser.SerializeResponses(responses), connectionToken),
                    responses => CompleteResponseBatch(batch!, responses), _logger,
                    () => NotifyResponseBatchChanged(batch!),
                    () => RetireUnretryableResponseBatch(batch!));
                _responseBatches.Add(batch);
            }

            var responseIndex = 0;
            foreach (var item in frame.Items)
            {
                if (!IsCurrentConnection(connectionToken))
                {
                    batch?.Abandon();
                    return;
                }
                if (item.Error is not null)
                {
                    if (frame.IsResponseBatch || item.IsResponse)
                    {
                        _logger.Log(AcpClientLogLevel.Warning, "INVALID_BATCH_RESPONSE", item.Error);
                    }
                    else
                    {
                        _ = batch!.SubmitAsync(responseIndex++, new JsonRpcResponse(null,
                            JsonRpcError.CreateInvalidRequest(item.Error)));
                    }
                    continue;
                }

                try
                {
                    switch (item.Message)
                    {
                        case JsonRpcResponse response:
                            HandleResponse(response);
                            break;
                        case JsonRpcRequest request:
                            _responseRoutes.Add(request.Id, new InboundResponseRoute(connectionToken, batch, responseIndex++));
                            if (_pendingInboundRequests.ContainsKey(RequestKey(request.Id)))
                            {
                                _ = SendResponseAsync(new JsonRpcResponse(request.Id,
                                    JsonRpcError.CreateInvalidRequest("The request id is already pending on this connection.")));
                                break;
                            }
                            HandleRequest(request);
                            break;
                        case JsonRpcNotification notification:
                            HandleNotification(notification, connectionToken);
                            break;
                    }
                }
                catch (Exception exception) when (frame.IsBatch)
                {
                    // One host subscriber cannot suppress later, independently valid batch items.
                    // Notifications never get a reply; calls retain any answer already prepared.
                    _logger.Log(AcpClientLogLevel.Error, "BATCH_ITEM_HANDLER_FAILED",
                        "Failed to handle a batch item.", exception: exception);
                    if (item.Message is JsonRpcRequest failedRequest)
                    {
                        FailPendingInboundRequest(failedRequest,
                            JsonRpcError.CreateInternalError("The client could not handle this request."));
                    }
                }
            }
        }

        private void CompleteResponseBatch(InboundResponseBatch batch, IReadOnlyList<JsonRpcResponse> responses)
        {
            lock (_lock)
            {
                _responseBatches.Remove(batch);
                // A retry may be initiated by only one sibling. The batch owner commits every
                // prepared form after the shared write succeeds, including siblings whose first
                // send attempt already returned false.
                foreach (var pending in _pendingInboundRequests.Values)
                {
                    if (pending.PreparedCancellationResponse is not null
                        && pending.ElicitationRequest is null && pending.MessageId is not null
                        && _responseRoutes.TryGetValue(pending.MessageId, out var cancelRoute) && cancelRoute.Batch == batch)
                    {
                        _pendingInboundRequests.TryRemove(new KeyValuePair<AcpRequestId, PendingInboundRequest>(
                            RequestKey(pending.MessageId), pending));
                        pending.PermissionEvent?.NotifyChanged();
                        RemovePendingUrlElicitation(pending);
                        continue;
                    }
                    if ((pending.PreparedElicitationResponse is not null || pending.HasPreparedElicitationFailure
                            || (pending.ElicitationRequest is not null && pending.PreparedCancellationResponse is not null))
                        && pending.MessageId is not null
                        && _responseRoutes.TryGetValue(pending.MessageId, out var route) && route.Batch == batch)
                    {
                        // A cancel may arrive while an already serialized accept is being written.
                        // URL completion follows the response that actually reached the peer.
                        var response = responses[route.Index];
                        var elicitation = response.Result is { } result
                            ? FromElement<CreateElicitationResponse>(result) : null;
                        CompleteElicitationResponse(pending, elicitation, sent: true);
                    }
                    if (pending.PreparedAskUserResponse is not null && pending.MessageId is not null
                        && _responseRoutes.TryGetValue(pending.MessageId, out var askRoute) && askRoute.Batch == batch)
                    {
                        CompleteAskUserResponse(pending, pending.PreparedAskUserResponse, sent: true);
                    }
                    if (pending.PreparedPermissionResponse is not null && pending.MessageId is not null
                        && _responseRoutes.TryGetValue(pending.MessageId, out var permissionRoute) && permissionRoute.Batch == batch)
                    {
                        CompletePermissionResponse(pending, isCancellation: false, sent: true);
                    }
                }
            }
        }

        private void NotifyResponseBatchChanged(InboundResponseBatch batch)
        {
            lock (_lock)
            {
                foreach (var pending in _pendingInboundRequests.Values)
                {
                    if (pending.MessageId is not null && pending.PermissionEvent is not null
                        && _responseRoutes.TryGetValue(pending.MessageId, out var route) && route.Batch == batch)
                    {
                        pending.PermissionEvent.NotifyChanged();
                    }
                }
            }
        }

        private void RetireUnretryableResponseBatch(InboundResponseBatch batch)
        {
            lock (_lock)
            {
                foreach (var pending in _pendingInboundRequests.Values)
                {
                    if (pending.MessageId is not null && IsInboundRequestCurrent(pending)
                        && _responseRoutes.TryGetValue(pending.MessageId, out var route) && route.Batch == batch)
                    {
                        return;
                    }
                }

                // Invalid items and already rejected calls have no responder that can retry them.
                // Keeping their failed array until disconnect would grow without bound on a live
                // connection. Interactive siblings retain the whole batch through their owner.
                if (_responseBatches.Remove(batch))
                {
                    batch.Abandon();
                    try
                    {
                        _logger.Log(AcpClientLogLevel.Warning, "BATCH_RESPONSE_ABANDONED",
                            "The response batch could not be sent and has no pending request available to retry it.");
                    }
                    catch (Exception)
                    {
                        // A host logger cannot restore an abandoned batch or leave its detached
                        // completion task faulted. There is no second reliable diagnostic sink.
                    }
                }
            }
        }

        private void HandleResponse(JsonRpcResponse response)
        {
            if (!_pendingRequests.TryRemove(RequestKey(response.Id), out var pending))
            {
                return;
            }
            pending.ResponseObserver?.Invoke(response);
            if (!pending.Completion.TrySetResult(response))
            {
                // Caller cancellation leaves correlation alive until the peer sends its terminal
                // result. Batched responses take this same path and settle it exactly once.
                _logger.Log(AcpClientLogLevel.Information, "CANCELLED_REQUEST_SETTLED",
                    "The peer settled a cancelled request.", nameof(OnMessageReceived));
            }
        }

        /// <summary>
        /// Handles an inbound notification message.
        /// </summary>
        private void HandleNotification(JsonRpcNotification notification, CancellationToken connectionToken)
        {
            switch (notification.Method)
            {
                case "$/cancel_request":
                    HandleInboundCancellation(notification, connectionToken);
                    break;
                case "session/update":
                    HandleSessionUpdate(notification, connectionToken);
                    break;
                case ElicitationMethods.Complete:
                    HandleElicitationCompleted(notification);
                    break;
                default:
                    // Unknown notification type.
                    break;
            }
        }

        private void HandleInboundCancellation(JsonRpcNotification notification, CancellationToken connectionToken)
        {
            if (notification.Params is not { ValueKind: JsonValueKind.Object } parameters
                || !parameters.TryGetProperty("requestId", out var id) || !AcpRequestId.TryFromEnvelopeId(id, out var key))
            {
                return;
            }
            lock (_lock)
            {
                if (!IsCurrentConnection(connectionToken) || !TryGetPendingInboundRequest(key, out var pending)
                    || !IsInboundRequestCurrent(pending)) return;
                var response = new JsonRpcResponse(pending.MessageId,
                    new JsonRpcError(JsonRpcErrorCode.Cancelled, "The peer cancelled this request."));
                if (pending.Method == "session/request_permission" && !IsBatchedResponse(pending))
                {
                    // ACP v1 and v2 both permit peer cancellation. Latch it on the original
                    // request before a concurrent user choice can claim another terminal response.
                    pending.PreparedCancellationResponse = response;
                    pending.IsPermissionCancellationRequested = true;
                    pending.PermissionEvent?.NotifyChanged();
                    _ = TrySendPermissionOutcomeResponseAsync(pending.MessageId, "cancelled", null, pending);
                    return;
                }
                if (ProtocolVersion != AcpProtocolVersion.V2) return;
                RemovePendingUrlElicitation(pending);
                if (TrySubmitBatchCancellation(pending, response)) return;
                _pendingInboundRequests.TryRemove(new KeyValuePair<AcpRequestId, PendingInboundRequest>(key, pending));
                _ = SendResponseAsync(response, connectionToken);
            }
        }

        /// <summary>
        /// Handles an inbound request message (agent -> client, a response is required).
        /// </summary>
        private void HandleRequest(JsonRpcRequest request)
        {
            var requestIdStr = RequestKey(request.Id);

            switch (request.Method)
            {
                case "session/request_permission":
                    TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    HandlePermissionRequest(request);
                    break;
                case "fs/read_text_file":
                case "fs/write_text_file":
                    if (!SupportsAdvertisedFileSystemCapability(request.Method))
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    HandleFileSystemRequest(request);
                    break;
                case "terminal/create":
                case "terminal/output":
                case "terminal/wait_for_exit":
                case "terminal/kill":
                case "terminal/release":
                    if (!SupportsAdvertisedTerminalExecution)
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    _ = HandleTerminalRequestAsync(request);
                    break;
                case ElicitationMethods.Create:
                    if (!SupportsAdvertisedElicitation)
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    HandleElicitationRequest(request);
                    break;
                case ClientCapabilityMetadata.AskUserExtensionMethod:
                    if (!SupportsAdvertisedAskUserExtension(request.Method))
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    HandleAskUserRequest(request);
                    break;
                default:
                    // Best-effort: respond with "method not found" so the agent doesn't hang waiting.
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateMethodNotFound(request.Method)));
                    break;
            }
        }

        private bool SupportsAdvertisedFileSystemCapability(string method)
            => ProtocolVersion == AcpProtocolVersion.V1
                && (method switch
                {
                    "fs/read_text_file" => _clientCapabilities?.Fs?.ReadTextFile == true,
                    "fs/write_text_file" => _clientCapabilities?.Fs?.WriteTextFile == true,
                    _ => false
                });

        private bool SupportsAdvertisedAskUserExtension(string method)
            => _clientCapabilities?.SupportsExtension(method) == true;

        /// <summary>
        /// Whether the client advertised elicitation at all.
        /// </summary>
        /// <remarks>
        /// An omitted or <c>null</c> capability object means the whole family is unsupported, so the
        /// method genuinely does not exist here and <c>-32601</c> is the honest answer, symmetric with the
        /// fs and terminal gates. Mode-level refusal is a different case, handled by
        /// <see cref="SupportsAdvertisedElicitationMode"/>.
        /// </remarks>
        private bool SupportsAdvertisedElicitation => _clientCapabilities?.Elicitation is not null;

        /// <summary>
        /// Whether the client advertised the elicitation mode the agent asked for.
        /// </summary>
        /// <remarks>
        /// Unlike the fs and terminal gates, an un-advertised mode is answered with
        /// <c>-32602 Invalid params</c> rather than <c>-32601</c>: the elicitation specification names that
        /// code explicitly, because the method itself exists and only the requested mode is unavailable.
        /// A request whose mode this client does not model at all is never advertised, so it lands here
        /// too instead of being rendered as a known mode.
        /// </remarks>
        private bool SupportsAdvertisedElicitationMode(CreateElicitationRequest request)
            => request switch
            {
                FormElicitationRequest => _clientCapabilities?.Elicitation?.SupportsForm == true,
                UrlElicitationRequest => _clientCapabilities?.Elicitation?.SupportsUrl == true,
                _ => false
            };

        private void EnsureMcpServersSupported(IEnumerable<McpServer>? mcpServers, string method)
        {
            if (mcpServers == null)
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    $"{method} requires mcpServers to be an array.");
            }

            var result = McpServerSupportPolicy.Validate(mcpServers, _agentCapabilities);
            if (result.IsSupported)
            {
                return;
            }

            throw new AcpException(
                JsonRpcErrorCode.InvalidParams,
                $"{method} contains unsupported MCP server configuration: {result.ErrorMessage}");
        }

        private void RejectUnsupportedClientRequest(JsonRpcRequest request)
        {
            RemovePendingInboundTracking(RequestKey(request.Id));
            _ = SendResponseAsync(new JsonRpcResponse(
                request.Id,
                JsonRpcError.CreateMethodNotFound(request.Method)));
        }

        /// <summary>
        /// Handles the session/update notification.
        /// </summary>
        private void HandleSessionUpdate(JsonRpcNotification notification, CancellationToken connectionToken)
        {
            try
            {
                if (!notification.Params.HasValue)
                {
                    return;
                }

                AcpWireFormat wire;
                lock (_lock)
                {
                    if (connectionToken.CanBeCanceled
                        && (connectionToken.IsCancellationRequested || _messageLoopCts?.Token != connectionToken))
                    {
                        return;
                    }
                    wire = _wire;
                }
                var updateParams = notification.Params.Value.Deserialize(wire.TypeInfo<SessionUpdateParams>());
                if (updateParams == null || updateParams.Update == null)
                {
                    return;
                }
                if (connectionToken.CanBeCanceled
                    && !_sessionWork.ReceiveUpdate(
                        updateParams.SessionId,
                        updateParams.Update,
                        connectionToken,
                        wire.Version == AcpProtocolVersion.V2 ? notification.Params.Value.GetProperty("update") : null))
                {
                    return;
                }
                SessionUpdateReceived?.Invoke(this, new SessionUpdateEventArgs(updateParams.SessionId, updateParams.Update));
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process session/update notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles an inbound permission request.
        /// </summary>
        private void HandlePermissionRequest(JsonRpcRequest request)
        {
            if (ProtocolVersion == AcpProtocolVersion.V2)
            {
                HandleDraftPermissionRequest(request);
                return;
            }

            PendingInboundRequest pendingPermission;
            PermissionRequestEventArgs eventArgs;
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp)
                    || sessionIdProp.ValueKind != JsonValueKind.String)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (request.Id == null)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = RequestKey(messageId);
                SetPendingInboundSessionId(requestId, sessionId);
                if (!rawParams.TryGetProperty("toolCall", out var toolCall)
                    || toolCall.ValueKind != JsonValueKind.Object)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing toolCall")));
                    return;
                }

                if (!toolCall.TryGetProperty("toolCallId", out var toolCallId)
                    || toolCallId.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(toolCallId.GetString()))
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing toolCallId")));
                    return;
                }

                if (!rawParams.TryGetProperty("options", out var optionsProp)
                    || optionsProp.ValueKind != JsonValueKind.Array)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing options")));
                    return;
                }

                var optionsList = new List<PermissionOption>();
                foreach (var option in optionsProp.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.Object
                        || !option.TryGetProperty("optionId", out var id)
                        || id.ValueKind != JsonValueKind.String
                        || !option.TryGetProperty("name", out var n)
                        || n.ValueKind != JsonValueKind.String
                        || !option.TryGetProperty("kind", out var k)
                        || k.ValueKind != JsonValueKind.String)
                    {
                        RemovePendingInboundTracking(RequestKey(request.Id));
                        _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Invalid permission option")));
                        return;
                    }

                    optionsList.Add(new PermissionOption(
                        id.GetString() ?? string.Empty,
                        n.GetString() ?? string.Empty,
                        k.GetString() ?? string.Empty)
                    {
                        Meta = AcpMetaJson.Read(option)
                    });
                }

                if (!TryGetPendingInboundRequest(requestId, out pendingPermission)) return;
                pendingPermission.PermissionOptionIds = optionsList.Select(static option => option.OptionId).ToHashSet(StringComparer.Ordinal);
                var permissionResponseFunc = new Func<string, string?, Task<bool>>((outcome, optionId) =>
                    TrySendPermissionOutcomeResponseAsync(messageId, outcome, optionId, pendingPermission));

                eventArgs = new PermissionRequestEventArgs(
                    messageId,
                    sessionId,
                    toolCall,
                    optionsList,
                    permissionResponseFunc,
                    () => CanRespondToPermissionRequest(pendingPermission),
                    () => IsPermissionResponsePrepared(pendingPermission),
                    () => IsPermissionResponseSending(pendingPermission),
                    () => IsPermissionCancellationRequested(pendingPermission), _logger);
                pendingPermission.PermissionEvent = eventArgs;

            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process permission request: {ex.Message}");
                FailPendingInboundRequest(
                    request,
                    JsonRpcError.CreateInternalError("Client failed to process inbound permission request."));
                return;
            }

            PublishPermissionRequest(pendingPermission, eventArgs);
        }

        private void HandleDraftPermissionRequest(JsonRpcRequest request)
        {
            var requestId = RequestKey(request.Id);
            AcpPermissionRequestSnapshot snapshot;
            try
            {
                snapshot = AcpPermissionDraftExtensions.ReadRequest(request.Params
                    ?? throw new JsonException("Missing permission params."));
            }
            catch (JsonException error)
            {
                FailPendingInboundRequest(request, JsonRpcError.CreateInvalidParams(error.Message));
                return;
            }

            PendingInboundRequest pending;
            PermissionRequestEventArgs eventArgs;
            try
            {
                SetPendingInboundSessionId(requestId, snapshot.SessionId);
                if (!TryGetPendingInboundRequest(requestId, out pending) || !IsInboundRequestCurrent(pending)) return;
                var options = snapshot.Options.ToList();
                pending.PermissionOptionIds = options.Select(static option => option.OptionId).ToHashSet(StringComparer.Ordinal);
                // The same request owner carries cancellation and responses. Draft display data is
                // reachable only through its opt-in accessor; it never becomes a v1 tool-call event.
                eventArgs = new PermissionRequestEventArgs(request.Id!, snapshot.SessionId, null, options,
                    (outcome, optionId) => TrySendPermissionOutcomeResponseAsync(pending.MessageId, outcome, optionId, pending),
                    () => CanRespondToPermissionRequest(pending),
                    () => IsPermissionResponsePrepared(pending),
                    () => IsPermissionResponseSending(pending),
                    () => IsPermissionCancellationRequested(pending), _logger)
                {
                    DraftRequest = snapshot,
                    Title = snapshot.Title,
                    Description = snapshot.Description
                };
                pending.PermissionEvent = eventArgs;
            }
            catch (Exception error)
            {
                OnErrorOccurred($"Failed to process permission request: {error.Message}");
                FailPendingInboundRequest(request,
                    JsonRpcError.CreateInternalError("Client failed to process inbound permission request."));
                return;
            }

            PublishPermissionRequest(pending, eventArgs);
        }

        private void PublishPermissionRequest(PendingInboundRequest pending, PermissionRequestEventArgs eventArgs)
        {
            try
            {
                if (PermissionRequestReceived is null)
                {
                    // No host is available to answer, so cancellation still uses the same claim.
                    _ = TrySendPermissionOutcomeResponseAsync(pending.MessageId, "cancelled", null, pending);
                    return;
                }
                PermissionRequestReceived.Invoke(this, eventArgs);
            }
            catch (Exception error)
            {
                // A subscriber can answer, reconnect, or replace this id before throwing. Errors
                // must claim the original request too, never remove or answer a newer request.
                _ = TrySendPermissionFailureResponseAsync(pending);
                OnErrorOccurred($"Failed to process permission request: {error.Message}");
            }
        }

        /// <summary>
        /// Handles an inbound file system request.
        /// </summary>
        private void HandleFileSystemRequest(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp) ||
                    !rawParams.TryGetProperty("path", out var pathProp))
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId or path")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (request.Id == null)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = RequestKey(messageId);
                SetPendingInboundSessionId(requestId, sessionId);
                var path = pathProp.GetString() ?? string.Empty;
                var content = rawParams.TryGetProperty("content", out var cont) ? cont.GetString() : null;

                var kind = request.Method switch
                {
                    "fs/read_text_file" => FileSystemRequestKind.ReadTextFile,
                    "fs/write_text_file" => FileSystemRequestKind.WriteTextFile,
                    _ => throw new InvalidOperationException($"Unsupported file system request method: {request.Method}")
                };
                var encoding = (string?)null;

                var fileSystemResponseFunc = new Func<bool, string?, string?, Task>((success, respContent, respMessage) =>
                RespondToFileSystemRequestAsync(messageId, success, respContent, respMessage));

                var eventArgs = new FileSystemRequestEventArgs(
                messageId,
                sessionId,
                request.Method,
                kind,
                path,
                encoding,
                content,
                fileSystemResponseFunc);

                if (FileSystemRequestReceived == null)
                {
                    // No UI hooked up; deny to avoid deadlock.
                    _ = RespondToFileSystemRequestAsync(messageId, success: false, content: null, message: "File system requests are not supported.");
                    return;
                }

                FileSystemRequestReceived.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process file system request: {ex.Message}");
                FailPendingInboundRequest(
                    request,
                    JsonRpcError.CreateInternalError("Client failed to process inbound file system request."));
            }
        }

        /// <summary>
        /// Handles an inbound ask-user request.
        /// </summary>
        private void HandleAskUserRequest(JsonRpcRequest request)
        {
            AskUserRequest? askUserRequest;
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                askUserRequest = FromElement<AskUserRequest>(request.Params.Value);
                if (askUserRequest == null)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Failed to deserialize ask_user request.")));
                    return;
                }

                AskUserContract.ValidateRequest(askUserRequest);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                FailPendingInboundRequest(request, JsonRpcError.CreateInvalidParams(ex.Message));
                return;
            }

            var messageId = request.Id;
            var requestId = RequestKey(messageId);
            SetPendingInboundAskUserRequest(requestId, askUserRequest);
            if (!TryGetPendingInboundRequest(requestId, out var pending))
            {
                return;
            }
            var handler = AskUserRequestReceived;
            if (handler is null)
            {
                _ = TrySendAskUserFailureResponseAsync(pending, new JsonRpcError(
                    JsonRpcErrorCode.CapabilityNotSupported, "Ask-user requests are not supported."));
                return;
            }

            try
            {
                handler.Invoke(this, new AskUserRequestEventArgs(messageId, askUserRequest,
                    answers => TrySendAskUserResponseAsync(messageId, answers, pending)));
            }
            catch (Exception ex)
            {
                _ = TrySendAskUserFailureResponseAsync(pending,
                    JsonRpcError.CreateInternalError("The client failed to display the question."));
                OnErrorOccurred($"Failed to display ask_user request: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles an inbound <c>elicitation/create</c> request.
        /// </summary>
        private void HandleElicitationRequest(JsonRpcRequest request)
        {
            PendingInboundRequest? pending;
            ElicitationRequestEventArgs eventArgs;
            EventHandler<ElicitationRequestEventArgs> handler;
            try
            {
                if (!request.Params.HasValue)
                {
                    FailPendingInboundRequest(request, JsonRpcError.CreateInvalidParams("Missing params"));
                    return;
                }

                var elicitationRequest = FromElement<CreateElicitationRequest>(
                    request.Params.Value);
                if (elicitationRequest == null)
                {
                    FailPendingInboundRequest(request, JsonRpcError.CreateInvalidParams("Failed to deserialize elicitation/create request."));
                    return;
                }

                if (request.Id == null)
                {
                    _ = SendResponseAsync(new JsonRpcResponse(null, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                if (!SupportsAdvertisedElicitationMode(elicitationRequest))
                {
                    FailPendingInboundRequest(request,
                        JsonRpcError.CreateInvalidParams(
                            $"Elicitation mode '{elicitationRequest.Mode}' was not advertised by the client."));
                    return;
                }

                var currentHandler = ElicitationRequestReceived;
                if (currentHandler == null)
                {
                    FailPendingInboundRequest(request,
                        new JsonRpcError(JsonRpcErrorCode.CapabilityNotSupported, "Elicitation requests are not supported."));
                    return;
                }

                handler = currentHandler;
                pending = TrackInboundElicitationRequest(request.Id, elicitationRequest);
                if (pending is null)
                {
                    return;
                }

                eventArgs = new ElicitationRequestEventArgs(
                    request.Id,
                    elicitationRequest,
                    content => TrySendElicitationResponseAsync(pending, new ElicitationAcceptResponse { Content = content?.ToWireContent() }),
                    () => TrySendElicitationResponseAsync(pending, new ElicitationDeclineResponse()),
                    () => TrySendElicitationResponseAsync(pending, new ElicitationCancelResponse()));
            }
            catch (JsonException)
            {
                FailPendingInboundRequest(request, JsonRpcError.CreateInvalidParams("Invalid elicitation/create parameters."));
                return;
            }
            catch (Exception)
            {
                const string message = "Failed to process elicitation/create request.";
                FailPendingInboundRequest(request, JsonRpcError.CreateInternalError(message));
                OnErrorOccurred(message);
                return;
            }

            try
            {
                handler.Invoke(this, eventArgs);
            }
            catch (Exception)
            {
                // A subscriber's JsonException is a host error, not malformed peer input. Never
                // include its payload: it may contain private answers or an authorization URL.
                _ = TrySendElicitationFailureResponseAsync(pending);
                OnErrorOccurred("Failed to process elicitation/create request.");
            }
        }

        /// <summary>
        /// Handles the <c>elicitation/complete</c> notification.
        /// </summary>
        private void HandleElicitationCompleted(JsonRpcNotification notification)
        {
            if (!notification.Params.HasValue)
            {
                return;
            }

            CompleteElicitationNotification? completion;
            try
            {
                completion = FromElement<CompleteElicitationNotification>(
                    notification.Params.Value);
            }
            catch (JsonException ex)
            {
                // A notification has no reply, and the specification tells clients to ignore ids they do
                // not recognize, so a malformed payload is a diagnostic rather than a user-visible fault.
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "ELICITATION_COMPLETE_INVALID",
                    ex.Message);
                return;
            }

            lock (_lock)
            {
                // IDs are opaque, and only outstanding URL interactions on this connection qualify.
                // Removing before publication makes duplicate notifications harmless even on re-entry.
                if (_disposed || !_transport.IsConnected || completion?.ElicitationId is not { } id
                    || !_pendingUrlElicitations.Remove(id))
                {
                    return;
                }

                ElicitationCompleted?.Invoke(this, new ElicitationCompletedEventArgs(id));
            }
        }

        /// <summary>
        /// Handles an inbound terminal request.
        /// </summary>
        private async Task HandleTerminalRequestAsync(JsonRpcRequest request)
        {
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                if (request.Id == null)
                {
                    RemovePendingInboundTracking(RequestKey(request.Id));
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id;
                var requestId = RequestKey(request.Id);
                SetPendingInboundSessionId(requestId, sessionId);

                string? terminalId = null;
                if (rawParams.TryGetProperty("terminalId", out var terminalIdProp))
                {
                    terminalId = terminalIdProp.GetString();
                }

                TerminalRequestReceived?.Invoke(
                    this,
                    new TerminalRequestEventArgs(
                        messageId,
                        sessionId,
                        terminalId,
                        request.Method,
                        rawParams,
                        _ => Task.FromResult(false)));

                switch (request.Method)
                {
                    case "terminal/create":
                        var createRequest = FromElement<TerminalCreateRequest>(rawParams)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/create request.");
                        var createResponse = await _terminalSessionManager.CreateAsync(createRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(sessionId, createResponse.TerminalId, request.Method);
                        await SendTerminalSuccessResponseAsync(messageId, createResponse).ConfigureAwait(false);
                        break;

                    case "terminal/output":
                        var outputRequest = FromElement<TerminalOutputRequest>(rawParams)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/output request.");
                        var outputResponse = await _terminalSessionManager.GetOutputAsync(outputRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(
                            sessionId,
                            outputRequest.TerminalId,
                            request.Method,
                            outputResponse.Output,
                            outputResponse.Truncated,
                            outputResponse.ExitStatus);
                        await SendTerminalSuccessResponseAsync(messageId, outputResponse).ConfigureAwait(false);
                        break;

                    case "terminal/wait_for_exit":
                        var waitRequest = FromElement<TerminalWaitForExitRequest>(rawParams)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/wait_for_exit request.");
                        var waitResponse = await _terminalSessionManager.WaitForExitAsync(waitRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(
                            sessionId,
                            waitRequest.TerminalId,
                            request.Method,
                            exitStatus: new TerminalExitStatus
                            {
                                ExitCode = waitResponse.ExitCode,
                                Signal = waitResponse.Signal
                            });
                        await SendTerminalSuccessResponseAsync(messageId, waitResponse).ConfigureAwait(false);
                        break;

                    case "terminal/kill":
                        var killRequest = FromElement<TerminalKillRequest>(rawParams)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/kill request.");
                        var killResponse = await _terminalSessionManager.KillAsync(killRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(sessionId, killRequest.TerminalId, request.Method);
                        await SendTerminalSuccessResponseAsync(messageId, killResponse).ConfigureAwait(false);
                        break;

                    case "terminal/release":
                        var releaseRequest = FromElement<TerminalReleaseRequest>(rawParams)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/release request.");
                        var releaseResponse = await _terminalSessionManager.ReleaseAsync(releaseRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(
                            sessionId,
                            releaseRequest.TerminalId,
                            request.Method,
                            isReleased: true);
                        await SendTerminalSuccessResponseAsync(messageId, releaseResponse).ConfigureAwait(false);
                        break;

                    default:
                        RemovePendingInboundTracking(RequestKey(request.Id));
                        await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateMethodNotFound(request.Method))).ConfigureAwait(false);
                        break;
                }
            }
            catch (KeyNotFoundException ex)
            {
                RemovePendingInboundTracking(RequestKey(request.Id));
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message))).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                RemovePendingInboundTracking(RequestKey(request.Id));
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message))).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                RemovePendingInboundTracking(RequestKey(request.Id));
                await SendResponseAsync(new JsonRpcResponse(
                    request.Id,
                    new JsonRpcError(JsonRpcErrorCode.CapabilityNotSupported, ex.Message))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process terminal request: {ex.Message}");
                RemovePendingInboundTracking(RequestKey(request.Id));
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInternalError(ex.Message))).ConfigureAwait(false);
            }
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalCreateResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement<TerminalCreateResponse>(result)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalOutputResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement<TerminalOutputResponse>(result)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalWaitForExitResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement<TerminalWaitForExitResponse>(result)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalKillResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement<TerminalKillResponse>(result)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalReleaseResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement<TerminalReleaseResponse>(result)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, JsonElement result)
        {
            RemovePendingInboundTracking(RequestKey(messageId));
            await SendResponseAsync(new JsonRpcResponse(messageId, result)).ConfigureAwait(false);
        }

        private void PublishTerminalStateChanged(
            string sessionId,
            string terminalId,
            string method,
            string? output = null,
            bool? truncated = null,
            TerminalExitStatus? exitStatus = null,
            bool isReleased = false)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(terminalId))
            {
                return;
            }

            TerminalStateChangedReceived?.Invoke(
                this,
                new TerminalStateChangedEventArgs(
                    sessionId,
                    terminalId,
                    method,
                    output,
                    truncated,
                    exitStatus,
                    isReleased));
        }

        private async Task CancelPendingInboundRequestsForSessionAsync(string sessionId, CancellationToken connectionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            // Each awaited send lets the peer finish and reuse other request ids. Cancellation
            // belongs to these request instances, never to a later request with the same id.
            KeyValuePair<AcpRequestId, PendingInboundRequest>[] pendingRequests;
            lock (_lock)
            {
                connectionToken.ThrowIfCancellationRequested();
                pendingRequests = _pendingInboundRequests
                    .Where(pair => pair.Value.ConnectionToken == connectionToken
                        && string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal))
                    .ToArray();
                PrepareSessionBatchCancellations(pendingRequests);
            }

            foreach (var (pendingId, pending) in pendingRequests)
            {
                connectionToken.ThrowIfCancellationRequested();
                if (IsBatchedResponse(pending))
                {
                    continue;
                }
                if (pending.ElicitationRequest is not null)
                {
                    await TrySendElicitationResponseAsync(pending, new ElicitationCancelResponse(), cancelForSession: true)
                        .ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(pending.Method, "session/request_permission", StringComparison.Ordinal))
                {
                    await TrySendPermissionOutcomeResponseAsync(pending.MessageId, "cancelled", null, pending,
                        cancelForSession: true).ConfigureAwait(false);
                    continue;
                }

                if (pending.AskUserRequest is not null)
                {
                    await TrySendAskUserFailureResponseAsync(pending,
                        new JsonRpcError(JsonRpcErrorCode.MethodNotAllowed,
                            "Session was cancelled before the client completed this request."), cancelForSession: true)
                        .ConfigureAwait(false);
                    continue;
                }

                Task<bool> responseTask;
                lock (_lock)
                {
                    connectionToken.ThrowIfCancellationRequested();
                    if (!IsInboundRequestCurrent(pending)
                        || !_pendingInboundRequests.TryRemove(new KeyValuePair<AcpRequestId, PendingInboundRequest>(pendingId, pending)))
                    {
                        continue;
                    }

                    if (pending.MessageId == null)
                    {
                        continue;
                    }

                    // Claim this response while the original callback still owns its connection;
                    // its physical write must observe the same token as the session cancellation.
                    responseTask = SendResponseAsync(new JsonRpcResponse(
                        pending.MessageId,
                        new JsonRpcError(
                            JsonRpcErrorCode.MethodNotAllowed,
                            "Session was cancelled before the client completed this request.")), connectionToken);
                }
                await responseTask.ConfigureAwait(false);
            }
        }

        private void PrepareSessionBatchCancellations(KeyValuePair<AcpRequestId, PendingInboundRequest>[] requests)
        {
            var batches = new HashSet<InboundResponseBatch>();
            try
            {
                foreach (var (_, pending) in requests)
                {
                    if (pending.MessageId is not null && _responseRoutes.TryGetValue(pending.MessageId, out var route)
                        && route.Batch is { } batch && batches.Add(batch))
                    {
                        batch.DeferSending();
                    }
                }

                // A retryable batch already has every slot filled. Prepare every cancellation
                // before the first slot can trigger another write containing its old siblings.
                foreach (var (_, pending) in requests)
                {
                    if (!IsBatchedResponse(pending)) continue;
                    var response = pending.PreparedCancellationResponse;
                    if (response is null)
                    {
                        response = pending.Method == "session/request_permission"
                            ? new JsonRpcResponse(pending.MessageId,
                                ToElement<PermissionOutcomeResult>(CreatePermissionOutcome(pending, "cancelled", null)))
                            : pending.ElicitationRequest is not null
                                ? new JsonRpcResponse(pending.MessageId,
                                    ToElement<CreateElicitationResponse>(new ElicitationCancelResponse()))
                                : new JsonRpcResponse(pending.MessageId, new JsonRpcError(JsonRpcErrorCode.MethodNotAllowed,
                                    "Session was cancelled before the client completed this request."));
                    }
                    TrySubmitBatchCancellation(pending, response);
                }
            }
            finally
            {
                // A batch may also contain another session's input. Its owner waits for that input
                // without holding up session/cancel, and commits all siblings after the shared write.
                foreach (var batch in batches) batch.ResumeSending();
            }
        }

        private bool TrySubmitBatchCancellation(PendingInboundRequest pending, JsonRpcResponse response)
        {
            if (pending.MessageId is null || !_responseRoutes.TryGetValue(pending.MessageId, out var route)
                || route.Batch is null || !IsInboundRequestCurrent(pending))
            {
                return false;
            }
            pending.PreparedCancellationResponse = response;
            route.Batch.SubmitCancellation(route.Index, response);
            pending.PermissionEvent?.NotifyChanged();
            return true;
        }

        private bool IsBatchedResponse(PendingInboundRequest pending)
            => pending.MessageId is not null && _responseRoutes.TryGetValue(pending.MessageId, out var route)
                && route.Batch is not null;

        private void RemovePendingInboundTracking(AcpRequestId idStr)
        {

            lock (_lock)
            {
                if (_pendingInboundRequests.TryRemove(idStr, out var pending))
                {
                    RemovePendingUrlElicitation(pending);
                    pending.PermissionEvent?.NotifyChanged();
                }
            }
        }

        private void FailPendingInboundRequest(JsonRpcRequest request, JsonRpcError error)
        {
            if (request.Id is not null && _responseRoutes.TryGetValue(request.Id, out var route)
                && route.Batch?.HasPreparedResponse(route.Index) == true)
            {
                // A handler may answer and then throw. That does not withdraw the prepared answer
                // or its URL completion tracking while the batch is waiting for other calls.
                return;
            }
            RemovePendingInboundTracking(RequestKey(request.Id));
            if (request.Id == null)
            {
                return;
            }

            _ = SendResponseAsync(new JsonRpcResponse(request.Id, error));
        }

        private void TrackPendingInboundRequest(AcpRequestId idStr, string method, object? messageId)
        {

            lock (_lock)
            {
                _pendingInboundRequests.TryGetValue(idStr, out var previous);
                _pendingInboundRequests[idStr] = new PendingInboundRequest(method, messageId,
                    _messageLoopCts?.Token ?? CancellationToken.None);
                previous?.PermissionEvent?.NotifyChanged();
            }
        }

        private bool TryGetPendingInboundRequest(AcpRequestId idStr, out PendingInboundRequest pending)
        {
            pending = default!;

            return _pendingInboundRequests.TryGetValue(idStr, out pending!);
        }

        private bool TryTakePendingInboundRequest(AcpRequestId idStr, out PendingInboundRequest pending)
        {
            pending = default!;

            return _pendingInboundRequests.TryRemove(idStr, out pending!);
        }

        private void SetPendingInboundSessionId(AcpRequestId idStr, string sessionId)
        {

            while (_pendingInboundRequests.TryGetValue(idStr, out var existing))
            {
                var updated = existing.WithSessionId(sessionId);
                if (_pendingInboundRequests.TryUpdate(idStr, updated, existing))
                {
                    return;
                }
            }
        }

        private PendingInboundRequest? TrackInboundElicitationRequest(object messageId, CreateElicitationRequest request)
        {
            lock (_lock)
            {
                if (_disposed || !_transport.IsConnected)
                {
                    return null;
                }

                var pending = new PendingInboundRequest(
                    ElicitationMethods.Create,
                    messageId,
                    _messageLoopCts?.Token ?? CancellationToken.None,
                    request.Scope.SessionId,
                    null,
                    request);
                if (request is UrlElicitationRequest url && !_pendingUrlElicitations.TryAdd(url.ElicitationId, pending))
                {
                    _ = SendResponseAsync(new JsonRpcResponse(messageId,
                        JsonRpcError.CreateInvalidParams("The elicitationId is already outstanding on this connection.")));
                    return null;
                }

                _pendingInboundRequests[RequestKey(messageId)] = pending;
                return pending;
            }
        }

        private void SetPendingInboundAskUserRequest(AcpRequestId idStr, AskUserRequest request)
        {

            _pendingInboundRequests.AddOrUpdate(
                idStr,
                _ => new PendingInboundRequest(
                    ClientCapabilityMetadata.AskUserExtensionMethod,
                    null,
                    _messageLoopCts?.Token ?? CancellationToken.None,
                    request.SessionId,
                    request),
                (_, existing) => existing.WithAskUserRequest(request));
        }

        /// <summary>
        /// Handles the transport error event.
        /// </summary>
        private void OnTransportError(object? sender, AcpTransportErrorEventArgs e)
        {
            if (e.Kind == AcpTransportErrorKind.AgentStderr)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "AGENT_STDERR",
                    e.ErrorMessage);
                return;
            }

            // A line that never looked like an ACP frame is agent diagnostics written to the stream
            // ACP reserves for the protocol; the spec directs such output to stderr. It carries no
            // request to answer, so replying -32700 would be a category error, and raising it as a
            // client error would blame the user for the agent's spec violation. Log it and move on,
            // exactly as AgentStderr above — the transport keeps reading either way.
            if (e.Kind == AcpTransportErrorKind.StdoutProtocolViolation)
            {
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "AGENT_STDOUT_VIOLATION",
                    e.ErrorMessage);
                return;
            }

            _lastTransportErrorMessage = e.ErrorMessage;
            OnErrorOccurred(e.ErrorMessage);
            if (!_transport.IsConnected)
            {
                ResetConnectionState();
                CancelPendingRequests(e.ErrorMessage);
            }
        }

        /// <summary>
        /// Raises the error event.
        /// </summary>
        private void OnErrorOccurred(string errorMessage)
        {
            _logger.Log(AcpClientLogLevel.Error, "CLIENT_ERROR", errorMessage);
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        private void ClearLastTransportError()
        {
            _lastTransportErrorMessage = null;
        }

        private string CreateTransportConnectFailureMessage()
        {
            var transportErrorMessage = _lastTransportErrorMessage;
            return string.IsNullOrWhiteSpace(transportErrorMessage)
                ? "Failed to connect to the transport."
                : transportErrorMessage;
        }

        private string CreateTransportSendFailureMessage(string method)
        {
            var transportErrorMessage = _lastTransportErrorMessage;
            var requestDescription = string.IsNullOrWhiteSpace(method)
                ? "ACP request"
                : $"ACP request '{method}'";
            return string.IsNullOrWhiteSpace(transportErrorMessage)
                ? $"{requestDescription} was not sent because the transport reported a send failure."
                : $"{requestDescription} was not sent because the transport reported a send failure: {transportErrorMessage}";
        }

        private static string CreateTransportDisconnectedMessage(string transportErrorMessage)
            => "ACP request failed because the transport disconnected: " + transportErrorMessage;

        /// <summary>
        /// Monitors the transport connection state. A transport can drop silently without raising
        /// ErrorOccurred (or still briefly report itself as connected at the moment of the error event),
        /// so this watchdog faults every pending request as a backstop and keeps callers from hanging
        /// forever on <see cref="SendRequestAsync"/>.
        /// </summary>
        private async Task MonitorTransportConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _transport.IsConnected)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // An explicit DisconnectAsync already cancels the pending requests.
                return;
            }

            lock (_lock)
            {
                // A previous watchdog can finish after explicit disconnect and reinitialization.
                // Only the token still owned by this connection may clear its request state.
                if (cancellationToken.IsCancellationRequested || _messageLoopCts?.Token != cancellationToken)
                {
                    return;
                }

                ResetConnectionState();
                CancelPendingRequests(_lastTransportErrorMessage ?? "The transport is no longer connected.");
            }
        }

        private void ResetConnectionState()
        {
            lock (_lock)
            {
                if (_connectionMessageHandler is not null)
                {
                    _transport.MessageReceived -= _connectionMessageHandler;
                    _connectionMessageHandler = null;
                    if (!_disposed)
                    {
                        _transport.MessageReceived += OnMessageReceived;
                    }
                }
                _messageLoopCts?.Cancel();
                _messageLoopCts?.Dispose();
                _messageLoopCts = null;
                foreach (var batch in _responseBatches)
                {
                    batch.Abandon();
                }
                _responseBatches.Clear();
                var permissions = _pendingInboundRequests.Values.Select(static pending => pending.PermissionEvent).ToArray();
                _pendingInboundRequests.Clear();
                foreach (var permission in permissions) permission?.NotifyChanged();
                _pendingUrlElicitations.Clear();
                _clientCapabilities = null;
                _agentInfo = null;
                _agentCapabilities = null;
                _authMethods = null;
                _wire = AcpWireFormat.For(AcpProtocolVersion.Default);
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Ensures the client has been initialized.
        /// </summary>
        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("ACP client is not initialized. Call InitializeAsync first.");
            }
        }

        private void ValidateRequiredAbsolutePath(string? path, string fieldName, string methodName)
        {
            if (string.IsNullOrWhiteSpace(path) || !ProtocolPathRules.IsAbsolutePath(path))
            {
                throw new AcpException(
                    JsonRpcErrorCode.InvalidParams,
                    $"{methodName} requires '{fieldName}' to be an absolute path.");
            }
        }

        private void ValidateOptionalAbsolutePath(string? path, string fieldName, string methodName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ValidateRequiredAbsolutePath(path, fieldName, methodName);
        }

        private void ValidateAdditionalDirectories(IReadOnlyList<string>? paths, string methodName)
        {
            if (paths is null || paths.Count == 0)
            {
                return;
            }

            if (!SupportsSessionAdditionalDirectories)
            {
                throw new AcpException(
                    JsonRpcErrorCode.MethodNotAllowed,
                    $"{methodName} cannot include additionalDirectories because the agent does not advertise sessionCapabilities.additionalDirectories.");
            }

            for (var i = 0; i < paths.Count; i++)
            {
                ValidateRequiredAbsolutePath(paths[i], $"additionalDirectories[{i}]", methodName);
            }
        }

        private void ValidateSessionListResponse(SessionListResponse response)
        {
            foreach (var session in response.Sessions)
            {
                if (string.IsNullOrWhiteSpace(session.SessionId))
                {
                    throw new AcpException(
                        JsonRpcErrorCode.ParseError,
                        "Invalid session/list response: sessionId is required.");
                }

                if (string.IsNullOrWhiteSpace(session.Cwd) || !ProtocolPathRules.IsAbsolutePath(session.Cwd))
                {
                    throw new AcpException(
                        JsonRpcErrorCode.ParseError,
                        $"Invalid session/list response: session '{session.SessionId}' must include an absolute cwd.");
                }

                if (session.AdditionalDirectories is null)
                {
                    continue;
                }

                for (var i = 0; i < session.AdditionalDirectories.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(session.AdditionalDirectories[i])
                        || !ProtocolPathRules.IsAbsolutePath(session.AdditionalDirectories[i]))
                    {
                        throw new AcpException(
                            JsonRpcErrorCode.ParseError,
                            $"Invalid session/list response: session '{session.SessionId}' additionalDirectories[{i}] must be an absolute path.");
                    }
                }
            }
        }

        // Both directions resolve their contract from the connection's wire format, and neither takes a
        // JsonTypeInfo argument any more. That is the point: a call site can no longer name a
        // serialization context, so it can no longer name the wrong one. The type argument is explicit
        // rather than inferred so the contract is chosen by the declared protocol type, not by whatever
        // static type the local variable happens to have.
        private JsonElement ToElement<T>(T value) =>
            JsonSerializer.SerializeToElement(value, _wire.TypeInfo<T>());

        private T? FromElement<T>(JsonElement value) =>
            value.Deserialize(_wire.TypeInfo<T>());

        private static JsonElement NullJsonElement()
        {
            using var document = JsonDocument.Parse("null");
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Releases the resources held by this client.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }
            ResetConnectionState();

            // Detach the transport events first so callbacks during teardown cannot re-enter disposed
            // handlers, then fault every pending request. Otherwise callers awaiting tcs.Task would hang
            // forever on the Dispose path, because the watchdog's cancellation only covers an explicit
            // DisconnectAsync and nothing covers Dispose.
            _transport.MessageReceived -= OnMessageReceived;
            _transport.ErrorOccurred -= OnTransportError;
            CancelPendingRequests(_lastTransportErrorMessage ?? "The ACP client was disposed.");

            // The transport is owned exclusively by this client (process / socket / HttpClient / Rx
            // subject), and Dispose is its authoritative release path; a graceful protocol-level
            // disconnect is the job of an explicit DisconnectAsync, awaited by the caller beforehand.
            // The terminal session manager is shared across connections, so this client must not
            // dispose it: releasing it here would kill terminals still owned by other live clients.
            // Its lifetime belongs to whoever supplied it, and that host must dispose it on its own
            // teardown path — registering it in a container is not by itself such a path, since a
            // container that is never disposed never runs it (this was a real process-leak defect).
            try
            {
                _transport.Dispose();
            }
            catch (Exception ex)
            {
                // A disposal failure on the cleanup path must not escape, or it would replace the real
                // business exception and wedge the call stack.
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "TRANSPORT_DISPOSE_FAILED",
                    "Failed to dispose transport during ACP client disposal.",
                    exception: ex);
            }

            _messageLoopCts?.Dispose();
            _messageLoopCts = null;
            GC.SuppressFinalize(this);
        }
    }
}
