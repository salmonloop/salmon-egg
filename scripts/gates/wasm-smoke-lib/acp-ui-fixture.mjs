import { navigateToSettingsSection } from "./settings-shell.mjs";
import { openApp } from "./browser-app.mjs";
import {
  clickStartComposerSendButton,
  clickVisibleNavigationTarget,
  collectVisibleComboBoxDebug,
  collectVisibleInteractiveDebug,
  collectVisibleNavigationTargetDebug,
  escapeRegExp,
  expectComboBoxSelectionText,
  readControlState,
  scrollToVisibleNavigationTarget,
  selectComboBoxItem,
  typeIntoVisibleTextField,
  waitForBodyText,
  waitForControlState,
  waitForSemanticText
} from "./ui-affordances.mjs";

const profilesAddAffordance = { labels: ["新建配置", "New profile"], automationIds: ["Acp.Profiles.Add"] };
const profileEditorNameField = { labels: [], automationIds: ["Acp.ProfileEditor.Name"] };
const profileEditorAttempts = 3;

// The editor's arrival is the Name field turning up in the semantic tree, not its labels turning up
// in body text: those labels reach the DOM only as the inputs' accessible names, so a body-text wait
// for them can never pass on Skia. The activation is retried against that same proof - the page's
// nodes are published before its ViewModel is ready, and an activation that arrives too early is
// dropped by the command without the node ever reporting itself disabled, so the click reports
// success and nothing opens.
export async function createWebSocketProfile(page, profileName, serverUrl) {
  let opened = false;
  for (let attempt = 1; attempt <= profileEditorAttempts && !opened; attempt += 1) {
    await clickVisibleNavigationTarget(page, profilesAddAffordance);
    opened = Boolean(await scrollToVisibleNavigationTarget(page, profileEditorNameField, 8_000));
  }

  if (!opened) {
    throw new Error(
      `The ACP profile editor did not open after ${profileEditorAttempts} activations of its New affordance.`);
  }

  await fillProfileEditorTextBoxes(page, profileName, serverUrl);
  await clickVisibleNavigationTarget(page, { labels: ["保存", "Save"], automationIds: [] });
  try {
    // Saving must return the list with the new profile on it. Both halves are read from the semantic
    // tree: the page's own affordance for "we are back on the list", and the profile's name wherever
    // the tree carries it - a list item's title reaches the DOM as an accessible name, so waiting for
    // it in body text would time out on a profile the user can plainly see.
    await waitForControlState(page, profilesAddAffordance, "ACP Agent settings page after profile save");
    await waitForSemanticText(page, new RegExp(escapeRegExp(profileName)), "saved ACP profile");
    return;
  } catch (error) {
    const debug = await page.evaluate(() => ({
      inputs: Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          const type = element.getAttribute("type")?.toLowerCase() ?? "";
          return {
            tag: element.tagName,
            className: element.className?.toString?.() ?? "",
            top: Math.round(rect.top),
            left: Math.round(rect.left),
            width: Math.round(rect.width),
            height: Math.round(rect.height),
            value: element.value ?? "",
            text: (element.textContent ?? "").trim(),
            aria: element.getAttribute("aria-label") ?? "",
            role: element.getAttribute("role") ?? "",
            placeholder: element.getAttribute("placeholder") ?? "",
            readOnly: element.readOnly === true,
            disabled: element.disabled === true,
            contentEditable: element.getAttribute("contenteditable") ?? "",
            type,
            visible: rect.width > 0
              && rect.height > 0
              && style.display !== "none"
              && style.visibility !== "hidden"
              && rect.left >= -1
              && rect.top >= -1
              && rect.left <= innerWidth
              && rect.top <= innerHeight
          };
        })
        .filter(candidate => candidate.visible),
      body: (document.body?.innerText ?? "").slice(0, 2_000)
    }));
    // This whole block only exists to say *why* the save was not observable, so it must never become
    // the reported failure itself: a reload that does not come back would otherwise replace the real
    // error with a bare selector timeout, which is exactly what it used to do.
    let persistedAfterReload = null;
    try {
      await page.reload({ waitUntil: "domcontentloaded", timeout: 60_000 });
      await openApp(page, page.url());
      await navigateToSettingsSection(
        page,
        { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
        /ACP Agent|ACP 连接配置|ACP connection profiles/,
        "ACP Agent settings page after forced reload");
      persistedAfterReload = await page.evaluate(
        name => Array.from(document.querySelectorAll("#uno-semantics-root [id^='uno-semantics-']"))
          .some(node => !node.hidden
            && (`${node.getAttribute("aria-label") ?? ""}|${node.textContent ?? ""}`).includes(name)),
        profileName);
    } catch (diagnosticError) {
      throw new Error(
        `Saving the ACP profile was not observable: ${error?.message ?? error} `
        + `The reload used to tell persistence apart from a UI hang also failed `
        + `(${diagnosticError?.message ?? diagnosticError}), so which one it is stays unknown. `
        + `Debug=${JSON.stringify(debug)}`,
        { cause: error });
    }

    if (persistedAfterReload) {
      throw new Error(
        `Saving ACP profile persisted across reload, but the editor never returned to the settings list. `
        + `This points to a post-save UI hang in the WASM editor/refresh path. `
        + `Debug=${JSON.stringify(debug)}. Cause=${error?.message ?? error}`);
    }

    throw new Error(
      `Saving ACP profile did not persist across reload, so WASM ACP settings save failed before the profile list refreshed. `
      + `Debug=${JSON.stringify(debug)}. Cause=${error?.message ?? error}`);
  }
}

