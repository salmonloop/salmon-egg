import assert from "node:assert/strict";
import {
  collectVisibleInteractiveDebug,
  typeIntoVisibleTextField,
  waitForControlEnabledState,
  waitForLaidOutControl,
  waitForSemanticText
} from "./ui-affordances.mjs";

const formTimeoutMs = 15_000;
const submitButton = { labels: ["Submit"], role: "button" };

// Reuse the caller's product session. Each request has one field so the native editor can be
// identified without adding test-only ids or assigning a ViewModel value from the browser.
export async function verifyRemainingFormInputs(page, server) {
  const suffix = Date.now();
  const requests = [];
  requests.push(await verifyNumericForm(page, server, suffix, {
    type: "integer", key: "count", minimum: 1, maximum: 20,
    firstValue: "7", invalidValue: "1.5", finalValue: "11", expected: 11
  }));
  requests.push(await verifyNumericForm(page, server, suffix, {
    type: "number", key: "ratio", minimum: 0, maximum: 1,
    firstValue: "0.25", invalidValue: "3", finalValue: "0.375", expected: 0.375
  }));
  requests.push(await verifyMultiSelectForm(page, server, suffix));
  requests.push(await verifyTitledMultiSelectForm(page, server, suffix));
  requests.push(await verifyDeclinedForm(page, server, suffix));
  console.log("WASM form inputs: integer/number validation, bounded and titled multi-select, native Decline, typed replies and dismissal");
  return requests;
}

async function verifyNumericForm(page, server, suffix, sample) {
  const form = await openForm(page, server, `WASM ${sample.type} input ${suffix}`, {
    [sample.key]: {
      type: sample.type,
      title: `Smoke ${sample.type} ${suffix}`,
      minimum: sample.minimum,
      maximum: sample.maximum
    }
  });
  await findSingleNewInput(page, form.existingInputIds);
  await waitForControlEnabledState(page, submitButton, false, `${sample.type}: required empty input`);
  assert.equal(form.request.responses().length, 0);

  // The valid -> invalid transition proves validation ran for this edit; an old disabled
  // button from the empty form cannot make the invalid-value assertion pass on its own.
  for (const [value, enabled] of [
    [sample.firstValue, true], [sample.invalidValue, false], [sample.finalValue, true]
  ]) {
    const selector = await findSingleNewInput(page, form.existingInputIds);
    await typeIntoVisibleTextField(page, { selector }, value, `${sample.type} elicitation input`, formTimeoutMs);
    await waitForFormTabCompletion(page, form);
    await waitForControlEnabledState(page, submitButton, enabled, `${sample.type}: value ${value}`, formTimeoutMs);
    assert.equal(form.request.responses().length, 0, "Editing or validating must not send a response.");
  }

  await activateFormAction(page, form, "Submit");
  await verifyResponseAndDismissal(page, form, {
    action: "accept", content: { [sample.key]: sample.expected }
  });
  return form.request;
}

async function verifyMultiSelectForm(page, server, suffix) {
  const options = [`Smoke API ${suffix}`, `Smoke UI ${suffix}`, `Smoke Docs ${suffix}`];
  const form = await openForm(page, server, `WASM multi-select input ${suffix}`, {
    targets: {
      type: "array", title: `Smoke targets ${suffix}`,
      items: { type: "string", enum: options }, minItems: 2, maxItems: 2
    }
  });
  const checkboxes = [];
  for (const name of options) {
    checkboxes.push(await findNamedCheckbox(page, name, false));
  }
  assert.equal(new Set(checkboxes).size, options.length);
  await waitForControlEnabledState(page, submitButton, false, "No multi-select choices blocks Submit");

  for (const [index, checked, enabled] of [
    [0, true, false], [1, true, true], [2, true, false], [1, false, true]
  ]) {
    await activateNativeControl(page, checkboxes[index], "Space", options[index]);
    await waitForCheckboxState(page, checkboxes[index], checked);
    await waitForControlEnabledState(page, submitButton, enabled, "Multi-select bounds update Submit", formTimeoutMs);
    assert.equal(form.request.responses().length, 0, "Toggling a choice must not submit the form.");
  }

  await activateFormAction(page, form, "Submit");
  await verifyResponseAndDismissal(page, form, {
    action: "accept", content: { targets: [options[0], options[2]] }
  });
  return form.request;
}

