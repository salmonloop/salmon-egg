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
variants, permission subjects, config-option wire shapes, and JSON-RPC batches are implemented
and protected by a separate experimental feature flag. The modeled v2 contracts are marked
`[Experimental("SEACP002")]`; see [ACP v2 draft surface](#acp-v2-draft-surface-seacp002).

## ACP v2 draft surface (SEACP002)

Every v2 draft contract on the public surface carries `[Experimental("SEACP002")]`, so naming one is
a **compile error** by default rather than a warning. That is deliberate: v2 is still an upstream
draft, no live client negotiates it (`AcpProtocolVersion.RuntimeServed` is v1), and code built on
these types cannot reach a real Agent today. The 37 marked types are the `state_update` work-state
family, the whole-message upsert updates, the terminal updates, streaming tool-call content, the
v2 `plan_update` envelope, permission subjects, the v2 capability markers, and the structured diff.

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
instead: a new draft registration fails the build until someone decides deliberately.

The same applies to reads. `SessionUpdate` declares the v2 discriminators as static polymorphic
metadata, which has no notion of the negotiated version, so a v1 connection whose Agent sends a v2
update still materializes the draft type. Treat any `SessionUpdate` you did not construct yourself as
possibly draft-typed, and match on the stable variants you handle rather than assuming the rest
cannot appear.

## Collection equality

Wire DTOs are `record` types, but collection properties remain mutable `List<T>` /
`Dictionary<TKey,TValue>` for STJ source-gen friendliness. Record equality is therefore
**reference equality on collections**, not deep structural equality. Hosts that need
value snapshots should clone via helpers such as `McpServerSnapshots` / `AcpMetaJson.Clone`
or re-serialize through `AcpJsonContext`.

## License

MIT
