import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  clearBrowserOriginStorage,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import { startAcpWebSocketServer } from "./wasm-smoke-lib/acp-test-server.mjs";
import { verifyStandalonePermissionQueue } from "./wasm-smoke-lib/permission-flow.mjs";
import { verifyRemainingFormInputs } from "./wasm-smoke-lib/elicitation-form-flow.mjs";
import {
  navigateToSettingsSection
} from "./wasm-smoke-lib/settings-shell.mjs";
import {
  createWebSocketProfile,
  expectProfilePresence,
  createRemoteDirectory,
  expectPersistedProfileAfterReload,
  expectRemoteDirectoryPresence,
  ensureAcpProfilesReady,
  clickProfileConnectionToggle,
  waitForInitializeWithDiagnostics,
  createSessionAndSendPromptFromStart
} from "./wasm-smoke-lib/acp-ui-fixture.mjs";
import {
  clickVisibleControl,
  collectVisibleInteractiveDebug,
  typeIntoVisibleTextField,
  waitForControlEnabledState,
  waitForLaidOutControl,
  waitForPersistedLocalFileContains,
  waitForSemanticText
} from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-acp-full-chain-smoke.mjs");
const profileName = `WASM full chain ${Date.now()}`;
const remoteDirectoryName = `WASM remote project ${Date.now()}`;
const remoteDirectoryPath = `/remote/wasm-full-chain-${Date.now()}`;
const fullChainPromptText = `WASM full chain prompt ${Date.now()}`;
const fullChainAgentReplyText = `WASM full chain agent reply ${Date.now()}`;
const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined
});
let acpServer;
const submitButton = { labels: ["Submit"], role: "button" };
const cancelButton = { labels: ["Cancel"], role: "button" };
const elicitationRequests = [];

try {
  acpServer = await startAcpWebSocketServer({
    agentReplyText: fullChainAgentReplyText,
    sessionTitle: "WASM full chain session",
    deferSessionNewResponse: true
  });
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP 连接配置|ACP connection profiles/,
      "ACP Agent settings page");

    await createWebSocketProfile(page, profileName, acpServer.url);
    await expectProfilePresence(page, profileName, "saved ACP profile");
    await createRemoteDirectory(page, remoteDirectoryName, remoteDirectoryPath);
    await waitForPersistedLocalFileContains(page, "/local/SalmonEgg/config/app.yaml",
      [remoteDirectoryName, remoteDirectoryPath], "remote directory settings");

    await expectPersistedProfileAfterReload(page, baseUrl, profileName);
    await expectRemoteDirectoryPresence(page, remoteDirectoryName, remoteDirectoryPath, "persisted remote directory");

    await ensureAcpProfilesReady(page);
    await clickProfileConnectionToggle(page, profileName);
    const initialize = await waitForInitializeWithDiagnostics(acpServer, page, profileName);
    assert.ok(initialize.params.clientCapabilities?.elicitation?.form,
      "The production client must advertise form elicitation before the server sends one.");
    await createSessionAndSendPromptFromStart(
      page,
      acpServer,
      profileName,
      remoteDirectoryName,
      remoteDirectoryPath,
      fullChainPromptText,
      fullChainAgentReplyText,
      {
        beforeSessionNewResponse: async sessionNew => {
          const request = acpServer.requestClient("elicitation/create", {
            requestId: sessionNew.id,
            mode: "form",
            message: "WASM request-scoped input before a session exists",
            requestedSchema: { type: "object", properties: {} }
          });
          elicitationRequests.push(request);
          const response = await request.waitForResponse();
          assert.deepEqual(response, { jsonrpc: "2.0", id: request.id, result: { action: "cancel" } },
            "Request-scoped input must be cancelled instead of leaving session/new waiting.");
          assert.equal(request.responses().length, 1);
          acpServer.completeSessionNew();
          console.log("WASM request-scoped elicitation returned cancel before session/new completed");
        }
      });

    elicitationRequests.push(await verifyKnownFormSubmission(page, acpServer));
    elicitationRequests.push(...await verifyRemainingFormInputs(page, acpServer));
    elicitationRequests.push(await verifyUnknownRequiredFieldCancellation(page, acpServer));
    elicitationRequests.push(...await verifyStandalonePermissionQueue(page, acpServer));
  } catch (error) {
    const artifacts = process.env.WASM_SMOKE_ARTIFACTS_DIR;
    if (artifacts) {
      await mkdir(artifacts, { recursive: true });
      await page.screenshot({ path: `${artifacts}/interaction-failure.png` });
      await writeFile(`${artifacts}/interaction-failure.json`, JSON.stringify({
        error: String(error),
        semantic: await page.evaluate(collectVisibleInteractiveDebug),
        fatalConsoleMessages
      }, null, 2));
    }
    throw error;
  } finally {
    await context.close();
  }

  // Check again after closing the client: duplicate responses can arrive after the first reply,
  // while the view is dismissing or while the next request is being exercised.
  for (const request of elicitationRequests) {
    assert.equal(request.responses().length, 1, `Elicitation ${request.id} must receive exactly one response.`);
  }
  assertNoFatalConsoleMessages(fatalConsoleMessages);
  console.log("WASM ACP full-chain and elicitation smoke passed");
} finally {
  try {
    await browser.close();
  } finally {
    await acpServer?.close();
  }
}