async function verifyTitledMultiSelectForm(page, server, suffix) {
  const options = [
    { const: `api-internal-${suffix}`, title: `Public API ${suffix}`, description: `Read interface contracts ${suffix}` },
    { const: `ui-internal-${suffix}`, title: `Desktop UI ${suffix}`, description: null },
    { const: `fallback-internal-${suffix}`, title: "  ", description: "  " }
  ];
  const form = await openForm(page, server, `WASM titled multi-select input ${suffix}`, {
    targets: {
      type: "array", title: `Smoke titled targets ${suffix}`,
      items: { anyOf: options }, default: [options[0].const], minItems: 1, maxItems: 2
    }
  });
  const labels = [options[0].title, options[1].title, options[2].const];
  const checkboxes = [];
  for (const [index, label] of labels.entries()) {
    checkboxes.push(await findNamedCheckbox(page, label, index === 0));
  }
  assert.equal(new Set(checkboxes).size, options.length);
  await waitForValue(page, description => Array.from(document.querySelectorAll(
    "#uno-semantics-root [id^='uno-semantics-']")).some(element => {
    const rect = element.getBoundingClientRect();
    const matches = element.getAttribute("aria-label") === description || element.textContent?.trim() === description;
    return matches && !element.closest("[hidden]") && rect.width > 0 && rect.height > 0;
  }), options[0].description, "this request's visible option description");
  await waitForControlEnabledState(page, submitButton, true, "The default uses the wire const value");
  assert.equal(form.request.responses().length, 0);

  for (const [index, checked, enabled] of [[0, false, false], [1, true, true], [2, true, true]]) {
    await activateNativeControl(page, checkboxes[index], "Space", labels[index]);
    await waitForCheckboxState(page, checkboxes[index], checked);
    await waitForControlEnabledState(page, submitButton, enabled, "Titled choices update Submit", formTimeoutMs);
    assert.equal(form.request.responses().length, 0, "Choosing a display label must not submit the form.");
  }

  await activateFormAction(page, form, "Submit");
  await verifyResponseAndDismissal(page, form, {
    action: "accept", content: { targets: [options[1].const, options[2].const] }
  });
  return form.request;
}

async function verifyDeclinedForm(page, server, suffix) {
  const form = await openForm(page, server, `WASM declined input ${suffix}`, {
    reason: { type: "string", title: `Smoke decline reason ${suffix}` }
  });
  await findSingleNewInput(page, form.existingInputIds);
  await waitForControlEnabledState(page, submitButton, false, "Unfilled required form blocks Submit");
  assert.equal(form.request.responses().length, 0);
  await activateFormAction(page, form, "Decline");
  await verifyResponseAndDismissal(page, form, { action: "decline" });
  return form.request;
}

async function openForm(page, server, prompt, properties) {
  const existingInputIds = await page.evaluate(() => Array.from(document.querySelectorAll(
    "#uno-semantics-root input"))
    .filter(element => !element.closest("[hidden]"))
    .map(element => element.id));
  const request = server.requestClient("elicitation/create", {
    sessionId: server.sessionId, mode: "form", message: prompt,
    requestedSchema: { type: "object", properties, required: Object.keys(properties) }
  });
  // The host's reused prompt TextBlock can retain an old semantic peer. A freshly projected
  // field has a unique title in every request, so it proves this turn reached the real form.
  const fieldTitle = Object.values(properties)[0].title;
  await waitForSemanticText(page, new RegExp(fieldTitle), "this request's field label", formTimeoutMs);
  const nodes = await waitForValue(page, () => {
    const visible = element => {
      const rect = element.getBoundingClientRect();
      return !element.closest("[hidden]") && rect.width >= 12 && rect.height >= 12;
    };
    const hosts = Array.from(document.querySelectorAll(
      "#uno-semantics-root [xamlautomationid='ElicitationHost']")).filter(visible);
    if (hosts.length !== 1 || !hosts[0].id) return null;
    const buttons = Array.from(document.querySelectorAll(
      "#uno-semantics-root button,#uno-semantics-root [role='button']")).filter(visible);
    const actions = {};
    for (const label of ["Submit", "Cancel", "Decline"]) {
      const matches = buttons.filter(element => element.getAttribute("aria-label") === label);
      if (matches.length !== 1 || !matches[0].id) return null;
      actions[label] = matches[0].id;
    }
    return { hostId: hosts[0].id, actions };
  }, null, "one laid-out form and its three actions");
  return { request, existingInputIds, ...nodes };
}

async function findSingleNewInput(page, existingInputIds) {
  return await waitForValue(page, existingIds => {
    const inputs = Array.from(document.querySelectorAll("#uno-semantics-root input"))
      .filter(element => {
        const rect = element.getBoundingClientRect();
        return ["text", "number"].includes(element.type) && element.id
          && !existingIds.includes(element.id) && !element.closest("[hidden]")
          && !element.disabled && !element.readOnly && element.getAttribute("aria-disabled") !== "true"
          && rect.width >= 12 && rect.height >= 12;
      });
    return inputs.length === 1 ? `#${inputs[0].id}` : null;
  }, existingInputIds, "exactly one new editable form input");
}

