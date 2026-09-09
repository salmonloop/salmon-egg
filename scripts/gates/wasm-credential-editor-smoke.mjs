import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  clearBrowserOriginStorage,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import { navigateToSettingsSection } from "./wasm-smoke-lib/settings-shell.mjs";
import {
  clickVisibleControl,
  waitForLaidOutControl,
  waitForControlEnabledState,
  waitForSemanticText,
  waitForPersistedLocalFileContains,
  setToggleSwitchValue,
  typeIntoVisibleTextField,
  selectComboBoxItem
} from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-credential-editor-smoke.mjs");
const profileName = `Credential editor ${Date.now()}`;
const credentialCanary = `invalid-local-gui-test-${randomUUID()}`;
const bindButton = { labels: ["Bind to current destination"], role: "button" };
const removeButton = { labels: ["Remove binding"], role: "button" };
// Uno uses an input[type=checkbox] with no explicit role attribute. The shared matcher reports
// the tag name, so identify this native toggle by its accessible name.
const clearCredential = { labels: ["Clear the stored credential when saving"] };
const tokenInput = { automationIds: ["TokenBox"] };
const apiKeyInput = { automationIds: ["ApiKeyBox"] };
const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined
});

try {
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);
  let endpointRequests = 0;
  let credentialLogged = false;
  // The test value only exercises the app's local storage. Even an accidental connection must
  // stop in the browser, before it can reach the reserved example host.
  await context.route("**://credential.example/**", route => {
    endpointRequests++;
    return route.abort();
  });
  await context.routeWebSocket("wss://credential.example/**", socket => {
    endpointRequests++;
    socket.close();
  });
  page.on("console", message => { credentialLogged ||= message.text().includes(credentialCanary); });
  page.on("pageerror", error => { credentialLogged ||= (error.stack ?? error.message).includes(credentialCanary); });
  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP 连接配置|ACP connection profiles/, "ACP settings");
    await clickVisibleControl(page, { automationIds: ["Acp.Profiles.Add"] });
    await waitForLaidOutControl(page, { automationIds: ["Acp.ProfileEditor.Name"] }, "profile editor");
    await typeIntoVisibleTextField(page, { automationIds: ["Acp.ProfileEditor.Name"] }, profileName, "profile name");
    await typeIntoVisibleTextField(page, { automationIds: ["Acp.ProfileEditor.ServerUrl"] },
      "wss://credential.example/acp", "WebSocket URL");

    // Open the native Expander using its keyboard affordance. The input fields remain in the
    // semantic tree while collapsed, so presence alone cannot prove they are available to a user.
    await openAdvancedSettings(page);
    await waitForSemanticText(page, /This platform cannot send credential headers over WebSocket/,
      "browser WebSocket capability explanation");
    await waitForControlEnabledState(page, bindButton, false, "unsupported WebSocket binding blocked");

    await selectComboBoxItem(page, "Transport", "Streamable HTTP");
    // Closing the native popup restores focus asynchronously. Wait for that lifecycle before
    // directing the next keystrokes to another field.
    const closedTransport = await page.waitForFunction(async () => {
      const describe = () => window.__salmoneggSmoke.semantic.describe({ automationIds: ["Transport"], role: "combobox" });
      const ready = () => {
        const state = describe();
        return state?.expanded === false && document.activeElement?.id === state.id;
      };
      if (!ready()) return false;
      await new Promise(requestAnimationFrame);
      return ready();
    }, undefined, { timeout: 10_000 });
    await closedTransport.dispose();
    await typeIntoVisibleTextField(page, { automationIds: ["Acp.ProfileEditor.ServerUrl"] },
      "https://credential.example/acp", "HTTP URL");
    await waitForControlEnabledState(page, bindButton, true, "HTTP binding available");
    await typeIntoVisibleTextField(page, { labels: ["HTTP header required by the agent"] },
      "Authorization", "credential header");
    await typeIntoVisibleTextField(page,
      { labels: ["Header prefix (for example, Bearer; empty sends the credential directly)"] },
      "Bearer", "credential header prefix");
    await clickVisibleControl(page, bindButton);
    await waitForSemanticText(page, /Bound: Token → Authorization/, "first explicit binding");
    await waitForControlEnabledState(page, removeButton, true, "bound destination can be removed");
    await clickVisibleControl(page, removeButton);
    await waitForSemanticText(page, /No binding\. Stored credentials will not be sent automatically\./,
      "binding removed");
    await waitForControlEnabledState(page, removeButton, false, "removed destination stays removed");

    // A different second header proves the next bind completed rather than matching the old text.
    await typeIntoVisibleTextField(page, { labels: ["HTTP header required by the agent"] },
      "X-Acp-Test", "replacement header");
    await clickVisibleControl(page, bindButton);
    await waitForSemanticText(page, /Bound: Token → X-Acp-Test/, "replacement binding");
    try {
      await typeIntoVisibleTextField(page, tokenInput, credentialCanary, "local test credential");
    } catch {
      // The shared input diagnostic includes the entered value. Keep even the invalid canary
      // out of logs, just as a production credential must stay out of diagnostics.
      throw new Error("The local test credential did not finish entering; its value is omitted.");
    }
    await waitForControlEnabledState(page, { labels: ["Save"], role: "button" }, true, "first save enabled");
    await clickVisibleControl(page, { labels: ["Save"], role: "button" });
    await waitForLaidOutControl(page, { automationIds: ["Acp.Profiles.Add"] }, "profile list after save");
    await waitForSemanticText(page, new RegExp(profileName), "saved profile");
    // Profiles have their own YAML files; app.yaml belongs to application preferences.
    const profilePath = await page.evaluate(() => {
      const directory = "/local/SalmonEgg/config/servers";
      const files = globalThis.FS.readdir(directory).filter(name => name.endsWith(".yaml"));
      if (files.length !== 1) throw new Error(`Expected one saved profile, found ${files.length}.`);
      return `${directory}/${files[0]}`;
    });
    const persisted = await waitForPersistedLocalFileContains(page, profilePath,
      [profileName, "credential_binding:", "X-Acp-Test", "Bearer", "authentication:\n  mode: bearer_token"],
      "binding metadata with a stored credential");
    assert.match(persisted, /https:\/\/credential\.example\/acp/);
    assert.match(persisted, /^  source: token$/m);
    assert.match(persisted, /^  target: header$/m);
    assert.match(persisted, /^  name: X-Acp-Test$/m);
    assert.match(persisted, /^  scheme: Bearer$/m);
    const identity = persisted.match(/^  target_identity: (.+)$/m)?.[1];
    const firstRevision = persisted.match(/^revision: (.+)$/m)?.[1];
    assert.ok(identity && firstRevision, "Saved binding metadata must identify the destination and revision.");
    assert.ok(!persisted.includes(credentialCanary), "The profile YAML must contain metadata only.");
    assert.doesNotMatch(persisted, /^  name: Authorization$/m, "The removed first binding must not return on save.");

    const secretPath = await page.evaluate(canary => {
      const directory = "/local/SalmonEgg/SecureStoragePlainText";
      const files = globalThis.FS.readdir(directory)
        .filter(name => name.endsWith(".secret"))
        .map(name => `${directory}/${name}`)
        .filter(path => globalThis.FS.readFile(path, { encoding: "utf8" }) === canary);
      if (files.length !== 1) throw new Error("Expected one local test credential in the platform's actual store.");
      return files[0];
    }, credentialCanary);
    await expectPersistedCredential(page, secretPath, credentialCanary, true);

    // Restart the app before editing: the second save must clear an already persisted credential,
    // not merely leave an initially empty input disabled.
    await openApp(page, baseUrl);
    await navigateToSettingsSection(page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP 连接配置|ACP connection profiles/, "saved ACP settings");
    await waitForSemanticText(page, new RegExp(profileName), "profile restored from IndexedDB");
    const profileMore = { automationIds: ["Acp.Profile.More"], role: "button" };
    const more = await waitForLaidOutControl(page, profileMore, "saved profile menu");
    assert.equal(more.aria, "More", "The profile menu must have its localized accessible name.");
    assert.equal(await page.locator("#uno-semantics-root button[xamlautomationid='Acp.Profile.More']")
      .evaluateAll(nodes => nodes.filter(node => {
        const rect = node.getBoundingClientRect();
        return !node.hidden && rect.width >= 12 && rect.height >= 12;
      }).length), 1, "This isolated context must contain exactly one realized profile menu.");
    await clickVisibleControl(page, profileMore);
    await clickVisibleControl(page, { labels: ["Edit"], role: "menuitem" });
    await waitForLaidOutControl(page, { automationIds: ["Acp.ProfileEditor.Name"] }, "saved profile editor");
    await openAdvancedSettings(page);
    await waitForSemanticText(page, /Bound: Token → X-Acp-Test/, "restored replacement binding");
    await waitForControlEnabledState(page, tokenInput, true, "stored credential can be replaced");
    await setToggleSwitchValue(page, clearCredential, true, "clear stored credential selected");
    await waitForControlEnabledState(page, tokenInput, false, "clear disables token input");
    await waitForControlEnabledState(page, apiKeyInput, false, "clear disables API-key input");
    await setToggleSwitchValue(page, clearCredential, false, "clear cancelled");
    await waitForControlEnabledState(page, tokenInput, true, "cancelling clear restores token input");
    await waitForControlEnabledState(page, apiKeyInput, true, "cancelling clear restores API-key input");
    await setToggleSwitchValue(page, clearCredential, true, "second clear selected");
    await waitForControlEnabledState(page, tokenInput, false, "second clear disables token input");
    await waitForControlEnabledState(page, { labels: ["Save"], role: "button" }, true, "clear save enabled");
    await clickVisibleControl(page, { labels: ["Save"], role: "button" });
    await waitForLaidOutControl(page, { automationIds: ["Acp.Profiles.Add"] }, "profile list after clear save");
    const cleared = await waitForPersistedLocalFileContains(page, profilePath,
      [profileName, "authentication:\n  mode: none", "credential_binding:", "X-Acp-Test", `target_identity: ${identity}`],
      "cleared credential with retained binding metadata");
    assert.notEqual(cleared.match(/^revision: (.+)$/m)?.[1], firstRevision,
      "The second save must produce a new persisted revision.");
    assert.ok(!cleared.includes(credentialCanary), "Cleared profile YAML must remain metadata only.");
    await expectPersistedCredential(page, secretPath, credentialCanary, false);
    await openApp(page, baseUrl);
    await navigateToSettingsSection(page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP 连接配置|ACP connection profiles/, "ACP settings after credential deletion");
    await waitForSemanticText(page, new RegExp(profileName), "profile restored after credential deletion");
    await expectPersistedCredential(page, secretPath, credentialCanary, false);
    assert.equal(await page.evaluate(path => globalThis.FS.analyzePath(path).exists, secretPath), false,
      "Restarting the app must not restore the deleted credential into the live filesystem.");
    assert.equal(endpointRequests, 0, "Editing and saving must never connect to the endpoint.");
    assert.equal(credentialLogged, false, "Credential values must stay out of console diagnostics.");
    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM credential editor passed: capability gating, bind/remove/rebind, local credential save, reload, clear/undo/clear, persisted deletion and retained binding metadata; no endpoint connection.");
  } finally {
    await context.close();
  }
} catch (error) {
  // Helpers can include semantic DOM or console contents in their errors. Preserve the failure
  // evidence without letting any diagnostic path print the local test value.
  const detail = error instanceof Error ? error.stack ?? error.message : String(error);
  throw new Error(detail.replaceAll(credentialCanary, "[omitted test credential]"));
} finally {
  await browser.close();
}

