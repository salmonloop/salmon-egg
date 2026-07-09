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

Guard coverage:

- `tests/SalmonEgg.Domain.Tests/Architecture/AcpSdkBoundaryTests.cs`
  - fails if `SalmonEgg.Acp` gains project references,
  - fails if `SalmonEgg.Acp` source references SalmonEgg business layers,
  - fails if Domain reintroduces `Models/Protocol/ProtocolPathRules.cs`,
  - fails if Domain reintroduces `Models/JsonRpc`, `Models/Content`, `Models/Plan`, or `Models/Tool`,
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

Do not mark the SDK extraction complete until these are moved or adapted behind SDK-facing abstractions:

1. Move MCP server and protocol DTOs into `SalmonEgg.Acp` with SDK-appropriate namespaces. Keep local support policy and app-specific session state outside the SDK.
2. Split protocol session DTOs from local session persistence/display models before moving session-related contracts.
3. Move source-generated serialization context and parser into `SalmonEgg.Acp`, or split app-specific serializers from protocol serializers.
4. Move `IAcpClient` and capability manager contracts into `SalmonEgg.Acp`; keep SalmonEgg chat orchestration in Application/Presentation.
5. Move transport-independent client logic into `SalmonEgg.Acp`; keep desktop stdio, UI capability probing, storage, and profile configuration outside the SDK.
6. Add packaging verification with `dotnet pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj --configuration Release`.

The current commit is intentionally only the first safe boundary cut, not the final SDK extraction.
