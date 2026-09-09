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
still an upstream Draft, so the SDK retains explicit v2 wire DTO and serializer coverage for
development without negotiating v2 in live connections. `AcpProtocolVersion.HighestModeled`
denotes the highest modeled wire version; it does not mean that the live client lifecycle is
complete, and initializing a client with it throws. `AcpProtocolVersion.Latest` is the obsolete
former name of `HighestModeled` and is kept only so 1.0.0 consumers still compile.

Do not enable live v2 connections until prompt acknowledgement/state updates, versioned update
variants, permission-subject handling, configuration workflows, and JSON-RPC batches are implemented
and protected by a separate experimental feature flag. The modeled v2 contracts are marked
`[Experimental("SEACP002")]`; see [ACP v2 draft surface](#acp-v2-draft-surface-seacp002).

## Capability support boundaries

The presence of a wire type does not advertise a capability or supply its host implementation.
`ClientCapabilityDefaults.Create()` is the authority for the capabilities SalmonEgg advertises;
hosts must enable optional capabilities only after implementing their interaction and lifecycle.

| Surface | Current behavior | Remaining work |
| --- | --- | --- |
| Agent authentication | Only an absent discriminator or the exact `agent` type can reach `authenticate`. Unsupported strings round-trip without being selected; non-string discriminators are rejected. | Interactive terminal authentication still needs a host implementation before opting into `ClientCapabilities.Auth.Terminal`; see [#147](https://github.com/salmonloop/salmon-egg/issues/147). SalmonEgg credential injection binds a stored value to an explicit transport destination independently of the ACP `authenticate` request. |
| Request cancellation | The SDK sends `$/cancel_request`, recognizes `-32800`, and retains the original request ID until its terminal response or disconnection. Transports preserve caller cancellation; each cancellation notification has a two-second send budget. A terminal response received first wins. | Peer cancellation is best effort. `session/cancel` remains a separate session operation. [#148](https://github.com/salmonloop/salmon-egg/issues/148) still requires the deployed stdio-to-WebSocket bridge acceptance gate. |
| Form elicitation | SalmonEgg's capability defaults advertise form mode. Hosts handle `ElicitationRequested` and return a typed accept, decline, or cancel response. | The host owns the form UI and must preserve the request's scope and connection ownership. |
| URL elicitation | URL wire contracts and SDK completion tracking exist, but URL mode is not advertised by default. | A host must provide explicit navigation consent, a context the Agent cannot inspect, and a UI driven by the SDK's completion events. SalmonEgg's platform integration is tracked in [#154](https://github.com/salmonloop/salmon-egg/issues/154); [#146](https://github.com/salmonloop/salmon-egg/issues/146) tracks the complete elicitation delivery. |
| ACP v2 | Explicit v2 wire contracts and the internal prompt/work-state lifecycle are covered by deterministic protocol peers. Live initialization rejects v2. | Update projections, permission handling, batch processing, and real-Agent interoperability remain incomplete; see [#149](https://github.com/salmonloop/salmon-egg/issues/149). |

V2 wire coverage includes `configId`/`groupId`, required `messageId` values, text/custom command
inputs, and v1-only session fields and MCP variants. Unknown extension fields are preserved;
default-on-error and skip-invalid-item behavior applies only where the upstream schema permits it.
Resource-link icons are available through the experimental `ResourceLinkDraftExtensions` helper,
so constructing them requires an explicit draft opt-in. Permission subject types are still not
connected to live request handling. These contracts do not supply message upserts, streaming tool
and terminal projections.

`SendPromptAsync` always waits for completed foreground work. The internal v2 development path
records the prompt acknowledgement separately and finishes on an idle `state_update`; cancellation
keeps accepting trailing updates until that idle arrives. One controller owns session work, including
unsolicited running/requires-action/idle updates, and uses the connection's existing lifetime token
to reject stale callbacks. This path is exercised through the actual JSON parser and client handlers,
not exposed as a public v2 opt-in. V1 still completes from its terminal prompt response.

The pinned [v2 lifecycle](https://github.com/agentclientprotocol/agent-client-protocol/blob/5ebaf0aceb04a4ba6574cd63fa6355352dc6d931/docs/protocol/v2/prompt-lifecycle.mdx)
says an ending idle must carry a stop reason, while its
[schema](https://github.com/agentclientprotocol/agent-client-protocol/blob/5ebaf0aceb04a4ba6574cd63fa6355352dc6d931/schema/v2/schema.json)
allows an omitted/null/default-on-error reason. The client preserves that unknown reason and still
finishes on idle (`HasStopReason == false`), without fabricating `end_turn` or waiting indefinitely.

Keep the v1 runtime and public API compatible while these gaps are addressed. Enabling v2 needs
both the upstream stabilization/Agent prerequisites and end-to-end verification of the complete
lifecycle. Passing DTO tests or suppressing `SEACP002` does not satisfy that requirement.

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

## ACP v2 draft surface (SEACP002)

Every v2 draft contract on the public surface carries `[Experimental("SEACP002")]`, so naming one is
a **compile error** by default rather than a warning. That is deliberate: v2 is still an upstream
draft, no live client negotiates it (`AcpProtocolVersion.RuntimeServed` is v1), and code built on
these types cannot reach a real Agent today. The 38 marked types are the `state_update` work-state
family, the whole-message upsert updates, the terminal updates, streaming tool-call content, the
v2 `plan_update` envelope, permission subjects, the v2 capability markers, the structured diff,
and `ResourceLinkDraftExtensions` for resource-link icons.

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
