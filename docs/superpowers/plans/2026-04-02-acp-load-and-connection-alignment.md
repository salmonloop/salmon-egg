# ACP Load And Connection Alignment Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make ACP `session/load` results visible in the product and prevent extension-capability drift without regressing remote session hydration or UI responsiveness.

**Architecture:** Keep the existing SSOT/MVVM split. Protocol parsing remains in Infrastructure, session/result projection remains in Presentation Core, and connection state remains in the chat connection store. Extend the current projector/store/coordinator seams instead of pushing new behavior into the View or adding coordinator-local mirror state.

**Tech Stack:** .NET 10, Uno/WinUI MVVM, Presentation Core MVUX store, xUnit, FlaUI GUI smoke tests, ACP protocol models

---

## Protocol References

- ACP Session Setup: `loadSession` capability must gate `session/load`
- ACP Schema: `session/load` may return `modes` and `configOptions`
- ACP Session Config Options: `configOptions` is the preferred state surface; `modes` is fallback/compatibility
- ACP Extensibility: custom method names should use underscore-prefixed namespace
- Stable ACP session setup is normative for capability gating; client MUST NOT call `session/load` without `loadSession=true`. Client MUST accept both `result: null` and draft-schema object payloads, projecting `modes` / `configOptions` only when those fields are actually present.

These references are the acceptance criteria source. If implementation behavior conflicts with current protocol text, protocol wins.

## File Structure

**Existing production files likely to change**
- Modify: `src/SalmonEgg.Domain/Models/Protocol/OtherSessionTypes.cs`
  Purpose: protocol shape for `SessionLoadResponse`
- Modify: `src/SalmonEgg.Infrastructure/Client/AcpClient.cs`
  Purpose: `session/load` request gating, `null`/payload result parsing, inbound request dispatch, extension compatibility
- Modify: `src/SalmonEgg.Domain/Models/Protocol/ClientCapabilityMetadata.cs`
  Purpose: canonical extension metadata / aliases
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionUpdateProjector.cs`
  Purpose: add `session/load` projection entry point
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
  Purpose: apply `session/load` result to conversation session state and hydration UX
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/AcpConnectionCoordinator.cs`
  Purpose: resync/load behavior discipline only within current state model

**Existing test files likely to change**
- Modify: `tests/SalmonEgg.Infrastructure.Tests/Client/AcpClientTests.cs`
  Purpose: protocol parsing / extension compatibility coverage
- Modify: `tests/SalmonEgg.Domain.Tests/Protocol/InitializeTypesTests.cs`
  Purpose: capability metadata serialization coverage
- Modify: `tests/SalmonEgg.Infrastructure.Tests/Services/CapabilityManagerTests.cs`
  Purpose: capability lookup coverage
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpSessionUpdateProjectorTests.cs`
  Purpose: load-response projection coverage
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpConnectionCoordinatorTests.cs`
  Purpose: resync lifecycle and load-result application coverage
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`
  Purpose: result-oriented hydration and mode/config projection coverage
- Modify: `tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs`
  Purpose: GUI-visible remote hydration result and non-freeze coverage
- Modify: `tests/SalmonEgg.GuiTests.Windows/RealUserConfigSmokeTests.cs`
  Purpose: real-config regression coverage if deterministic smoke is insufficient

**New files only if necessary**
- Create only if code duplication becomes material:
  - `src/SalmonEgg.Presentation.Core/Services/Chat/AcpExtensionRegistry.cs`
  - `tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpExtensionRegistryTests.cs`

Prefer extending existing focused files first. Do not create abstraction-only files unless they remove real duplication and become the single source of truth.

---

## Chunk 1: Lock Protocol-Compatible `session/load` Interop Semantics

### Task 1: Pin `session/load` compatibility behavior in Infrastructure

**Files:**
- Modify: `src/SalmonEgg.Domain/Models/Protocol/OtherSessionTypes.cs`
- Modify: `src/SalmonEgg.Infrastructure/Client/AcpClient.cs`
- Test: `tests/SalmonEgg.Infrastructure.Tests/Client/AcpClientTests.cs`

- [ ] **Step 1: Write failing `AcpClient` tests for protocol interop cases**

Add tests that assert:
- `loadSession=false` or missing => client does not send `session/load`
- `loadSession=true` + `result: null` => no parse failure, load completes successfully
- `loadSession=true` + object payload => `configOptions` / `modes` are parsed when present
- object payload with neither field present => treated as compatible empty result, not a protocol failure

- [ ] **Step 2: Run focused Infrastructure tests to verify failure**

Run:
```powershell
dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AcpClientTests"
```

Expected: new interop cases fail before implementation if protocol behavior is still incomplete.

- [ ] **Step 3: Implement minimal compatibility behavior**

Rules:
- capability gating follows ACP Session Setup
- `null` is accepted as the stable completion result
- object payloads are accepted only as compatibility extensions to the stable baseline
- parsing stays in Infrastructure; no Presentation-specific interpretation here

- [ ] **Step 4: Run focused Infrastructure tests to verify pass**

Run the same command.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Domain/Models/Protocol/OtherSessionTypes.cs src/SalmonEgg.Infrastructure/Client/AcpClient.cs tests/SalmonEgg.Infrastructure.Tests/Client/AcpClientTests.cs
git commit -m "fix: align acp session load interop"
```

