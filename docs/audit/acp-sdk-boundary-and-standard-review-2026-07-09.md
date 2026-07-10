# ACP SDK Boundary and Standard Review - 2026-07-09

## Goal

Prepare SalmonEgg's ACP implementation to become a standalone SDK package while keeping the app-specific chat, settings, storage, and UI layers outside the protocol package.

## Current Boundary

The first extraction step introduces `src/SalmonEgg.Acp/SalmonEgg.Acp.csproj`.

Rules for this project:

- It targets `netstandard2.1;net10.0`.
- It is packable as `SalmonEgg.Acp`.
- It must not reference `SalmonEgg.Domain`, `SalmonEgg.Application`, `SalmonEgg.Infrastructure`, `SalmonEgg.Presentation.Core`, or the UI app.
- It owns protocol-only primitives that can be reused by future DTO/client/parser extraction.

Moved in this step:

- `ProtocolPathRules` moved from `SalmonEgg.Domain.Models.Protocol` to `SalmonEgg.Acp.Protocol`.
- JSON-RPC request/response/notification/error primitives moved from `SalmonEgg.Domain.Models.JsonRpc` to `SalmonEgg.Acp.JsonRpc`.
- ACP content blocks moved from `SalmonEgg.Domain.Models.Content` to `SalmonEgg.Acp.Content`.
- ACP tool call payloads moved from `SalmonEgg.Domain.Models.Tool` to `SalmonEgg.Acp.Tool`.
- ACP plan payloads moved from `SalmonEgg.Domain.Models.Plan` to `SalmonEgg.Acp.Plan`.
- MCP server wire payloads moved from `SalmonEgg.Domain.Models.Mcp` to `SalmonEgg.Acp.Mcp`; local `Enabled` catalog state now lives in Domain `McpServerCatalogEntry`.
- ACP protocol request/response/update DTOs moved from `SalmonEgg.Domain.Models.Protocol` to `SalmonEgg.Acp.Protocol`.
- `StopReason` moved out of Domain session state and into `SalmonEgg.Acp.Protocol` with the prompt response contract.
- JSON-RPC parser/validator interfaces and implementations moved into `SalmonEgg.Acp.JsonRpc`.
- Protocol source-generated serialization moved into `SalmonEgg.Acp.Serialization.AcpJsonContext`; Infrastructure keeps only `AcpInfrastructureJsonContext` for legacy `AcpMessage` and app-local callback payloads.
- ACP client and capability contracts moved into `SalmonEgg.Acp.Client`; `IAcpClientFactory` stays in Domain because it accepts the app-local transport interface.
- Permission request option payloads moved into `SalmonEgg.Acp.Protocol`.
- ACP client runtime moved into `SalmonEgg.Acp.Client.AcpClient`. SDK-facing host seams are now `IAcpTransport`, `IAcpClientSessionStore`, `IAcpTerminalSessionManager`, and `IAcpClientLogger`; Infrastructure owns only adapters from app-local transport, session, terminal, and logging services.
- MCP server support validation moved into `SalmonEgg.Acp.Mcp`; Domain keeps only app-local MCP settings/catalog state.
- Client callback response payloads for permission outcome and `fs/read_text_file` now serialize through `AcpJsonContext`.
- The legacy non-JSON-RPC `AcpMessage` / `IAcpProtocolService` / `ConnectionManager` stack was removed from Domain, Application, Infrastructure, DI, and tests. It used string `ProtocolVersion = "1.0"` and a non-standard `type: request/response/notification/initialize` envelope, so keeping it would have preserved an obsolete ACP-shaped runtime path.

Guard coverage:

- `tests/SalmonEgg.Domain.Tests/Architecture/AcpSdkBoundaryTests.cs`
  - fails if `SalmonEgg.Acp` gains project references,
  - fails if `SalmonEgg.Acp` source references SalmonEgg business layers,
  - fails if Domain reintroduces protocol DTO source files under `Models/Protocol`,
  - fails if Domain reintroduces `Models/JsonRpc`, `Models/Content`, `Models/Plan`, or `Models/Tool`,
  - fails if Domain or Infrastructure reintroduces the JSON-RPC parser/validator or protocol `AcpJsonContext`,
  - fails if Domain reintroduces ACP client/capability contracts or permission option payloads,
  - fails if Infrastructure reintroduces the ACP client runtime,
  - fails if Domain reintroduces `Models/Mcp/McpServerConfig.cs`,
  - fails if Domain reintroduces MCP support validation policy,
  - fails if Domain or Infrastructure reintroduces the legacy `AcpMessage` / `IAcpProtocolService` / `ConnectionManager` stack,
  - fails if DI re-registers the legacy ACP parser or connection manager,
  - fails if Domain session state reintroduces `StopReason`,
  - fails if SDK MCP wire payloads contain app-local `Enabled` state,
  - requires Domain to reference `SalmonEgg.Acp`.

## Latest Standard Review

Source checked:

- `https://agentclientprotocol.com/llms.txt`
- `https://agentclientprotocol.com/protocol/v1/schema.md`
- `https://agentclientprotocol.com/protocol/v1/initialization.md`
- `https://agentclientprotocol.com/protocol/v1/session-setup.md`

Relevant current standard facts:

- ACP v1 uses integer `protocolVersion`.
- `clientInfo` and `agentInfo` are `Implementation` objects and may include `name`, `title`, and `version`.
- Omitted capabilities mean unsupported.
- Baseline agent support includes `session/new`, `session/prompt`, `session/cancel`, and `session/update`.
- Optional stable agent methods include `authenticate`, `logout`, `session/load`, `session/resume`, `session/close`, `session/delete`, `session/list`, `session/set_mode`, and `session/set_config_option`.
- Client callbacks include `session/request_permission`, `fs/read_text_file`, `fs/write_text_file`, and `terminal/*`.
- `$/cancel_request` is a protocol-level notification. The spec says notifications whose methods start with `$/` are implementation dependent and may be ignored, so lack of explicit handling is not a violation by itself.

Current implementation status:

- `AcpClient` implements requests for the stable agent methods listed above.
- `AcpClient` handles inbound `session/update`, `session/request_permission`, `fs/read_text_file`, `fs/write_text_file`, and `terminal/*`.
- Existing tests already guard the recent v1 schema corrections for:
  - `current_mode_update.currentModeId`,
  - no non-standard root fields on `session/prompt`,
  - empty standard responses for `session/set_mode`, `session/delete`, and `logout`,
  - `additionalDirectories`, `session/delete`, and `logout` capability gating.

No new ACP schema violation was found in this review pass.

## Remaining SDK Extraction Work

The transport-independent client runtime is now in `SalmonEgg.Acp`. Desktop stdio, UI capability probing, storage, profile configuration, and Domain session projection remain outside the SDK behind host interfaces.

Before publishing the SDK package, keep these gates in the release checklist:

1. `dotnet pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj --configuration Release`.
2. SDK source scan: no `SalmonEgg.Domain`, `SalmonEgg.Application`, `SalmonEgg.Infrastructure`, or `SalmonEgg.Presentation` references.
3. App integration tests through `AcpClientFactory`, to ensure host adapters still project transport errors, session cache updates, terminal capability failures, and permission/file-system callbacks.

Dependency update note: the follow-up dependency modernization adopted `FluentValidation 12.x` by making internal layers (`Domain`, `Application`, `Infrastructure`, and `Infrastructure.Desktop`) `net10.0`-only. `SalmonEgg.Acp` remains the SDK compatibility boundary and continues to target `netstandard2.1;net10.0`. Test execution moved from VSTest/xUnit v2 to xUnit v3 on Microsoft.Testing.Platform, with `tests/SalmonEgg.Presentation.Core.Tests/testconfig.json` as the Presentation.Core parallelization contract.
