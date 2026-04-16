# Chat Markdown Rendering Design

## Background

Current chat message rendering uses a plain `TextBlock` in the shared bubble template (`MessageTemplate` in `SalmonEgg/SalmonEgg/Styles/ChatStyles.xaml`). This is stable but cannot render Markdown semantics required by AI coding conversations (code fences, tables, task lists, links, etc.).

We need a Markdown rendering design that:

- keeps the existing bubble visual language and layout contract,
- remains MVVM-driven (ViewModel owns state; View only projects),
- supports Uno + WinUI cross-platform build constraints,
- and avoids streaming regressions such as flicker and malformed code block rendering.

## Confirmed Product Decisions

The following decisions were explicitly confirmed in brainstorming:

1. Component choice: `CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock`.
2. Scope: render Markdown for assistant text messages only.
3. Bubble contract: Markdown must be rendered inside existing message bubbles.
4. Syntax scope: GFM-level subset for v1 (base markdown + tables + task lists + strikethrough).
5. Streaming mode: safe mode; keep plain text while fenced blocks are unclosed.
6. Link policy: click opens external browser directly.
7. Image policy: allow network image rendering.
8. Failure handling: renderer failure must auto-fallback to plain text.
9. Testing policy: comprehensive behavior-based gates; avoid implementation-detail assertions.

## Goals

1. Deliver readable, stable assistant Markdown rendering without changing conversation behavior semantics.
2. Preserve current bubble styling and transcript layout rhythm.
3. Keep state transitions deterministic under token streaming and replay.
4. Guarantee readability under all failures via plain text fallback.

## Non-Goals (V1)

1. No code block action UX (`Copy`, `Insert to composer`, `Apply to file`).
2. No Markdown rendering for user messages, tool cards, plan cards, or resource cards.
3. No custom Markdown parser implementation.

## Approaches Considered

### Option 1 (Recommended): Bubble-Preserved Dual Renderer

Keep current bubble containers and replace only assistant text body with a render host that can switch between plain text and markdown view.

Pros:

- minimal blast radius,
- aligns with current style system,
- easiest to enforce fallback and streaming safety.

Cons:

- introduces message-level render state fields.

### Option 2: Full Text Template Replacement With Markdown Control

Replace all text rendering with markdown control directly.

Pros:

- lower initial code volume.

Cons:

- poor streaming stability,
- harder failure isolation,
- higher regression risk.

### Option 3: New Markdown IR Then UI Mapping

Build an intermediate markdown block model and render block-by-block.

Pros:

- strongest long-term extensibility.

Cons:

- over-scoped for current requirement,
- delays delivery.

Decision: adopt Option 1.

## Architecture Design

### 1. Rendering Ownership and Boundaries

1. `ChatViewModel` / `ChatMessageViewModel` own render state.
2. View (`ChatStyles.xaml`) projects state only; no markdown business rules in code-behind.
3. Markdown-specific UI control usage remains in UI project; state evaluation logic remains in Presentation.Core.

### 2. Assistant Text Render State Machine

For assistant text messages only:

- `PlainStreaming`: plain text display during unstable markdown phase (for example unclosed code fence).
- `MarkdownReady`: render with `MarkdownTextBlock` when content is stable.
- `FallbackPlain`: render plain text when markdown rendering fails.

Transition rules:

1. Initial assistant streaming content enters `PlainStreaming`.
2. If markdown is stable (including closed fenced blocks), switch to `MarkdownReady`.
3. On markdown render exception/error signal, switch to `FallbackPlain` and keep it sticky for that message.
4. User/outgoing messages always remain plain text.

### 3. Bubble-Preserved Template Strategy

Keep the existing two bubble containers (incoming/outgoing), spacing, corner radius, and timestamp behavior.

Only change incoming assistant text body area:

1. Wrap text area in a dedicated content host.
2. Show plain `TextBlock` when state is `PlainStreaming` or `FallbackPlain`.
3. Show `MarkdownTextBlock` when state is `MarkdownReady`.

This preserves visual consistency and avoids layout regressions.

### 4. Markdown Feature Configuration (V1)

Enable configured support for:

- headers, paragraphs, emphasis, block quotes,
- inline code + fenced code blocks,
- links,
- GFM tables,
- GFM task lists,
- strikethrough.

Policy constraints:

- links open external browser directly,
- network images are allowed,
- if image fails to load, message remains readable (alt text or link text projection).

## Data Flow

1. Conversation snapshots/projected updates continue updating `TextContent` as today.
2. For assistant text messages, render-policy logic computes message render state from latest text content.
3. XAML template binds to render-state properties and chooses plain or markdown view.
4. Render failures signal state downgrade to `FallbackPlain` for that message.
5. Workspace persistence remains unchanged for message truth (`TextContent` only), avoiding schema churn.

## Error Handling and Resilience

1. Any markdown render failure must degrade to plain text for that message without affecting other messages.
2. Failure path must not block UI thread or transcript updates.
3. Streaming must stay responsive even for malformed markdown.
4. If markdown control cannot render specific content, never hide original text.

## Behavior-Driven Testing and Gates

All tests below must assert user-visible behavior and state outcomes, not private method internals.

### 1. Core Behavior Tests (Presentation.Core)

1. Assistant streaming with unclosed fenced code block stays in plain mode.
2. Same message switches to markdown mode after fence closure.
3. Markdown failure path enters fallback plain mode and remains readable.
4. Outgoing/user text message never enters markdown mode.
5. Table/task-list/strikethrough content resolves to markdown-ready state when syntactically stable.

### 2. XAML Contract Tests

1. Message template keeps original bubble containers and timestamp area.
2. Incoming assistant text area contains renderer host with plain/markdown switching projection.
3. No business-state computation is added to View code-behind.

### 3. Integration/Replay Behavior Tests

1. Replaying persisted transcript yields deterministic render states for same message content.
2. Rapid transcript updates do not cause message disappearance or cross-message contamination.
3. Markdown fallback in one message does not affect adjacent messages.

### 4. Build/Test Gate Commands

Before completion, run and pass:

1. `dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-desktop`
2. `dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -f net10.0-browserwasm`
3. targeted tests for new behavior and XAML contracts.

If any environment constraint prevents full gate execution, document exactly which gate was skipped and why.

## Rollout Plan

1. Add render-state model and policy logic in Presentation.Core.
2. Update assistant incoming text section in `ChatStyles.xaml` to dual-render host.
3. Wire markdown control package references needed for target frameworks.
4. Add behavior-focused tests (core + xaml contract + replay behavior).
5. Run gates and verify no regression in existing chat behavior tests.

## Risks and Mitigations

1. Risk: cross-platform differences in preview markdown control behavior.
   Mitigation: keep plain fallback path and test on both desktop and browserwasm builds.
2. Risk: network images may increase latency or loading noise.
   Mitigation: preserve text readability and keep image failure non-blocking.
3. Risk: streaming churn causes frequent mode flipping.
   Mitigation: stable transition rules and sticky fallback behavior per message.

## Acceptance Criteria

1. Assistant markdown appears inside current incoming bubble style.
2. GFM subset selected for v1 renders correctly for stable content.
3. Streaming with unclosed code fences remains readable and stable.
4. Any renderer issue degrades gracefully to plain text.
5. All required behavior gates pass.