async function openAdvancedSettings(page) {
  const readExpanders = () => page.locator("#uno-semantics-root button[aria-expanded]").evaluateAll(nodes => nodes
    .filter(node => node.getAttribute("role") !== "combobox" && !node.closest("[hidden]"))
    .map(node => ({ id: node.id, expanded: node.getAttribute("aria-expanded") === "true",
      // Uno can project the native header focus onto its template ToggleButton instead of the
      // outer Expander peer. The editor has one Expander, so this template part is unambiguous.
      focused: document.activeElement === node
        || document.activeElement?.getAttribute("xamlautomationid") === "ExpanderToggleButton" })));
  let expanders = await readExpanders();
  assert.equal(expanders.length, 1, "The editor must expose one advanced-settings expander.");
  if (!expanders[0].expanded) {
    // DOM focus on the mirrored Expander does not move Uno's native focus. Real Tab navigation
    // must reach the header before Space; otherwise the key can activate the previous page's owner.
    let direction = "Tab";
    for (let step = 0; !expanders[0].focused && step < 32; step++) {
      const previousFocus = await page.evaluate(() => ({ id: document.activeElement?.id,
        atEnd: document.activeElement === document.body || document.activeElement?.id === "uno-focus-sentinel-end" }));
      if (previousFocus.atEnd) direction = "Shift+Tab";
      await page.keyboard.press(direction);
      const changedFocus = await page.waitForFunction(previous => document.activeElement?.id !== previous,
        previousFocus.id, { timeout: 5_000 });
      await changedFocus.dispose();
      await page.evaluate(() => new Promise(requestAnimationFrame));
      expanders = await readExpanders();
      assert.equal(expanders.length, 1, "Native Tab navigation must stay in the profile editor.");
    }
    assert.ok(expanders[0].focused, "Native Tab navigation must reach the advanced-settings header.");
    await page.keyboard.press("Space");
  }
  await waitForLaidOutControl(page, bindButton, "expanded credential controls");
}

async function expectPersistedCredential(page, path, canary, present) {
  // Read the existing IDBFS database. Do not flush it or create a second database connection:
  // persistence belongs to the app, and the deleted file may already be absent from its live FS.
  const handle = await page.waitForFunction(async ({ path, canary, present }) => {
    const mount = globalThis.FS?.lookupPath("/local/SalmonEgg")?.node?.mount;
    const database = mount?.type?.dbs?.[mount.mountpoint];
    const storeName = mount?.type?.DB_STORE_NAME;
    if (!database || !storeName) return false;
    return await new Promise((resolve, reject) => {
      const transaction = database.transaction(storeName, "readonly");
      const request = transaction.objectStore(storeName).get(path);
      transaction.oncomplete = () => resolve(present
        ? new TextDecoder().decode(request.result?.contents ?? new Uint8Array()) === canary
        : request.result === undefined);
      transaction.onabort = () => reject(new Error("The credential persistence read was aborted."));
    });
  }, { path, canary, present }, { timeout: 20_000 }).catch(() => {
    throw new Error(`The local credential was not ${present ? "saved to" : "removed from"} IndexedDB; its value is omitted.`);
  });
  await handle.dispose();
}