async function verifyKnownFormSubmission(page, server) {
  const existingControlIds = await page.evaluate(() => Array.from(document.querySelectorAll(
    "#uno-semantics-root input,#uno-semantics-root [role='checkbox'],#uno-semantics-root [aria-checked]"))
    .filter(element => !element.closest("[hidden]"))
    .map(element => element.id));
  const fieldValue = `WASM form value ${Date.now()}`;
  const prompt = `WASM required input ${Date.now()}`;
  const request = server.requestClient("elicitation/create", {
    sessionId: server.sessionId,
    mode: "form",
    message: prompt,
    requestedSchema: {
      type: "object",
      properties: {
        name: { type: "string", title: "Smoke name" },
        enabled: { type: "boolean", title: "Smoke enabled", default: true }
      },
      required: ["name", "enabled"]
    }
  });

  await waitForSemanticText(page, new RegExp(prompt), "known-type elicitation form");
  await waitForLaidOutControl(page, submitButton, "elicitation Submit button");
  const formNodeIds = await waitForVisibleForm(page);
  await waitForControlEnabledState(page, submitButton, false, "empty required field blocks Submit");
  assert.equal(request.responses().length, 0, "An incomplete form must not send a response.");

  const inputSelector = await findSingleVisibleFormInput(page, existingControlIds);
  await typeIntoVisibleTextField(page, { selector: inputSelector }, fieldValue, "required elicitation name");
  await toggleSingleVisibleFormCheckbox(page, existingControlIds);
  await waitForControlEnabledState(page, submitButton, true, "filled form enables Submit");
  await clickVisibleControl(page, submitButton);

  const response = await request.waitForResponse();
  assert.deepEqual(response, {
    jsonrpc: "2.0",
    id: request.id,
    result: { action: "accept", content: { name: fieldValue, enabled: false } }
  }, "Form values must survive the real UI binding and ACP wire response.");
  assert.equal(request.responses().length, 1);
  await waitForFormDismissal(page, formNodeIds);
  assert.equal(request.responses().length, 1, "Dismissing the accepted form must not send another response.");
  console.log("WASM known-type elicitation blocked empty input and returned typed string/boolean content");
  return request;
}

async function verifyUnknownRequiredFieldCancellation(page, server) {
  const prompt = `WASM unsupported required input ${Date.now()}`;
  const request = server.requestClient("elicitation/create", {
    sessionId: server.sessionId,
    mode: "form",
    message: prompt,
    requestedSchema: {
      type: "object",
      properties: { future: { type: "_future", title: "Unsupported required field" } },
      required: ["future"]
    }
  });

  // Uno can omit the prompt TextBlock's semantic node when this surface reappears. The error is
  // unique to this request, so it proves the new form arrived without relying on the old heading.
  await waitForSemanticText(page, /This form contains required fields that cannot be displayed/,
    "localized unsupported-required explanation");
  const formNodeIds = await waitForVisibleForm(page);
  await waitForControlEnabledState(page, submitButton, false, "unsupported required field blocks Submit");
  assert.equal(request.responses().length, 0, "An unsupported required field must never be accepted.");
  await waitForControlEnabledState(page, cancelButton, true, "unsupported form remains cancellable");
  await clickVisibleControl(page, cancelButton);

  const response = await request.waitForResponse();
  assert.deepEqual(response, { jsonrpc: "2.0", id: request.id, result: { action: "cancel" } });
  assert.equal(request.responses().length, 1, "Cancelling must send exactly one response.");
  await waitForFormDismissal(page, formNodeIds);
  assert.equal(request.responses().length, 1, "Dismissing the cancelled form must not send another response.");
  console.log("WASM unknown required field prevented accept and returned cancel");
  return request;
}

async function findSingleVisibleFormInput(page, existingControlIds) {
  // The form's TextBox has no automation id or accessible name. Chat's composer is a textarea,
  // and the shell already owns a search input. Only this request's newly visible single-line
  // input belongs to the form; count it explicitly instead of choosing the first page input.
  const handle = await page.waitForFunction(existingIds => {
    const inputs = Array.from(document.querySelectorAll("#uno-semantics-root input"))
      .filter(element => {
        const rect = element.getBoundingClientRect();
        return element.type === "text"
          && element.id !== ""
          && !existingIds.includes(element.id)
          && !element.closest("[hidden]")
          && !element.disabled
          && !element.readOnly
          && rect.width >= 12
          && rect.height >= 12;
      });
    return inputs.length === 1 ? `#${inputs[0].id}` : null;
  }, existingControlIds, { timeout: 15_000 }).catch(async error => {
    throw new Error(`Expected one editable elicitation input. Semantic DOM=${JSON.stringify(
      await collectVisibleInteractiveDebug(page))}`, { cause: error });
  });
  try {
    return await handle.jsonValue();
  } finally {
    await handle.dispose();
  }
}