async function findNamedCheckbox(page, name, expectedChecked) {
  return await waitForValue(page, ({ name, expectedChecked }) => {
    const matches = Array.from(document.querySelectorAll(
      "#uno-semantics-root input[type='checkbox'],#uno-semantics-root [role='checkbox'],#uno-semantics-root [aria-checked]"))
      .filter(element => {
        const rect = element.getBoundingClientRect();
        return element.getAttribute("aria-label") === name && element.id
          && !element.closest("[hidden]") && !element.disabled
          && element.getAttribute("aria-disabled") !== "true" && rect.width >= 12 && rect.height >= 12;
      });
    if (matches.length !== 1) return null;
    const checkbox = matches[0];
    const aria = checkbox.getAttribute("aria-checked");
    const checked = aria === null ? checkbox.checked : aria === "true" ? true : aria === "false" ? false : null;
    return checked === expectedChecked ? checkbox.id : null;
  }, { name, expectedChecked }, `enabled checkbox ${name} with its default state`);
}

async function waitForCheckboxState(page, id, expectedChecked) {
  await waitForValue(page, ({ id, expectedChecked }) => {
    const checkbox = document.getElementById(id);
    if (!checkbox || checkbox.closest("[hidden]")) return false;
    const aria = checkbox.getAttribute("aria-checked");
    const checked = aria === null ? checkbox.checked : aria === "true" ? true : aria === "false" ? false : null;
    return checked === expectedChecked;
  }, { id, expectedChecked }, `same checkbox ${id} changing to ${expectedChecked}`);
}

async function activateFormAction(page, form, label) {
  const options = { labels: [label], role: "button" };
  await waitForControlEnabledState(page, options, true, `form ${label} enabled`, formTimeoutMs);
  const state = await waitForLaidOutControl(page, options, `form ${label} laid out`, formTimeoutMs);
  assert.equal(state.id, form.actions[label], "The action must still belong to the recorded form.");
  await activateNativeControl(page, state.id, "Enter", `form ${label}`);
}

async function waitForFormTabCompletion(page, form) {
  // The editor helper commits with Tab, but its value was already correct before that key.
  // Wait for the form's next native tab stop; otherwise its delayed focus can override the
  // next edit. Read only: never focus Cancel or resend Tab to manufacture this completion.
  const readCancelFocus = id => {
    const element = document.getElementById(id);
    const rect = element?.getBoundingClientRect();
    return document.activeElement?.id === id && element && !element.closest("[hidden]")
      && !element.disabled && element.getAttribute("aria-disabled") !== "true"
      && rect.width >= 12 && rect.height >= 12 ? id : null;
  };
  const deadline = Date.now() + formTimeoutMs;
  while (Date.now() < deadline) {
    const id = await waitForValue(page, readCancelFocus, form.actions.Cancel,
      "the form's Cancel button receiving Tab focus", Math.max(1, deadline - Date.now()));
    await page.evaluate(() => new Promise(requestAnimationFrame));
    if (await page.evaluate(readCancelFocus, form.actions.Cancel) === id) return;
  }
  throw new Error("The form's Cancel button did not retain Tab focus across a frame.");
}

async function activateNativeControl(page, id, key, label) {
  await page.locator(`#${id}`).focus();
  const focused = await page.evaluate(async id => {
    await new Promise(requestAnimationFrame);
    const element = document.getElementById(id);
    const rect = element?.getBoundingClientRect();
    return document.activeElement?.id === id && element && !element.closest("[hidden]")
      && !element.disabled && element.getAttribute("aria-disabled") !== "true"
      && rect.width >= 12 && rect.height >= 12;
  }, id);
  assert.equal(focused, true, `${label} must retain native focus, layout and enabled state before ${key}.`);
  await page.keyboard.press(key);
}

async function verifyResponseAndDismissal(page, form, result) {
  assert.deepEqual(await form.request.waitForResponse(), {
    jsonrpc: "2.0", id: form.request.id, result
  }, "The real input/command path must preserve the expected ACP action and JSON types.");
  assert.equal(form.request.responses().length, 1);
  const nodeIds = [form.hostId, ...Object.values(form.actions)];
  await waitForValue(page, ids => {
    const dismissed = element => !element || Boolean(element.closest("[hidden]"));
    return ids.every(id => dismissed(document.getElementById(id)))
      && Array.from(document.querySelectorAll(
        "#uno-semantics-root [xamlautomationid='ElicitationHost']")).every(dismissed);
  }, nodeIds, "answered form host and all actions removed or natively hidden");
  assert.equal(form.request.responses().length, 1, "Native dismissal must not send a second response.");
}

async function waitForValue(page, predicate, argument, label, timeoutMs = formTimeoutMs) {
  // Predicates must be synchronous: this Playwright polls immediate truthiness, so an async
  // predicate's Promise could turn a false condition into a successful gate.
  const handle = await page.waitForFunction(predicate, argument, { timeout: timeoutMs })
    .catch(async error => {
      throw new Error(`Timed out waiting for ${label}. Semantic DOM=${JSON.stringify(
        await page.evaluate(collectVisibleInteractiveDebug))}`, { cause: error });
    });
  try {
    return await handle.jsonValue();
  } finally {
    await handle.dispose();
  }
}
