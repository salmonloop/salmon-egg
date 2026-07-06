import { navigateToSettingsSection } from "./settings-shell.mjs";
import {
  clickStartComposerSendButton,
  clickVisibleNavigationTarget,
  clickVisibleNavigationTargetUntilBodyText,
  collectVisibleComboBoxDebug,
  collectVisibleInteractiveDebug,
  collectVisibleNavigationTargetDebug,
  escapeRegExp,
  readControlState,
  scrollToVisibleNavigationTarget,
  selectComboBoxItem,
  typeIntoAutomationTextBox,
  typeIntoVisibleTextField,
  waitForBodyText
} from "./ui-affordances.mjs";

export async function createWebSocketProfile(page, profileName, serverUrl) {
  await clickVisibleNavigationTargetUntilBodyText(
    page,
    { labels: ["新建配置", "New profile"], automationIds: ["Acp.Profiles.Add"] },
    /名称|Name|服务器地址|Server URL/,
    "agent profile editor");

  await fillProfileEditorTextBoxes(page, profileName, serverUrl);
  await clickVisibleNavigationTarget(page, { labels: ["保存", "Save"], automationIds: [] });
  try {
    await waitForBodyText(page, /ACP Agent|ACP 连接配置|ACP connection profiles/, "ACP Agent settings page after profile save");
    await waitForBodyText(page, new RegExp(escapeRegExp(profileName)), "saved ACP profile");
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
    await page.reload({ waitUntil: "domcontentloaded", timeout: 60_000 });
    await page.waitForSelector('[aria-label="StartView.Title"]', { timeout: 60_000 });
    await navigateToSettingsSection(
      page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP 连接配置|ACP connection profiles/,
      "ACP Agent settings page after forced reload");

    const persistedAfterReload = await page.evaluate(
      name => (document.body?.innerText ?? "").includes(name),
      profileName);

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
  await waitForBodyText(page, new RegExp(escapeRegExp(profileName)), label);
}

export async function createRemoteDirectory(page, displayName, remotePath) {
  await scrollToVisibleNavigationTarget(page, { labels: ["新增远程项目", "Add remote project"], automationIds: ["Acp.RemoteDirectories.Add"] });
  await clickVisibleNavigationTarget(page, { labels: ["新增远程项目", "Add remote project"], automationIds: ["Acp.RemoteDirectories.Add"] });
  await waitForBodyText(page, /显示名称|Project name|ACP 工作路径|ACP working path/, "remote directory editor");

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
  await waitForBodyText(page, new RegExp(escapeRegExp(displayName)), `${label} name`);
  await waitForBodyText(page, new RegExp(escapeRegExp(remotePath)), `${label} path`);
}

export async function expectPersistedProfileAfterReload(page, baseUrl, profileName) {
  let lastError;

  for (let attempt = 1; attempt <= 2; attempt += 1) {
    try {
      await page.reload({ waitUntil: "domcontentloaded", timeout: 60_000 });
      await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
      await page.waitForSelector('[aria-label="StartView.Title"]', { timeout: 60_000 });
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

export async function clickProfileConnectionToggle(page, profileName) {
  await page.waitForFunction(
    name => (document.body?.innerText ?? "").includes(name),
    profileName,
    { timeout: 30_000 });

  const point = await page.evaluate(findProfileConnectionTogglePoint, profileName);
  if (!point) {
    const debug = await page.evaluate(collectVisibleInteractiveDebug);
    throw new Error(`No connection toggle found for profile '${profileName}'. Candidates: ${JSON.stringify(debug)}`);
  }

  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(500);
}

export async function ensureGlobalAcpEnabled(page) {
  const state = await readControlState(page, {
    labels: ["启用 ACP Agent", "Enable ACP Agent"],
    automationIds: ["Acp.Global.Enabled"]
  });

  if (!state?.found) {
    throw new Error(`Global ACP toggle was not found. State=${JSON.stringify(state)}`);
  }

  const checked = await page.evaluate(readGlobalAcpToggleState);
  if (checked !== false) {
    return;
  }

  await page.mouse.click(state.x, state.y);
  try {
    await page.waitForFunction(readGlobalAcpToggleState, null, { timeout: 10_000 });
  } catch (error) {
    const debug = {
      checked: await page.evaluate(readGlobalAcpToggleState).catch(diagnosticError => `read error: ${diagnosticError?.message ?? diagnosticError}`),
      interactive: await page.evaluate(collectVisibleInteractiveDebug).catch(diagnosticError => [`read error: ${diagnosticError?.message ?? diagnosticError}`]),
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000)
    };
    throw new Error(
      `Global ACP toggle remained disabled after click. State=${JSON.stringify(state)} Debug=${JSON.stringify(debug)} `
      + `Cause=${error?.message ?? error}`);
  }
}

export async function waitForInitializeWithDiagnostics(acpServer, page, profileName) {
  try {
    return await acpServer.waitForInitialize();
  } catch (error) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      globalAcpEnabled: await page.evaluate(readGlobalAcpToggleState).catch(diagnosticError => `read error: ${diagnosticError?.message ?? diagnosticError}`),
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
  await clickVisibleNavigationTargetUntilBodyText(
    page,
    { labels: ["Start", "开始"], automationIds: ["MainNav.Start"] },
    /Salmon Egg/,
    "start page");
  await page.waitForSelector('[aria-label="StartView.PromptBox"]', { timeout: 30_000 });

  await selectComboBoxItem(
    page,
    "StartView.AgentSelector",
    profileName,
    { keyboardSelectVisibleItem: true });
  await waitForBodyText(
    page,
    new RegExp(escapeRegExp(profileName)),
    "selected ACP profile on Start",
    30_000);
  await selectComboBoxItem(
    page,
    "StartView.ProjectSelector",
    directoryName,
    { verifySelectionText: false, keyboardSelectVisibleItem: true });
  const sessionNewRequest = await waitForSessionNewWithDiagnostics(acpServer, page);
  const requestedCwd = sessionNewRequest?.params?.cwd;
  if (requestedCwd !== directoryPath) {
    throw new Error(`session/new used unexpected cwd. Expected=${directoryPath} Request=${JSON.stringify(sessionNewRequest)}`);
  }

  await waitForBodyText(page, /Agent 01|Planner 01/, "ready ACP modes after remote directory selection", 30_000);
  await typeIntoAutomationTextBox(page, "StartView.PromptBox", promptText);
  await clickStartComposerSendButton(page);

  const promptRequest = await waitForSessionPromptWithDiagnostics(acpServer, page);
  const promptTextFromRequest = extractPromptText(promptRequest);
  if (promptTextFromRequest !== promptText) {
    throw new Error(`session/prompt used unexpected text. Expected=${promptText} Request=${JSON.stringify(promptRequest)}`);
  }

  await waitForBodyText(page, /ChatView\.MessagesList|Salmon Egg|WASM full chain agent reply/, "chat view after prompt", 30_000);
  await waitForBodyText(page, new RegExp(escapeRegExp(expectedAgentReply)), "agent reply projected into chat UI", 30_000);
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

function readGlobalAcpToggleState() {
  const control = window.__salmoneggSmoke.findVisibleControl(
    { automationIds: ["Acp.Global.Enabled"] },
    ["启用 ACP Agent", "Enable ACP Agent"],
    ["Acp.Global.Enabled"]);
  if (!control) {
    return null;
  }

  const toggle = control.matches("input,[role='switch'],[aria-checked]")
    ? control
    : control.querySelector("input,[role='switch'],[aria-checked]") ?? control;
  const ariaChecked = toggle.getAttribute("aria-checked");
  if (ariaChecked === "true") {
    return true;
  }

  if (ariaChecked === "false") {
    return false;
  }

  if (typeof toggle.checked === "boolean") {
    return toggle.checked;
  }

  return null;
}

function findProfileConnectionTogglePoint(profileName) {
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
      return window.__salmoneggSmoke.resolveToggleClickPoint(toggle.element);
    }

    container = container.parentElement;
  }

  return null;
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
