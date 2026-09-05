// Interaction primitives for the Skia WASM smoke gates.
//
// Skia paints into a <canvas>, so the only DOM Uno publishes is the semantic tree it mirrors from
// the automation peers: nodes under `#uno-semantics-root` with id `uno-semantics-<handle>`,
// `aria-label` from AutomationProperties.Name (localized), `xamlautomationid` from
// AutomationProperties.AutomationId (stable), and `hidden` whenever Uno considers the control not
// visible. Uno wires each node's `click` to the peer's Invoke / Toggle / Selection /
// ExpandCollapse and programs that click without hit testing - so activation needs neither a
// scroll nor a coordinate, and options/menu items whose `pointer-events` is `none` are still
// activatable. Text boxes and sliders are real `<input>` elements on their semantic nodes.
//
// The matching and activation logic itself lives in the page (`window.__salmoneggSmoke.semantic`,
// injected by browser-app.mjs), because Playwright's `page.evaluate` serializes the callback body
// alone - closures over this module do not exist inside the browser. Every helper below is a thin
// node-side driver over that one runtime, and the exported page-side callbacks delegate to it too.
//
// AutomationIds win over labels whenever both are given: the id is stable by contract, the label
// is whatever the resw says today. Labels remain supported for affordances that have no id of
// their own, and because TextBlock text flows into semantic nodes without an accessible name.
//
// The gate build opts into IsUiAutomationMappingEnabled so ids reach the DOM at all; without it
// no semantic node carries `xamlautomationid` and only label matching can work.

const defaultTimeoutMs = 30_000;

const notFoundState = Object.freeze({
  found: false,
  enabled: false,
  text: "",
  aria: "",
  automationId: "",
  x: null,
  y: null
});

// ---- presence and state ----------------------------------------------------------------------

export async function readControlState(page, options) {
  const state = await page.evaluate(input => window.__salmoneggSmoke.semantic.describe(input), options);
  return state ?? notFoundState;
}

