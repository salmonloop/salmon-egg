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

## License

MIT
