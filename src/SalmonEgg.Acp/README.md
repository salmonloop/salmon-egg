# SalmonEgg.Acp

.NET 10 Agent Client Protocol (ACP) SDK: wire contracts, client primitives, and source-generated serialization.

## What is public

- Protocol / content / tool / plan / MCP wire DTOs (`AcpProtocolObject` hierarchy)
- `IAcpClient` / `AcpClient` and host seams (`IAcpTransport`, logger, session store, terminal manager)
- `AcpJsonContext` source-generated serialization entry point
- Host helpers: `AcpMetaJson`, `McpServerSnapshots`, `ProtocolPathRules`, capability defaults
- `AcpException` / `JsonRpcErrorCode`

JSON-RPC envelopes, message parser/validator, and all JsonConverters are assembly-internal implementation details. Hosts should serialize ACP wire types through `AcpJsonContext` rather than re-source-generating them.

## Requirements

- .NET 10 (`net10.0`)
- Zero package dependencies; AOT/trim compatible (`IsAotCompatible`)

## Protocol versions

The live client runtime defaults to stable ACP v1 (`AcpProtocolVersion.Default`). ACP v2 is
still an upstream Draft. A host can evaluate its full runtime only with explicit client options
and an offered version of 2; ordinary clients reject that offer. `AcpProtocolVersion.HighestModeled`
denotes the highest modeled wire version, not the production default. `AcpProtocolVersion.Latest` is the obsolete
former name of `HighestModeled` and is kept only so 1.0.0 consumers still compile.