export async function expectProfilePresence(page, profileName, label) {
  await waitForSemanticText(page, new RegExp(escapeRegExp(profileName)), label);
}

// Same shape as the profile editor above: the editor's own field is the arrival proof (its label
// exists only as that input's accessible name), and the activation is retried against it because an
// activation delivered before the row's ViewModel is ready is dropped without a trace.
const remoteDirectoryAddAffordance = {
  labels: ["新增远程项目", "Add remote project"],
  automationIds: ["Acp.RemoteDirectories.Add"]
};
const remoteDirectoryNameField = { labels: [], automationIds: ["Acp.RemoteDirectories.DisplayName"] };

export async function createRemoteDirectory(page, displayName, remotePath) {
  await scrollToVisibleNavigationTarget(page, remoteDirectoryAddAffordance);
  let opened = false;
  for (let attempt = 1; attempt <= profileEditorAttempts && !opened; attempt += 1) {
    await clickVisibleNavigationTarget(page, remoteDirectoryAddAffordance);
    opened = Boolean(await scrollToVisibleNavigationTarget(page, remoteDirectoryNameField, 8_000));
  }

  if (!opened) {
    throw new Error(
      `The remote directory editor did not open after ${profileEditorAttempts} activations of its Add affordance.`);
  }

  await typeIntoVisibleTextField(
    page,
    { labels: ["项目名称", "Project name"], automationIds: ["Acp.RemoteDirectories.DisplayName"] },
    displayName,
    "remote directory display name");
  await typeIntoVisibleTextField(
    page,
    { labels: ["ACP 工作路径", "ACP working path"], automationIds: ["Acp.RemoteDirectories.RemotePath"] },
    remotePath,
    "remote directory path");
  await clickVisibleNavigationTarget(page, { labels: ["保存", "Save"], automationIds: ["Acp.RemoteDirectories.Save"] });
  await expectRemoteDirectoryPresence(page, displayName, remotePath, "saved remote directory");
}

export async function expectRemoteDirectoryPresence(page, displayName, remotePath, label) {
  // Read from the semantic tree for the same reason as the profile list above: a row's title and
  // subtitle can exist only as accessible names.
  await waitForSemanticText(page, new RegExp(escapeRegExp(displayName)), `${label} name`);
  await waitForSemanticText(page, new RegExp(escapeRegExp(remotePath)), `${label} path`);
}

