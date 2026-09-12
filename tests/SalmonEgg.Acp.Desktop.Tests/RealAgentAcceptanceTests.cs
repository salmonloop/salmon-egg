using System.Collections.Concurrent;
using System.Text;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Infrastructure.Transport;
using Xunit;

namespace SalmonEgg.Acp.Desktop.Tests;

/// <summary>Opt-in acceptance through the production client, adapter and real Agent process.</summary>
public sealed class RealAgentAcceptanceTests
{
    private const string CommandVariable = "SALMONEGG_REAL_AGENT_COMMAND";
    private const string ArgumentsVariable = "SALMONEGG_REAL_AGENT_ARGUMENTS";

    [Fact]
    public async Task RealAgent_InitializeCreateAndPrompt_CompletesThroughProductionClient()
        => await RunAsync(expectForm: false);

    [Fact]
    public async Task RealAgent_FormAnswer_ReturnsThroughStandardElicitation()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_FORM") == "1",
            "Enable only for a real Agent that supplies standard form elicitation.");
        await RunAsync(expectForm: true);
    }

    [Fact]
    public async Task RealAgent_McpUrlRequest_CompletesTheStandardConsentLifecycle()
    {
        // Arrange: the MCP helper triggers a real Agent, rather than replacing the Agent.
        var command = Environment.GetEnvironmentVariable(CommandVariable);
        var peer = Environment.GetEnvironmentVariable("SALMONEGG_REAL_AGENT_MCP_PEER");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(peer),
            "Set the installed Agent and the local MCP elicitation helper path.");
        var directory = Path.Combine(Path.GetTempPath(), "salmon-egg-url-agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        try
        {
            using var transport = new StdioTransport(command!, ReadArguments());
            using var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            var requested = new TaskCompletionSource<ElicitationRequestEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.ElicitationRequestReceived += (_, request) => requested.TrySetResult(request);
            client.ElicitationCompleted += (_, value) => completed.TrySetResult(value.ElicitationId);
            var permissions = new ConcurrentQueue<Task>();
            client.PermissionRequestReceived += (_, request) => permissions.Enqueue(request.Respond("cancelled", null));
            await client.InitializeAsync(new InitializeParams(new ClientInfo("salmon-egg-url-interop", "1"),
                new ClientCapabilities { Elicitation = new ElicitationCapabilities { Url = new ElicitationUrlCapabilities() } }), token);
            const string url = "https://example.invalid/salmon-egg-local-acceptance";
            var session = await client.CreateSessionAsync(new SessionNewParams(directory,
                [new StdioMcpServer("acceptance", "/usr/bin/python3", [peer!, url], [])]), token);

            // Act: URL consent is answered explicitly; this protocol gate does not open a browser.
            var pending = client.SendPromptAsync(new SessionPromptParams(session.SessionId,
                [new TextContentBlock("Call the acceptance MCP request_url tool exactly once. Do not use any other tool or access files. When done reply DONE.")]), token);
            var received = await requested.Task.WaitAsync(token);
            var request = Assert.IsType<UrlElicitationRequest>(received.Request);
            Assert.Equal(url, request.Url);
            Assert.Equal(session.SessionId, request.Scope.SessionId);
            Assert.True(await received.Accept(null));
            var result = await pending;

            // Assert
            Assert.Equal(request.ElicitationId, await completed.Task.WaitAsync(token));
            Assert.Equal(ElicitationActions.Accept, received.State.ResponseAction);
            Assert.True(received.State.IsCompleted);
            Assert.Equal(StopReason.EndTurn, result.StopReason);
            await Task.WhenAll(permissions);
            Assert.True(await client.DisconnectAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunAsync(bool expectForm)
    {
        // Arrange
        var command = Environment.GetEnvironmentVariable(CommandVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(command), $"Set {CommandVariable} to an installed ACP Agent.");
        var directory = Path.Combine(Path.GetTempPath(), "salmon-egg-agent-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "AGENTS.md"),
                "This is an isolated protocol acceptance test. Do not run tools, read other paths or modify files.", token);
            using var transport = new StdioTransport(command!, ReadArguments());
            using var client = new AcpClient(new DomainAcpTransportAdapter(transport));
            var errors = new ConcurrentQueue<string>();
            var text = new StringBuilder();
            var formReceived = new TaskCompletionSource<ElicitationRequestEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.ErrorOccurred += (_, _) => errors.Enqueue("client-error");
            client.ElicitationRequestReceived += (_, request) => formReceived.TrySetResult(request);
            client.SessionUpdateReceived += (_, update) =>
            {
                if (update.Update is AgentMessageUpdate { Content: TextContentBlock content })
                {
                    lock (text) text.Append(content.Text);
                }
            };

            // Act
            var initialized = await client.InitializeAsync(new InitializeParams(
                new ClientInfo("salmon-egg-real-agent-acceptance", "1"), new ClientCapabilities
                {
                    Elicitation = expectForm ? new ElicitationCapabilities { Form = new ElicitationFormCapabilities() } : null
                }), token);
            var session = await client.CreateSessionAsync(new SessionNewParams(directory, []), token);
            var marker = "ACP_ACCEPTANCE_" + Guid.NewGuid().ToString("N");
            var prompt = "Do not use any tools or access files. Reply with exactly: " + marker;
            if (expectForm)
            {
                var planOption = Assert.Single(session.ConfigOptions ?? [], option =>
                    option.Options.Any(value => value.Value == "plan"));
                await client.SetSessionConfigOptionAsync(
                    new SessionSetConfigOptionParams(session.SessionId, planOption.Id, "plan"), token);
                prompt = "Use only request_user_input once: ask one choice question with options Blue and Green. "
                    + "Do not inspect files or call any other tool. After I answer, reply with exactly: " + marker;
            }
            var pending = client.SendPromptAsync(new SessionPromptParams(session.SessionId,
                [new TextContentBlock(prompt)]), token);
            if (expectForm)
            {
                await AnswerFormAsync(await formReceived.Task.WaitAsync(token), session.SessionId);
            }
            var result = await pending;

            // Assert: a handshake alone cannot establish that the authenticated Agent actually works.
            Assert.Equal(AcpProtocolVersion.V1, initialized.ProtocolVersion);
            Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
            Assert.True(result.HasStopReason);
            Assert.Equal(StopReason.EndTurn, result.StopReason);
            lock (text) Assert.Contains(marker, text.ToString(), StringComparison.Ordinal);
            Assert.Empty(errors);
            Assert.True(await client.DisconnectAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AnswerFormAsync(ElicitationRequestEventArgs received, string sessionId)
    {
        Assert.Equal(sessionId, received.SessionId);
        var form = Assert.IsType<FormElicitationRequest>(received.Request);
        Assert.NotEmpty(form.RequestedSchema.Properties);
        var content = new ElicitationAcceptContent();
        foreach (var property in form.RequestedSchema.Properties)
        {
            var field = Assert.IsType<StringPropertySchema>(property.Value);
            content.SetString(property.Key, field.OneOf?.FirstOrDefault()?.Const
                ?? field.Enum?.FirstOrDefault() ?? "Blue");
        }
        Assert.True(await received.Accept(content));
    }

    private static string[] ReadArguments()
        => (Environment.GetEnvironmentVariable(ArgumentsVariable) ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
