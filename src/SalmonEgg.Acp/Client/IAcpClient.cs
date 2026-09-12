using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Acp.Client
{
    /// <summary>
    /// ACP client interface.
    /// Defines the core methods for communicating with an Agent.
    /// The client exclusively owns its transport (process/socket/HttpClient) and the message-loop CTS;
    /// its lifetime is owned by the holder (ChatService), so the contract includes
    /// <see cref="IDisposable"/>: disconnecting only stops traffic, whereas disposing returns the
    /// underlying resources.
    /// </summary>
    public interface IAcpClient : IDisposable
    {
        /// <summary>
        /// Initialization event. Raised when initialization completes.
        /// </summary>
        event EventHandler<InitializeResponse>? Initialized;

        /// <summary>
        /// Session update event. Raised when a session update notification is received.
        /// </summary>
        event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived;

        /// <summary>
        /// Permission request event. Raised when a permission request is received.
        /// </summary>
        event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived;

        /// <summary>
        /// File system request event. Raised when a file system operation request is received.
        /// </summary>
        event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived;

        /// <summary>
        /// Terminal request event. Raised when a terminal operation request is received.
        /// </summary>
        event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived;

        /// <summary>
        /// Terminal state event. Raised when the client executes an ACP terminal request and obtains a state snapshot.
        /// </summary>
        event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived;

        /// <summary>
        /// Ask-user request event. Raised when the Agent needs a structured answer from the user.
        /// </summary>
        event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived;

        /// <summary>
        /// Elicitation request event. Raised when the Agent requests structured user input through the
        /// standard <c>elicitation/create</c> method.
        /// </summary>
        /// <remarks>
        /// Carries a default implementation so adding the elicitation family to this published interface
        /// does not break existing external implementers. A client that leaves the default in place never
        /// raises the event, which is consistent with not advertising the capability. The shipped
        /// <see cref="AcpClient"/> implements it.
        /// </remarks>
        event EventHandler<ElicitationRequestEventArgs>? ElicitationRequestReceived
        {
            add { }
            remove { }
        }

        /// <summary>
        /// Elicitation completion event. Raised when the Agent reports that a URL-mode elicitation
        /// finished out of band.
        /// </summary>
        event EventHandler<ElicitationCompletedEventArgs>? ElicitationCompleted
        {
            add { }
            remove { }
        }

        /// <summary>
        /// Connection error event. Raised when a connection error occurs.
        /// </summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Gets a value indicating whether the client has been initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Gets a value indicating whether the client is connected to the Agent.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>The protocol negotiated by the active connection, or the stable default before initialization.</summary>
        int NegotiatedProtocolVersion => AcpProtocolVersion.Default;

        /// <summary>
        /// Whether configuration RPC results also arrive through the ordered session update stream.
        /// Legacy host implementations may retain their response-based application path.
        /// </summary>
        bool PublishesConfigurationResponses => false;

        /// <summary>
        /// Gets the current Agent information.
        /// </summary>
        AgentInfo? AgentInfo { get; }

        /// <summary>
        /// Gets the current Agent capabilities.
        /// </summary>
        AgentCapabilities? AgentCapabilities { get; }

        /// <summary>
        /// Initializes the connection to the Agent.
        /// Sends an initialize request and waits for the Agent's response.
        /// </summary>
        /// <param name="params">The initialization parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The initialization response</returns>
        Task<InitializeResponse> InitializeAsync(InitializeParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new session.
        /// Sends a session/new request and waits for the Agent's response.
        /// </summary>
        /// <param name="params">The create-session parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The create-session response</returns>
        Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads an existing session.
        /// Sends a session/load request and waits for the Agent to replay history through session/update notifications.
        /// </summary>
        /// <param name="params">The load-session parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The load-session response</returns>
        Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resumes an existing session without requiring the Agent to replay history.
        /// Sends a session/resume request and waits for the Agent to restore the run context.
        /// </summary>
        /// <param name="params">The resume-session parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The resume-session response</returns>
        Task<SessionResumeResponse> ResumeSessionAsync(SessionResumeParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes an existing session and releases the Agent-side resources.
        /// Sends a session/close request.
        /// </summary>
        /// <param name="params">The close-session parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The close-session response</returns>
        Task<SessionCloseResponse> CloseSessionAsync(SessionCloseParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a remote Agent session.
        /// Sends a session/delete request.
        /// </summary>
        /// <param name="params">The delete-session parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The delete-session response</returns>
        Task<SessionDeleteResponse> DeleteSessionAsync(SessionDeleteParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the sessions supported by the remote Agent.
        /// Sends a session/list request and waits for the Agent's response.
        /// </summary>
        /// <param name="params">The list parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The session list response</returns>
        Task<SessionListResponse> ListSessionsAsync(SessionListParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a prompt to the session and waits for foreground work to finish.
        /// On stable v1 the terminal session/prompt response supplies the completion.
        /// A prompt acceptance acknowledgement is never returned as completed work.
        /// </summary>
        /// <param name="params">The send-prompt parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The send-prompt response</returns>
        Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the session mode.
        /// Sends a session/set_mode request.
        /// </summary>
        /// <param name="params">The set-mode parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The set-mode response</returns>
        Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a session configuration option.
        /// Sends a session/set_config_option request.
        /// </summary>
        /// <param name="params">The set-config parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The set-config response</returns>
        Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends the ACP <c>session/cancel</c> notification.
        /// </summary>
        Task CancelSessionAsync(SessionCancelParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs authentication.
        /// Sends an authenticate request.
        /// </summary>
        /// <param name="params">The authentication parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The authentication response</returns>
        Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Logs out of the current authentication state.
        /// Sends a logout request.
        /// </summary>
        /// <param name="params">The logout parameters</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The logout response</returns>
        Task<LogoutResponse> LogoutAsync(LogoutParams @params, CancellationToken cancellationToken = default);

        /// <summary>
        /// Responds to a permission request.
        /// Sends the response to a previously received permission request.
        /// </summary>
        /// <param name="messageId">The message ID of the original request</param>
        /// <param name="outcome">The outcome (`selected` or `cancelled`)</param>
        /// <param name="optionId">The ID of the selected option (optional)</param>
        /// <returns>Whether the response was sent successfully</returns>
        Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null);

        /// <summary>
        /// Responds to a file system request.
        /// Sends the response to a previously received file system request.
        /// </summary>
        /// <param name="messageId">The message ID of the original request</param>
        /// <param name="success">Whether the operation succeeded</param>
        /// <param name="content">The file content (read operations)</param>
        /// <param name="message">The error message (when the operation failed)</param>
        /// <returns>Whether the response was sent successfully</returns>
        Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null);

        /// <summary>
        /// Responds to an ask-user request.
        /// Sends the structured answers for a previously received interactive question request.
        /// </summary>
        /// <param name="messageId">The message ID of the original request</param>
        /// <param name="answers">A mapping from question to answer.</param>
        /// <returns>Whether the response was sent successfully</returns>
        Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers);

        /// <summary>
        /// Accepts an elicitation request, optionally submitting the collected form content.
        /// </summary>
        /// <param name="messageId">The message ID of the original request.</param>
        /// <param name="content">The submitted content, or <c>null</c> to accept without content.</param>
        /// <returns>Whether the response was sent successfully</returns>
        Task<bool> RespondToElicitationRequestAsync(object messageId, ElicitationAcceptContent? content)
            => Task.FromResult(false);

        /// <summary>
        /// Declines an elicitation request on the user's behalf.
        /// </summary>
        /// <param name="messageId">The message ID of the original request.</param>
        /// <returns>Whether the response was sent successfully</returns>
        Task<bool> DeclineElicitationRequestAsync(object messageId)
            => Task.FromResult(false);

        /// <summary>
        /// Cancels an elicitation request the user dismissed without choosing.
        /// </summary>
        /// <param name="messageId">The message ID of the original request.</param>
        /// <returns>Whether the response was sent successfully</returns>
        Task<bool> CancelElicitationRequestAsync(object messageId)
            => Task.FromResult(false);

        /// <summary>
        /// Disconnects from the Agent.
        /// </summary>
        /// <returns>Whether the disconnect succeeded</returns>
        Task<bool> DisconnectAsync();
    }

    /// <summary>
    /// Session update event arguments.
    /// </summary>
    public sealed class SessionUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// The session ID.
        /// </summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The update payload.
        /// </summary>
        public SessionUpdate? Update { get; init; }

        /// <summary>
        /// Complete entity state projected by the SDK. Hosts consume this view without depending
        /// on draft wire types; it is absent on legacy external client implementations.
        /// </summary>
        public AcpSessionUpdateView? View { get; init; }

        /// <summary>True for authoritative configuration supplied by an RPC response rather than a notification.</summary>
        public bool IsResponseProjection { get; init; }

        internal Func<bool>? ConnectionIsCurrent { get; init; }

        /// <summary>Whether this captured update still belongs to its receiving connection.</summary>
        public bool IsCurrent => ConnectionIsCurrent?.Invoke() ?? true;

        /// <summary>
        /// Creates new session update event arguments.
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <param name="update">The update payload</param>
        public SessionUpdateEventArgs(string sessionId, SessionUpdate? update)
        {
            SessionId = sessionId;
            Update = update;
        }
    }

    /// <summary>
    /// Permission request event arguments.
    /// </summary>
    public sealed class PermissionRequestEventArgs : EventArgs
    {
        private readonly Func<string, string?, Task<bool>>? _tryRespond;
        private readonly Func<bool>? _canRespond;
        private readonly Func<bool>? _isResponsePrepared;
        private readonly Func<bool>? _isResponseSending;
        private readonly Func<bool>? _isCancellationRequested;
        private readonly IAcpClientLogger? _logger;
        private int _changeVersion;
        private int _notificationScheduled;

        /// <summary>
        /// Creates new permission request event arguments.
        /// </summary>
        /// <param name="messageId">The message ID</param>
        /// <param name="sessionId">The session ID</param>
        /// <param name="toolCall">The tool call</param>
        /// <param name="options">The permission options</param>
        /// <param name="respond">The response callback</param>
        public PermissionRequestEventArgs(
            object messageId,
            string sessionId,
            object? toolCall,
            List<PermissionOption> options,
            Func<string, string?, Task> respond)
        {
            MessageId = messageId;
            SessionId = sessionId;
            ToolCall = toolCall;
            Title = ReadToolCallTitle(toolCall);
            Options = options;
            Respond = respond;
        }

        internal PermissionRequestEventArgs(
            object messageId,
            string sessionId,
            object? toolCall,
            List<PermissionOption> options,
            Func<string, string?, Task<bool>> tryRespond,
            Func<bool> canRespond)
            : this(messageId, sessionId, toolCall, options, (outcome, optionId) => tryRespond(outcome, optionId))
        {
            _tryRespond = tryRespond;
            _canRespond = canRespond;
        }

        internal PermissionRequestEventArgs(
            object messageId, string sessionId, object? toolCall, List<PermissionOption> options,
            Func<string, string?, Task<bool>> tryRespond, Func<bool> canRespond,
            Func<bool> isResponsePrepared, Func<bool> isCancellationRequested, IAcpClientLogger? logger = null)
            : this(messageId, sessionId, toolCall, options, tryRespond, canRespond)
        {
            _isResponsePrepared = isResponsePrepared;
            _isCancellationRequested = isCancellationRequested;
            _logger = logger;
        }

        internal PermissionRequestEventArgs(
            object messageId, string sessionId, object? toolCall, List<PermissionOption> options,
            Func<string, string?, Task<bool>> tryRespond, Func<bool> canRespond,
            Func<bool> isResponsePrepared, Func<bool> isResponseSending,
            Func<bool> isCancellationRequested, IAcpClientLogger? logger = null)
            : this(messageId, sessionId, toolCall, options, tryRespond, canRespond,
                isResponsePrepared, isCancellationRequested, logger)
        {
            _isResponseSending = isResponseSending;
        }

        internal AcpPermissionRequestSnapshot? DraftRequest { get; init; }

        /// <summary>
        /// The message ID of the original request.
        /// </summary>
        public object MessageId { get; init; } = string.Empty;

        /// <summary>
        /// The session ID.
        /// </summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The tool call data.
        /// </summary>
        public object? ToolCall { get; init; }

        /// <summary>The permission title supplied by the peer, or null when the protocol omits it.</summary>
        public string? Title { get; init; }

        /// <summary>The optional explanation supplied by the peer.</summary>
        public string? Description { get; init; }

        /// <summary>
        /// The list of available permission options.
        /// </summary>
        public List<PermissionOption> Options { get; init; } = new List<PermissionOption>();

        /// <summary>
        /// The response callback.
        /// </summary>
        public Func<string, string?, Task> Respond { get; init; } = null!;

        /// <summary>Whether the receiving client still owns this exact unanswered request.</summary>
        /// <remarks>Events created by the legacy public constructor have no client lifetime query.</remarks>
        public bool CanRespond => _canRespond?.Invoke() ?? true;

        /// <summary>Whether an answer is prepared or being sent, without confirmation of delivery.</summary>
        /// <remarks>A failed write resets this value so the original request may be retried.</remarks>
        public bool IsResponsePrepared => _isResponsePrepared?.Invoke() ?? false;

        /// <summary>Whether the original response is awaiting completion of its physical send.</summary>
        /// <remarks>
        /// A prepared batch answer may wait for sibling answers before sending. Independent replies
        /// keep their presentation until this send finishes. Legacy events conservatively treat a
        /// prepared response as sending when they do not provide a separate delivery query.
        /// </remarks>
        public bool IsResponseSending => _isResponseSending?.Invoke() ?? IsResponsePrepared;

        /// <summary>Whether the original owner has withdrawn selection and only cancellation may be retried.</summary>
        public bool IsCancellationRequested => _isCancellationRequested?.Invoke() ?? false;

        /// <summary>Raised asynchronously when the original request's response availability changes.</summary>
        /// <remarks>
        /// Notifications may coalesce. Subscribe, then read the current properties; marshal UI updates
        /// to the UI thread and unsubscribe when the interaction leaves the view. Observer exceptions
        /// cannot change the response result or prevent other observers from receiving a notification.
        /// Unsubscribing does not retract an already captured callback; check ownership again after
        /// dispatching. Observers run sequentially for this request, outside the response send path.
        /// </remarks>
        public event EventHandler? Changed;

        /// <summary>Responds through the original request owner and reports whether sending succeeded.</summary>
        /// <remarks>
        /// A false result does not confirm completion; consult <see cref="CanRespond"/> before retrying.
        /// For events created with the legacy public constructor, successful completion of its Task
        /// callback is treated as a successful response.
        /// </remarks>
        public async Task<bool> TryRespondAsync(string outcome, string? optionId = null)
        {
            if (_tryRespond is not null)
            {
                return await _tryRespond(outcome, optionId).ConfigureAwait(false);
            }

            await Respond(outcome, optionId).ConfigureAwait(false);
            return true;
        }

        internal void NotifyChanged()
        {
            if (Changed is null) return;
            Interlocked.Increment(ref _changeVersion);
            if (Interlocked.CompareExchange(ref _notificationScheduled, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(static state => state.DrainChanges(), this, preferLocal: false);
            }
        }

        private void DrainChanges()
        {
            while (true)
            {
                var version = Volatile.Read(ref _changeVersion);
                var subscribers = Changed;
                if (subscribers is not null)
                {
                    foreach (EventHandler subscriber in subscribers.GetInvocationList())
                    {
                        try
                        {
                            subscriber(this, EventArgs.Empty);
                        }
                        catch (Exception error)
                        {
                            LogObserverFailure(error);
                        }
                    }
                }
                if (version != Volatile.Read(ref _changeVersion)) continue;
                Volatile.Write(ref _notificationScheduled, 0);
                if (version == Volatile.Read(ref _changeVersion)
                    || Interlocked.CompareExchange(ref _notificationScheduled, 1, 0) != 0) return;
            }
        }

        private void LogObserverFailure(Exception error)
        {
            try
            {
                _logger?.Log(AcpClientLogLevel.Error, "PERMISSION_OBSERVER_FAILED",
                    "A permission availability observer failed.", nameof(PermissionRequestEventArgs), error);
            }
            catch (Exception)
            {
                // Even a faulty host logger cannot fault this ThreadPool callback or skip sibling
                // observers. There is no second logger to report that host failure without recursion.
            }
        }

        private static string? ReadToolCallTitle(object? toolCall)
            => toolCall switch
            {
                ToolCallUpdate update => update.Title,
                JsonElement { ValueKind: JsonValueKind.Object } value when value.TryGetProperty("title", out var title)
                    && title.ValueKind == JsonValueKind.String => title.GetString(),
                _ => null
            };
    }

    public enum FileSystemRequestKind
    {
        ReadTextFile,
        WriteTextFile
    }

    /// <summary>
    /// File system request event arguments.
    /// </summary>
    public sealed class FileSystemRequestEventArgs : EventArgs
    {
        /// <summary>
        /// The message ID of the original request.
        /// </summary>
        public object MessageId { get; init; } = string.Empty;

        /// <summary>
        /// The session ID.
        /// </summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The ACP file system request method.
        /// </summary>
        public string Method { get; init; } = string.Empty;

        /// <summary>
        /// The file system request kind.
        /// </summary>
        public FileSystemRequestKind Kind { get; init; }

        /// <summary>
        /// The file path.
        /// </summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>
        /// The file encoding (read operations).
        /// </summary>
        public string? Encoding { get; init; }

        /// <summary>
        /// The file content (write operations).
        /// </summary>
        public string? Content { get; init; }

        /// <summary>
        /// The response callback.
        /// </summary>
        public Func<bool, string?, string?, Task> Respond { get; init; } = null!;

        /// <summary>
        /// Creates new file system request event arguments.
        /// </summary>
        /// <param name="messageId">The message ID</param>
        /// <param name="sessionId">The session ID</param>
        /// <param name="method">The ACP method name</param>
        /// <param name="kind">The request kind</param>
        /// <param name="path">The file path</param>
        /// <param name="encoding">The encoding</param>
        /// <param name="content">The content</param>
        /// <param name="respond">The response callback</param>
        public FileSystemRequestEventArgs(
            object messageId,
            string sessionId,
            string method,
            FileSystemRequestKind kind,
            string path,
            string? encoding = null,
            string? content = null,
            Func<bool, string?, string?, Task> respond = null!)
        {
            MessageId = messageId;
            SessionId = sessionId;
            Method = method;
            Kind = kind;
            Path = path;
            Encoding = encoding;
            Content = content;
            Respond = respond;
        }
    }
}