export async function expectPersistedProfileAfterReload(page, baseUrl, profileName) {
  let lastError;

  for (let attempt = 1; attempt <= 2; attempt += 1) {
    try {
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
      await page.setViewportSize({ width: 1280, height: 900 });
      // openApp owns what "the app is up" means - the shell's own landmarks, the splash being gone,
      // and a readable failure when it is not. The hand-rolled selector wait here reported a bare
      // 60s timeout on a blank page, which said nothing about whether the reload had even started
      // rendering, and it did not wait out the splash that swallows the first pointer gestures.
      await openApp(page, baseUrl);
      await navigateToSettingsSection(
        page,
        { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
        /ACP Agent|ACP 连接配置|ACP connection profiles/,
        attempt === 1 ? "ACP Agent settings page after reload" : "ACP Agent settings page after retry reload");
      await expectProfilePresence(
        page,
        profileName,
        attempt === 1 ? "persisted ACP profile" : "persisted ACP profile after retry reload");
      return;
    } catch (error) {
      lastError = error;
      if (attempt < 2) {
        await page.waitForTimeout(2_000);
      }
    }
  }

  const storageDebug = await page.evaluate(() => {
    const result = {
      body: (document.body?.innerText ?? "").slice(0, 2_000),
      serverFiles: [],
      errors: []
    };

    try {
      const fs = globalThis.FS;
      if (!fs) {
        result.errors.push("globalThis.FS unavailable");
        return result;
      }

      const directory = "/local/SalmonEgg/config/servers";
      const entries = fs.readdir(directory).filter(name => name !== "." && name !== "..");
      result.serverFiles = entries.map(name => {
        const path = `${directory}/${name}`;
        let content = "";
        try {
          content = fs.readFile(path, { encoding: "utf8" });
        } catch (error) {
          content = `read error: ${error?.message ?? error}`;
        }

        return {
          name,
          content
        };
      });
    } catch (error) {
      result.errors.push(error?.message ?? String(error));
    }

    return result;
  });

  throw new Error(
    `ACP profile was not visible after reload. `
    + `StorageDebug=${JSON.stringify(storageDebug)}. `
    + `Cause=${lastError?.message ?? lastError}`);
}

// The row's ToggleSwitch carries no automation id of its own, so it is found by walking up from the
// profile's name to the row and taking the switch inside it. Activation goes through the semantic
// node's own click, which is what Uno programs the Toggle pattern onto; a real pointer at the
// reported centre does not reach it, because the node has pointer-events: none and the canvas hit
// test does not pick the switch up (measured: aria-checked stayed false and no connection started).
//
// One activation is all this does. The switch reflects IsConnected, not the request, so it stays off
// until the connection is actually up - waiting for it to flip here would be waiting for the very
// thing the caller is about to assert.
export async function clickProfileConnectionToggle(page, profileName) {
  await waitForSemanticText(page, new RegExp(escapeRegExp(profileName)), `profile row for '${profileName}'`);

  const activated = await page.evaluate(activateProfileConnectionToggle, profileName);
  if (!activated?.found) {
    const debug = await page.evaluate(collectVisibleInteractiveDebug);
    throw new Error(`No connection toggle found for profile '${profileName}'. Candidates: ${JSON.stringify(debug)}`);
  }

  if (activated.disabled) {
    throw new Error(
      `The connection toggle for profile '${profileName}' is disabled, so the connection cannot be started.`);
  }

  await page.waitForTimeout(500);
}

export async function ensureAcpProfilesReady(page) {
  try {
    await page.waitForFunction(readAcpProfilesAnchorState, null, { timeout: 10_000 });
  } catch (error) {
    const debug = {
      profilesAnchor: await page.evaluate(readAcpProfilesAnchorState).catch(diagnosticError => `read error: ${diagnosticError?.message ?? diagnosticError}`),
      interactive: await page.evaluate(collectVisibleInteractiveDebug).catch(diagnosticError => [`read error: ${diagnosticError?.message ?? diagnosticError}`]),
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000)
    };
    throw new Error(
      `ACP settings profiles section was not visible. Debug=${JSON.stringify(debug)} `
      + `Cause=${error?.message ?? error}`);
  }
}

export async function waitForInitializeWithDiagnostics(acpServer, page, profileName) {
  try {
    return await acpServer.waitForInitialize();
  } catch (error) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      profilesAnchor: await page.evaluate(readAcpProfilesAnchorState).catch(diagnosticError => `read error: ${diagnosticError?.message ?? diagnosticError}`),
      rowState: await page.evaluate(readProfileConnectionRowState, profileName).catch(diagnosticError => `read error: ${diagnosticError?.message ?? diagnosticError}`),
      interactive: await page.evaluate(collectVisibleInteractiveDebug).catch(diagnosticError => [`read error: ${diagnosticError?.message ?? diagnosticError}`])
    };
    throw new Error(
      `Timed out waiting for ACP initialize request. PageDebug=${JSON.stringify(debug)}. `
      + `Cause=${error?.message ?? error}`);
  }
}

