using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Transport;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Starts a launch plan over stdio and performs a real ACP <c>initialize</c> handshake, attributing any
/// failure to the stage it happened in.
/// </summary>
/// <remarks>
/// This deliberately exercises the same transport and ACP client the app uses for real conversations, so
/// a passing test means the saved profile will work rather than merely that the executable exists.
///
/// The attempt is always torn down: a wizard test must not leave an agent process running.
/// </remarks>
public sealed class StdioAcpSetupHandshakeProbe : IAcpSetupHandshakeProbe
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(60);

    private readonly IStdioTransportFactory _transportFactory;
    private readonly Func<ITransport, IAcpClient> _createClient;

    public StdioAcpSetupHandshakeProbe(
        IStdioTransportFactory transportFactory,
        Func<ITransport, IAcpClient> createClient)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
    }

    public async Task<AcpSetupTestResult> ProbeAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchPlan);

        ITransport transport;
        try
        {
            transport = _transportFactory.Create(
                launchPlan.Command,
                CopyArguments(launchPlan.Arguments),
                Encoding.UTF8,
                launchPlan.Environment);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return AcpSetupTestResult.Failure(
                AcpSetupTestStage.CommandResolution,
                ex.Message,
                RemediationKeys.CommandResolution);
        }

        var stderr = new StderrCollector(transport);
        try
        {
            return await ProbeWithTransportAsync(transport, stderr, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stderr.Detach();
            transport.Dispose();
        }
    }

    private async Task<AcpSetupTestResult> ProbeWithTransportAsync(
        ITransport transport,
        StderrCollector stderr,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(HandshakeTimeout);

        var client = _createClient(transport);
        try
        {
            if (!await transport.ConnectAsync(timeoutSource.Token).ConfigureAwait(false))
            {
                return AcpSetupTestResult.Failure(
                    AcpSetupTestStage.AdapterStartup,
                    stderr.Describe() ?? "The adapter process did not start.",
                    RemediationKeys.AdapterStartup);
            }

            var response = await client
                .InitializeAsync(CreateInitializeParams(), timeoutSource.Token)
                .ConfigureAwait(false);

            return AcpSetupTestResult.Success(
                response.ProtocolVersion,
                response.AgentInfo?.Name);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked source fired, so the deadline elapsed rather than the user cancelling.
            return AcpSetupTestResult.Failure(
                AcpSetupTestStage.Handshake,
                stderr.Describe() ?? $"No ACP response within {HandshakeTimeout.TotalSeconds:0}s.",
                RemediationKeys.Handshake);
        }
        catch (AcpException ex)
        {
            return AcpSetupTestResult.Failure(
                AcpSetupTestStage.Handshake,
                Combine(ex.Message, stderr.Describe()),
                RemediationKeys.Handshake);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AcpSetupTestResult.Failure(
                AcpSetupTestStage.Handshake,
                Combine(ex.Message, stderr.Describe()),
                RemediationKeys.Handshake);
        }
        finally
        {
            await TryDisconnectAsync(client).ConfigureAwait(false);
        }
    }

    private static async Task TryDisconnectAsync(IAcpClient client)
    {
        try
        {
            await client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Teardown of a probe attempt: the result is already decided, and the transport is disposed
            // by the caller regardless.
        }
    }

    private static InitializeParams CreateInitializeParams()
        => new()
        {
            ProtocolVersion = AcpProtocolVersion.Default,
            ClientInfo = new ClientInfo
            {
                Name = "SalmonEgg",
                Title = "SalmonEgg",
                Version = "1.0.0"
            },
            ClientCapabilities = ClientCapabilityDefaults.Create()
        };

    private static string[] CopyArguments(IReadOnlyList<string> arguments)
    {
        var copy = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            copy[index] = arguments[index];
        }

        return copy;
    }

    private static string? Combine(string? primary, string? secondary)
        => string.IsNullOrWhiteSpace(secondary)
            ? primary
            : string.IsNullOrWhiteSpace(primary)
                ? secondary
                : primary + Environment.NewLine + secondary;

    private static class RemediationKeys
    {
        public const string CommandResolution = "AcpSetup_Remediation_CommandResolution";
        public const string AdapterStartup = "AcpSetup_Remediation_AdapterStartup";
        public const string Handshake = "AcpSetup_Remediation_Handshake";
    }

    /// <summary>
    /// Captures the adapter's stderr so a failed handshake can report what the agent complained about
    /// instead of only a timeout.
    /// </summary>
    private sealed class StderrCollector
    {
        private const int MaxLength = 2000;

        private readonly ITransport _transport;
        private readonly StringBuilder _builder = new();
        private readonly object _gate = new();

        public StderrCollector(ITransport transport)
        {
            _transport = transport;
            _transport.ErrorOccurred += OnError;
        }

        public void Detach() => _transport.ErrorOccurred -= OnError;

        public string? Describe()
        {
            lock (_gate)
            {
                return _builder.Length == 0 ? null : _builder.ToString().TrimEnd();
            }
        }

        private void OnError(object? sender, TransportErrorEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.ErrorMessage))
            {
                return;
            }

            lock (_gate)
            {
                if (_builder.Length >= MaxLength)
                {
                    return;
                }

                _builder.AppendLine(e.ErrorMessage);
            }
        }
    }
}