import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import {
  clickVisibleControl,
  clickVisibleControlWithTrustedPointer,
  waitForControlEnabledState,
  waitForLaidOutControl,
  waitForNativeControlFocus,
  waitForSemanticText
} from "./ui-affordances.mjs";

export async function verifySessionConfiguration(page, server, initialize) {
  assert.deepEqual(initialize.params.clientCapabilities?.session?.configOptions?.boolean, {},
    "The real initialize must advertise boolean editing before the agent sends a boolean setting.");
  const selector = { labels: ["Response style"], role: "combobox" };
  const settings = { automationIds: ["ChatView.SessionSettings"], role: "button" };
  const grouped = {
    id: "response-style", name: "Response style", description: "Choose the detail used in this conversation.",
    type: "select", currentValue: "short",
    options: [{ group: "standard", name: "Standard", options: [{ value: "short", name: "Brief" }] },
      { group: "deep", name: "Deep work", options: [{ value: "long", name: "Detailed" }] }]
  };
  const flag = { id: "show-plan", name: "Show the plan", description: "Include the current plan.",
    type: "boolean", currentValue: false };
  server.publishConfiguration([grouped, flag,
    { id: "future-setting", name: "Future knob", type: "future", currentValue: { opaque: true } }]);
  const entry = await waitForLaidOutControl(page, settings, "session settings icon");
  assert.ok(entry.rect.width <= 24 && entry.rect.height <= 24,
    "The settings entry must stay a compact inline icon.");
  const entryName = await page.locator(`#${entry.id}`).getAttribute("aria-label");
  assert.equal(entryName, "Session settings", "The icon needs a readable accessible name.");
  await clickVisibleControlWithTrustedPointer(page, settings, "session settings");
  await waitForLaidOutControl(page, { automationIds: ["ChatView.SessionSettingsDialog"] }, "native session settings dialog");
  await waitForSessionSettingsFocus(page);
  await waitForSemanticText(page, /Choose the detail used in this conversation\./, "configuration description");
  await waitForLaidOutControl(page, selector, "response style selector");
  if (process.env.WASM_SMOKE_ARTIFACTS_DIR) {
    await mkdir(process.env.WASM_SMOKE_ARTIFACTS_DIR, { recursive: true });
    await page.screenshot({ path: `${process.env.WASM_SMOKE_ARTIFACTS_DIR}/session-settings.png` });
  }
  await verifyCompactSessionSettings(page, server, settings, [grouped, flag]);
  await focusWithKeyboard(page, selector, "configuration selector");
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("Tab");
  const apply = { labels: ["Apply"], role: "button" };
  const initial = server.configurationRequests().length;
  server.failNextConfiguration("Backend unavailable");
  await waitForControlEnabledState(page, apply, true, "apply edited setting");
  await clickVisibleControl(page, apply, "apply response style");
  await waitForSemanticText(page, /Could not apply this setting\./, "configuration failure");
  if (process.env.WASM_SMOKE_ARTIFACTS_DIR)
    await page.screenshot({ path: `${process.env.WASM_SMOKE_ARTIFACTS_DIR}/session-settings-error.png` });
  await waitForControlEnabledState(page, apply, true, "retry edited setting");
  await clickVisibleControl(page, apply, "retry response style");
  await waitForConfigurationRequests(server, initial + 2);
  await waitForControlEnabledState(page, selector, true, "response style editor after retry");
  await waitForControlEnabledState(page, apply, false, "accepted response style");
  assert.equal(server.configurationRequests().length, initial + 2);
  for (const request of server.configurationRequests().slice(initial)) {
    assert.equal(request.params.configId, "response-style");
    assert.equal(request.params.value, "long");
    assert.equal(request.params.type, undefined, "V1 string values retain the stable wire format.");
    assert.equal(request.params.sessionId, server.sessionId);
  }
  await closeSessionSettings(page, settings, "Escape");
  server.publishConfiguration([flag]);
  await openSessionSettingsWithKeyboard(page, settings, "boolean session settings");
  const toggle = { labels: ["Show the plan"], role: "switch" };
  await waitForLaidOutControl(page, toggle, "plan switch");
  await clickVisibleControl(page, toggle);
  await waitForControlEnabledState(page, apply, true, "apply boolean setting");
  const beforeBoolean = server.configurationRequests().length;
  await clickVisibleControl(page, apply, "apply boolean setting");
  await waitForConfigurationRequests(server, beforeBoolean + 1);
  await waitForControlEnabledState(page, toggle, true, "plan editor after apply");
  await waitForControlEnabledState(page, apply, false, "accepted boolean setting");
  const boolean = server.configurationRequests().at(-1);
  assert.equal(boolean.params.configId, "show-plan");
  assert.equal(boolean.params.type, "boolean");
  assert.equal(boolean.params.value, true);
  await closeSessionSettings(page, settings, "Close");
  console.log("WASM session configuration passed: icon entry, native modal, per-setting apply, retry, boolean and focus return");
}

