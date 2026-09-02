using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

        private sealed class PendingInboundRequest
        {
            public PendingInboundRequest(
                string method,
                object? messageId,
                string? sessionId = null,
                AskUserRequest? askUserRequest = null,
                CreateElicitationRequest? elicitationRequest = null)
            {
                Method = method;
                MessageId = messageId;
                SessionId = sessionId;
                AskUserRequest = askUserRequest;
                ElicitationRequest = elicitationRequest;
            }

            public string Method { get; }

            public object? MessageId { get; }

            public string? SessionId { get; }

            public AskUserRequest? AskUserRequest { get; }

            public CreateElicitationRequest? ElicitationRequest { get; }

            public PendingInboundRequest WithSessionId(string sessionId)
                => new(Method, MessageId, sessionId, AskUserRequest, ElicitationRequest);

            public PendingInboundRequest WithAskUserRequest(AskUserRequest request)
                => new(
                    string.IsNullOrWhiteSpace(Method) ? ClientCapabilityMetadata.AskUserExtensionMethod : Method,
                    MessageId,
                    request.SessionId,
                    request,
                    ElicitationRequest);

            public PendingInboundRequest WithElicitationRequest(CreateElicitationRequest request)
                => new(
                    string.IsNullOrWhiteSpace(Method) ? ElicitationMethods.Create : Method,
                    MessageId,
                    request.Scope.SessionId ?? SessionId,
                    AskUserRequest,
                    request);
        }
        private readonly IAcpTransport _transport;
        private readonly MessageParser _parser;
        private readonly MessageValidator _validator;
        private readonly IAcpClientSessionStore _sessionStore;
        private readonly IAcpTerminalSessionManager _terminalSessionManager;
        private readonly IAcpClientLogger _logger;


        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
        // Inbound tool requests (agent -> client) are correlated by request id so we can format responses correctly.
        private readonly ConcurrentDictionary<string, PendingInboundRequest> _pendingInboundRequests = new();

        private readonly object _lock = new();
        private bool _disposed;
        private CancellationTokenSource? _messageLoopCts;
        private string? _lastTransportErrorMessage;

        private bool _isInitialized;
        private int _protocolVersion = AcpProtocolVersion.V1;
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
            _protocolVersion == AcpProtocolVersion.V1
                && _clientCapabilities?.Terminal == true;
        private bool SupportsLogout =>
            _protocolVersion == AcpProtocolVersion.V2
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
        public async Task<InitializeResponse> InitializeAsync(InitializeParams @params, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(@params);
            if (AcpProtocolVersion.IsSupported(@params.ProtocolVersion)
                && @params.ProtocolVersion != AcpProtocolVersion.V1)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    StableV1RuntimeOnlyMessage);
            }

            InitializeClientProtocolPolicy.Validate(@params.ProtocolVersion, @params.ClientCapabilities);

            if (_isInitialized)
            {
                throw new InvalidOperationException("ACP client is already initialized.");
            }

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
                ToElement(@params, AcpJsonContext.Default.InitializeParams));
            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

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
            var initializeResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.InitializeResponse);
            if (initializeResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse initialize response");
            }

            var serverVersion = initializeResponse.ProtocolVersion;
            var clientVersion = @params.ProtocolVersion;

            if (!AcpProtocolVersion.IsSupported(serverVersion) || serverVersion > clientVersion)
            {
                throw new AcpException(
                    JsonRpcErrorCode.ProtocolVersionMismatch,
                    $"Protocol version mismatch. Expected by client: {clientVersion}, Server: {serverVersion}");
            }

            // Store the agent information.
            _protocolVersion = serverVersion;
            _agentInfo = initializeResponse.AgentInfo;
            _agentCapabilities = initializeResponse.AgentCapabilities;
            _authMethods = initializeResponse.AuthMethods;
            _clientCapabilities = @params.ClientCapabilities;
            _isInitialized = true;

            // Start the transport disconnect watchdog.
            _messageLoopCts = new CancellationTokenSource();
            _ = MonitorTransportConnectionAsync(_messageLoopCts.Token);

            // Raise the event.
            Initialized?.Invoke(this, initializeResponse);

            return initializeResponse;
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        public async Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            ValidateRequiredAbsolutePath(@params.Cwd, "cwd", "session/new");
            ValidateAdditionalDirectories(@params.AdditionalDirectories, "session/new");
            EnsureMcpServersSupported(@params.McpServers, "session/new");

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/new",
                ToElement(@params, AcpJsonContext.Default.SessionNewParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var sessionNewResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionNewResponse);
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

            return sessionNewResponse;
        }

        /// <summary>
        /// Loads an existing session.
        /// </summary>
        public async Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
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
                ToElement(@params, AcpJsonContext.Default.SessionLoadParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // A successful session/load means the agent has acknowledged the session, so register it in
            // the local store. Otherwise the existence fast-fail in session/prompt would locally reject
            // the official load -> prompt flow as SessionNotFound.
            await RegisterSessionAsync(@params.SessionId, @params.Cwd).ConfigureAwait(false);

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SessionLoadResponse.Completed;
            }

            var sessionLoadResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionLoadResponse);

            return sessionLoadResponse ?? SessionLoadResponse.Completed;
        }

        /// <summary>
        /// Resumes an existing session. Omitting <see cref="SessionResumeParams.ReplayFrom"/> requests no
        /// history replay; setting <c>replayFrom: { type: "start" }</c> requests a full history replay (V2).
        /// </summary>
        public async Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            ArgumentNullException.ThrowIfNull(@params);
            if (_protocolVersion == AcpProtocolVersion.V1 && @params.ReplayFrom is not null)
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
                ToElement(@params, AcpJsonContext.Default.SessionResumeParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            // The agent has acknowledged the resumed session, so register the local tracking entry. This
            // keeps the existence fast-fail gate in SendPromptAsync from misreporting the official
            // resume -> prompt flow as SessionNotFound.
            await RegisterSessionAsync(@params.SessionId, @params.Cwd).ConfigureAwait(false);

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SessionResumeResponse.Completed;
            }

            var sessionResumeResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionResumeResponse);

            return sessionResumeResponse ?? SessionResumeResponse.Completed;
        }

        /// <summary>
        /// Closes an existing session and releases the resources held on the agent side.
        /// </summary>
        public async Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (!SupportsSessionClose)
            {
                _logger.Log(
                    AcpClientLogLevel.Information,
                    "SESSION_CLOSE_UNSUPPORTED",
                    "Agent does not support session/close capability",
                    nameof(CloseSessionAsync));

                _sessionStore.RemoveSession(@params.SessionId);
                return SessionCloseResponse.Completed;
            }

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/close",
                ToElement(@params, AcpJsonContext.Default.SessionCloseParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                _sessionStore.RemoveSession(@params.SessionId);
                return SessionCloseResponse.Completed;
            }

            var sessionCloseResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionCloseResponse);

            _sessionStore.RemoveSession(@params.SessionId);
            return sessionCloseResponse ?? SessionCloseResponse.Completed;
        }

        /// <summary>
        /// Deletes a session on the remote agent.
        /// </summary>
        public async Task<SessionDeleteResponse> DeleteSessionAsync(SessionDeleteParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

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
                ToElement(@params, AcpJsonContext.Default.SessionDeleteParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            if (!response.Result.HasValue ||
                response.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                _sessionStore.RemoveSession(@params.SessionId);
                return SessionDeleteResponse.Completed;
            }

            var sessionDeleteResponse = FromElement(response.Result.Value, AcpJsonContext.Default.SessionDeleteResponse);

            _sessionStore.RemoveSession(@params.SessionId);
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
                ToElement(@params, AcpJsonContext.Default.SessionListParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var listResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionListResponse);
            if (listResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/list response");
            }

            ValidateSessionListResponse(listResponse);
            return listResponse;
        }

        /// <summary>
        /// Sends a prompt to a session.
        /// </summary>
        public async Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            // Check whether the session exists.
            if (!_sessionStore.ContainsSession(@params.SessionId))
            {
                throw new AcpException(JsonRpcErrorCode.SessionNotFound, $"Session '{@params.SessionId}' not found");
            }

            EnsurePromptContentAllowed(@params);

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                "session/prompt",
                ToElement(@params, AcpJsonContext.Default.SessionPromptParams));

            // ACP requires a real session/prompt response with a protocol stopReason.
            // The client must wait for the protocol response instead of fabricating a terminal result.
            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var promptResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionPromptResponse);
            if (promptResponse == null)
            {
                throw new AcpException(JsonRpcErrorCode.ParseError, "Failed to parse session/prompt response");
            }

            return promptResponse;
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
                ToElement(@params, AcpJsonContext.Default.SessionSetModeParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var setModeResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionSetModeResponse);
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
                ToElement(@params, AcpJsonContext.Default.SessionSetConfigOptionParams));

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var configResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.SessionSetConfigOptionResponse);
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

            var notification = new JsonRpcNotification(
                "session/cancel",
                ToElement(@params, AcpJsonContext.Default.SessionCancelParams));

            await _transport.SendMessageAsync(
                _parser.SerializeMessage(notification),
                cancellationToken).ConfigureAwait(false);

            await CancelPendingInboundRequestsForSessionAsync(@params.SessionId).ConfigureAwait(false);
            await _sessionStore.CancelSessionAsync(@params.SessionId).ConfigureAwait(false);
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

            var methodName = _protocolVersion == AcpProtocolVersion.V2 ? "auth/login" : "authenticate";

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                methodName,
                ToElement(@params, AcpJsonContext.Default.AuthenticateParams));

            var response = await SendRequestAsync(request, cancellationToken);

            if (response.IsError)
            {
                throw new AcpException(response.Error!.Code, response.Error.Message, response.Error.Data);
            }

            var authResponse = FromElement(response.Result!.Value, AcpJsonContext.Default.AuthenticateResponse);
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

            var methodName = _protocolVersion == AcpProtocolVersion.V2 ? "auth/logout" : "logout";

            var request = new JsonRpcRequest(
                Interlocked.Increment(ref _nextMessageId),
                methodName,
                ToElement(@params, AcpJsonContext.Default.LogoutParams));

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

            return FromElement(response.Result.Value, AcpJsonContext.Default.LogoutResponse)
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

        private async Task<bool> TrySendElicitationResponseAsync(
            object messageId,
            CreateElicitationResponse response)
        {
            var idStr = messageId?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            // Taking the pending entry is what makes the three actions mutually exclusive: whichever one
            // runs first removes the correlation, so a late second action cannot answer the same request
            // twice.
            if (!TryTakePendingInboundRequest(idStr, out var pending)
                || pending.ElicitationRequest == null)
            {
                return false;
            }

            return await SendResponseAsync(
                new JsonRpcResponse(
                    messageId!,
                    ToElement(response, AcpJsonContext.Default.CreateElicitationResponse))).ConfigureAwait(false);
        }

        private async Task<bool> TrySendPermissionOutcomeResponseAsync(object? messageId, string outcome, string? optionId)
        {
            if (messageId == null)
            {
                return false;
            }

            // Only respond once per inbound request id. Unknown or stale ids are not a
            // protocol payload, so they should not fail ACP schema validation.
            var idStr = messageId.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(idStr)
                || !TryTakePendingInboundRequest(idStr, out _))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(outcome))
            {
                return false;
            }

            if (string.Equals(outcome, "selected", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(optionId))
                {
                    throw new AcpException(JsonRpcErrorCode.InvalidParams, "Permission outcome 'selected' requires optionId.");
                }
            }
            else if (!string.Equals(outcome, "cancelled", StringComparison.Ordinal))
            {
                throw new AcpException(JsonRpcErrorCode.InvalidParams, $"Unsupported permission outcome '{outcome}'.");
            }

            var outcomePayload = new PermissionOutcomeResult
            {
                Outcome = new PermissionOutcome
                {
                    Outcome = outcome,
                    OptionId = string.IsNullOrWhiteSpace(optionId) ? null : optionId
                }
            };

            var response = new JsonRpcResponse(
                messageId,
                ToElement(outcomePayload, AcpJsonContext.Default.PermissionOutcomeResult));
            return await SendResponseAsync(response).ConfigureAwait(false);
        }

        private async Task<bool> TrySendFileSystemResponseAsync(object messageId, bool success, string? content, string? message)
        {
            var idStr = messageId?.ToString() ?? string.Empty;
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
                return await SendResponseAsync(new JsonRpcResponse(messageId, error)).ConfigureAwait(false);
            }

            JsonElement result;
            if (string.Equals(pending.Method, "fs/read_text_file", StringComparison.Ordinal))
            {
                result = ToElement(
                    new ReadTextFileResult { Content = content ?? string.Empty },
                    AcpJsonContext.Default.ReadTextFileResult);
            }
            else
            {
                // fs/write_text_file returns null on success.
                result = NullJsonElement();
            }

            return await SendResponseAsync(new JsonRpcResponse(messageId, result)).ConfigureAwait(false);
        }

        private async Task<bool> TrySendAskUserResponseAsync(object messageId, IReadOnlyDictionary<string, string> answers)
        {
            var idStr = messageId?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            if (!TryTakePendingInboundRequest(idStr, out var pending))
            {
                return false;
            }

            if (pending.AskUserRequest == null)
            {
                return false;
            }

            AskUserContract.ValidateAnswers(pending.AskUserRequest, answers);
            var response = new AskUserResponse(pending.AskUserRequest.Questions, answers);
            return await SendResponseAsync(
                new JsonRpcResponse(
                    messageId,
                    ToElement(response, AcpJsonContext.Default.AskUserResponse))).ConfigureAwait(false);
        }

        /// <summary>
        /// Disconnects from the agent.
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            _messageLoopCts?.Cancel();
            CancelPendingRequests();

            await _transport.DisconnectAsync();
            return true;
        }

        /// <summary>
        /// Sends a request and awaits its response.
        /// </summary>

        private async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            using var activity = AcpActivitySources.StartClientRequest(request.Method);
            var requestIdStr = request.Id?.ToString() ?? string.Empty;
            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestIdStr] = tcs;
            var requestWriteStarted = false;
            var retainPendingRequest = false;

            try
            {
                var json = _parser.SerializeMessage(request);
                ClearLastTransportError();
                cancellationToken.ThrowIfCancellationRequested();
                // Once the transport call begins, a pipe/socket implementation may have written
                // some or all of the frame before observing its token. Treat it as potentially
                // delivered for cancellation purposes: skipping $/cancel_request here would leave
                // a peer running an operation the caller has already abandoned.
                requestWriteStarted = true;
                var sent = await _transport.SendMessageAsync(json, cancellationToken).ConfigureAwait(false);
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
                AcpActivitySources.MarkCancelled(activity);

                // A caller can abandon its own await immediately, but the matching response still
                // belongs in _pendingRequests: ACP requires the peer to send a terminal response
                // for the original request (possibly -32800), and OnMessageReceived owns removing
                // that correlation entry. Do not use the caller's already-cancelled token here —
                // cancelling a request must itself reach the peer.
                if (requestWriteStarted && AcpRequestId.TryFromEnvelopeId(request.Id, out var requestId))
                {
                    retainPendingRequest = true;
                    await SendCancelRequestNotificationAsync(requestId).ConfigureAwait(false);
                }

                throw new OperationCanceledException(cancellationToken);
            }
            catch (TaskCanceledException ex)
            {
                var exception = new OperationCanceledException(
                    "ACP request was canceled because the transport disconnected.",
                    ex);
                AcpActivitySources.RecordException(activity, exception);
                throw exception;
            }
            catch (Exception ex)
            {
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
                if (!retainPendingRequest)
                {
                    _pendingRequests.TryRemove(requestIdStr, out _);
                }
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
        private async Task SendCancelRequestNotificationAsync(AcpRequestId requestId)
        {
            if (!_transport.IsConnected)
            {
                return;
            }

            try
            {
                var notification = new JsonRpcNotification(
                    CancelRequestParams.Method,
                    ToElement(
                        new CancelRequestParams(requestId),
                        AcpJsonContext.Default.CancelRequestParams));
                var sent = await _transport.SendMessageAsync(_parser.SerializeMessage(notification)).ConfigureAwait(false);
                if (!sent)
                {
                    _logger.Log(
                        AcpClientLogLevel.Warning,
                        "CANCEL_REQUEST_SEND_FAILED",
                        $"Failed to send $/cancel_request for request {requestId}.",
                        nameof(SendCancelRequestNotificationAsync));
                }
            }
            catch (Exception ex)
            {
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "CANCEL_REQUEST_SEND_FAILED",
                    $"Failed to send $/cancel_request for request {requestId}: {ex.Message}",
                    nameof(SendCancelRequestNotificationAsync),
                    ex);
            }
        }

        /// <summary>
        /// Idempotently registers the local session tracking entry. It is recorded after a successful
        /// session/new, session/load, or session/resume so the existence fast-fail gate in
        /// SendPromptAsync does not reject the official load/resume -> prompt flow.
        /// The local entry is only an optional fast-fail optimization; the agent remains the source of
        /// truth for session existence, and nothing is tightened when a capability is not advertised.
        /// </summary>
        private async Task RegisterSessionAsync(string sessionId, string cwd)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || _sessionStore.ContainsSession(sessionId))
            {
                return;
            }

            await _sessionStore.CreateSessionAsync(sessionId, cwd).ConfigureAwait(false);
        }

        private void CancelPendingRequests(string? transportErrorMessage = null)
        {
            foreach (var pendingRequest in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(pendingRequest.Key, out var pending))
                {
                    if (string.IsNullOrWhiteSpace(transportErrorMessage))
                    {
                        pending.TrySetCanceled();
                    }
                    else
                    {
                        pending.TrySetException(new InvalidOperationException(
                            CreateTransportDisconnectedMessage(transportErrorMessage)));
                    }
                }
            }
        }

        /// <summary>
        /// Sends a response (used to answer inbound requests).
        /// </summary>
        private async Task<bool> SendResponseAsync(JsonRpcResponse response)
        {
            try
            {
                var json = _parser.SerializeMessage(response);
                await _transport.SendMessageAsync(json).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to send response: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handles the transport message-received event.
        /// </summary>
        private void OnMessageReceived(object? sender, AcpTransportMessageReceivedEventArgs e)
        {
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
                var message = _parser.ParseMessage(AcpFrame.StripByteOrderMark(e.Message));


                if (message is JsonRpcResponse response)
                {
                    var responseIdStr = response.Id?.ToString() ?? string.Empty;
                    // Match the pending request.
                    if (_pendingRequests.TryRemove(responseIdStr, out var tcs)
                        && !tcs.TrySetResult(response))
                    {
                        // The entry was still correlated, so the only thing that can already have
                        // completed it is the caller's own cancellation: this is the terminal
                        // response ACP requires the peer to send for a cancelled request (a partial
                        // result, or -32800). Nobody is awaiting it, so record the outcome and let
                        // the removal above close the correlation rather than dropping it silently.
                        _logger.Log(
                            AcpClientLogLevel.Information,
                            "CANCELLED_REQUEST_SETTLED",
                            response.IsError
                                ? $"Cancelled request {responseIdStr} was settled by the peer with error {response.Error!.Code} ({JsonRpcErrorCode.GetErrorMessage(response.Error.Code)})."
                                : $"Cancelled request {responseIdStr} was settled by the peer with a result.",
                            nameof(OnMessageReceived));
                    }
                }

                else if (message is JsonRpcRequest request)
                {
                    // Agent -> client tool invocation (requires a JSON-RPC response).
                    HandleRequest(request);
                }
                else if (message is JsonRpcNotification notification)
                {
                    // Handle the notification.
                    HandleNotification(notification);
                }
            }
            catch (AcpException ex) when (ex.ErrorCode == JsonRpcErrorCode.ParseError)
            {
                // The frame looked like an ACP message (the transport only forwards '{'-leading
                // lines) but did not parse, so the agent did intend to send something. JSON-RPC 2.0
                // covers exactly this: reply -32700 with an explicit null id, since no id could be
                // recovered. Lines that never looked like frames are filtered upstream as
                // StdoutProtocolViolation and deliberately get no reply.
                OnErrorOccurred($"Failed to process message: {ex.Message}");
                _ = SendParseErrorResponseAsync(ex.Message);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process message: {ex.Message}");
            }
        }

        /// <summary>
        /// Replies to an unparseable ACP frame per JSON-RPC 2.0: code -32700, id explicitly null.
        /// </summary>
        private async Task SendParseErrorResponseAsync(string detail)
        {
            try
            {
                await SendResponseAsync(new JsonRpcResponse(
                        id: null,
                        error: JsonRpcError.CreateParseError(detail)))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Never let the courtesy reply become a second failure surface.
                _logger.Log(
                    AcpClientLogLevel.Warning,
                    "PARSE_ERROR_REPLY_FAILED",
                    ex.Message);
            }
        }

        /// <summary>
        /// Handles an inbound notification message.
        /// </summary>
        private void HandleNotification(JsonRpcNotification notification)
        {
            switch (notification.Method)
            {
                case "session/update":
                    HandleSessionUpdate(notification);
                    break;
                case ElicitationMethods.Complete:
                    HandleElicitationCompleted(notification);
                    break;
                default:
                    // Unknown notification type.
                    break;
            }
        }

        /// <summary>
        /// Handles an inbound request message (agent -> client, a response is required).
        /// </summary>
        private void HandleRequest(JsonRpcRequest request)
        {
            var requestIdStr = request.Id?.ToString() ?? string.Empty;

            switch (request.Method)
            {
                case "session/request_permission":
                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    HandlePermissionRequest(request);
                    break;
                case "fs/read_text_file":
                case "fs/write_text_file":
                    if (!SupportsAdvertisedFileSystemCapability(request.Method))
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
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

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    _ = HandleTerminalRequestAsync(request);
                    break;
                case ElicitationMethods.Create:
                    if (!SupportsAdvertisedElicitation)
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }

                    HandleElicitationRequest(request);
                    break;
                case ClientCapabilityMetadata.AskUserExtensionMethod:
                    if (!SupportsAdvertisedAskUserExtension(request.Method))
                    {
                        RejectUnsupportedClientRequest(request);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(requestIdStr))
                    {
                        TrackPendingInboundRequest(requestIdStr, request.Method, request.Id);
                    }
                    HandleAskUserRequest(request);
                    break;
                default:
                    // Best-effort: respond with "method not found" so the agent doesn't hang waiting.
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateMethodNotFound(request.Method)));
                    break;
            }
        }

        private bool SupportsAdvertisedFileSystemCapability(string method)
            => _protocolVersion == AcpProtocolVersion.V1
                && (method switch
                {
                    "fs/read_text_file" => _clientCapabilities?.Fs?.ReadTextFile == true,
                    "fs/write_text_file" => _clientCapabilities?.Fs?.WriteTextFile == true,
                    _ => false
                });

        private bool SupportsAdvertisedAskUserExtension(string method)
            => _clientCapabilities?.SupportsExtension(method) == true;

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
            RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
            _ = SendResponseAsync(new JsonRpcResponse(
                request.Id,
                JsonRpcError.CreateMethodNotFound(request.Method)));
        }

        /// <summary>
        /// Handles the session/update notification.
        /// </summary>
        private void HandleSessionUpdate(JsonRpcNotification notification)
        {
            try
            {
                if (!notification.Params.HasValue)
                {
                    return;
                }

                var updateParams = FromElement(notification.Params.Value, AcpJsonContext.Default.SessionUpdateParams);
                if (updateParams == null || updateParams.Update == null)
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
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp)
                    || sessionIdProp.ValueKind != JsonValueKind.String)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = messageId.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundSessionId(requestId, sessionId);
                }
                if (!rawParams.TryGetProperty("toolCall", out var toolCall)
                    || toolCall.ValueKind != JsonValueKind.Object)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing toolCall")));
                    return;
                }

                if (!toolCall.TryGetProperty("toolCallId", out var toolCallId)
                    || toolCallId.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(toolCallId.GetString()))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing toolCallId")));
                    return;
                }

                if (!rawParams.TryGetProperty("options", out var optionsProp)
                    || optionsProp.ValueKind != JsonValueKind.Array)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
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
                        RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
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

                var permissionResponseFunc = new Func<string, string?, Task>((outcome, optionId) =>
                    RespondToPermissionRequestAsync(messageId, outcome, optionId));

                var eventArgs = new PermissionRequestEventArgs(
                    messageId,
                    sessionId,
                    toolCall,
                    optionsList,
                    permissionResponseFunc);

                if (PermissionRequestReceived == null)
                {
                    // No UI hooked up; cancel to avoid deadlock.
                    _ = RespondToPermissionRequestAsync(messageId, "cancelled", null);
                    return;
                }

                PermissionRequestReceived.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process permission request: {ex.Message}");
                FailPendingInboundRequest(
                    request,
                    JsonRpcError.CreateInternalError("Client failed to process inbound permission request."));
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
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp) ||
                    !rawParams.TryGetProperty("path", out var pathProp))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId or path")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = messageId.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundSessionId(requestId, sessionId);
                }
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
            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var askUserRequest = FromElement(request.Params.Value, AcpJsonContext.Default.AskUserRequest);
                if (askUserRequest == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Failed to deserialize ask_user request.")));
                    return;
                }

                AskUserContract.ValidateRequest(askUserRequest);

                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id!;
                var requestId = messageId.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundAskUserRequest(requestId, askUserRequest);
                }

                if (AskUserRequestReceived == null)
                {
                    RemovePendingInboundTracking(requestId);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        messageId,
                        new JsonRpcError(
                            JsonRpcErrorCode.CapabilityNotSupported,
                            "Ask-user requests are not supported.")));
                    return;
                }

                var eventArgs = new AskUserRequestEventArgs(
                    messageId,
                    askUserRequest,
                    answers => RespondToAskUserRequestAsync(messageId, answers));

                AskUserRequestReceived.Invoke(this, eventArgs);
            }
            catch (InvalidOperationException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message)));
            }
            catch (Exception ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                OnErrorOccurred($"Failed to process ask_user request: {ex.Message}");
                _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInternalError(ex.Message)));
            }
        }

        /// <summary>
        /// Handles an inbound <c>elicitation/create</c> request.
        /// </summary>
        private void HandleElicitationRequest(JsonRpcRequest request)
        {
            var requestIdStr = request.Id?.ToString() ?? string.Empty;

            try
            {
                if (!request.Params.HasValue)
                {
                    RemovePendingInboundTracking(requestIdStr);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var elicitationRequest = FromElement(
                    request.Params.Value,
                    AcpJsonContext.Default.CreateElicitationRequest);
                if (elicitationRequest == null)
                {
                    RemovePendingInboundTracking(requestIdStr);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateInvalidParams("Failed to deserialize elicitation/create request.")));
                    return;
                }

                if (request.Id == null)
                {
                    RemovePendingInboundTracking(requestIdStr);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                if (!SupportsAdvertisedElicitationMode(elicitationRequest))
                {
                    RemovePendingInboundTracking(requestIdStr);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        request.Id,
                        JsonRpcError.CreateInvalidParams(
                            $"Elicitation mode '{elicitationRequest.Mode}' was not advertised by the client.")));
                    return;
                }

                var messageId = request.Id!;
                if (!string.IsNullOrWhiteSpace(requestIdStr))
                {
                    SetPendingInboundElicitationRequest(requestIdStr, elicitationRequest);
                }

                if (ElicitationRequestReceived == null)
                {
                    RemovePendingInboundTracking(requestIdStr);
                    _ = SendResponseAsync(new JsonRpcResponse(
                        messageId,
                        new JsonRpcError(
                            JsonRpcErrorCode.CapabilityNotSupported,
                            "Elicitation requests are not supported.")));
                    return;
                }

                var eventArgs = new ElicitationRequestEventArgs(
                    messageId,
                    elicitationRequest,
                    content => RespondToElicitationRequestAsync(messageId, content),
                    () => DeclineElicitationRequestAsync(messageId),
                    () => CancelElicitationRequestAsync(messageId));

                ElicitationRequestReceived.Invoke(this, eventArgs);
            }
            catch (JsonException ex)
            {
                RemovePendingInboundTracking(requestIdStr);
                _ = SendResponseAsync(new JsonRpcResponse(
                    request.Id,
                    JsonRpcError.CreateInvalidParams(ex.Message)));
            }
            catch (Exception ex)
            {
                RemovePendingInboundTracking(requestIdStr);
                OnErrorOccurred($"Failed to process elicitation/create request: {ex.Message}");
                _ = SendResponseAsync(new JsonRpcResponse(
                    request.Id,
                    JsonRpcError.CreateInternalError(ex.Message)));
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
                completion = FromElement(
                    notification.Params.Value,
                    AcpJsonContext.Default.CompleteElicitationNotification);
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

            if (completion == null || string.IsNullOrWhiteSpace(completion.ElicitationId))
            {
                return;
            }

            ElicitationCompleted?.Invoke(this, new ElicitationCompletedEventArgs(completion.ElicitationId));
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
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing params")));
                    return;
                }

                var rawParams = request.Params.Value;
                if (!rawParams.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                var sessionId = sessionIdProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams("Missing sessionId")));
                    return;
                }

                if (request.Id == null)
                {
                    RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                    _ = SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidRequest("Missing request id")));
                    return;
                }

                var messageId = request.Id;
                var requestId = request.Id?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    SetPendingInboundSessionId(requestId, sessionId);
                }

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
                        var createRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalCreateRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/create request.");
                        var createResponse = await _terminalSessionManager.CreateAsync(createRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(sessionId, createResponse.TerminalId, request.Method);
                        await SendTerminalSuccessResponseAsync(messageId, createResponse).ConfigureAwait(false);
                        break;

                    case "terminal/output":
                        var outputRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalOutputRequest)
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
                        var waitRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalWaitForExitRequest)
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
                        var killRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalKillRequest)
                            ?? throw new InvalidOperationException("Failed to deserialize terminal/kill request.");
                        var killResponse = await _terminalSessionManager.KillAsync(killRequest).ConfigureAwait(false);
                        PublishTerminalStateChanged(sessionId, killRequest.TerminalId, request.Method);
                        await SendTerminalSuccessResponseAsync(messageId, killResponse).ConfigureAwait(false);
                        break;

                    case "terminal/release":
                        var releaseRequest = FromElement(rawParams, AcpJsonContext.Default.TerminalReleaseRequest)
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
                        RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                        await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateMethodNotFound(request.Method))).ConfigureAwait(false);
                        break;
                }
            }
            catch (KeyNotFoundException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message))).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInvalidParams(ex.Message))).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(
                    request.Id,
                    new JsonRpcError(JsonRpcErrorCode.CapabilityNotSupported, ex.Message))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to process terminal request: {ex.Message}");
                RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
                await SendResponseAsync(new JsonRpcResponse(request.Id, JsonRpcError.CreateInternalError(ex.Message))).ConfigureAwait(false);
            }
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalCreateResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalCreateResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalOutputResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalOutputResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalWaitForExitResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalWaitForExitResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalKillResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalKillResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, TerminalReleaseResponse result)
        {
            await SendTerminalSuccessResponseAsync(messageId, ToElement(result, AcpJsonContext.Default.TerminalReleaseResponse)).ConfigureAwait(false);
        }

        private async Task SendTerminalSuccessResponseAsync(object? messageId, JsonElement result)
        {
            RemovePendingInboundTracking(messageId?.ToString() ?? string.Empty);
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

        private async Task CancelPendingInboundRequestsForSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var pendingIds = _pendingInboundRequests
                .Where(pair => string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var pendingId in pendingIds)
            {
                if (!TryGetPendingInboundRequest(pendingId, out var pending))
                {
                    RemovePendingInboundTracking(pendingId);
                    continue;
                }

                if (pending.MessageId == null)
                {
                    RemovePendingInboundTracking(pendingId);
                    continue;
                }

                if (string.Equals(pending.Method, "session/request_permission", StringComparison.Ordinal))
                {
                    await TrySendPermissionOutcomeResponseAsync(pending.MessageId, "cancelled", null).ConfigureAwait(false);
                    continue;
                }

                RemovePendingInboundTracking(pendingId);
                await SendResponseAsync(new JsonRpcResponse(
                    pending.MessageId,
                    new JsonRpcError(
                        JsonRpcErrorCode.MethodNotAllowed,
                        "Session was cancelled before the client completed this request."))).ConfigureAwait(false);
            }
        }

        private void RemovePendingInboundTracking(string idStr)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests.TryRemove(idStr, out _);
        }

        private void FailPendingInboundRequest(JsonRpcRequest request, JsonRpcError error)
        {
            RemovePendingInboundTracking(request.Id?.ToString() ?? string.Empty);
            if (request.Id == null)
            {
                return;
            }

            _ = SendResponseAsync(new JsonRpcResponse(request.Id, error));
        }

        private void TrackPendingInboundRequest(string idStr, string method, object? messageId)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests[idStr] = new PendingInboundRequest(method, messageId);
        }

        private bool TryGetPendingInboundRequest(string idStr, out PendingInboundRequest pending)
        {
            pending = default!;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            return _pendingInboundRequests.TryGetValue(idStr, out pending!);
        }

        private bool TryTakePendingInboundRequest(string idStr, out PendingInboundRequest pending)
        {
            pending = default!;
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return false;
            }

            return _pendingInboundRequests.TryRemove(idStr, out pending!);
        }

        private void SetPendingInboundSessionId(string idStr, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            while (_pendingInboundRequests.TryGetValue(idStr, out var existing))
            {
                var updated = existing.WithSessionId(sessionId);
                if (_pendingInboundRequests.TryUpdate(idStr, updated, existing))
                {
                    return;
                }
            }
        }

        private void SetPendingInboundElicitationRequest(string idStr, CreateElicitationRequest request)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests.AddOrUpdate(
                idStr,
                _ => new PendingInboundRequest(
                    ElicitationMethods.Create,
                    null,
                    request.Scope.SessionId,
                    null,
                    request),
                (_, existing) => existing.WithElicitationRequest(request));
        }

        private void SetPendingInboundAskUserRequest(string idStr, AskUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(idStr))
            {
                return;
            }

            _pendingInboundRequests.AddOrUpdate(
                idStr,
                _ => new PendingInboundRequest(
                    ClientCapabilityMetadata.AskUserExtensionMethod,
                    null,
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

            var enrichedErrorMessage = EnrichTransportErrorMessage(e.ErrorMessage, e.Kind);
            _lastTransportErrorMessage = enrichedErrorMessage;
            OnErrorOccurred(enrichedErrorMessage);
            if (!_transport.IsConnected)
            {
                CancelPendingRequests(enrichedErrorMessage);
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

        private static string EnrichTransportErrorMessage(string errorMessage, AcpTransportErrorKind kind)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return errorMessage;
            }

            if (!ShouldAppendStdioBridgeGuidance(errorMessage, kind))
            {
                return errorMessage;
            }

            const string sshBridgeGuidance =
                " If this is an SSH stdio bridge, avoid ssh -t, ensure stdout emits only ACP frames, and prefer BatchMode=yes.";

            return errorMessage.Contains("ssh -t", StringComparison.Ordinal)
                ? errorMessage
                : errorMessage + sshBridgeGuidance;
        }

        private static bool ShouldAppendStdioBridgeGuidance(string errorMessage, AcpTransportErrorKind kind)
        {
            return kind is AcpTransportErrorKind.ProcessStartFailed
                    or AcpTransportErrorKind.ProcessExited
                    or AcpTransportErrorKind.StdoutReadFailed
                || errorMessage.Contains("stdout", StringComparison.OrdinalIgnoreCase);
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
                : "Failed to connect to the transport: " + transportErrorMessage;
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

            if (!cancellationToken.IsCancellationRequested)
            {
                CancelPendingRequests(_lastTransportErrorMessage ?? "The transport is no longer connected.");
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

        private JsonElement ToElement<T>(T value, JsonTypeInfo<T> typeInfo)
        {
            // Carry the negotiated protocol version along the call flow to the internal converters (for
            // example the version-specific McpServer write path). set -> SerializeToElement -> restore
            // closes synchronously with no await in between, so concurrent requests cannot mix versions.
            using (AcpProtocolWriteContext.Enter(_protocolVersion))
            {
                return JsonSerializer.SerializeToElement(value, typeInfo);
            }
        }

        private static T? FromElement<T>(JsonElement value, JsonTypeInfo<T> typeInfo) =>
            value.Deserialize(typeInfo);

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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _messageLoopCts?.Cancel();

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
