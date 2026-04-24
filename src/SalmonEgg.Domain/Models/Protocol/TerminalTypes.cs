using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Terminal/create method request parameters.
    /// Agent initiates this request to create a terminal and execute a command.
    /// </summary>
    public class TerminalCreateRequest
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The command to execute.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// Array of command arguments.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        /// <summary>
        /// Environment variables for the command.
        /// </summary>
        [JsonPropertyName("env")]
        public List<EnvVariable>? Env { get; set; }

        /// <summary>
        /// Working directory for the command (absolute path).
        /// </summary>
        [JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        /// <summary>
        /// Maximum number of output bytes to retain.
        /// </summary>
        [JsonPropertyName("outputByteLimit")]
        public int? OutputByteLimit { get; set; }
    }

    /// <summary>
    /// Environment variable for terminal commands.
    /// </summary>
    public class EnvVariable
    {
        /// <summary>
        /// The name of the environment variable.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The value of the environment variable.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/create method.
    /// </summary>
    public class TerminalCreateResponse
    {
        /// <summary>
        /// The unique identifier for the created terminal.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Terminal/output method request parameters.
    /// Agent initiates this request to get terminal output.
    /// </summary>
    public class TerminalOutputRequest
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to get output from.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/output method.
    /// </summary>
    public class TerminalOutputResponse
    {
        /// <summary>
        /// The terminal output captured so far.
        /// </summary>
        [JsonPropertyName("output")]
        public string Output { get; set; } = string.Empty;

        /// <summary>
        /// Whether the output was truncated due to byte limits.
        /// </summary>
        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }

        /// <summary>
        /// Exit status if the command has completed.
        /// </summary>
        [JsonPropertyName("exitStatus")]
        public TerminalExitStatus? ExitStatus { get; set; }
    }

    /// <summary>
    /// Terminal exit status information.
    /// </summary>
    public class TerminalExitStatus
    {
        /// <summary>
        /// The process exit code (may be null if terminated by signal).
        /// </summary>
        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; }

        /// <summary>
        /// The signal that terminated the process (may be null if exited normally).
        /// </summary>
        [JsonPropertyName("signal")]
        public string? Signal { get; set; }
    }

    /// <summary>
    /// Terminal/wait_for_exit method request parameters.
    /// Agent initiates this request to wait for terminal command to exit.
    /// </summary>
    public class TerminalWaitForExitRequest
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to wait for.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/wait_for_exit method.
    /// </summary>
    public class TerminalWaitForExitResponse
    {
        /// <summary>
        /// The process exit code (may be null if terminated by signal).
        /// </summary>
        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; }

        /// <summary>
        /// The signal that terminated the process (may be null if exited normally).
        /// </summary>
        [JsonPropertyName("signal")]
        public string? Signal { get; set; }
    }

    /// <summary>
    /// Terminal/kill method request parameters.
    /// Agent initiates this request to kill a terminal command without releasing the terminal.
    /// </summary>
    public class TerminalKillRequest
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to kill.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/kill method.
    /// </summary>
    public class TerminalKillResponse
    {
        // Empty response - success is indicated by no error
    }

    /// <summary>
    /// Terminal/release method request parameters.
    /// Agent initiates this request to release a terminal and free its resources.
    /// </summary>
    public class TerminalReleaseRequest
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to release.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/release method.
    /// </summary>
    public class TerminalReleaseResponse
    {
        // Empty response - success is indicated by no error
    }

    /// <summary>
    /// Event arguments for terminal request events.
    /// </summary>
    public class TerminalRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Original request message ID.
        /// </summary>
        public object MessageId { get; set; } = string.Empty;

        /// <summary>
        /// Session ID.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Terminal ID (if applicable).
        /// </summary>
        public string? TerminalId { get; set; }

        /// <summary>
        /// Request method name.
        /// </summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// Request parameters as JSON element.
        /// </summary>
        public object? Params { get; set; }

        /// <summary>
        /// Raw request parameters as received from the protocol request.
        /// </summary>
        public object? RawParams => Params;

        /// <summary>
        /// Response callback.
        /// </summary>
        public Func<object, Task<bool>> Respond { get; set; } = null!;

        /// <summary>
        /// Creates a new TerminalRequestEventArgs instance.
        /// </summary>
        public TerminalRequestEventArgs()
        {
        }

        /// <summary>
        /// Creates a new TerminalRequestEventArgs instance.
        /// </summary>
        public TerminalRequestEventArgs(
            object messageId,
            string sessionId,
            string? terminalId,
            string method,
            object? @params,
            Func<object, Task<bool>> respond)
        {
            MessageId = messageId;
            SessionId = sessionId;
            TerminalId = terminalId;
            Method = method;
            Params = @params;
            Respond = respond;
        }
    }

    /// <summary>
    /// Event arguments for client-owned terminal state snapshots.
    /// </summary>
    public class TerminalStateChangedEventArgs : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;

        public string TerminalId { get; set; } = string.Empty;

        public string Method { get; set; } = string.Empty;

        public string? Output { get; set; }

        public bool? Truncated { get; set; }

        public TerminalExitStatus? ExitStatus { get; set; }

        public bool IsReleased { get; set; }

        public TerminalStateChangedEventArgs()
        {
        }

        public TerminalStateChangedEventArgs(
            string sessionId,
            string terminalId,
            string method,
            string? output = null,
            bool? truncated = null,
            TerminalExitStatus? exitStatus = null,
            bool isReleased = false)
        {
            SessionId = sessionId;
            TerminalId = terminalId;
            Method = method;
            Output = output;
            Truncated = truncated;
            ExitStatus = exitStatus;
            IsReleased = isReleased;
        }
    }
}