async function verifyCompactSessionSettings(page, server, settings, originalOptions) {
  const artifacts = process.env.WASM_SMOKE_ARTIFACTS_DIR;
  const originalViewport = page.viewportSize();
  const originalRequests = server.configurationRequests().length;
  await closeSessionSettings(page, settings, "Escape");
  if (artifacts) await page.screenshot({ path: `${artifacts}/session-settings-icon.png` });
  await page.setViewportSize({ width: 640, height: 800 });
  const title = { automationIds: ["ChatView.CurrentSessionTitle"] };
  await waitForLaidOutControl(page, title, "narrow session title");
  const entry = await waitForLaidOutControl(page, settings, "narrow session settings icon");
  const titleState = await waitForLaidOutControl(page, title, "narrow session title alignment");
  assert.ok(Math.abs(entry.y - titleState.y) <= 3, "The settings icon must stay on the title row when Agent details wrap.");
  await page.waitForFunction(() => {
    const semantic = window.__salmoneggSmoke.semantic;
    const title = semantic.describe({ automationIds: ["ChatView.CurrentSessionTitle"] });
    const agent = semantic.describe({ automationIds: ["ChatView.CurrentAgentDisplay"] });
    return agent && title && agent.rect.top >= title.rect.top + title.rect.height;
  });
  if (artifacts) await page.screenshot({ path: `${artifacts}/session-settings-icon-narrow.png` });
  await openSessionSettingsWithKeyboard(page, settings, "narrow session settings icon");
  const close = await waitForLaidOutControl(page, { labels: ["Close"], role: "button" }, "narrow dialog close");
  assert.ok(close.x > 0 && close.x < 640 && close.y > 0 && close.y < 800, "Native close must remain within the window.");
  if (artifacts) await page.screenshot({ path: `${artifacts}/session-settings-narrow.png` });
  await verifyOverflowScroll(page, server, settings, originalOptions);
  await closeSessionSettings(page, settings, "Escape");
  assert.equal(server.configurationRequests().length, originalRequests, "Opening or closing a dialog must not apply any row.");
  await page.setViewportSize(originalViewport);
  await openSessionSettingsWithKeyboard(page, settings, "session settings after resize");
  console.log("WASM compact session settings passed: inline icon, wrapped Agent label, overflow scroll and native focus return");
}

async function waitForConfigurationRequests(server, count) {
  const deadline = Date.now() + 30_000;
  while (server.configurationRequests().length < count && Date.now() < deadline)
    await new Promise(resolve => setTimeout(resolve, 50));
  assert.equal(server.configurationRequests().length, count, "The current Apply must reach the Agent before checking completion.");
}

async function openSessionSettingsWithKeyboard(page, settings, label) {
  await waitForControlEnabledState(page, settings, true, label);
  await focusWithKeyboard(page, settings, label);
  await page.keyboard.press("Enter");
  await waitForLaidOutControl(page, { automationIds: ["ChatView.SessionSettingsDialog"] }, `${label} dialog`);
  await waitForSessionSettingsFocus(page);
}

async function waitForSessionSettingsFocus(page) {
  // The native dialog may focus its scroll presenter before the first editor.
  // Skia's semantic peers are siblings, so DOM ancestry does not describe XAML ownership.
  await page.waitForFunction(() => {
    const focused = document.activeElement;
    const name = focused?.getAttribute("aria-label");
    return name === "Response style" || name === "Show the plan" || name === "Close"
      || (focused?.getAttribute("elementtype") === "ScrollContentPresenter"
        && focused.textContent.includes("Settings supplied by the agent apply to this conversation."));
  });
}

async function verifyOverflowScroll(page, server, settings, originalOptions) {
  const overflow = Array.from({ length: 8 }, (_, index) => ({
    id: `overflow-setting-${index}`, name: `Additional setting ${index + 1}`,
    type: "boolean", currentValue: false
  }));
  server.publishConfiguration([...originalOptions, ...overflow]);
  const lastOptions = { labels: ["Additional setting 8"], role: "switch" };
  await focusWithKeyboard(page, lastOptions, "last setting below the fold");
  await page.waitForFunction(() => {
    const focused = document.activeElement;
    const rect = focused?.getBoundingClientRect();
    return focused?.getAttribute("aria-label") === "Additional setting 8"
      && rect.height >= 12 && rect.top > 0 && rect.bottom <= window.innerHeight;
  });
  // Native footer semantics can retain template-local coordinates, so verify its keyboard
  // reachability and use the focused row's updated bounds to assert actual scrolling.
  await focusWithKeyboard(page, { labels: ["Close"], role: "button" }, "close after last setting");
  if (process.env.WASM_SMOKE_ARTIFACTS_DIR)
    await page.screenshot({ path: `${process.env.WASM_SMOKE_ARTIFACTS_DIR}/session-settings-scrolled.png` });
  await closeSessionSettings(page, settings, "Escape");
  server.publishConfiguration(originalOptions);
  await openSessionSettingsWithKeyboard(page, settings, "session settings after overflow");
}

async function focusWithKeyboard(page, options, label) {
  // Traverse with real keys so Popup captures the same native focus owner the user invoked.
  // Assigning DOM focus alone can precede Uno's managed focus handoff after a resize.
  for (let step = 0; step < 40; step += 1) {
    const focused = await page.evaluate(options => {
      const state = window.__salmoneggSmoke.semantic.describe(options);
      return state?.enabled && state.id === document.activeElement?.id;
    }, options);
    if (focused) {
      await waitForNativeControlFocus(page, options, label);
      return;
    }
    await page.keyboard.press("Tab");
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  }
  assert.fail(`Keyboard navigation did not reach ${label}.`);
}

async function closeSessionSettings(page, settings, action) {
  if (action === "Escape") {
    await page.keyboard.press("Escape");
  } else {
    await waitForControlEnabledState(page, { labels: ["Close"], role: "button" }, true, "native dialog close");
    await clickVisibleControl(page, { labels: ["Close"], role: "button" }, "close session settings");
  }
  await page.waitForFunction(() => Array.from(document.querySelectorAll(
    "#uno-semantics-root [xamlautomationid='ChatView.SessionSettingsDialog']"))
    .every(element => Boolean(element.closest("[hidden]")) || element.getBoundingClientRect().width === 0));
  await waitForControlEnabledState(page, settings, true, "settings icon after dialog closes");
  await waitForNativeControlFocus(page, settings, "focus restored to session settings icon");
}