export async function createSessionAndSendPromptFromStart(
  page,
  acpServer,
  profileName,
  directoryName,
  directoryPath,
  promptText,
  expectedAgentReply) {
  // The composer is located by shape, not by id: StartView sets its automation id through an x:Bind
  // on AutomationProperties.AutomationId, which never reaches the exported accessibility node on
  // Skia - the node has no id and its accessible name is the localized placeholder. The Start shell
  // has exactly one multi-line text box, which is stable in a way the placeholder wording is not.
  const promptBoxSelector = "#uno-semantics-root textarea";
  await ensureStartPromptVisible(page, promptBoxSelector);

  // Matched on the ComboBox's own x:Name, which Uno exports as the automation id when none is set
  // explicitly. The ids the hosts pass in (StartView.AgentSelector and friends) are applied through an
  // x:Bind on AutomationProperties.AutomationId and never reach the exported node, so nothing in the
  // accessibility view carries them.
  await selectComboBoxItem(
    page,
    "AgentSelectorHost",
    profileName,
    { keyboardSelectVisibleItem: true });
  await selectComboBoxItem(
    page,
    "ProjectSelectorHost",
    directoryName,
    { verifySelectionText: false, keyboardSelectVisibleItem: true });
  const sessionNewRequest = await waitForSessionNewWithDiagnostics(acpServer, page);
  const requestedCwd = sessionNewRequest?.params?.cwd;
  if (requestedCwd !== directoryPath) {
    throw new Error(`session/new used unexpected cwd. Expected=${directoryPath} Request=${JSON.stringify(sessionNewRequest)}`);
  }

  // The mode selector is what shows the session's modes arrived, and a collapsed ComboBox on Skia
  // mirrors no selection text at all - its value is only readable by opening the dropdown, which is
  // what this helper does. Waiting for the mode names in body text could never pass here.
  await expectComboBoxSelectionText(
    page,
    "ModeSelectorHost",
    ["Agent 01", "Planner 01"],
    "ready ACP modes after remote directory selection");
  await typeIntoVisibleTextField(page, { selector: promptBoxSelector }, promptText, "start composer prompt");
  await clickStartComposerSendButton(page);

  const promptRequest = await waitForSessionPromptWithDiagnostics(acpServer, page);
  const promptTextFromRequest = extractPromptText(promptRequest);
  if (promptTextFromRequest !== promptText) {
    throw new Error(`session/prompt used unexpected text. Expected=${promptText} Request=${JSON.stringify(promptRequest)}`);
  }

  // Read from the semantic tree: chat turns reach the DOM as accessible names on their message nodes,
  // never as body text, so the reply a user can read is invisible to a body-text wait on Skia.
  await waitForSemanticText(page, /ChatView\.MessagesList|Salmon Egg|WASM full chain agent reply/, "chat view after prompt", 30_000);
  await waitForSemanticText(page, new RegExp(escapeRegExp(expectedAgentReply)), "agent reply projected into chat UI", 30_000);
}

async function ensureStartPromptVisible(page, promptBoxSelector) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    if (await page.locator(promptBoxSelector).isVisible().catch(() => false)) {
      return;
    }

    try {
      await clickVisibleNavigationTarget(page, { labels: ["Start", "开始"], automationIds: ["MainNav.Start"] });
    } catch (error) {
      lastError = error;
    }

    await page.waitForTimeout(500);
  }

  const debug = {
    body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
    navigation: await page.evaluate(collectVisibleNavigationTargetDebug).catch(error => [`debug error: ${error?.message ?? error}`]),
    prompt: await page.locator(promptBoxSelector).evaluate(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {
        className: element.className?.toString?.() ?? "",
        display: style.display,
        visibility: style.visibility,
        width: rect.width,
        height: rect.height,
        top: rect.top,
        left: rect.left
      };
    }).catch(error => ({ error: error?.message ?? String(error) }))
  };
  throw new Error(
    `Start prompt did not become visible after navigating to Start. `
    + `LastError=${lastError?.message ?? lastError}. Debug=${JSON.stringify(debug)}`);
}

async function fillProfileEditorTextBoxes(page, profileName, serverUrl) {
  await typeIntoVisibleTextField(
    page,
    { labels: ["名称", "Name"], automationIds: ["Acp.ProfileEditor.Name"] },
    profileName,
    "ACP profile name");
  await typeIntoVisibleTextField(
    page,
    { labels: ["服务器地址", "Server URL"], automationIds: ["Acp.ProfileEditor.ServerUrl"] },
    serverUrl,
    "ACP profile server url");
}

async function waitForSessionNewWithDiagnostics(acpServer, page) {
  try {
    return await acpServer.waitForSessionNew();
  } catch (error) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
    };
    throw new Error(
      `Timed out waiting for ACP session/new request. PageDebug=${JSON.stringify(debug)} `
      + `Cause=${error?.message ?? error}`);
  }
}

