using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Terminal/create method request parameters.
    /// Agent initiates this request to create a terminal and execute a command.
    /// </summary>
    public sealed record TerminalCreateRequest : AcpProtocolObject
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The command to execute.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command { get; init; } = string.Empty;

        /// <summary>
        /// Array of command arguments.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string>? Args { get; init; }

        /// <summary>
        /// Environment variables for the command.
        /// </summary>
        [JsonPropertyName("env")]
        public List<EnvVariable>? Env { get; init; }

        /// <summary>
        /// Working directory for the command (absolute path).
        /// </summary>
        [JsonPropertyName("cwd")]
        public string? Cwd { get; init; }

        /// <summary>
        /// Maximum number of output bytes to retain.
        /// </summary>
        [JsonPropertyName("outputByteLimit")]
        public ulong? OutputByteLimit { get; init; }
    }

    /// <summary>
    /// Environment variable for terminal commands.
    /// </summary>
    public sealed record EnvVariable : AcpProtocolObject
    {
        /// <summary>
        /// The name of the environment variable.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// The value of the environment variable.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;

        public EnvVariable()
        {
        }

        [SetsRequiredMembers]
        public EnvVariable(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Response for terminal/create method.
    /// </summary>
    public sealed record TerminalCreateResponse : AcpProtocolObject
    {
        /// <summary>
        /// The unique identifier for the created terminal.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Terminal/output method request parameters.
    /// Agent initiates this request to get terminal output.
    /// </summary>
    public sealed record TerminalOutputRequest : AcpProtocolObject
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to get output from.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/output method.
    /// </summary>
    public sealed record TerminalOutputResponse : AcpProtocolObject
    {
        /// <summary>
        /// The terminal output captured so far.
        /// </summary>
        [JsonPropertyName("output")]
        public string Output { get; init; } = string.Empty;

        /// <summary>
        /// Whether the output was truncated due to byte limits.
        /// </summary>
        [JsonPropertyName("truncated")]
        public bool Truncated { get; init; }

        /// <summary>
        /// Exit status if the command has completed.
        /// </summary>
        [JsonPropertyName("exitStatus")]
        public TerminalExitStatus? ExitStatus { get; init; }
    }

    /// <summary>
    /// Terminal exit status information.
    /// </summary>
    public sealed record TerminalExitStatus : AcpProtocolObject
    {
        /// <summary>
        /// The process exit code (may be null if terminated by signal).
        /// </summary>
        [JsonPropertyName("exitCode")]
        public uint? ExitCode { get; init; }

        /// <summary>
        /// The signal that terminated the process (may be null if exited normally).
        /// </summary>
        [JsonPropertyName("signal")]
        public string? Signal { get; init; }
    }

    /// <summary>
    /// Terminal/wait_for_exit method request parameters.
    /// Agent initiates this request to wait for terminal command to exit.
    /// </summary>
    public sealed record TerminalWaitForExitRequest : AcpProtocolObject
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to wait for.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/wait_for_exit method.
    /// </summary>
    public sealed record TerminalWaitForExitResponse : AcpProtocolObject
    {
        /// <summary>
        /// The process exit code (may be null if terminated by signal).
        /// </summary>
        [JsonPropertyName("exitCode")]
        public uint? ExitCode { get; init; }

        /// <summary>
        /// The signal that terminated the process (may be null if exited normally).
        /// </summary>
        [JsonPropertyName("signal")]
        public string? Signal { get; init; }
    }

    /// <summary>
    /// Terminal/kill method request parameters.
    /// Agent initiates this request to kill a terminal command without releasing the terminal.
    /// </summary>
    public sealed record TerminalKillRequest : AcpProtocolObject
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to kill.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/kill method.
    /// </summary>
    public sealed record TerminalKillResponse : AcpProtocolObject
    {
        // Empty response - success is indicated by no error
    }

    /// <summary>
    /// Terminal/release method request parameters.
    /// Agent initiates this request to release a terminal and free its resources.
    /// </summary>
    public sealed record TerminalReleaseRequest : AcpProtocolObject
    {
        /// <summary>
        /// The session ID for this request.
        /// </summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The ID of the terminal to release.
        /// </summary>
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Response for terminal/release method.
    /// </summary>
    public sealed record TerminalReleaseResponse : AcpProtocolObject
    {
        // Empty response - success is indicated by no error
    }

    /// <summary>
    /// Event arguments for terminal request events.
    /// </summary>
    public sealed class TerminalRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Original request message ID.
        /// </summary>
        public object MessageId { get; init; } = string.Empty;

        /// <summary>
        /// Session ID.
        /// </summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// Terminal ID (if applicable).
        /// </summary>
        public string? TerminalId { get; init; }

        /// <summary>
        /// Request method name.
        /// </summary>
        public string Method { get; init; } = string.Empty;

        /// <summary>
        /// Request parameters as JSON element.
        /// </summary>
        public object? Params { get; init; }

        /// <summary>
        /// Raw request parameters as received from the protocol request.
        /// </summary>
        public object? RawParams => Params;

        /// <summary>
        /// Response callback.
        /// </summary>
        public Func<object, Task<bool>> Respond { get; init; } = null!;

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
    public sealed class TerminalStateChangedEventArgs : EventArgs
    {
        public string SessionId { get; init; } = string.Empty;

        public string TerminalId { get; init; } = string.Empty;

        public string Method { get; init; } = string.Empty;

        public string? Output { get; init; }

        public bool? Truncated { get; init; }

        public TerminalExitStatus? ExitStatus { get; init; }

        public bool IsReleased { get; init; }

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