async function waitForVisibleForm(page) {
  const handle = await page.waitForFunction(() => {
    const isVisible = element => {
      const rect = element.getBoundingClientRect();
      return !element.closest("[hidden]") && rect.width >= 12 && rect.height >= 12;
    };
    const hosts = Array.from(document.querySelectorAll(
      "#uno-semantics-root [xamlautomationid='ElicitationHost']")).filter(isVisible);
    if (hosts.length !== 1) {
      return null;
    }
    const buttons = Array.from(document.querySelectorAll(
      "#uno-semantics-root button,#uno-semantics-root [role='button']")).filter(isVisible);
    const ids = [hosts[0].id];
    for (const label of ["Submit", "Cancel", "Decline"]) {
      const matches = buttons.filter(element => element.getAttribute("aria-label") === label);
      if (matches.length !== 1 || matches[0].id === "") {
        return null;
      }
      ids.push(matches[0].id);
    }
    return ids;
  }, null, { timeout: 15_000 });
  try {
    return await handle.jsonValue();
  } finally {
    await handle.dispose();
  }
}

async function waitForFormDismissal(page, formNodeIds) {
  // These nodes must first have been visible. Text may disappear before the form collapses, and
  // collapsed controls can retain old bounds, so require native removal/hidden state for the host
  // and every action, and reject a replacement host that is still present.
  const handle = await page.waitForFunction(ids => {
    const isDismissed = node => !node || Boolean(node.closest("[hidden]"));
    return ids.every(id => isDismissed(document.getElementById(id)))
      && Array.from(document.querySelectorAll(
        "#uno-semantics-root [xamlautomationid='ElicitationHost']")).every(isDismissed);
  }, formNodeIds, { timeout: 15_000 }).catch(async error => {
    throw new Error(`Elicitation host and actions did not dismiss. Semantic DOM=${JSON.stringify(
      await collectVisibleInteractiveDebug(page))}`, { cause: error });
  });
  await handle.dispose();
}

async function toggleSingleVisibleFormCheckbox(page, existingControlIds) {
  // Uno may expose Toggle through aria-checked without an explicit checkbox role. The only
  // new toggle in this form is its boolean field; wait for its native default and enabled state
  // before one activation, then wait for the same node's state to change before submitting.
  const handle = await page.waitForFunction(existingIds => {
    const checkboxes = Array.from(document.querySelectorAll(
      "#uno-semantics-root input[type='checkbox'],#uno-semantics-root [role='checkbox'],#uno-semantics-root [aria-checked]"))
      .filter(element => {
        const rect = element.getBoundingClientRect();
        return element.id !== "" && !existingIds.includes(element.id)
          && !element.closest("[hidden]") && rect.width >= 12 && rect.height >= 12;
      });
    if (checkboxes.length !== 1) {
      return null;
    }
    const checkbox = checkboxes[0];
    const ariaChecked = checkbox.getAttribute("aria-checked");
    const checked = ariaChecked === "true" || (ariaChecked === null && checkbox.checked === true);
    const enabled = !checkbox.disabled && checkbox.getAttribute("aria-disabled") !== "true";
    return checked && enabled ? checkbox.id : null;
  }, existingControlIds, { timeout: 15_000 }).catch(async error => {
    throw new Error(`Expected one enabled elicitation checkbox with its true default. Semantic DOM=${JSON.stringify(
      await collectVisibleInteractiveDebug(page))}`, { cause: error });
  });
  let checkboxId;
  try {
    checkboxId = await handle.jsonValue();
  } finally {
    await handle.dispose();
  }

  const activated = await page.evaluate(id => {
    const checkbox = document.getElementById(id);
    if (!checkbox || checkbox.closest("[hidden]")
      || checkbox.disabled || checkbox.getAttribute("aria-disabled") === "true") {
      return false;
    }
    checkbox.click();
    return true;
  }, checkboxId);
  assert.equal(activated, true, "The elicitation checkbox must remain enabled for activation.");

  const unchecked = await page.waitForFunction(id => {
    const checkbox = document.getElementById(id);
    if (!checkbox || checkbox.closest("[hidden]")) {
      return false;
    }
    const ariaChecked = checkbox.getAttribute("aria-checked");
    return ariaChecked === "false" || (ariaChecked === null && checkbox.checked === false);
  }, checkboxId, { timeout: 15_000 });
  await unchecked.dispose();
}
