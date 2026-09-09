// Compiled against the packed SDK by run-acp-consumer-package-smoke.sh, never a ProjectReference.
#pragma warning disable SEACP002
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;

const string Options = "\"options\":[{\"optionId\":\"allow\",\"name\":\"Allow\",\"kind\":\"future\",\"_meta\":{\"kept\":true}}]";
var recorded = Parse("{\"sessionId\":\"recorded\",\"title\":\"Approve?\",\"description\":\"Review the operation\","
    + "\"subject\":{\"type\":\"_future\", \"value\":1e2,\"_meta\":17,\"command\":\"must not execute\"}," + Options + "}");
var request = AcpPermissionDraftExtensions.ReadRequest(recorded);
Require(request.SessionId == "recorded" && request.Title == "Approve?" && request.Description == "Review the operation",
    "Permission prompt identity and text must come from the original request.");
Require(request.RawParameters.GetRawText() == recorded.GetRawText(), "Recorded params must preserve every unknown field verbatim.");
Require(request.Subject is CustomRequestPermissionSubject custom && custom.Type == "_future"
    && custom.RawPayload.GetRawText() == recorded.GetProperty("subject").GetRawText(),
    "A future subject must remain opaque, even if it contains command-looking fields.");
Require(request.Options[0].Kind == "future", "An unknown option kind must remain available as a generic choice.");
request.Options[0].Meta!.Clear();
Require(request.Options[0].Meta!.Count == 1, "Changing a returned option must not mutate the original permission snapshot.");

foreach (var subject in new[] { "", ",\"subject\":null" })
{
    var generic = AcpPermissionDraftExtensions.ReadRequest(Parse("{\"sessionId\":\"\",\"title\":\"\",\"description\":17," + Options + subject + "}"));
    Require(generic.Subject is null && generic.Description is null && generic.SessionId == "" && generic.Title == "",
        "Optional subjects stay absent and default-on-error applies only to the fields that allow it.");
}

var command = AcpPermissionDraftExtensions.ReadRequest(Parse("{\"sessionId\":\"s\",\"title\":\"Run?\","
    + "\"subject\":{\"type\":\"command\",\"command\":\"echo\",\"cwd\":\"C:\\\\work\",\"toolCallId\":17,\"terminalId\":{}}," + Options + "}"));
Require(command.Subject is CommandPermissionSubject { Command: "echo", Cwd: "C:\\work", ToolCallId: null, TerminalId: null },
    "Command context must accept platform-independent absolute paths and schema-defaulted optional ids.");

var tool = AcpPermissionDraftExtensions.ReadRequest(Parse("{\"sessionId\":\"s\",\"title\":\"Permission title\","
    + "\"subject\":{\"type\":\"tool_call\",\"toolCall\":{\"toolCallId\":\"tool\",\"title\":17,\"content\":[27,{\"type\":\"terminal\",\"terminalId\":\"term\"}]}}," + Options + "}"));
Require(tool.Title == "Permission title" && tool.Subject is ToolCallPermissionSubject { ToolCall.Title: null, ToolCall.Content.Count: 1 },
    "Tool subjects use the v2 partial update contract and must not replace permission prompt text.");
((ToolCallPermissionSubject)tool.Subject!).ToolCall.Content!.Clear();
Require(((ToolCallPermissionSubject)tool.Subject!).ToolCall.Content!.Count == 1,
    "Returned tool DTOs must be detached from the permission snapshot.");

ExpectInvalid("{\"sessionId\":\"s\",\"title\":\"Review\",\"subject\":{}," + Options + "}");
ExpectInvalid("{\"sessionId\":\"s\",\"title\":\"Review\",\"options\":[]}");
ExpectInvalid("{\"sessionId\":\"s\",\"title\":\"Review\",\"subject\":{\"type\":\"tool_call\",\"toolCall\":{}}," + Options + "}");
ExpectInvalid("{\"sessionId\":\"s\",\"title\":\"Review\",\"subject\":{\"type\":\"command\",\"command\":\"echo\",\"cwd\":\"relative\"}," + Options + "}");

var stableEvent = new PermissionRequestEventArgs(1L, "stable", Parse("{\"toolCallId\":\"t\"}"), [], (_, _) => Task.CompletedTask);
Require(stableEvent.GetDraftRequest() is null, "Stable permission events must not silently acquire draft metadata.");
using var transport = new UnopenedTransport();
using var client = new AcpClient(transport);
try
{
    await client.InitializeAsync(new InitializeParams(new ClientInfo("consumer", "1.0"), new ClientCapabilities())
    {
        ProtocolVersion = AcpProtocolVersion.V2
    });
    throw new InvalidOperationException("Live v2 initialization must fail before connecting.");
}
catch (AcpException)
{
    Require(transport.ConnectCount == 0 && transport.SendCount == 0,
        "Suppressing the draft diagnostic must not enable live v2 negotiation.");
}
Console.WriteLine("draft-permission-consumer-ok");

static JsonElement Parse(string json)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

static void ExpectInvalid(string json)
{
    try
    {
        AcpPermissionDraftExtensions.ReadRequest(Parse(json));
    }
    catch (JsonException)
    {
        return;
    }
    throw new InvalidOperationException("Invalid permission params must not produce an actionable prompt.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class UnopenedTransport : IAcpTransport
{
    public bool IsConnected => false;
    public int ConnectCount { get; private set; }
    public int SendCount { get; private set; }
    public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
    public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        return Task.FromResult(false);
    }
    public Task<bool> DisconnectAsync() => Task.FromResult(true);
    public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        SendCount++;
        return Task.FromResult(false);
    }
    public void Dispose() { }
}
#pragma warning restore SEACP002