---

## Chunk 2: Project `session/load` Results Into User-Visible Session State

### Task 2: Add projector entry point for `session/load`

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionUpdateProjector.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpSessionUpdateProjectorTests.cs`

- [ ] **Step 1: Write failing projector tests for `session/load`**

Add tests that assert:
- `ProjectSessionLoad` maps `configOptions` + `modes`
- `configOptions` takes precedence over legacy `modes`
- legacy `modes` still populate UI state when no mode config option exists
- null/empty load result yields empty delta rather than clearing unrelated state by accident

- [ ] **Step 2: Run projector tests to verify failure**

Run: `dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpSessionUpdateProjectorTests"`

Expected: FAIL because `ProjectSessionLoad` does not exist yet.

- [ ] **Step 3: Implement minimal projector support**

Add `ProjectSessionLoad(SessionLoadResponse response)` that reuses the same precedence logic already used for `ProjectSessionNew`.

Implementation rules:
- no UI types in projector
- shared mode/config projection logic must live in one helper
- empty response must not fabricate modes or config state

- [ ] **Step 4: Run projector tests to verify pass**

Run the same command.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionUpdateProjector.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/AcpSessionUpdateProjectorTests.cs
git commit -m "feat: project acp session load state"
```

### Task 3: Apply `session/load` result in `ChatViewModel`

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

- [ ] **Step 1: Write failing ChatViewModel tests for visible load results**

Add result-oriented tests that verify:
- after remote hydration starts from `LoadSessionAsync`, the selected mode in conversation state matches `session/load` result
- config options panel visibility and option values match `session/load` result
- load result does not regress hydration overlay timing or transcript replay ownership

Test at the ViewModel boundary. Assert final user-visible state, not internal helper calls.

- [ ] **Step 2: Run focused ChatViewModel tests to verify failure**

Run: `dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatViewModelTests"`

Expected: new tests FAIL because `LoadSessionAsync` result is not projected.

- [ ] **Step 3: Implement minimal `session/load` application path**

In `ChatViewModel`:
- after `LoadSessionAsync(...)` returns, project the `SessionLoadResponse`
- apply delta through existing `ApplySessionUpdateDeltaAsync`
- do not bypass current hydration guards / latest-token checks
- do not move transcript ownership out of SSOT
- `ChatViewModel` may only forward typed `SessionLoadResponse` into existing projector/store application seams
- no protocol precedence logic, no capability checks, and no connection lifecycle state mutations in `ChatViewModel`

- [ ] **Step 4: Run focused tests to verify pass**

