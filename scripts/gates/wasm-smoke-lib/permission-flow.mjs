import assert from "node:assert/strict";
import {
  waitForControlEnabledState,
  waitForLaidOutControl,
  waitForSemanticText
} from "./ui-affordances.mjs";

// These requests deliberately have no matching transcript tool card. The production fallback
// surface must expose them, and dismissing one must advance the same conversation's queue.
export async function verifyStandalonePermissionQueue(page, server) {
  const suffix = Date.now();
  const makeRequest = index => {
    const title = `WASM permission ${suffix} action ${index}`;
    const optionId = `allow-${index}`;
    const optionName = `Allow action ${index} ${suffix}`;
    const request = server.requestClient("session/request_permission", {
      sessionId: server.sessionId,
      toolCall: { toolCallId: `unlisted-tool-${suffix}-${index}`, title },
      options: [{ optionId, name: optionName, kind: "allow_once" }]
    });
    return { request, title, optionId, optionName };
  };

  const first = makeRequest(1);
  await waitForSemanticText(page, new RegExp(first.title), "first standalone permission");
  const second = makeRequest(2);
  assert.equal(first.request.responses().length, 0);
  assert.equal(second.request.responses().length, 0);

  for (const item of [first, second]) {
    await waitForSemanticText(page, new RegExp(item.title), "current standalone permission");
    const button = { labels: [item.optionName], role: "button" };
    await waitForControlEnabledState(page, button, true, "current permission choice enabled");
    const state = await waitForLaidOutControl(page, button, "current permission choice laid out");
    assert.equal(state.enabled, true);
    await page.locator(`#${state.id}`).focus();
    assert.equal(await page.evaluate(() => document.activeElement?.id), state.id,
      "The actual native permission Button must own focus before Enter.");
    await page.keyboard.press("Enter");
    assert.deepEqual(await item.request.waitForResponse(), {
      jsonrpc: "2.0",
      id: item.request.id,
      result: { outcome: { outcome: "selected", optionId: item.optionId } }
    });
    assert.equal(item.request.responses().length, 1);
  }

  await page.waitForFunction(() => {
    const panel = document.querySelector("#uno-semantics-root [xamlautomationid='ChatView.PermissionRequest']");
    return !panel || Boolean(panel.closest("[hidden]")) || panel.getBoundingClientRect().height === 0;
  });
  console.log("WASM permissions: two real standalone prompts, native choices, queue advancement, one reply each and final dismissal");
  return [first.request, second.request];
}
