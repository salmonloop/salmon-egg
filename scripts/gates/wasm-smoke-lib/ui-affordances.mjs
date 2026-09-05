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

export async function expectControlEnabledState(page, options, expectedEnabled, label) {
  const state = await waitForControlState(page, options, label);
  if (state.enabled !== expectedEnabled) {
    throw new Error(`Expected ${label} enabled=${expectedEnabled}. State=${JSON.stringify(state)}`);
  }
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

// ---- text entry ------------------------------------------------------------------------------

// The semantic `<input>` forwards `input` events to the managed text box (OnTextInput), and Uno's
// key handling suppresses native insertion for canvas input - assigning the value and dispatching
// the event is the supported path, not a workaround.
export async function typeIntoAutomationTextBox(page, automationId, value) {
  const options = { automationIds: [automationId], labels: [] };
  return await setSemanticInputValue(page, options, value, `text box '${automationId}'`, defaultTimeoutMs);
}

export async function typeIntoVisibleTextField(page, options, value, label, timeoutMs = defaultTimeoutMs) {
  return await setSemanticInputValue(page, options, value, label, timeoutMs);
}

async function setSemanticInputValue(page, options, value, label, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastResult = null;

  while (Date.now() < deadline) {
    lastResult = await page.evaluate(
      input => window.__salmoneggSmoke.semantic.setInput(input.options, input.value),
      { options, value });
    if (lastResult?.editable && !lastResult?.disabled) {
      return lastResult.state;
    }

    await page.waitForTimeout(200);
  }

  if (lastResult?.disabled) {
    throw new Error(
      `${label} is disabled. State=${JSON.stringify(lastResult.state)} `
      + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
  }

  throw new Error(
    `No editable field found for ${label}. Options=${JSON.stringify(options)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

// ---- numeric fields --------------------------------------------------------------------------

function tryParseInteger(value) {
  const match = String(value ?? "").match(/-?\d+/);
  return match ? Number.parseInt(match[0], 10) : null;
}

export async function readNumericControlValue(page, options, label) {
  const deadline = Date.now() + defaultTimeoutMs;
  let lastState = notFoundState;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    const parsedValue = tryParseInteger(lastState.value ?? lastState.text);
    if (parsedValue != null) {
      return parsedValue;
    }

    await page.waitForTimeout(200);
  }

  throw new Error(
    `Timed out reading a numeric value from ${label}. Last state=${JSON.stringify(lastState)} `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
}

export async function setNumericControlValue(page, options, value, label) {
  // The field's value path is the same input event as a text box; blur commits the edit, which the
  // old keyboard path did by pressing Tab.
  await setSemanticInputValue(page, options, String(value), label, 10_000);

  const deadline = Date.now() + 5_000;
  let observedValue = null;
  while (Date.now() < deadline) {
    observedValue = await readNumericControlValue(page, options, `${label} after edit`);
    if (observedValue === value) {
      return;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(
    `Failed to set ${label}. Expected ${value}, observed ${observedValue}. `
    + `Semantic DOM=${JSON.stringify(await collectSemanticDebug(page))}`);
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
  const combo = await findComboBox(page, selectorAutomationId);
  if (!combo?.found) {
    throw new Error(`combo box '${selectorAutomationId}' was not found in the semantic DOM.`);
  }

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
  // not there is a no-op after the timeout, so no presence probe is needed first.
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

export async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  // `waitForFunction` confirms the pattern is in body text, then a *separate*
  // `locator().innerText()` re-reads. Those two reads straddle the LoadAsync
  // Clear-then-refill window (ObservableCollection Reset momentarily empties
  // the body), so the re-read can come back empty despite the poll just
  // succeeding. Retry the whole poll+read cycle until the re-read is stable.
  const deadline = Date.now() + timeoutMs;
  let lastBodyText = "";
  while (Date.now() < deadline) {
    await page.waitForFunction(
      source => new RegExp(source).test(document.body?.innerText ?? ""),
      pattern.source,
      { timeout: Math.min(5_000, Math.max(250, deadline - Date.now())) });

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
