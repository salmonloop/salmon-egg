# WASM Smoke Design

Date: 2026-07-05
Scope: `scripts/gates/*`, `tests/SalmonEgg.Presentation.Core.Tests/WasmStartupAssetsTests.cs`
Status: Ready for user review before implementation

## Background

The current BrowserWasm smoke coverage already uses a real browser against the current `net10.0-browserwasm` build output. That direction is correct and should remain the authoritative validation path for WASM runtime behavior.

The main issue is boundary ownership. `scripts/gates/wasm-file-system-availability-smoke.mjs` currently owns too many unrelated concerns at once:

1. settings persistence
2. browser capability boundary
3. ACP profile persistence
4. ACP remote directory persistence
5. ACP session full-chain prompt flow

This makes failures slow to localize and pushes the script toward implementation-coupled helpers and diagnostics. The result is a smoke suite that still catches regressions, but no longer has clean architectural boundaries.

## Goals

1. Keep WASM smoke on the real browserwasm build output, not a fake host or implementation-only unit harness.
2. Split smoke coverage by behavior boundary so each gate has one clear responsibility.
3. Assert user-visible or protocol-visible behavior, not page internals, helper structure, or storage text layout.
4. Preserve strong diagnostics for failures without making those diagnostics the primary pass/fail contract.
5. Keep the smallest possible custom compatibility layer: native browser behavior first, Uno projected UI behavior second, custom helper code last and thin.

## Non-Goals

1. Do not replace browser automation with desktop GUI smoke.
2. Do not add implementation-detail tests for DOM shape, internal helper names, or exact persistence file formatting.
3. Do not introduce a second testing stack for WASM behavior outside the existing gate model.
4. Do not broaden this work into unrelated settings-center coverage outside the WASM smoke boundary.

## Recommended Approach

Adopt a three-layer WASM smoke model:

1. Static contract layer in `WasmStartupAssetsTests`
2. Headless browser behavior smokes, one behavior family per gate
3. A single runner script that builds the browserwasm artifact and executes the gates against that artifact

This keeps each layer narrow:

- the static layer verifies packaging and gate wiring
- the browser layer verifies observable runtime behavior
- the runner layer verifies we are testing the actual build output

## Alternatives Considered

### Option A: Keep one large smoke script and add more sections

Pros:

- minimal file churn
- lowest short-term rewrite cost

Cons:

- failure localization remains poor
- helper sprawl continues
- unrelated behavior boundaries stay coupled

Verdict: rejected.

### Option B: Split browser smokes by behavior boundary with a thin shared helper

Pros:

- best failure localization
- easiest to maintain
- aligns with behavior-first assertions
- preserves current real-browser validation path

Cons:

- requires moderate restructuring
- requires careful control of helper scope

Verdict: recommended.

### Option C: Move WASM smoke into GUI-oriented test infrastructure

Pros:

- could look uniform with some desktop smoke patterns

Cons:

- wrong abstraction for browserwasm
- adds unnecessary harnessing
- risks testing the wrapper more than the runtime behavior

Verdict: rejected.

## Target Architecture

### 1. Static Contract Layer

`tests/SalmonEgg.Presentation.Core.Tests/WasmStartupAssetsTests.cs` remains the place for static BrowserWasm contract checks.

Responsibilities:

1. verify required BrowserWasm assets and interop scripts are included
2. verify BrowserWasm persistence-related project switches remain enabled where required
3. verify the WASM smoke runner invokes the expected smoke gates

Constraints:

1. no runtime navigation assertions
2. no ordered string-shape assertions for smoke internals
3. only assert presence of gate entrypoints and required asset references

Expected change:

The current monolithic smoke-contract assertion should be replaced with contract checks that the runner invokes three distinct behavior gates.

### 2. Browser Behavior Smoke Layer

The browser behavior layer will be split into the following gate files:

1. `scripts/gates/wasm-settings-persistence-smoke.mjs`
2. `scripts/gates/wasm-capability-boundary-smoke.mjs`
3. `scripts/gates/wasm-acp-full-chain-smoke.mjs`

#### 2.1 Settings Persistence Smoke

Purpose:

Validate that settings written through the BrowserWasm UI survive reload and are projected back through the same user-visible settings surface.

Primary assertions:

1. the app starts successfully
2. the test can navigate to the target settings section through the rendered UI
3. a persisted setting can be changed through the UI
4. after reload, the same setting is visible with the updated value

Allowed diagnostics:

1. read browser-side persistence substrate for debugging
2. dump visible control state when a failure occurs

Not allowed as primary assertions:

1. exact YAML contents
2. exact local file path contents as the main success criterion
3. internal helper call sequence

#### 2.2 Capability Boundary Smoke

Purpose:

Validate that BrowserWasm does not advertise or expose desktop-only file-system affordances through the ACP/runtime capability boundary.

Primary assertions:

1. the app can create and connect an ACP profile in BrowserWasm
2. the observed initialize payload does not advertise forbidden desktop file-system capability
3. browser-restricted file-system actions in settings respect the unsupported-platform boundary