async function waitForSessionPromptWithDiagnostics(acpServer, page) {
  try {
    return await acpServer.waitForSessionPrompt();
  } catch (error) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      interactive: await page.evaluate(collectVisibleInteractiveDebug)
    };
    throw new Error(
      `Timed out waiting for ACP session/prompt request. PageDebug=${JSON.stringify(debug)} `
      + `Cause=${error?.message ?? error}`);
  }
}

function extractPromptText(promptRequest) {
  const prompt = promptRequest?.params?.prompt;
  if (!Array.isArray(prompt)) {
    return null;
  }

  return prompt
    .filter(block => block?.type === "text")
    .map(block => block.text ?? "")
    .join("");
}

function readAcpProfilesAnchorState() {
  const control = window.__salmoneggSmoke.findVisibleControl(
    { automationIds: ["Acp.Profiles.Refresh"] },
    ["刷新", "Refresh"],
    ["Acp.Profiles.Refresh"]);
  return control ? { found: true } : null;
}

// Runs inside the page, so the row walk is inlined: page.evaluate ships only this function's body,
// and a helper referenced from module scope does not exist in the browser.
function activateProfileConnectionToggle(profileName) {
  const isOnScreen = rect => rect.width > 0
    && rect.height > 0
    && rect.left >= 0
    && rect.top >= 0
    && rect.left <= innerWidth
    && rect.top <= innerHeight;

  const nameNode = Array.from(document.querySelectorAll("body *"))
    .find(element => isOnScreen(element.getBoundingClientRect())
      && (element.textContent ?? "").trim() === profileName);

  let container = nameNode;
  while (container && container !== document.body) {
    const toggle = Array.from(container.querySelectorAll("*"))
      .map(element => {
        const className = element.className?.toString?.() ?? "";
        return {
          element,
          rect: element.getBoundingClientRect(),
          isToggle: element.matches("input[type='checkbox']")
            || element.getAttribute("role") === "switch"
            || element.getAttribute("aria-checked") != null
            || className.toLowerCase().includes("toggle")
        };
      })
      .filter(candidate => candidate.isToggle && isOnScreen(candidate.rect))
      .sort((left, right) => right.rect.right - left.rect.right)[0];

    if (toggle) {
      const element = toggle.element;
      if (element.getAttribute("aria-disabled") === "true" || element.disabled === true) {
        return { found: true, disabled: true };
      }

      element.click();
      return { found: true, disabled: false, checked: element.getAttribute("aria-checked") };
    }

    container = container.parentElement;
  }

  return { found: false };
}

function readProfileConnectionRowState(profileName) {
  const nameNode = Array.from(document.querySelectorAll("body *"))
    .find(element => {
      const rect = element.getBoundingClientRect();
      return rect.width > 0
        && rect.height > 0
        && rect.left >= 0
        && rect.top >= 0
        && rect.left <= innerWidth
        && rect.top <= innerHeight
        && (element.textContent ?? "").trim() === profileName;
    });

  let container = nameNode;
  while (container && container !== document.body) {
    const toggle = Array.from(container.querySelectorAll("input,[role='switch'],[aria-checked],.uno-toggleswitch,*"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        const className = element.className?.toString?.() ?? "";
        return {
          element,
          rect,
          className,
          isToggle:
            element.matches("input[type='checkbox']")
            || element.getAttribute("role") === "switch"
            || element.getAttribute("aria-checked") != null
            || className.toLowerCase().includes("toggle")
        };
      })
      .filter(candidate =>
        candidate.isToggle
        && candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.rect.left >= 0
        && candidate.rect.top >= 0
        && candidate.rect.left <= innerWidth
        && candidate.rect.top <= innerHeight)
      .sort((left, right) => right.rect.right - left.rect.right)[0];

    if (toggle) {
      const status = Array.from(container.querySelectorAll("span,div,p,text,body *"))
        .map(element => (element.textContent ?? "").trim())
        .find(text => /已连接|连接中|断开中|重连中|已断开|Connected|Connecting|Disconnecting|Reconnecting|Disconnected/i.test(text))
        ?? "";
      const ariaChecked = toggle.element.getAttribute("aria-checked");
      return {
        checked: ariaChecked != null
          ? ariaChecked === "true"
          : typeof toggle.element.checked === "boolean"
          ? toggle.element.checked
          : null,
        status,
        className: toggle.className,
        html: toggle.element.outerHTML
      };
    }

    container = container.parentElement;
  }

  return null;
}