Run the same command, or a narrower filter for the new tests if needed.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs
git commit -m "feat: apply acp load results to chat state"
```

---

## Chunk 3: Add Result-Oriented Regression Tests For Remote Hydration UX

### Task 4: Add deterministic UI-level regression for hydrated mode/config outcome

**Files:**
- Modify: `tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs`
- Modify if needed: `tests/SalmonEgg.GuiTests.Windows/GuiAppDataScope.cs`

- [ ] **Step 1: Write failing GUI smoke**

Add a deterministic smoke that:
- launches seeded remote conversation data
- selects remote session
- waits through real loading overlay lifecycle
- verifies final header + latest replayed message + mode/config outcome visible in UI

The assertion must be about final rendered outcome. Do not assert helper call order or implementation-specific automation IDs unless they are the product contract.

- [ ] **Step 2: Run the smoke to verify failure**

Run:
```powershell
$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatSkeletonSmokeTests.<NEW_TEST_NAME>"
```

Expected: FAIL before implementation if UI does not reflect `session/load` state.

- [ ] **Step 3: Implement only the missing seeded test data or UI plumbing**

If deterministic seed data cannot express mode/config load state yet, extend the test data generator minimally. Do not add production-only test hooks.

- [ ] **Step 4: Run the smoke to verify pass**

Run the same command.

- [ ] **Step 5: Commit**

```bash
git add tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs tests/SalmonEgg.GuiTests.Windows/GuiAppDataScope.cs
git commit -m "test: cover remote hydration load-state outcome"
```

### Task 5: Keep existing anti-freeze / anti-flicker protection green

**Files:**
- Reuse existing tests only

- [ ] **Step 1: Run targeted regression set**

Run:
```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~NavigationCoordinatorTests|FullyQualifiedName~AcpConnectionCoordinatorTests|FullyQualifiedName~ChatViewModelTests"
```

- [ ] **Step 2: Run deterministic GUI regression set**

Run:
```powershell
$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatSkeletonSmokeTests.SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection|FullyQualifiedName~ChatSkeletonSmokeTests.SelectAcrossProfilesAndLocal_LongRandomSwitch_RemainsInteractive"
```

- [ ] **Step 3: If deterministic smoke passes but uncertainty remains, run real-config smoke**

Run:
```powershell
$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~RealUserConfigSmokeTests.RandomSwitchBetweenLocalRemote_WithOneSecondCadence_RemainsInteractive"
```

- [ ] **Step 4: Commit if any necessary test stabilization changes were made**

```bash
git add tests/SalmonEgg.Presentation.Core.Tests tests/SalmonEgg.GuiTests.Windows
git commit -m "test: preserve remote hydration interaction regressions"
```

---

## Chunk 4: Converge ACP Extension Capability Declaration And Dispatch

### Task 6: Introduce one extension-method registry / SSOT

**Files:**
- Modify: `src/SalmonEgg.Domain/Models/Protocol/ClientCapabilityMetadata.cs`
- Modify: `src/SalmonEgg.Infrastructure/Client/AcpClient.cs`
- Modify if needed: `src/SalmonEgg.Infrastructure/Services/CapabilityManager.cs`
- Test: `tests/SalmonEgg.Infrastructure.Tests/Client/AcpClientTests.cs`
- Test: `tests/SalmonEgg.Infrastructure.Tests/Services/CapabilityManagerTests.cs`
- Test: `tests/SalmonEgg.Domain.Tests/Protocol/InitializeTypesTests.cs`

- [ ] **Step 1: Write failing tests for drift prevention**

Add tests that assert:
- canonical custom method names are underscore-prefixed
- outbound advertisement exposes only canonical extension metadata
- inbound dispatch may accept documented legacy aliases for compatibility
- unknown legacy names fail cleanly
- advertised canonical metadata and accepted inbound names stay consistent for the same extension descriptor set

- [ ] **Step 2: Run targeted tests to verify failure or current drift**

Run:
```powershell
dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AcpClientTests|FullyQualifiedName~CapabilityManagerTests"
dotnet test tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj --filter "FullyQualifiedName~InitializeTypesTests"
```

- [ ] **Step 3: Implement registry convergence**

Rules:
- one canonical extension descriptor list
- aliases are inbound-compatibility only, not separate outbound-advertised capabilities unless protocol evidence requires it
- no string literals for extension method names outside the registry owner

- [ ] **Step 4: Run targeted tests to verify pass**

Run the same commands.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Domain/Models/Protocol/ClientCapabilityMetadata.cs src/SalmonEgg.Infrastructure/Client/AcpClient.cs src/SalmonEgg.Infrastructure/Services/CapabilityManager.cs tests/SalmonEgg.Infrastructure.Tests/Client/AcpClientTests.cs tests/SalmonEgg.Infrastructure.Tests/Services/CapabilityManagerTests.cs tests/SalmonEgg.Domain.Tests/Protocol/InitializeTypesTests.cs
git commit -m "refactor: unify acp extension capability registry"
```

