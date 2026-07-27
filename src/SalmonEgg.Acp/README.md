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

## Collection equality

Wire DTOs are `record` types, but collection properties remain mutable `List<T>` /
`Dictionary<TKey,TValue>` for STJ source-gen friendliness. Record equality is therefore
**reference equality on collections**, not deep structural equality. Hosts that need
value snapshots should clone via helpers such as `McpServerSnapshots` / `AcpMetaJson.Clone`
or re-serialize through `AcpJsonContext`.

## License

MIT
