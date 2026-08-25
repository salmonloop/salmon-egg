using System;
using System.Collections.Generic;
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
        /// Sends a prompt to the session.
        /// Sends a session/prompt request and waits for the Agent's response.
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

        /// <summary>
        /// The list of available permission options.
        /// </summary>
        public List<PermissionOption> Options { get; init; } = new List<PermissionOption>();

        /// <summary>
        /// The response callback.
        /// </summary>
        public Func<string, string?, Task> Respond { get; init; } = null!;

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
            Options = options;
            Respond = respond;
        }
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
