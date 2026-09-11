import assert from "node:assert/strict";
import {
  waitForControlEnabledState,
  waitForLaidOutControl,
  waitForNativeControlFocus,
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
  const choiceIds = [];
  assert.equal(first.request.responses().length, 0);
  assert.equal(second.request.responses().length, 0);

  for (const item of [first, second]) {
    await waitForSemanticText(page, new RegExp(item.title), "current standalone permission");
    const button = { labels: [item.optionName], role: "button" };
    await waitForControlEnabledState(page, button, true, "current permission choice enabled");
    const state = await waitForLaidOutControl(page, button, "current permission choice laid out");
    assert.equal(state.enabled, true);
    choiceIds.push(state.id);
    await page.locator(`#${state.id}`).focus();
    await waitForNativeControlFocus(page, button, "current permission choice");
    await page.keyboard.press("Enter");
    assert.deepEqual(await item.request.waitForResponse(), {
      jsonrpc: "2.0",
      id: item.request.id,
      result: { outcome: { outcome: "selected", optionId: item.optionId } }
    });
    assert.equal(item.request.responses().length, 1);
  }

  await waitForPermissionDismissal(page, [first, second], choiceIds);

  const cancelled = makeRequest(3);
  await waitForSemanticText(page, new RegExp(cancelled.title), "permission before peer cancellation");
  const cancelChoice = { labels: [cancelled.optionName], role: "button" };
  await waitForControlEnabledState(page, cancelChoice, true, "peer-cancelled permission initially actionable");
  const cancelState = await waitForLaidOutControl(page, cancelChoice, "peer-cancelled permission initially laid out");
  assert.equal(cancelState.enabled, true);
  choiceIds.push(cancelState.id);
  assert.equal(cancelled.request.responses().length, 0);
  server.notifyClient("$/cancel_request", { requestId: cancelled.request.id });
  const response = await cancelled.request.waitForResponse();
  assert.equal(response.jsonrpc, "2.0");
  assert.equal(response.id, cancelled.request.id);
  assert.equal(response.error?.code, -32800);
  assert.equal(Object.hasOwn(response, "result"), false,
    "Peer cancellation must return the protocol error rather than an authorization outcome.");
  assert.equal(cancelled.request.responses().length, 1);
  await waitForPermissionDismissal(page, [first, second, cancelled], choiceIds);
  assert.equal(cancelled.request.responses().length, 1,
    "Automatic cancellation dismissal must not send a second response.");
  console.log("WASM permissions: native choices advance two requests; peer cancellation returns -32800 and retires the third without user input");
  return [first.request, second.request, cancelled.request];
}

async function waitForPermissionDismissal(page, items, choiceIds) {
  // Uno flattens the semantic tree: a zero-sized or old hidden group does not prove that
  // its action peers disappeared. Require every panel and all actual choices to retire.
  await page.waitForFunction(({ choiceIds, choiceNames }) => {
    const dismissed = element => !element || Boolean(element.closest("[hidden]"));
    const panels = Array.from(document.querySelectorAll(
      "#uno-semantics-root [xamlautomationid='ChatView.PermissionRequest']"));
    const choices = Array.from(document.querySelectorAll(
      "#uno-semantics-root button,#uno-semantics-root [role='button']"))
      .filter(element => choiceNames.includes(element.getAttribute("aria-label")));
    return panels.every(dismissed) && choices.every(dismissed)
      && choiceIds.every(id => dismissed(document.getElementById(id)));
  }, { choiceIds, choiceNames: items.map(item => item.optionName) });
}
