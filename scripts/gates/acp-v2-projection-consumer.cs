// Compiled against the packed SDK by run-acp-consumer-package-smoke.sh, never a ProjectReference.
#pragma warning disable SEACP002
using System.Text;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Tool;

var history = new List<JsonElement>
{
    Parse("""{"sessionUpdate":"user_message","messageId":"u","content":[{"type":"text","text":"prompt"}]}"""),
    Parse("""{"sessionUpdate":"agent_message_chunk","messageId":"a","content":{"type":"text","text":"old"}}"""),
    Parse("""{"sessionUpdate":"agent_message","messageId":"a","content":[{"type":"text","text":"replacement","_meta":{"private":"kept"}}],"future":1e2}"""),
    Parse("""{"sessionUpdate":"agent_message_chunk","messageId":"a","content":{"type":"text","text":" tail"}}"""),
    Parse("""{"sessionUpdate":"agent_message","messageId":"a"}"""),
    Parse("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"content","content":{"type":"text","text":"discarded"}}}"""),
    Parse("""{"sessionUpdate":"tool_call_update","toolCallId":"t","status":"completed","content":[{"type":"terminal","terminalId":"term"}]}"""),
    Parse("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"content","content":{"type":"text","text":"tool tail"}}}"""),
    Parse("""{"sessionUpdate":"terminal_output_chunk","terminalId":"term","data":"eA=="}"""),
    Parse("""{"sessionUpdate":"terminal_update","terminalId":"term","command":"echo","output":{"data":"YQ=="}}"""),
    Parse("""{"sessionUpdate":"terminal_output_chunk","terminalId":"term","data":"YmM="}"""),
    Parse("""{"sessionUpdate":"terminal_update","terminalId":"term","exitStatus":{}}"""),
    Parse("""{"sessionUpdate":"_vendor","value":1e2}""")
};

var snapshot = AcpSessionDraftExtensions.ReplaySession("recorded-session", history);
Require(snapshot.Messages.Length == 2, "Message ids must upsert instead of creating duplicates.");
Require(snapshot.Messages[0].Kind == AcpMessageKind.User, "First-seen order must be retained.");
Require(Text(snapshot.Messages[1]) == "replacement tail", "A whole message replaces chunks; later chunks append.");
Require(snapshot.ToolCalls.Length == 1 && snapshot.ToolCalls[0].Status == ToolCallStatus.Completed,
    "Tool chunks and patches must share an id owner.");
Require(snapshot.ToolCalls[0].Content.Length == 2
    && snapshot.ToolCalls[0].Content[0] is TerminalToolCallContent { TerminalId: "term" },
    "Tool upserts must replace previous content and keep agent-owned terminal references.");
Require(Encoding.UTF8.GetString(snapshot.Terminals[0].Output.AsSpan()) == "abc",
    "Terminal snapshots replace output and each base64 chunk must decode independently.");
Require(snapshot.Terminals[0].HasExited && snapshot.Terminals[0].ExitStatus!.ExitCode is null,
    "An empty exit-status object still means the terminal exited.");
Require(snapshot.UnprojectedUpdates.Single().GetRawText() == history[^1].GetRawText(),
    "Unknown updates must survive replay verbatim.");
Require(snapshot.Messages[1].ExtensionData["future"].GetRawText() == "1e2",
    "Current unknown entity fields must retain raw values.");

var detached = snapshot.Messages[1].Content;
detached[0].Meta!["private"] = "mutated";
Require(((JsonElement)snapshot.Messages[1].Content[0].Meta!["private"]!).GetString() == "kept",
    "Mutable wire DTOs must not allow callers to edit the snapshot.");

history.Add(Parse("""{"sessionUpdate":"agent_message","messageId":"a","content":null}"""));
history.Add(Parse("""{"sessionUpdate":"tool_call_update","toolCallId":"t","content":[],"status":null}"""));
history.Add(Parse("""{"sessionUpdate":"terminal_update","terminalId":"term","output":null,"exitStatus":null}"""));
var cleared = AcpSessionDraftExtensions.ReplaySession("recorded-session", history);
Require(cleared.Messages[1].Content.IsEmpty && cleared.ToolCalls[0].Content.IsEmpty
    && cleared.ToolCalls[0].Status is null && cleared.Terminals[0].Output.IsEmpty && !cleared.Terminals[0].HasExited,
    "Explicit null and empty-array patches must clear prior state.");
Require(Text(snapshot.Messages[1]) == "replacement tail" && snapshot.Terminals[0].HasExited,
    "Replaying another history must not mutate a prior snapshot.");
var recent = AcpSessionDraftExtensions.ReplaySession("bounded-history",
    Enumerable.Repeat(snapshot.UnprojectedUpdates.Single(), AcpSessionSnapshot.MaxUnprojectedUpdates + 1));
Require(recent.UnprojectedUpdates.Length == AcpSessionSnapshot.MaxUnprojectedUpdates
    && recent.OmittedUnprojectedUpdateCount == 1 && snapshot.OmittedUnprojectedUpdateCount == 0,
    "Unprojected evidence must be bounded and omissions must remain visible on each snapshot.");
Console.WriteLine("draft-projection-consumer-ok");

static JsonElement Parse(string json)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

static string Text(AcpMessageSnapshot message)
    => string.Concat(message.Content.Select(static value => ((TextContentBlock)value).Text));

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
#pragma warning restore SEACP002
