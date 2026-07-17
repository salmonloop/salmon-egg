# Conversation-Owned Activation Failure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:test-driven-development for every behavior change. Do not commit unless the user explicitly requests it.

**Goal:** Prevent one conversation's activation failure from appearing beneath another conversation's authoritative transcript.

**Architecture:** `SessionActivationSnapshot` becomes the single owner of activation identity, phase, reason, and user-visible failure message. `ChatViewModel` projects activation failure only for the matching `CurrentSessionId`; a separate ephemeral `ConversationOperationFailure` preserves non-activation errors with its own conversation owner. Chat and MiniChat bind both explicit projections.

**Tech Stack:** .NET 10, C#, CommunityToolkit.Mvvm, Uno/WinUI XAML, xUnit, Moq.

## Global Constraints

- Strict MVVM: View is presentation and binding only.
- Preserve latest-intent semantics and the `INavigationCoordinator -> IConversationSessionSwitcher` owner chain.
- Do not change ACP method, schema, capability, or session-update routing behavior.
- Do not add timers, delayed clearing, title matching, local transcript recovery, or UI-only hiding conditions.
- Do not create a second activation-failure store.
- Do not hide, drop, or reclassify existing non-activation command/manual-hydration errors.
- All Core / Presentation.Core tests must remain cross-platform runnable.
- Use structured logging only; do not add diagnostic production logging.
- Keep design and plan documents local-only and do not commit any files without explicit user approval.

---

### Task 1: Make the activation snapshot own terminal failure text

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Services/Navigation/SessionActivationSnapshot.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Activation/ConversationActivationOutcomePublisher.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ConversationActivationOutcomePublisherTests.cs`

**Interfaces:**
- Produces: `SessionActivationSnapshot.FailureMessage`
- Produces: `ConversationActivationOutcomePublisher.TryPublishFailureAsync(string conversationId, long? activationVersion, string reason, string message)`
- Temporarily preserves the legacy `TrySetActivationErrorAsync` API and `Action<string> setError` constructor dependency so this task can compile and turn GREEN before callers migrate. Task 3 removes both after the final caller is replaced.

- [ ] **Step 1: Write failing publisher tests**

Add tests proving that a current failure atomically stores `Faulted`, reason, and message; stale or mismatched versions do not mutate state; a conversation mismatch does not mutate state; and a queued callback superseded before dispatcher execution cannot commit.

- [ ] **Step 2: Run the publisher tests and verify RED**

Run:

```powershell
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter-class SalmonEgg.Presentation.Core.Tests.Chat.ConversationActivationOutcomePublisherTests --timeout 3m --output Normal
```

Expected: compilation or assertion failure because `FailureMessage` and `TryPublishFailureAsync` do not exist.

- [ ] **Step 3: Implement the snapshot and atomic publisher**

Add optional `FailureMessage` to the snapshot and add one UI-dispatched mutation that validates current shell, latest version, active snapshot owner, and activation version before setting the terminal failure. Keep the old two-step API callable but do not add new callers; its removal is a Task 3 cleanup after full migration.

- [ ] **Step 4: Run the publisher tests and verify GREEN**

Run the command from Step 2. Expected: all publisher tests pass.

### Task 2: Add the owner-aware activation failure projection

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.SessionPresentation.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

**Interfaces:**
- Consumes: `SessionActivationSnapshot.FailureMessage` from Task 1.
- Produces: `ChatViewModel.SessionActivationFailureMessage` and `ChatViewModel.HasSessionActivationFailure`.

- [ ] **Step 1: Write failing projection and notification tests**

Require activation failure visibility only for a matching faulted snapshot with a message. Assert `PropertyChanged` for the activation derived-property pair when `ActiveSessionActivation` or `CurrentSessionId` changes.

- [ ] **Step 2: Run the projection tests and verify RED**

Run each exact test with MTP `--filter-class` or `--filter-method`. Expected: the activation projection properties and notifications do not yet exist, so the new tests fail. XAML contract tests are intentionally deferred to Task 3.

- [ ] **Step 3: Implement owner-aware projections and bindings**

Derive activation properties from the matching faulted snapshot and notify them from both source-change paths. Do not change XAML yet: the operation-error replacement is introduced atomically with its real producers in Task 3, so no existing error path is hidden between tasks.

- [ ] **Step 4: Run projection tests and verify GREEN**

Run the exact tests from Step 2. Expected: all pass.

### Task 3: Add the operation owner and route every failure atomically

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.SessionPresentation.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.AcpSessionLifecycle.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.RemoteConversationLifecycle.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/MiniWindow/MiniChatView.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs`

