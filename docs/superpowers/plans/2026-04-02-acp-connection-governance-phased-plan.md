# ACP Connection Governance Phased Plan

> **For agentic workers:** REQUIRED: Use subagent-driven execution with strict result-oriented tests and regression gates. Do not merge phases.

**Goal:** Stabilize ACP connection reuse/scheduling without breaking remote hydration UX, while staying aligned with ACP protocol boundaries.

**Architecture:** Keep SSOT in store/workspace and keep protocol interpretation below ViewModel. Treat connection reuse as transport-level optimization, treat session identity as session-level protocol state, and stage rollout to protect existing anti-freeze/anti-flicker behavior.

**Tech Stack:** .NET 10, Uno/WinUI MVVM, Presentation Core MVUX store, xUnit, FlaUI GUI smoke tests

---

## Protocol Baseline (Non-Negotiable)

- ACP initializes per connection; session work happens after initialization.
- ACP session identity is `sessionId`; session lifecycle fields (`cwd`, `mcpServers`, etc.) are session-scoped.
- ACP does not mandate connection pooling/TTL/LRU policies.
- Therefore:
  - Connection reuse key is a client-side architecture decision.
  - Session scope must not be folded into connection identity.
  - Eviction policies are product/runtime choices and must not break correctness.

Reference pages:
- `https://agentclientprotocol.com/protocol/initialization`
- `https://agentclientprotocol.com/protocol/session-setup`
- `https://agentclientprotocol.com/protocol/transports`
- `https://agentclientprotocol.com/protocol/schema`

---

## Phase 1: Reuse Key Hardening (Must Do First)

### Objective
- Replace fragile string signature with a typed, canonicalized connection reuse key aligned to actual transport creation semantics.

### In Scope
- Connection identity only.
- No profile-sharing behavior changes.
- No scheduling model changes.

### Required Changes
- Add `AcpConnectionReuseKey` (transport-level only).
- Canonicalize fields exactly as runtime transport creation does today:
  - transport type
  - stdio command (trimmed)
  - stdio args (tokenized canonical argv, not raw string)
  - remote URL (trimmed)
- Keep session-scoped fields out of key:
  - `sessionId`, `cwd`, `mcpServers`, modes/configOptions, runtime capabilities/extensions.
- Update coordinator/registry use sites to consume typed key.

### Guardrails
- Do not move endpoint-resolution logic into `ChatViewModel`.
- Do not broaden cross-profile pooling in this phase.
- Keep registry as profile-scoped cache for now.

### Tests (Result-Oriented)
- Same profile + semantically equivalent endpoint => reuse.
- Same profile + endpoint change => recreate.
- Session binding/cwd changes only => no impact on connection key.
- Existing hydration/nav tests remain green.

### Files (Expected)
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpChatCoordinator.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpConnectionSessionRegistry.cs`
- new key type under `src/SalmonEgg.Presentation.Core/Services/Chat/`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpChatCoordinatorTests.cs`
- new focused key canonicalization tests

---

## Phase 2: Safe Eviction Infrastructure (Metadata + Conservative Policy)

### Objective
- Add bounded cleanup capability without introducing correctness regressions.

### In Scope
- Registry metadata and policy seam.
- Conservative eviction only.

### Required Changes
- Extend cache entries with usage metadata (e.g., last-used timestamp).
- Add eviction policy abstraction (`IAcpConnectionEvictionPolicy`) with snapshot-based decisions.
- Wire registry/cleaner/policy via DI (stop constructing internals ad hoc in coordinator).
- Add opportunistic trim points:
  - before/after transport apply
  - explicit disconnect
  - optional post-connect hook

### Conservative Rules (v1)
- Always pin active service.
- Pin services with `loadSession=false` when bound remote conversations still depend on them.
- TTL/LRU knobs implemented but default disabled until measured rollout.

### Guardrails
- No background timer-based trimming in v1.
- Eviction eligibility must derive from authoritative binding/catalog/store state, not duplicated registry truth.
- Eviction must never mutate current visible selection/navigation state.

### Tests (Result-Oriented)
- Pinned active service never evicted.
- `loadSession=false` + bound remote conversation remains pinned.
- Unpinned stale sessions evictable when policy enabled.
- Existing anti-freeze and latest-selection smokes remain green.

### Files (Expected)
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpConnectionSessionRegistry.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpConnectionSessionCleaner.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpChatCoordinator.cs`
- `SalmonEgg/SalmonEgg/DependencyInjection.cs`
- tests for cleaner/coordinator + regression suite

---

## Phase 3: Scheduling Boundary Clarification (No Concurrent Live Sessions Yet)

### Objective
- Formalize current supported model: one foreground remote hydration/interaction lane per live connection.

### In Scope
- Clarify and enforce scheduling contract.
- Preserve current user-facing behavior.

### Out of Scope
- Full concurrent multi-live-session execution on one connection.
- Broad connection state-machine redesign.
- Cross-profile physical-connection sharing rollout.

### Required Changes
- Make “single foreground lane” explicit in coordinator/scheduler seam.
- Ensure stale operations are cancelable and cannot project into latest selected conversation.
- Keep ViewModel protocol-light: consume authoritative coordinator/store signals, do not resolve endpoint identity in VM.
- If `IChatService` singleton session surfaces are adjusted, replace them with explicit equivalent query seams before removal.

### Guardrails
- No silent dropping of non-foreground behavior without explicit product rule.
- No regression in hydration overlay lifecycle, latest-selection-wins, or navigation responsiveness.

### Tests (Result-Oriented)
- Random/click-storm switching remains interactive.
- Latest selected conversation always wins projection.
- No stale replay leakage after cancellation/preemption.
- Real-user config smoke remains green.

### Files (Expected)
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpChatCoordinator.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/AcpConnectionCoordinator.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs` (minimal, seam-only)
- targeted tests + existing GUI/real smokes

---

## Explicit Defers (Not In This Plan)

- Protocol version negotiation tightening (per user decision).
- Cross-profile shared physical connection rollout (defer until Phases 1-3 are stable).
- Full multi-session concurrent live scheduling.

---

## Verification Gate Per Phase

Run and pass before moving to next phase:

1. `dotnet build SalmonEgg\SalmonEgg\SalmonEgg.csproj -v minimal`
2. `dotnet test tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj --filter "FullyQualifiedName~InitializeTypesTests"`
3. `dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AcpClientTests|FullyQualifiedName~CapabilityManagerTests"`
4. `dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpChatCoordinatorTests|FullyQualifiedName~AcpConnectionCoordinatorTests|FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~NavigationCoordinatorTests"`
5. `$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatSkeletonSmokeTests.SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection|FullyQualifiedName~ChatSkeletonSmokeTests.SelectAcrossProfilesAndLocal_LongRandomSwitch_RemainsInteractive"`
6. `$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~RealUserConfigSmokeTests.RandomSwitchBetweenLocalRemote_WithOneSecondCadence_RemainsInteractive"`

---

## Rollback and Safety

- Each phase lands as an independent commit series.
- If any phase regresses interactive switching or hydration lifecycle:
  - stop rollout
  - revert phase commits only
  - keep prior phase stable baseline
- Never batch multiple phases into one PR/commit.