### Task 7: Run end-to-end verification sweep

**Files:**
- No new files required unless tests expose a gap

- [ ] **Step 1: Build application**

Run:
```powershell
dotnet build SalmonEgg\SalmonEgg\SalmonEgg.csproj -v minimal
```

- [ ] **Step 2: Run protocol + presentation regression**

Run:
```powershell
dotnet test tests/SalmonEgg.Domain.Tests/SalmonEgg.Domain.Tests.csproj --filter "FullyQualifiedName~InitializeTypesTests"
dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AcpClientTests|FullyQualifiedName~CapabilityManagerTests"
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpSessionUpdateProjectorTests|FullyQualifiedName~AcpConnectionCoordinatorTests|FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~NavigationCoordinatorTests"
```

- [ ] **Step 3: Explicitly verify the three `session/load` interop outcomes**

Must-pass outcomes:
- capability absent => no `session/load` request is sent
- capability present + `result: null` => hydration completes without parse failure
- capability present + payload result => mode/config projection reaches product state

If these are not named by test output yet, add narrow test filters and run them directly.

- [ ] **Step 4: Run GUI regression**

Run:
```powershell
$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatSkeletonSmokeTests.SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection|FullyQualifiedName~ChatSkeletonSmokeTests.SelectAcrossProfilesAndLocal_LongRandomSwitch_RemainsInteractive|FullyQualifiedName~ChatSkeletonSmokeTests.<NEW_TEST_NAME>"
```

- [ ] **Step 5: Run real-config smoke if any navigation/hydration timing changed**

Run:
```powershell
$env:SALMONEGG_GUI='1'; dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~RealUserConfigSmokeTests.RandomSwitchBetweenLocalRemote_WithOneSecondCadence_RemainsInteractive|FullyQualifiedName~RealUserConfigSmokeTests.SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection"
```

- [ ] **Step 6: Final commit**

```bash
git add src tests docs/superpowers/plans/2026-04-02-acp-load-and-connection-alignment.md
git commit -m "feat: align acp load projection and extension contracts"
```

---

## Review Checklist For Any Worker Executing This Plan

- Does the change preserve SSOT, or did it introduce new coordinator-local mirrors?
- Does any test assert implementation details instead of user-visible outcome?
- Does any new lifecycle flag duplicate state already present in the store?
- Does `session/load` projection respect `configOptions` precedence over `modes`?
- Does capability-absent / `null`-result / payload-result `session/load` interop remain explicitly covered?
- Does remote hydration still keep latest-selection-wins semantics under rapid switching?
- Did any extension string literal escape the registry owner?
- Are all logs structured templates rather than interpolation?

## Notes

- `ChatViewModel` is already large. Do not opportunistically refactor unrelated sections while executing this plan.
- Existing GUI anti-freeze and anti-flicker smokes are hard constraints, not optional confidence checks.
- Explicit ACP connection state-machine redesign is deferred to a follow-up plan. Do not expand this plan into reducer/state-shape refactoring.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-02-acp-load-and-connection-alignment.md`. Ready to execute.