Prompt acknowledgement/state updates, versioned entity views, permission subjects, configuration
workflows and JSON-RPC batches share the existing client owners. Experimental negotiation is
separate from the stable default. The modeled v2 contracts are marked
`[Experimental("SEACP002")]`; see [ACP v2 draft surface](#acp-v2-draft-surface-seacp002).

## Capability support boundaries

The presence of a wire type does not advertise a capability or supply its host implementation.
`ClientCapabilityDefaults.Create()` is the authority for the capabilities SalmonEgg advertises;
hosts must enable optional capabilities only after implementing their interaction and lifecycle.

| Surface | Current behavior | Remaining work |
| --- | --- | --- |
| Agent authentication | Only an absent discriminator or the exact `agent` type can reach `authenticate`. Unsupported strings round-trip without being selected; non-string discriminators are rejected. | Hosts must implement interactive login before opting into `ClientCapabilities.Auth.Terminal`. The Windows PTY host is implemented, but SalmonEgg does not advertise it until packaged-application acceptance completes; see [#147](https://github.com/salmonloop/salmon-egg/issues/147). SalmonEgg credential injection binds a stored value to an explicit transport destination independently of the ACP `authenticate` request. |
| Request cancellation | The SDK sends `$/cancel_request`, recognizes `-32800`, and retains the original request ID until its terminal response or disconnection. Transports preserve caller cancellation; each cancellation notification has a two-second send budget. A terminal response received first wins. | Peer cancellation is best effort. `session/cancel` remains a separate session operation. [#148](https://github.com/salmonloop/salmon-egg/issues/148) still requires the deployed stdio-to-WebSocket bridge acceptance gate. |
| Form elicitation | SalmonEgg's capability defaults advertise form mode. Hosts handle `ElicitationRequested` and return a typed accept, decline, or cancel response. | The host owns the form UI and must preserve the request's scope and connection ownership. |
| URL elicitation | The SDK owns consent-response availability, connection lifetime, and completion in `ElicitationRequestEventArgs.State`. URL mode stays off in SDK defaults; SalmonEgg enables it on WASM and Linux desktop through its platform capability service. Linux uses the existing system opener after explicit consent. | The Linux product gate exercises its real card, stdio peer, native pointer input and isolated external browser. Other native platforms and independent real-Agent interoperability remain open in [#154](https://github.com/salmonloop/salmon-egg/issues/154). [#146](https://github.com/salmonloop/salmon-egg/issues/146) tracks the complete elicitation delivery. |
| ACP v2 | Explicit opt-in enables the staged lifecycle while ordinary initialization continues to reject V2. The existing application event stream carries complete entity views without a draft-type dependency. | V2 remains an upstream draft. The official Rust echo Agent is interoperable; LLM Agent/platform acceptance must be reported separately in [#149](https://github.com/salmonloop/salmon-egg/issues/149). |

Internal v2 batch validation follows the upstream schema at
`5ebaf0aceb04a4ba6574cd63fa6355352dc6d931` and JSON-RPC 2.0 section 6.
Call batches may mix requests and notifications; responses are collected into one array after all
requests have answers. Notification-only batches produce no response. Empty batches return a
single `-32600` response; malformed JSON returns a single `-32700`, both with explicit `id: null`.
An invalid call item receives `-32600` without discarding valid siblings. Response batches correlate
each valid response by its original id type and value. Malformed response items, including responses
embedded in a call batch, are logged and ignored without sending replies to responses.

The transport recognizes object and array frames independently of negotiation; a v1 connection
explicitly rejects batches. Cancellation and disconnect retain each response's original connection
and batch ownership. Failed response batches remain retryable through their original pending
requests. A batch with no remaining retry owner is abandoned after its failed send and releases
its retained responses, without starting an automatic retry loop.
`tests/SalmonEgg.Acp.Desktop.Tests` and `scripts/gates/run-acp-batch-stdio-gate.sh` exercise real
Linux pipes through the production transport and adapter. This gate does not establish
interoperability with a public v2 Agent. The separate pinned official Rust Agent gate covers that boundary.

Terminal methods append their arguments to the invocation used by the active stdio connection and
override its effective environment. SalmonEgg asks for consent before starting a separate PTY,
reclaims that process tree on cancellation, and reconnects through its existing connection owner
only after a normal zero exit. Terminal methods never reach `authenticate`. Product capability
advertisement remains disabled, including on Windows, until the packaged application's consent,
terminal interaction, exit, reconnect, retry and cancellation paths pass the GUI gate. The dedicated
Windows process gate opts in explicitly to validate ConPTY independently of product rollout. Other
platforms also need trustworthy process exit and cleanup semantics. The process gate and remaining
GUI prerequisites are described in the repository's `BUILD_GUIDE.md`.

V2 wire coverage includes `configId`/`groupId`, required `messageId` values, text/custom command
inputs, and v1-only session fields and MCP variants. Unknown extension fields are preserved;
default-on-error and skip-invalid-item behavior applies only where the upstream schema permits it.
Resource-link icons are available through the experimental `ResourceLinkDraftExtensions` helper,
so constructing them requires an explicit draft opt-in. Permission subjects reach the staged v2
handler through the same pending-request owner as v1. Draft session snapshots supply message upserts, streaming
tool content, and Agent-owned terminal projections. The same projection supplies stable
`AcpSessionUpdateView` values on `SessionUpdateEventArgs`, so application consumers do not have to
name draft DTOs. Message content replaces one authoritative message, tool views are complete,
terminal views only display Agent-owned state, and idle ends work even when no reason is supplied.

`SendPromptAsync` always waits for completed foreground work. The internal v2 development path
records the prompt acknowledgement separately and finishes on an idle `state_update`; cancellation
keeps accepting trailing updates until that idle arrives. One controller owns session work, including
unsolicited running/requires-action/idle updates, and uses the connection's existing lifetime token
to reject stale callbacks. This path is exercised through the actual JSON parser and client handlers,
available only with explicit experimental client options. V1 still completes from its terminal prompt response.

The pinned [v2 lifecycle](https://github.com/agentclientprotocol/agent-client-protocol/blob/5ebaf0aceb04a4ba6574cd63fa6355352dc6d931/docs/protocol/v2/prompt-lifecycle.mdx)
says an ending idle must carry a stop reason, while its
[schema](https://github.com/agentclientprotocol/agent-client-protocol/blob/5ebaf0aceb04a4ba6574cd63fa6355352dc6d931/schema/v2/schema.json)
allows an omitted/null/default-on-error reason. The client preserves that unknown reason and still
finishes on idle (`HasStopReason == false`), without fabricating `end_turn` or waiting indefinitely.

`AcpSessionDraftExtensions.ReplaySession(sessionId, updates)` is an **offline** SDK host API for
recorded histories. Each input is a JSON update object with `sessionUpdate`, in receive order and
scoped to the supplied session. It uses the same v2 reader and projection as the normal client
handler without connecting, executing terminal commands, or changing a client's state.
`client.GetSessionSnapshot(sessionId)` reads the same owner on an internally staged v2 connection;
it returns null on the public v1 runtime and after session close or disconnect. Suppressing
`SEACP002` alone does not enable live v2 initialization.

Snapshots expose messages, tool calls, and terminal output in first-seen order. Patch omission
keeps a value, null clears it, and arrays replace complete content; chunks append. Terminal chunks
are independently decoded into immutable bytes. Message/tool/terminal metadata is separate from
chunk metadata. Entity `ExtensionData` retains the last-seen value of each unknown top-level field,
including explicit null, without interpreting its semantics. Protocol DTO getters return detached
copies because wire DTO collections are mutable; editing a returned
DTO cannot alter an existing snapshot or client state. A full `replayFrom: { type: "start" }` begins
a fresh projection, while resume without replay retains prior history. Overlapping full replays
are rejected because session updates carry no request id that could separate them; cancelling the
local wait or receiving an unknown write outcome retains this claim until the peer responds or the
connection ends. Only a request that never started transport I/O releases the claim immediately.

Configuration follows the same session and connection owner. `session/new`, `session/resume`,
`session/set_config_option` responses and `config_option_update` replace the complete list in wire
receive order, including replies received after the caller abandons its wait. A later notification
cannot be overwritten by an earlier response's delayed continuation. `HasConfigOptions` distinguishes
an unreported list from an authoritative empty list; `ConfigOptions` returns detached DTOs in Agent
priority order. Offline replay uses the same rules. The configuration contract is checked against
[schema revision f1293d8e](https://github.com/agentclientprotocol/agent-client-protocol/blob/f1293d8e43d09a6745ff8fe717f9acd8299591b7/schema/v2/schema.json).
Known option types are `select` and `boolean`; unknown types preserve their complete payload through
`ConfigOption.RawPayload` and round-trip serialization. Unknown value types also round-trip raw
`session/set_config_option` data. V2 value IDs carry `type: "id"`; V1 retains its default string
variant with no discriminator. Configuration metadata recovers only at schema-authorized V2 fields.
The application configuration projection retains boolean values and unknown payloads through its
existing workspace path; only supported option types produce editable rows.

A snapshot is a current view, not a lossless event archive. `UnprojectedUpdates` retains recent
unhandled updates verbatim within `MaxUnprojectedUpdates` (64 entries) and
`MaxUnprojectedUtf8Bytes` (256 KiB of JSON). A fitting update evicts the oldest entries as needed;
a single oversized update is omitted without evicting existing entries. Both cases increment
`OmittedUnprojectedUpdateCount`. A fresh full replay resets this accounting. These limits do not
truncate known message/tool/terminal content or suppress `SessionUpdateReceived`. Hosts record that
event for complete update history, or raw transport messages for a wire-exact archive; snapshots
do not retain replaced entity values or chunk-scoped metadata and extension history.

The [pinned v2 schema](https://github.com/agentclientprotocol/agent-client-protocol/blob/5ebaf0aceb04a4ba6574cd63fa6355352dc6d931/schema/v2/schema.json)
explicitly allows default-on-error for optional patch fields and skip-invalid-items for message,
tool-content, and location arrays. Those recoveries apply only to the v2 contracts that declare
them. Required identities and chunks remain strict, unknown string discriminators survive, and
content annotations, metadata, optional image URIs, and optional resource-link/resource fields
recover independently before an enclosing list may discard an invalid content item. Valid payloads
survive malformed hints; required field type errors remain invalid. Embedded resources follow the
schema's untagged union, selecting a valid text branch before a valid blob branch and rejecting a
payload that satisfies neither. V1 optional-field type validation is unchanged. The actual nupkg consumer gate replays mixed
history and asserts replacement, append, clear, terminal bytes, and snapshot isolation.

`AcpPermissionDraftExtensions.ReadRequest(parameters)` parses recorded v2 permission params;
`permissionEvent.GetDraftRequest()` returns the same immutable view on the existing
`PermissionRequestReceived` event (null for v1). It supplies the required prompt title, optional
description, optional tool-call/command/custom subject, and detached options. `RawParameters` keeps
the entire input for recording or forwarding. Missing/null subjects stay absent; unknown subject
types stay opaque. Prompt text and subjects never update transcript, tool, or terminal projections,
and a command subject never executes a local command. V2 events leave the legacy `ToolCall` null.
The host explicitly selects an offered option or cancels; failed sends and invalid choices retain
the original request for retry, and stale callbacks cannot answer a new request with a reused id.
Subscriber failures also claim that original request, so an exception after an answer cannot send
a second response. The nupkg consumer gate verifies optional subjects, opaque custom payloads,
detached prompt data, and rejection of unapproved v2 before any connection or write.

The V1 runtime and existing public interfaces remain compatible. To evaluate V2, construct the
client with `new AcpClientOptions { ExperimentalProtocolVersions = [2] }` as the fifth constructor
argument and explicitly offer `InitializeParams.ProtocolVersion = 2`. The list is copied at
construction and defaults to empty. The Agent may choose V1, after which all wire behavior follows
V1. Other experimental version numbers are rejected. The application exposes this through the
process-level `SALMONEGG_EXPERIMENTAL_ACP_V2=1` opt-in; the same immutable policy configures both
client creation and initialization. This is experimental support, not a change to `Default` or
`RuntimeServed`, and suppressing `SEACP002` does not change that policy.

Configuration replies publish `IsResponseProjection=true` through the existing ordered session
stream. Hosts that see `PublishesConfigurationResponses` must use that stream rather than reapply
the returned full list from a delayed await. Wire recorders can distinguish these projections from
peer notifications. Captured events expose `IsCurrent` so buffered consumers reject a closed
connection's update. The SDK maps the application's stable history-load intent to V2
`session/resume` with `replayFrom: { type: "start" }`; V2 never sends the removed `session/load`.

### Cancellation transport integration

Existing `IAcpTransport.SendMessageAsync(string, CancellationToken)` implementations remain source
and binary compatible. The three-argument overload defaults to the original method. Implementations
can override it to honor `AcpTransportSendOptions.DiagnosticOnly`: return false or throw for that
send's transient failure without raising `ErrorOccurred`. Connection loss, process exit, and reader
failures must still raise normal events and settle pending requests. Errors without operation
ownership from legacy transports remain visible. Implementations must honor the supplied token;
the SDK bounds its own wait even if an external transport ignores cancellation.

`scripts/gates/run-acp-cancellation-transport-gates.sh` exercises real stdio, direct WebSocket,
HTTP/2 and SSE traffic and checks that every required case passes without skips. The separate
`scripts/gates/run-acp-cancellation-bridge-gate.sh` requires a deployed bridge fronting
`scripts/gates/fixtures/cancellation-peer.py`, `SALMONEGG_ACP_CANCELLATION_BRIDGE_URL`, and a fresh
absolute `SALMONEGG_ACP_CANCELLATION_PEER_LOG` path. Missing deployment evidence fails that gate;
the ordinary test suite explicitly skips this external integration when it is unconfigured.

### Permission response ownership

Hosts should retain the `PermissionRequestEventArgs` they receive and answer through
`TryRespondAsync(outcome, optionId)`. It reports whether the original request's response was sent;
`CanRespond` checks whether that exact pending request still belongs to its receiving client.
Disconnecting, completing a request, or reusing its ID invalidates the old event. Failed sends
remain retryable while the original request is still current. A host must dismiss only the prompt
associated with the successful response, even if the active conversation or connection has changed.

`IsResponsePrepared` means an answer is prepared or in flight without confirming delivery.
`IsResponseSending` distinguishes a physical send from a prepared batch answer waiting for siblings.
Keep an independent prompt in place during that send, with the native asynchronous command disabled;
only a batch answer waiting for other answers may yield its input surface before delivery. A failed write
makes the original request retryable. When `IsCancellationRequested` is true, offer cancellation
retry instead of authorization. `Title` and `Description` expose the peer's prompt text without a
draft-type dependency; missing titles should use the host's localized fallback.

If a request cannot be associated with a conversation, attempt cancellation once. A failed cancellation
retains a standalone cancellation-only retry in the same interaction owner, bound to the original request
and connection. Successful delivery, disconnection or service replacement removes that retry; it never
creates a conversation binding or an automatic retry loop.
When no chat surface can offer that retry, the connection coordinator closes only the original
service and retains a localized connection failure asking the user to reconnect. Its bounded cleanup
never clears a newer service or publishes the old failure over a replacement connection.

Subscribe to the request's `Changed` event and immediately read its properties to cover the
subscription window. Notifications are asynchronous and may coalesce; marshal UI projection to its
dispatcher, recheck request ownership there, and unsubscribe when the interaction is removed.
An already captured callback may still run after unsubscription. Observers for one request execute
sequentially, outside the send path; slow observers cannot delay delivery. Observer failures are
logged and isolated, including a host logger that itself throws.

ACP v1 peer `$/cancel_request` for a permission request latches cancellation on that original owner.
An answer already being written may finish successfully; otherwise cancellation returns `-32800`.
Failed cancellation writes retain the owner for explicit retry, and later user selection cannot
replace the cancellation. Queued callbacks retain the connection that originally received them,
so a delayed cancellation cannot withdraw a replacement request after reconnecting.

The existing public constructor and `Respond` callback retain their signatures. For events created
through that constructor, the SDK cannot query a client lifetime: `CanRespond` returns true, and
normal completion of the supplied `Task` callback makes `TryRespondAsync` return true. Exceptions
from that callback remain observable. Callbacks received from `AcpClient` retain the actual send
result without casting a `Task` to `Task<bool>`.

### URL consent and completion

Hosts display the complete URL and destination host before navigation. Opening requires an explicit
user action and an isolated external browser context; WASM uses native `noopener,noreferrer` and
rejects application-origin URLs. A browser dispatch does not prove that a popup opened, so the UI
allows a new explicit **Open again** action without sending a second ACP response. An `accept`
response carries no form content and acknowledges consent only; `elicitation/complete` independently
marks the existing request complete. Unknown or duplicate completion IDs do nothing.

The state is scoped to the connection that received the request. Hosts observe `State.Changed` and
`State.ConnectionClosed`, marshal updates to their UI thread, and stop using expired callbacks.
Connection shutdown marks the token cancelled and clears pending requests synchronously; host
cancellation callbacks run asynchronously and cannot delay disconnect. Callback failures are
observed without logging their potentially private data.

SalmonEgg currently presents session-scoped requests. Request-scoped requests are explicitly
cancelled when no conversation surface can present them. WASM and Linux desktop advertise URL
elicitation after their product consent-to-browser gates. The Linux gate uses its real card, native
pointer input and system opener. Other native targets remain disabled until their own installed
application acceptance passes; Linux success does not establish five-platform validation.

## ACP v2 draft surface (SEACP002)

Every v2 draft contract on the public surface carries `[Experimental("SEACP002")]`, so naming one is
a **compile error** by default rather than a warning. That is deliberate: v2 is still an upstream
draft and `AcpProtocolVersion.RuntimeServed` remains V1. Naming draft contracts and allowing
experimental negotiation are separate opt-ins. The 46 marked types are the `state_update` work-state
family, the whole-message upsert updates, the terminal updates, streaming tool-call content, the
v2 `plan_update` envelope, permission subjects, the v2 capability markers, the structured diff,
`ResourceLinkDraftExtensions` for resource-link icons, and six session-projection types (the draft
entry point, message kind, and session/message/tool/terminal snapshots). The projection types are
not source-generated wire DTOs and introduce no serialization-context bypass. The permission draft
entry point and request snapshot likewise add no serialization-context bypass.

To evaluate them anyway, opt in explicitly:

```xml
<!-- whole project -->
<NoWarn>$(NoWarn);SEACP002</NoWarn>
```

```csharp
#pragma warning disable SEACP002 // one region
var update = new StateSessionUpdate(new IdleSessionWorkState());
#pragma warning restore SEACP002
```

`[SuppressMessage]` does **not** work here. `SEACP002` is produced by the compiler itself rather than
by an analyzer, so no attribute-based suppression applies, whatever category you give it.

### Known residual channel: the serialization context

`AcpJsonContext` is public and registers the draft contracts, and the JSON source generator emits a
public `JsonTypeInfo<T>` property per registered type **without copying the attribute onto it**. So
this compiles clean today, with no `SEACP002` anywhere:

```csharp
var info = AcpJsonContext.Default.StateSessionUpdate;   // never names the draft type
var update = JsonSerializer.Deserialize(json, info);    // draft contract in hand
```

26 context members are reachable this way. The hole cannot be closed while the DTOs are public - an
internal context over public types is `CS0053` - so it is pinned by name in the SDK's gate tests
instead: a new draft registration fails the build until someone decides deliberately. What you get
through it is a draft *contract*, not a draft-populated connection: the context resolves the stable
surface, so nothing a v1 connection reads binds to one of these.

Reads are a different story now, and a better one. `SessionUpdate` declares only the **v1** surface as
static polymorphic metadata, and the negotiated surface is assembled per connection - so a v1
connection whose Agent sends a v2 update gets the base type with the payload preserved, not a draft
contract. Reading through `AcpJsonContext.Default` yields the same stable surface. You still cannot
assume the set of variants is closed: an update your version does not define arrives as `SessionUpdate`
with `UnknownUpdateKind` set, which on v1 means the Agent sent something v1 does not have.

## Collection equality

Wire DTOs are `record` types, but collection properties remain mutable `List<T>` /
`Dictionary<TKey,TValue>` for STJ source-gen friendliness. Record equality is therefore
**reference equality on collections**, not deep structural equality. Hosts that need
value snapshots should clone via helpers such as `McpServerSnapshots` / `AcpMetaJson.Clone`
or re-serialize through `AcpJsonContext`.

## License

MIT
