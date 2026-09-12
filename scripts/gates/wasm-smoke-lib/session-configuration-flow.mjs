import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import {
  clickVisibleControl,
  clickVisibleControlWithTrustedPointer,
  waitForControlEnabledState,
  waitForLaidOutControl,
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
  await clickVisibleControlWithTrustedPointer(page, settings, "session settings");
  await waitForSemanticText(page, /Choose the detail used in this conversation\./, "configuration description");
  await waitForLaidOutControl(page, selector, "response style selector");
  if (process.env.WASM_SMOKE_ARTIFACTS_DIR) {
    await mkdir(process.env.WASM_SMOKE_ARTIFACTS_DIR, { recursive: true });
    await page.screenshot({ path: `${process.env.WASM_SMOKE_ARTIFACTS_DIR}/session-settings.png` });
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
  await page.keyboard.press("Escape");
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
  await page.keyboard.press("Escape");
  console.log("WASM session configuration passed: grouped select, retry, boolean and stable V1 wire");
}
