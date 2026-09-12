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
  const entryName = await page.locator(`#${entry.id}`).getAttribute("aria-label");
  assert.equal(entryName, "Session settings", "The icon needs a readable accessible name.");
  await clickVisibleControlWithTrustedPointer(page, settings, "session settings");
  await waitForLaidOutControl(page, { automationIds: ["ChatView.SessionSettingsDialog"] }, "native session settings dialog");
  await waitForSemanticText(page, /Choose the detail used in this conversation\./, "configuration description");
  await waitForLaidOutControl(page, selector, "response style selector");
  if (process.env.WASM_SMOKE_ARTIFACTS_DIR) {
    await mkdir(process.env.WASM_SMOKE_ARTIFACTS_DIR, { recursive: true });
    await page.screenshot({ path: `${process.env.WASM_SMOKE_ARTIFACTS_DIR}/session-settings.png` });
  }
  if (process.env.SALMONEGG_CAPTURE_SESSION_SETTINGS === "1") {
    await verifyCompactSessionSettings(page, server, settings);
  }
  const focused = await page.evaluate(options => {
    const state = window.__salmoneggSmoke.semantic.describe(options);
    const element = state?.id ? document.getElementById(state.id) : null;
    element?.focus();
    return element && document.activeElement === element;
  }, selector);
  assert.equal(focused, true, "The native configuration selector must own focus.");
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
  await clickVisibleControlWithTrustedPointer(page, settings, "boolean session settings");
  const toggle = { labels: ["Show the plan"], role: "switch" };
  await waitForLaidOutControl(page, toggle, "plan switch");
  await clickVisibleControl(page, toggle);
  await waitForControlEnabledState(page, apply, true, "apply boolean setting");
  await clickVisibleControl(page, apply, "apply boolean setting");
  await waitForControlEnabledState(page, apply, false, "accepted boolean setting");
  const boolean = server.configurationRequests().at(-1);
  assert.equal(boolean.params.configId, "show-plan");
  assert.equal(boolean.params.type, "boolean");
  assert.equal(boolean.params.value, true);
  await closeSessionSettings(page, settings, "Close");
  console.log("WASM session configuration passed: icon entry, native modal, per-setting apply, retry, boolean and focus return");
}

async function verifyCompactSessionSettings(page, server, settings) {
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
  if (artifacts) await page.screenshot({ path: `${artifacts}/session-settings-icon-narrow.png` });
  // After a viewport resize Skia's pointer coordinates can lag its native layout. Exercise
  // the same icon through native keyboard focus; screenshots verify the rendered placement.
  await page.locator(`#${entry.id}`).focus();
  await waitForNativeControlFocus(page, settings, "narrow session settings icon");
  await page.keyboard.press("Enter");
  await waitForLaidOutControl(page, { automationIds: ["ChatView.SessionSettingsDialog"] }, "narrow native dialog");
  const close = await waitForLaidOutControl(page, { labels: ["Close"], role: "button" }, "narrow dialog close");
  assert.ok(close.x > 0 && close.x < 640 && close.y > 0 && close.y < 800, "Native close must remain within the window.");
  if (artifacts) await page.screenshot({ path: `${artifacts}/session-settings-narrow.png` });
  await closeSessionSettings(page, settings, "Escape");
  assert.equal(server.configurationRequests().length, originalRequests, "Opening or closing a dialog must not apply any row.");
  await page.setViewportSize(originalViewport);
  const restoredEntry = await waitForLaidOutControl(page, settings, "session settings after resize");
  await page.locator(`#${restoredEntry.id}`).focus();
  await waitForNativeControlFocus(page, settings, "restored settings icon");
  await page.keyboard.press("Enter");
  await waitForLaidOutControl(page, { automationIds: ["ChatView.SessionSettingsDialog"] }, "restored native dialog");
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