Allowed diagnostics:

1. initialize payload capture
2. visible control state and page text on failure

Not allowed as primary assertions:

1. JS interop function names
2. `FS` global usage pattern
3. implementation-specific branching order inside the page

#### 2.3 ACP Full-Chain Smoke

Purpose:

Validate the end-to-end ACP behavior chain in BrowserWasm using a real browser and a deterministic in-process ACP test server.

Primary assertions:

1. profile creation persists across reload
2. remote directory creation persists across reload
3. starting a session emits the expected `session/new` request semantics
4. sending a prompt emits the expected `session/prompt` request semantics
5. the agent reply is rendered back to the user-visible conversation surface

Allowed diagnostics:

1. captured ACP messages
2. visible page state
3. current body text excerpts

Not allowed as primary assertions:

1. DOM subtree structure
2. internal list item ordering unless it is user-visible behavior
3. helper-specific implementation markers

### 3. Thin Shared Helper Layer

Shared browser helpers are allowed, but must stay thin and behavior-agnostic.

Proposed location:

`scripts/gates/wasm-smoke-lib/`

Allowed helper responsibilities:

1. normalize base URL
2. open app and wait for shell readiness
3. locate and click visible navigation targets
4. read or edit visible controls by accessible/user-facing targeting
5. reload and re-enter a target settings section
6. host the deterministic ACP test server
7. collect failure diagnostics

Disallowed helper responsibilities:

1. embedding the semantics of multiple gates into one generic super-helper
2. enforcing pass/fail policy for unrelated behavior families
3. exposing internals only used to assert implementation details

Design rule:

If a helper name only makes sense for one gate's business meaning, it should live in that gate file, not the shared library.

### 4. Runner Layer

`scripts/gates/run-wasm-smoke-gates.sh` remains the authoritative BrowserWasm smoke entrypoint.

Responsibilities:

1. clean, restore, and build the real `net10.0-browserwasm` artifact
2. serve the built `wwwroot`
3. install Playwright and Chromium in an isolated temp workspace
4. run the three BrowserWasm smoke gates in sequence
5. log artifact path, commit, and base URL for evidence

The runner should not own test semantics beyond orchestration.

## Assertion Policy

This work explicitly separates primary assertions from diagnostics.

### Primary assertions

These determine pass/fail:

1. visible settings values before and after reload
2. visible navigation success
3. ACP protocol requests and responses
4. visible conversation reply
5. absence of forbidden advertised capability

### Diagnostics only

These may be captured on failure, but should not be the main contract:

1. local persistence file contents
2. raw DOM snapshots
3. helper-specific debug markers
4. incidental internal text unrelated to the user-facing behavior being validated

## Test Data and Isolation

Each browser gate should generate unique names for ACP profiles, remote directories, and prompt text. Each run should clear origin storage before starting when isolation requires it.

Isolation requirements:

1. no dependency on previous local browser state
2. no dependency on pre-existing ACP profiles or settings rows
3. deterministic ACP mock server behavior per run

## Failure Reporting

On failure, gates should emit enough evidence to localize the broken boundary without requiring interactive reproduction.

Preferred diagnostics:

1. the gate name and target behavior
2. current base URL
3. current visible body text excerpt
4. targeted control state or navigation candidate debug
5. captured ACP request/response snippets where relevant

## Verification Plan

Implementation will be accepted only if the following pass against the current worktree:

1. focused `WasmStartupAssetsTests`
2. `node --check` for the new WASM smoke scripts
3. `bash scripts/gates/run-wasm-smoke-gates.sh Debug`
4. `git diff --check`

If a behavior moves from one gate to another, the verification target stays the same: the real BrowserWasm artifact and the behavior-observable contract.

## File Plan

Expected file changes:

1. `tests/SalmonEgg.Presentation.Core.Tests/WasmStartupAssetsTests.cs`
2. `scripts/gates/run-wasm-smoke-gates.sh`
3. `scripts/gates/wasm-settings-navigation-smoke.mjs` only if shared navigation logic is extracted cleanly
4. `scripts/gates/wasm-file-system-availability-smoke.mjs` to be retired after its responsibilities are moved into the new split gates
5. new gate files for settings persistence, capability boundary, and ACP full-chain
6. new shared helper files under `scripts/gates/wasm-smoke-lib/`

## Risks

1. Over-extracting helpers could recreate the current monolith in library form.
2. Under-extracting helpers could duplicate brittle selector logic.
3. Some current checks may rely on persistence-file inspection as a shortcut; these must be demoted to diagnostics carefully so coverage stays behavior-complete.

## Acceptance Criteria

1. BrowserWasm smoke is split into separate gates with single responsibilities.
2. The runner executes all BrowserWasm gates against the current built artifact.
3. The static test layer verifies gate wiring without asserting smoke implementation shape.
4. Settings persistence is verified via reload-visible behavior, not persistence-file text as the primary contract.
5. Capability boundary validation remains covered for BrowserWasm.
6. ACP end-to-end session behavior remains covered for BrowserWasm.
7. The final tests and gates prove behavior, not implementation details.