export async function waitForControlState(page, options, label, timeoutMs = defaultTimeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastState = notFoundState;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    if (lastState.found) {
      return lastState;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out waiting for ${label}. Options=${JSON.stringify(options)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function countVisibleControls(page, options) {
  return await page.evaluate(input => window.__salmoneggSmoke.semantic.countMatches(input), options);
}

// A control inside a collapsed container is present and not hidden - Uno simply has not laid it out,
// so it reports a placeholder rect a few pixels wide at the viewport origin. "Present in the
// semantic tree" is therefore not the same as "on screen": anything a user has to see, point at, or
// focus has to be checked for a real rect, or the smoke will happily drive a control that is not
// there yet (and a pointer aimed at the placeholder lands on whatever occupies the top-left corner).
const laidOutMinimumSize = 12;

export function isLaidOut(state) {
  return Boolean(state?.found)
    && Boolean(state?.rect)
    && state.rect.width >= laidOutMinimumSize
    && state.rect.height >= laidOutMinimumSize;
}

export async function waitForLaidOutControl(page, options, label, timeoutMs = defaultTimeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastState = notFoundState;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    if (isLaidOut(lastState)) {
      return lastState;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out waiting for ${label} to be laid out on screen. Last state=${JSON.stringify(lastState)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function expectControlEnabledState(page, options, expectedEnabled, label) {
  const state = await waitForControlState(page, options, label);
  if (state.enabled !== expectedEnabled) {
    throw new Error(`Expected ${label} enabled=${expectedEnabled}. State=${JSON.stringify(state)}`);
  }
}

// Polls until the control's enabled state flips to the expected value. Skia renders the app into a
// canvas, so "a dialog opened" is not observable as text or DOM - but modality is observable as
// state: the semantic tree marks the page's controls disabled while the dialog is up and re-enables
// them once it is dismissed. Waiting on that flip is how a smoke asserts dialog round-trips without
// depending on how (or whether) the dialog itself is rendered.
export async function waitForControlEnabledState(page, options, expectedEnabled, label, timeoutMs = defaultTimeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastState = notFoundState;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    if (lastState.found && lastState.enabled === expectedEnabled) {
      return lastState;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out waiting for ${label} to become enabled=${expectedEnabled}. Last state=${JSON.stringify(lastState)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

// Naming note: this used to scroll - activation needed a hit-testable point, so out-of-viewport
// controls had to be dragged into one first, and callers distinguished "found" from "scrolled" by
// return value. Semantic activation has no such requirement, so what remains is waiting for the
// control to exist. The truthy/falsy return (and never throwing) is kept for the callers that
// branch on it.
export async function scrollToVisibleControl(page, options, timeoutMs = defaultTimeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const state = await readControlState(page, options);
    if (state.found) {
      return state;
    }

    await page.waitForTimeout(200);
  }

  return false;
}

export async function scrollToVisibleNavigationTarget(page, options, timeoutMs = defaultTimeoutMs) {
  return await scrollToVisibleControl(page, options, timeoutMs);
}

// ---- activation ------------------------------------------------------------------------------

async function collectSemanticDebug(page) {
  return await page.evaluate(() => window.__salmoneggSmoke.semantic.collectDebug());
}

async function activateWhenReady(page, options, label, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    lastState = await page.evaluate(
      input => window.__salmoneggSmoke.semantic.activate(input),
      options);
    if (lastState?.activated) {
      return lastState.state;
    }

    await page.waitForTimeout(200);
  }

  if (lastState?.matched) {
    throw new Error(
      `Control ${label} is disabled and cannot be activated. State=${JSON.stringify(lastState.state)} `
      + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  throw new Error(
    `Control ${label} did not appear in the semantic DOM. Options=${JSON.stringify(options)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function clickVisibleControl(page, options) {
  return await activateWhenReady(page, options, describeTarget(options), defaultTimeoutMs);
}

export async function clickVisibleNavigationTarget(page, options) {
  return await activateWhenReady(page, options, describeTarget(options), defaultTimeoutMs);
}

// A real Playwright mouse click at the semantic node's center. The two synthetic routes both fail
// here and need different medicine:
// - A raw locator click can never pass Playwright's actionability check: Uno bakes no `role`
//   attribute into semantic elements (the `<button>`/`<input>` tag carries the semantics
//   implicitly, and CSS attribute selectors match the attribute, not the ARIA role), and their
//   `pointer-events` is `none` so the browser's hit test hands the pointer to the canvas below.
// - `element.click()` on the node fires the peer's Invoke callback but does not always reach the
//   XAML command (the hero cards, the Gamepad expander, and the gamepad refresh button all
//   documented that gap).
// Clicking the node's reported center is the user's actual gesture path: a trusted pointer goes
// through Uno's canvas hit testing and raises the same pointer events a human click would.
export async function clickVisibleControlWithTrustedPointer(page, options, label) {
  const state = await waitForControlState(page, options, label);
  if (!state.enabled) {
    throw new Error(
      `Control ${label} is disabled and cannot receive a pointer click. State=${JSON.stringify(state)} `
      + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  // A control Uno has not laid out yet reports a placeholder rect - a few pixels at the viewport's
  // top-left - which is what a control inside a collapsed container looks like. Its centre is a
  // perfectly clickable point belonging to whatever really sits there (the title bar's back button,
  // in the case that surfaced this), so clicking it navigates away and the failure appears wherever
  // the caller next looks. Refuse it and name it.
  if (!isLaidOut(state)) {
    throw new Error(
      `Control ${label} reports an unlaid-out rect (${state.rect.width}x${state.rect.height} at `
      + `${state.rect.left},${state.rect.top}), so it is not on screen for a pointer - it is most `
      + `likely inside a collapsed container that has to be opened first. State=${JSON.stringify(state)}`);
  }

  // `getBoundingClientRect` and `page.mouse.click` share the viewport coordinate space, but a
  // control Uno keeps laid out off-screen reports a center outside it; clicking there would miss
  // the canvas. Surface that instead of silently no-oping.
  const viewport = page.viewportSize();
  if (state.x == null || state.y == null
    || state.x < 0 || state.y < 0
    || state.x > viewport.width || state.y > viewport.height) {
    throw new Error(
      `Control ${label} center (${state.x}, ${state.y}) is outside the `
      + `${viewport.width}x${viewport.height} viewport, so a pointer click would miss it. `
      + `State=${JSON.stringify(state)} Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  // A control that has just appeared can still be mid-entrance-animation, so the center read
  // above describes where it was, not where the click lands. Wait for the reported center to
  // stop moving, then give Uno's canvas hit test one more beat to catch up with the layout it
  // just reported - both stillness and a settled hit test are part of what "clickable" means
  // for a real pointer.
  const deadline = Date.now() + defaultTimeoutMs;
  let settledState = state;
  let stableReads = 0;
  while (stableReads < 3 && Date.now() < deadline) {
    await page.waitForTimeout(200);
    const nextState = await readControlState(page, options);
    if (!nextState.found || !nextState.enabled) {
      throw new Error(
        `Control ${label} disappeared or became disabled while waiting for its layout to settle. `
        + `Last state=${JSON.stringify(nextState)} `
        + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
    }

    stableReads = (nextState.x === settledState.x && nextState.y === settledState.y)
      ? stableReads + 1
      : 0;
    settledState = nextState;
  }

  if (stableReads < 3) {
    throw new Error(
      `Control ${label} center never settled (first seen (${state.x}, ${state.y}), `
      + `last seen (${settledState.x}, ${settledState.y})), so a pointer click could not be `
      + `aimed reliably. Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  await page.waitForTimeout(500);
  await page.mouse.click(settledState.x, settledState.y);
  return settledState;
}

export async function clickVisibleNavigationTargetUntilBodyText(page, options, pattern, label) {
  await clickVisibleNavigationTarget(page, options);
  await waitForBodyText(page, pattern, label);
}

// Openers are for targets that only exist once something is toggled open (e.g. the sidebar behind
// TitleBar.ToggleSidebar). Try the target first - most navigation targets are always present - and
// only spend effort on the opener when it is not.
export async function ensureVisibleNavigationTarget(page, targetOptions, openerOptions) {
  if (await scrollToVisibleControl(page, targetOptions, 3_000)) {
    return;
  }

  await activateWhenReady(page, openerOptions, describeTarget(openerOptions), defaultTimeoutMs);
  await waitForControlState(page, targetOptions, describeTarget(targetOptions), 30_000);
}

function describeTarget(options) {
  const ids = options.automationIds ?? [];
  const labels = options.labels ?? [];
  return ids.length > 0 ? `automation id '${ids.join("', '")}'` : `label '${labels.join("', '")}'`;
}

// ---- collapsed sections ----------------------------------------------------------------------

// Open a collapsed Expander and wait until what it holds is genuinely on screen.
//
// Two things make this its own helper. The section's contents are in the semantic tree even while the
// Expander is shut - unhidden, but reporting a placeholder rect at the viewport origin - so "the
// control exists" is not a usable signal for "the section is open"; the exit condition has to be that
// the control is laid out. And the thing to click is the Expander's own header button (a real button
// carrying aria-expanded and a real rect), not the header content inside it: that inner node comes
// through as a role=group with a 0x0 rect, which cannot be activated and cannot be pointed at.
//
// A trusted pointer does the toggling, since BrowserWasm does not reliably expand an Expander from
// synthetic activation. Clicking is skipped whenever the header already reports itself expanded, so a
// slow layout can never be "fixed" by a second click that closes the section again.
export async function revealCollapsedSection(page, toggleTargets, revealedControl, label, attempts = 4) {
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    if (isLaidOut(await readControlState(page, revealedControl))) {
      return;
    }

    const toggleState = await readControlState(page, toggleTargets);
    if (toggleState.expanded !== true) {
      await clickVisibleControlWithTrustedPointer(page, toggleTargets, `${label} expander header`);
    }

    const deadline = Date.now() + 5_000;
    while (Date.now() < deadline) {
      if (isLaidOut(await readControlState(page, revealedControl))) {
        return;
      }

      await page.waitForTimeout(200);
    }
  }

  throw new Error(
    `Could not open the collapsed section for ${label} in ${attempts} attempts. `
    + `Header=${JSON.stringify(await readControlState(page, toggleTargets))} `
    + `Contents=${JSON.stringify(await readControlState(page, revealedControl))}`);
}

// ---- text entry ------------------------------------------------------------------------------

// Text goes in as real keystrokes. Uno mirrors a TextBox as a real <input>, so assigning `.value`
// and dispatching an `input` event looks like it works - and for most fields it does - but the
// managed side does not always pick it up: the ACP profile editor's Server URL sits inside a
// conditionally visible container and keeps the ViewModel's old (empty) value while the DOM shows
// the new one. The app then saves an empty field, the validation message blames the field, and the
// smoke blames the save. Typing is the user's own path and lands in both cases.
export async function typeIntoAutomationTextBox(page, automationId, value) {
  const options = { automationIds: [automationId], labels: [] };
  return await setSemanticInputValue(page, options, value, `text box '${automationId}'`, defaultTimeoutMs);
}

export async function typeIntoVisibleTextField(page, options, value, label, timeoutMs = defaultTimeoutMs) {
  return await setSemanticInputValue(page, options, value, label, timeoutMs);
}

async function setSemanticInputValue(page, options, value, label, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let resolved = null;
  while (Date.now() < deadline) {
    resolved = await page.evaluate(
      input => window.__salmoneggSmoke.semantic.resolveEditableField(input),
      options);
    if (resolved?.id && !resolved.disabled) {
      break;
    }

    await page.waitForTimeout(200);
  }

  if (resolved?.disabled) {
    throw new Error(
      `${label} is disabled. State=${JSON.stringify(resolved.state)} `
      + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  if (!resolved?.id) {
    throw new Error(
      `No editable field found for ${label}. Options=${JSON.stringify(options)} `
      + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  const field = page.locator(`#${resolved.id}`);
  await field.focus();
  const focusedId = await page.evaluate(() => document.activeElement?.id ?? null);
  if (focusedId !== resolved.id) {
    throw new Error(
      `${label} did not take focus before typing (focus went to ${JSON.stringify(focusedId)}).`);
  }

  await page.keyboard.press("Control+a");
  await page.keyboard.type(String(value), { delay: 25 });
  // Commit with Tab. Some fields update their binding per keystroke, but not all: the ACP profile
  // editor's Server URL only pushes its value to the ViewModel when the field is left, so without
  // this the app saves an empty URL while the DOM shows the typed one. It has to be the key, not a
  // DOM blur() - measured, blur() alone does not commit, because the managed side is driven by Uno's
  // keyboard pipeline rather than by DOM focus events.
  await page.keyboard.press("Tab");

  const observed = await field.inputValue().catch(() => null);
  if (observed !== String(value)) {
    throw new Error(`Typing into ${label} did not land. Expected ${JSON.stringify(String(value))}, observed ${JSON.stringify(observed)}.`);
  }

  return resolved.state;
}

// ---- numeric fields --------------------------------------------------------------------------

// NumberBox is the one control whose Skia semantics export splits in two: the spinbutton node
// carries the automation id but only the two spin buttons as children, while the editable value
// lives in a separate real `<input>` carrying Uno's template part id ("InputBox") directly under
// the application root. That input is the control's actual text field - `.value` is live, focus
// lands on it, and real keyboard events drive the TwoWay binding (synthetic input events do not
// commit). The selector is only unambiguous while exactly one NumberBox is mounted, so every
// helper anchors on the spinbutton first and fails loudly if more than one editor exists.
const numericEditorSelector = '#uno-semantics-root input[xamlautomationid="InputBox"]';

const readNumericEditor = page => page.evaluate(selector => {
  const editors = Array.from(document.querySelectorAll(selector));
  const editor = editors[0] ?? null;
  const active = document.activeElement;
  return {
    count: editors.length,
    id: editor?.id ?? null,
    value: editor?.value ?? null,
    focused: Boolean(editor) && active === editor,
    activeId: active?.id ?? null,
    activeAutomationId: active?.getAttribute?.("xamlautomationid") ?? null
  };
}, numericEditorSelector);

async function waitForNumericEditor(page, controlOptions, label) {
  // The anchor proves the right page is mounted, so a stale editor from a previously visited page
  // can never be edited by mistake.
  await waitForControlState(page, controlOptions, `number box anchor for ${label}`);

  const deadline = Date.now() + defaultTimeoutMs;
  let editor = null;
  while (Date.now() < deadline) {
    editor = await readNumericEditor(page);
    if (editor.count === 1) {
      return editor;
    }

    if (editor.count > 1) {
      throw new Error(
        `Found ${editor.count} number box editors while resolving ${label}; `
        + `the editor selector requires a single mounted NumberBox.`);
    }

    await page.waitForTimeout(200);
  }

  throw new Error(`No number box editor input found for ${label}. Last read=${JSON.stringify(editor)}`);
}

// Focus, then confirm the editor still holds focus. Navigating to a settings section hands focus to
// the NavigationView's selected item asynchronously, a beat after the navigation itself is
// observable (measured ~800ms later), so an editor focused before that hand-off loses focus to it
// and every keystroke afterwards is dropped in silence - the input keeps its old value and nothing
// reports a problem. Rather than guess how long that beat lasts, focus and check the editor kept
// it; if something took it, focus again, since the hand-off happens once per navigation.
async function focusNumericEditor(page, controlOptions, label) {
  const attempts = [];
  for (let attempt = 0; attempt < 5; attempt += 1) {
    const editor = await waitForNumericEditor(page, controlOptions, label);
    await page.locator(numericEditorSelector).first().focus();
    await page.waitForTimeout(300);
    const held = await readNumericEditor(page);
    if (held.focused && held.id === editor.id) {
      return held;
    }

    attempts.push(held);
  }

  throw new Error(
    `The number box editor for ${label} kept losing focus. Attempts=${JSON.stringify(attempts)}`);
}

function tryParseInteger(value) {
  const match = String(value ?? "").match(/-?\d+/);
  return match ? Number.parseInt(match[0], 10) : null;
}

export async function readNumericControlValue(page, controlOptions, label) {
  const editor = await waitForNumericEditor(page, controlOptions, label);
  const deadline = Date.now() + defaultTimeoutMs;
  let lastValue = null;

  while (Date.now() < deadline) {
    lastValue = await editor.inputValue().catch(() => null);
    const parsedValue = tryParseInteger(lastValue);
    if (parsedValue != null) {
      return parsedValue;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out reading a numeric value from ${label}. Last editor value=${JSON.stringify(lastValue)}`);
}

export async function focusNumericControl(page, controlOptions, label) {
  const editor = await waitForNumericEditor(page, controlOptions, label);
  // Bring the real editor row into view first: the focused-state contrast check below only sees
  // visible inputs, and the spinbutton node's rect is a virtual layout coordinate that cannot be
  // used for that.
  await editor.scrollIntoViewIfNeeded();
  await editor.focus();
  const focused = await page.evaluate(
    () => document.activeElement?.getAttribute?.("xamlautomationid") === "InputBox");
  if (!focused) {
    throw new Error(`The number box editor did not take focus for ${label}.`);
  }
}

export async function setNumericControlValue(page, controlOptions, value, label) {
  const editor = await waitForNumericEditor(page, controlOptions, label);
  // A user path, not a synthetic one: focus, select all, type, blur. The TwoWay binding commits
  // on blur - Enter steals focus without committing, and synthetic input events are ignored.
  await editor.focus();
  await page.keyboard.press("Control+a");
  await page.keyboard.type(String(value), { delay: 40 });
  await page.keyboard.press("Tab");

  const deadline = Date.now() + 5_000;
  let observedValue = null;
  while (Date.now() < deadline) {
    observedValue = await readNumericControlValue(page, controlOptions, `${label} after edit`);
    if (observedValue === value) {
      return;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(
    `Failed to set ${label}. Expected ${value}, observed ${observedValue}.`);
}

export function selectAlternateCacheRetentionValue(currentValue) {
  // Only contract: return a value different from the current one, whatever it is.
  if (!Number.isFinite(currentValue)) {
    return 7;
  }

  if (currentValue >= 60) {
    return 59;
  }

  if (currentValue <= 1) {
    return 2;
  }

  return currentValue + 1;
}

// ---- toggles ---------------------------------------------------------------------------------

export async function readToggleSwitchValue(page, options, label, timeoutMs = defaultTimeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    if (lastState.found && typeof lastState.checked === "boolean") {
      return lastState.checked;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out reading toggle ${label}. Last state=${JSON.stringify(lastState)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function setToggleSwitchValue(page, options, expectedValue, label) {
  const state = await waitForControlState(page, options, label, 10_000);
  if (!state.enabled) {
    throw new Error(`Toggle ${label} is disabled and cannot be toggled. State=${JSON.stringify(state)}`);
  }

  if (state.checked !== expectedValue) {
    await activateWhenReady(page, options, label, 10_000);
  }

  const deadline = Date.now() + 10_000;
  let lastChecked = state.checked;
  while (Date.now() < deadline) {
    lastChecked = (await readControlState(page, options)).checked;
    if (lastChecked === expectedValue) {
      return;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Toggle ${label} did not change to ${expectedValue}. Last checked=${JSON.stringify(lastChecked)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function expectToggleSwitchValue(page, options, expectedValue, label) {
  const actualValue = await readToggleSwitchValue(page, options, label);
  if (actualValue !== expectedValue) {
    throw new Error(`Expected toggle ${label} to be ${expectedValue}, got ${actualValue}.`);
  }
}

// ---- combo boxes -----------------------------------------------------------------------------

// Skia renders the app into a canvas and collapsed combo boxes mirror no selection text, so the
// selection is only observable by opening the dropdown and reading which option is highlighted
// (aria-activedescendant), mapped onto the clean item labels. Both helpers below share the
// keyboard path because it is the only one that actually commits a selection: a semantic click
// on an item merely collapses the dropdown.
//
// F4 is also racy on Skia: focus needs a settle beat before the key opens the popup, and the
// popup has a half-open ghost state (expanded=true, no option nodes yet). So opening loops:
// focus -> settle -> F4 -> poll for an aligned open state (option count matching the fresh
// labeled nodes) -> Escape and retry while budget remains.

const COMBO_SETTLE_MS = 300;
const COMBO_OPEN_TIMEOUT_MS = 20_000;

const findComboBox = (page, selectorAutomationId) => page.evaluate(
  automationId => window.__salmoneggSmoke.semantic.describe({
    automationIds: [automationId], labels: []
  }),
  selectorAutomationId);

async function openComboBoxAligned(page, selectorAutomationId, label) {
  await waitForControlState(page, { automationIds: [selectorAutomationId], labels: [] }, label);
  const deadline = Date.now() + COMBO_OPEN_TIMEOUT_MS;
  let beforeIds = null;
  let attempt = 0;

  while (Date.now() < deadline) {
    beforeIds = await page.evaluate(() => window.__salmoneggSmoke.semantic.comboBoxLabeledIds());
    attempt += 1;

    await waitForControlState(
      page,
      { automationIds: [selectorAutomationId], labels: [] },
      label,
      5_000);
    await page.evaluate(
      automationId => window.__salmoneggSmoke.semantic.focusControl({
        automationIds: [automationId], labels: []
      }),
      selectorAutomationId);
    await page.waitForTimeout(COMBO_SETTLE_MS);
    await page.keyboard.press("F4");

    const openDeadline = Date.now() + 2_000;
    while (Date.now() < openDeadline && Date.now() < deadline) {
      const state = await page.evaluate(
        input => window.__salmoneggSmoke.semantic.comboBoxOpenState(input.automationId, input.beforeIds),
        { automationId: selectorAutomationId, beforeIds });
      if (state?.aligned) {
        return state;
      }

      await page.waitForTimeout(200);
    }

    await page.keyboard.press("Escape");
    await page.waitForTimeout(200);
  }

  throw new Error(
    `${label} never produced an aligned open state after ${attempt} attempts. `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

async function closeComboBox(page) {
  await page.keyboard.press("Escape");
  await page.waitForTimeout(200);
}

// Opens the dropdown (retrying through the F4 race), reads the highlighted option's label, then
// closes it. The dropdown must end closed: a lingering popup would swallow the next gate's keys.
async function readComboBoxSelectionLabel(page, selectorAutomationId) {
  // Wait for the combo rather than demanding it be there already: a settings page publishes its
  // controls as it lays out, so a check made the instant navigation reports success can run before
  // this one exists. It used to be a bare read, which turned a page still coming up into "the combo
  // box does not exist".
  await waitForControlState(
    page,
    { automationIds: [selectorAutomationId], labels: [] },
    `combo box '${selectorAutomationId}'`);

  const state = await openComboBoxAligned(page, selectorAutomationId, `combo box '${selectorAutomationId}'`);
  const activeIndex = state.activeIndex;
  if (activeIndex < 0 || activeIndex >= state.itemLabels.length) {
    await closeComboBox(page);
    throw new Error(
      `combo box '${selectorAutomationId}' open state has no highlighted option. `
      + `State=${JSON.stringify(state)}`);
  }

  const selected = state.itemLabels[activeIndex];
  await closeComboBox(page);
  return selected;
}

// Keyboard-only selection: open (aligned), Home to reset to the first option, ArrowDown to the
// target, Enter to commit. Skia's item click only collapses the popup, so keys are the only
// commit path. The target name must match an item label exactly (case-insensitive) - a miss is
// an error, never a silent no-op.
export async function selectComboBoxItem(page, selectorAutomationId, expectedVisibleName, options = {}) {
  const expectedNames = Array.isArray(expectedVisibleName)
    ? expectedVisibleName
    : [expectedVisibleName];
  const label = `combo box '${selectorAutomationId}'`;

  const state = await openComboBoxAligned(page, selectorAutomationId, label);
  const targetIndex = state.itemLabels.findIndex(
    item => expectedNames.some(name => name.toLowerCase() === item.toLowerCase()));
  if (targetIndex < 0) {
    await closeComboBox(page);
    throw new Error(
      `${label} open state exposed ${JSON.stringify(state.itemLabels)}; `
      + `none matched ${JSON.stringify(expectedNames)}.`);
  }

  await page.keyboard.press("Home");
  await page.waitForTimeout(COMBO_SETTLE_MS);
  for (let i = 0; i < targetIndex; i += 1) {
    await page.keyboard.press("ArrowDown");
    await page.waitForTimeout(100);
  }
  await page.keyboard.press("Enter");
  await closeComboBox(page);

  if (options.verifySelectionText !== false) {
    const observed = await readComboBoxSelectionLabel(page, selectorAutomationId);
    if (!expectedNames.some(name => name.toLowerCase() === observed.toLowerCase())) {
      throw new Error(
        `${label} selection read back as ${JSON.stringify(observed)}, expected ${JSON.stringify(expectedNames)}.`);
    }
  }
}

// Reads the selection by reopening the dropdown and checking the highlighted option's label.
// Despite the legacy name, no collapsed-state text is read - Skia mirrors none.
export async function expectComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, label) {
  const expectedNames = Array.isArray(expectedVisibleNames)
    ? expectedVisibleNames
    : [expectedVisibleNames];
  const deadline = Date.now() + defaultTimeoutMs;
  let observed = null;
  let lastError = null;

  while (Date.now() < deadline) {
    try {
      observed = await readComboBoxSelectionLabel(page, selectorAutomationId);
      if (expectedNames.some(name => name.toLowerCase() === observed.toLowerCase())) {
        return;
      }
    } catch (error) {
      lastError = error;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `${label ?? `combo box '${selectorAutomationId}'`} selection never read back as `
    + `${JSON.stringify(expectedNames)}. Last observed=${JSON.stringify(observed)} `
    + `Last error=${String(lastError)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function readComboBoxSelectionText(page, selectorAutomationId) {
  return await readComboBoxSelectionLabel(page, selectorAutomationId);
}

// ---- focus -----------------------------------------------------------------------------------

export async function focusVisibleControl(page, options, label) {
  await activateWhenReady(page, options, label, defaultTimeoutMs);
  return await waitForFocusedElementSnapshot(page, `${label} focus`);
}

export async function waitForFocusedElementSnapshot(page, label, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  let snapshot = null;

  while (Date.now() < deadline) {
    snapshot = await page.evaluate(() => window.__salmoneggSmoke.semantic.focusedSnapshot());
    if (snapshot?.visible && !snapshot?.isBody) {
      return snapshot;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out waiting for ${label}. Snapshot=${JSON.stringify(snapshot)}`);
}

// ---- outbound-click safety -------------------------------------------------------------------

export async function expectControlDoesNotEscapePage(page, options, stayOnPagePattern) {
  const beforeUrl = page.url();
  const state = await readControlState(page, options);
  if (!state.found || !state.enabled) {
    return;
  }

  await clickVisibleControl(page, options);
  try {
    await waitForBodyText(
      page,
      /当前平台暂不支持打开本地文件或目录|Opening local files or folders is not supported on this platform/,
      "unsupported platform dialog",
      2_000);
    await dismissDialogIfPresent(page);
    return;
  } catch {
  }

  if (page.url() !== beforeUrl) {
    throw new Error(`Expected control ${JSON.stringify(options)} to stay on the current page, but url changed to ${page.url()}.`);
  }

  await waitForBodyText(page, stayOnPagePattern, "data storage page after external open attempt", 5_000);
}

async function dismissDialogIfPresent(page) {
  // The app's confirmation dialogs ship 确定/OK as their only button; activating a control that is
  // not there is a no-op after the timeout, so no presence probe is needed first. The activate path
  // (semantic invoke) needs no coordinates, which matters because Skia-rendered dialogs report
  // garbage rects for their buttons - only their presence in the semantic tree is trustworthy.
  try {
    await activateWhenReady(
      page,
      { labels: ["确定", "OK"], automationIds: [] },
      "dialog confirm button",
      3_000);
    await page.waitForTimeout(300);
  } catch {
  }
}

// ---- misc ------------------------------------------------------------------------------------

// ChatInputArea.Send is the button's AutomationProperties.AutomationId; before the id existed this
// helper guessed "rightmost enabled button in the bottom quarter", which breaks on any layout change.
export async function clickStartComposerSendButton(page) {
  await activateWhenReady(
    page,
    { automationIds: ["ChatInputArea.Send"], labels: [] },
    "start composer send button",
    defaultTimeoutMs);
}

// The semantic tree's equivalent of a body-text wait. Skia mirrors much of what a user reads only as
// an accessible name - list item titles, field labels, button captions on templated controls - so
// text that is plainly on screen can be absent from every node's textContent. Matching against both
// is what makes "the user can see this text" checkable on this renderer.
export async function waitForSemanticText(page, pattern, label, timeoutMs = defaultTimeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastSample = [];
  while (Date.now() < deadline) {
    lastSample = await page.evaluate(source => {
      const regex = new RegExp(source);
      return Array.from(document.querySelectorAll("#uno-semantics-root [id^='uno-semantics-']"))
        .filter(node => !node.hidden)
        .map(node => `${node.getAttribute("aria-label") ?? ""}|${(node.textContent ?? "").trim()}`)
        .filter(text => regex.test(text))
        .slice(0, 5);
    }, pattern.source);
    if (lastSample.length > 0) {
      return;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out waiting for ${label} in the semantic tree. Pattern=${pattern} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  // `waitForFunction` confirms the pattern is in body text, then a *separate*
  // `locator().innerText()` re-reads. Those two reads straddle the LoadAsync
  // Clear-then-refill window (ObservableCollection Reset momentarily empties
  // the body), so the re-read can come back empty despite the poll just
  // succeeding. Retry the whole poll+read cycle until the re-read is stable.
  const deadline = Date.now() + timeoutMs;
  let lastBodyText = "";
  while (Date.now() < deadline) {
    try {
      await page.waitForFunction(
        source => new RegExp(source).test(document.body?.innerText ?? ""),
        pattern.source,
        { timeout: Math.min(5_000, Math.max(250, deadline - Date.now())) });
    } catch (error) {
      // The poll timing out is not fatal - the outer deadline owns that verdict. Without this
      // catch a 5s poll timeout escapes the loop entirely and reports a raw Playwright
      // TimeoutError before the caller's 30s ever elapsed, hiding the collected body text.
      if (Date.now() >= deadline) {
        break;
      }
    }

    lastBodyText = await page.locator("body").innerText();
    if (pattern.test(lastBodyText)) {
      return;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Expected ${label} text was not visible. Last body: ${lastBodyText.slice(0, 500)}`);
}

export async function readAppSettingsPersistenceDebug(page, input) {
  return await page.evaluate(
    debugInput => window.__salmoneggSmoke.semantic.persistenceDebug(debugInput),
    input);
}

export async function readLocalTextFile(page, path) {
  return await page.evaluate(
    filePath => window.__salmoneggSmoke.semantic.readLocalTextFile(filePath),
    path);
}

// ---- page-side callbacks (self-contained by contract; passed straight to page.evaluate) -------

// The semantic DOM is the interactive set; these collectors are what the ACP fixture dumps on
// failure. Filtering by role keeps each dump readable without hiding the rest behind a second call.
export function collectVisibleNavigationTargetDebug() {
  return window.__salmoneggSmoke.semantic.collectDebug();
}

export function collectVisibleInteractiveDebug() {
  return window.__salmoneggSmoke.semantic.collectDebug();
}

export function collectVisibleComboBoxDebug() {
  return window.__salmoneggSmoke.semantic
    .collectDebug()
    .filter(node => ["combobox", "option", "listbox"].includes(node.role));
}

export function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