**Interfaces:**
- Consumes: `TryPublishFailureAsync(...)` from Task 1.
- Produces: `ChatViewModel.ConversationOperationFailureMessage` and `ChatViewModel.HasConversationOperationFailure`.
- Produces private owner-aware publish/clear methods exercised through real command, hydration, reconnect, and event producers.
- Preserves: `SetConversationRuntimeStateAsync(...)` diagnostic runtime phases.

- [ ] **Step 1: Write failing cross-conversation regression and path-classification tests**

Construct conversation A with a profile binding but no remote session ID and conversation B with its own transcript. Activate A and assert its owned activation failure is visible. Activate B and assert B's transcript is projected while A's activation failure is not visible.

Through real public command/hydration/event entry points, add tests proving command, manual-hydration, reconnect, and ACP `ErrorOccurred` failures use the operation owner and remain visible only for their conversation. Add the race A operation pending -> switch to B -> A late failure/clear, proving the late result neither displays in B nor clears B's failure. Tests must not use reflection or implementation-placement assertions.

Add new-session tests for both phases and both starting contexts: pre-create failure belongs to captured conversation A when A was current, is ownerless and visible when started without a conversation, and after `CreateAndActivateLocalConversationAsync` returns, a failure is owned by that exact returned ID even if selection changes afterward.

- [ ] **Step 2: Run the regression tests and verify RED**

Run the exact methods with MTP `--filter-method`. Expected: existing paths still publish global unowned errors.

- [ ] **Step 3: Replace and classify every failure publication path**

Add the ephemeral operation-failure owner, matching clear semantics, derived properties, and property notifications. Bind separate activation and operation callouts in main and mini chat, rejecting the inherited global bindings.

Replace every paired `TryPublishPhaseAsync(...Faulted...)` plus `TrySetActivationErrorAsync(...)` with `TryPublishFailureAsync(...)`. Classify reconnect, the three direct manual-hydration `SetError` paths, every `CommandWorkflow` `SetError/ClearError` path, and ACP `OnErrorOccurred` as operation failures. Capture an immutable conversation owner before the first `await` for commands/manual hydration, at reconnect-attempt start, and when `ErrorOccurred` arrives; all later failure and clear callbacks must reuse that captured owner and must not reread `CurrentSessionId` for attribution. Update existing activation assertions that read global `ErrorMessage` to read the activation projection.

For `CreateNewSessionAsync`, capture the starting `CurrentSessionId` before the first `await` (which may be null), then perform one explicit owner transfer only from the authoritative ID returned by `CreateAndActivateLocalConversationAsync`; do not reread or infer it from later selection state. After all activation callers migrate, remove `TrySetActivationErrorAsync` and the publisher's `Action<string> setError` dependency.

- [ ] **Step 4: Run ChatViewModel regression tests and verify GREEN**

Run the new tests plus existing missing-binding, capability, stale-session, command-error, and stale-activation tests. Expected: all pass.

### Task 4: Verify the complete owner boundary

**Files:**
- Test only; no planned production changes.

**Interfaces:**
- Verifies the complete Task 1-3 contract.

- [ ] **Step 1: Run focused owner and routing tests**

```powershell
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter-class SalmonEgg.Presentation.Core.Tests.Chat.ConversationActivationOutcomePublisherTests --timeout 3m --output Normal
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter-class SalmonEgg.Presentation.Core.Tests.Chat.ChatViewXamlTests --timeout 3m --output Normal
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter-class SalmonEgg.Presentation.Core.Tests.Chat.ChatViewModelTests --timeout 10m --output Normal
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AuthoritativeRemoteSessionRouterTests --timeout 3m --output Normal
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter-class SalmonEgg.Presentation.Core.Tests.Chat.Mvux.ChatReducerTests --timeout 3m --output Normal
```

Expected: zero failed tests.

- [ ] **Step 2: Run the full Presentation.Core suite**

```powershell
dotnet test --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --configuration Release --timeout 20m --output Normal
```

Expected: zero failed tests.

- [ ] **Step 3: Build affected targets**

Use the exact affected target commands from `BUILD_GUIDE.md`. Expected: exit code 0 with no new warnings attributable to this change.

- [ ] **Step 4: Run applicable GUI smoke**

Run the existing Windows session-activation failure smoke if it can express A-fails/B-streams. If not, report that GUI scenario as not automated rather than substituting a source scan.

- [ ] **Step 5: Request independent final review**

Provide the approved design, implementation plan, full worktree diff, and verification output to a fresh reviewer. Resolve all Critical and Important findings and re-run affected tests.
