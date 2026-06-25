import { createHash } from "node:crypto";
import { createServer } from "node:http";
import { chromium } from "playwright";

const baseUrl = normalizeBaseUrl(process.argv[2]);
const fatalConsolePattern =
  /ArgumentOutOfRange|NativeDispatcher unhandled exception|NavigationView\.GetItemFromIndex|System\.ArgumentOutOfRangeException|Unhandled exception/i;
const dataStorageCacheRetentionAutomationId = "DataStorage.CacheRetention";
const dataStorageOpenCacheFolderAutomationId = "DataStorage.OpenCacheFolder";
const dataStorageOpenExportsAutomationId = "DataStorage.OpenExports";
const remoteDirectoryName = `WASM remote project ${Date.now()}`;
const remoteDirectoryPath = `/remote/wasm-full-chain-${Date.now()}`;
const fullChainPromptText = `WASM full chain prompt ${Date.now()}`;
const fullChainAgentReplyText = `WASM full chain agent reply ${Date.now()}`;
const domHelperScript = `
(() => {
  window.__salmoneggSmoke = {
    resolveToggleClickPoint(toggleElement) {
      const interactiveDescendants = [toggleElement, ...toggleElement.querySelectorAll("*")]
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          return {
            rect,
            pointerEvents: style.pointerEvents,
            element
          };
        })
        .filter(candidate =>
          candidate.pointerEvents !== "none"
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.rect.left >= 0
          && candidate.rect.top >= 0
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight)
        .sort((left, right) => {
          const leftArea = left.rect.width * left.rect.height;
          const rightArea = right.rect.width * right.rect.height;
          return rightArea - leftArea;
        });

      const target = interactiveDescendants[0];
      if (!target) {
        const rect = toggleElement.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0
          ? { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
          : null;
      }

      return {
        x: target.rect.left + target.rect.width / 2,
        y: target.rect.top + target.rect.height / 2
      };
    },

    findVisibleControl(input, labels, automationIds) {
      const normalize = value => (value ?? "").trim().toLowerCase();
      const normalizedLabels = labels.map(normalize).filter(Boolean);
      const normalizedAutomationIds = automationIds.map(normalize).filter(Boolean);
      const nodes = Array.from(document.querySelectorAll("body *"))
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          const text = (element.textContent ?? "").trim();
          const aria = element.getAttribute("aria-label") ?? "";
          const automationId =
            element.getAttribute("data-automation-id")
            ?? element.getAttribute("data-automationid")
            ?? element.getAttribute("automationid")
            ?? "";

          return {
            element,
            rect,
            text,
            aria,
            automationId,
            display: style.display,
            visibility: style.visibility,
            automationMatch:
              normalizedAutomationIds.includes(normalize(aria))
              || normalizedAutomationIds.includes(normalize(automationId)),
            textMatch:
              normalizedLabels.some(label => normalize(text).includes(label))
              || normalizedLabels.some(label => normalize(aria).includes(label))
          };
        })
        .filter(candidate =>
          (candidate.automationMatch || candidate.textMatch)
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.display !== "none"
          && candidate.visibility !== "hidden"
          && candidate.rect.left >= -1
          && candidate.rect.top >= -1
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight);

      nodes.sort((left, right) => {
        if (left.automationMatch !== right.automationMatch) {
          return left.automationMatch ? -1 : 1;
        }

        return (left.rect.width * left.rect.height) - (right.rect.width * right.rect.height);
      });

      return nodes[0]?.element ?? null;
    },

    collectTopNavigationButtonCandidates() {
      return Array.from(document.querySelectorAll("body *"))
        .map(element => {
          const rect = element.getBoundingClientRect();
          return {
            element,
            rect: {
              left: rect.left,
              top: rect.top,
              right: rect.right,
              bottom: rect.bottom,
              width: rect.width,
              height: rect.height
            },
            text: (element.textContent ?? "").trim(),
            aria: element.getAttribute("aria-label") ?? "",
            title: element.getAttribute("title") ?? "",
            role: element.getAttribute("role") ?? "",
            className: element.className?.toString?.() ?? ""
          };
        })
        .filter(candidate =>
          candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.rect.left >= 0
          && candidate.rect.top >= 16
          && candidate.rect.top <= 96
          && candidate.rect.right <= innerWidth + 1
          && candidate.rect.width <= 80
          && candidate.rect.height <= 80
          && (
            candidate.role === "button"
            || candidate.className.includes("uno-button")
            || candidate.text === "\\uE10C"
            || candidate.text === "\\uE712"
            || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.aria)
            || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.title)))
        .sort((left, right) => right.rect.right - left.rect.right);
    }
  };
})();
`;

const acpServer = await startAcpWebSocketServer();
const browser = await chromium.launch({ headless: true });

try {
  await clearBrowserOriginStorage(browser, baseUrl);

  const fatalConsoleMessages = [];
  const context = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    deviceScaleFactor: 1
  });
  await context.addInitScript({ content: domHelperScript });
  const page = await context.newPage();

  page.on("console", message => {
    const text = message.text();
    if (fatalConsolePattern.test(text)) {
      fatalConsoleMessages.push({ type: message.type(), text });
    }
  });

  page.on("pageerror", error => {
    const text = error.stack ?? error.message;
    if (fatalConsolePattern.test(text)) {
      fatalConsoleMessages.push({ type: "pageerror", text });
    }
  });

  await openApp(page);
  await navigateToSettingsSection(
    page,
    { labels: ["数据与存储", "Data storage", "Data"], automationIds: ["SettingsNav.DataStorage"] },
    /数据与存储|Data storage|Save local history|缓存保留天数|Cache retention/,
    "data storage settings page");

  await navigateToSettingsSection(
    page,
    { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
    /ACP Agent|ACP 连接配置|ACP connection profiles/,
    "ACP Agent settings page");

  const profileName = `WASM smoke ${Date.now()}`;
  await createWebSocketProfile(page, profileName, acpServer.url);
  await expectProfilePresence(page, profileName, "saved ACP profile");
  await createRemoteDirectory(page, remoteDirectoryName, remoteDirectoryPath);

  await page.waitForTimeout(1_500);
  await expectPersistedProfileAfterReload(page, profileName);
  await expectRemoteDirectoryPresence(page, remoteDirectoryName, remoteDirectoryPath, "persisted remote directory");

  await ensureGlobalAcpEnabled(page);
  await clickProfileConnectionToggle(page, profileName);
  const initializeRequest = await waitForInitializeWithDiagnostics(acpServer, page, profileName);
  expectNoAdvertisedFileSystemCapability(initializeRequest);
  await createSessionAndSendPromptFromStart(
    page,
    acpServer,
    profileName,
    remoteDirectoryName,
    remoteDirectoryPath,
    fullChainPromptText,
    fullChainAgentReplyText);

  if (fatalConsoleMessages.length > 0) {
    throw new Error(`Fatal console errors detected: ${JSON.stringify(fatalConsoleMessages, null, 2)}`);
  }

  console.log("WASM file system availability and ACP chat full-chain smoke passed");
} finally {
  await browser.close();
  await acpServer.close();
}

function normalizeBaseUrl(value) {
  if (!value || !value.trim()) {
    throw new Error("usage: wasm-file-system-availability-smoke.mjs <base-url>");
  }

  return value.endsWith("/") ? value : `${value}/`;
}

async function openApp(page) {
  await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
  await page.waitForSelector('[aria-label="StartView.Title"]', { timeout: 60_000 });
}

async function clearBrowserOriginStorage(browser, targetUrl) {
  const context = await browser.newContext();
  const page = await context.newPage();
  const origin = new URL(targetUrl).origin;

  await page.goto(targetUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
  const cdp = await context.newCDPSession(page);
  await cdp.send("Storage.clearDataForOrigin", {
    origin,
    storageTypes: "indexeddb,local_storage,cache_storage,websql,service_workers"
  });
  await page.evaluate(async () => {
    if (!indexedDB.databases) {
      return;
    }

    const databases = await indexedDB.databases();
    await Promise.all(databases
      .map(database => database.name)
      .filter(Boolean)
      .map(name => new Promise((resolve, reject) => {
        const request = indexedDB.deleteDatabase(name);
        request.onsuccess = () => resolve();
        request.onblocked = () => resolve();
        request.onerror = () => reject(request.error);
      })));
  });

  await context.close();
}

async function navigateToSettingsSection(page, sectionTarget, bodyPattern, label) {
  const settingsNavigationTarget = {
    labels: ["设置", "Settings"],
    automationIds: ["SettingsItem"]
  };

  await ensureVisibleNavigationTarget(page, settingsNavigationTarget, {
    labels: [],
    automationIds: ["TitleBar.ToggleSidebar"]
  });
  await clickVisibleNavigationTargetUntilBodyText(
    page,
    settingsNavigationTarget,
    /常规|General|外观|Appearance|ACP Agent|ACP \/ Agent/,
    "settings shell");

  if (await page.evaluate(findVisibleNavigationTargetPoint, sectionTarget)) {
    await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
    return;
  }

  await page.setViewportSize({ width: 390, height: 844 });
  await waitForBodyText(page, /常规|General|外观|Appearance|ACP Agent|ACP \/ Agent/, "settings shell at mobile viewport");
  await clickTopNavigationOverflow(page);
  await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
  await page.setViewportSize({ width: 1280, height: 900 });
}

async function createWebSocketProfile(page, profileName, serverUrl) {
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

async function expectProfilePresence(page, profileName, label) {
  await waitForBodyText(page, new RegExp(escapeRegExp(profileName)), label);
}

async function createRemoteDirectory(page, displayName, remotePath) {
  await scrollToVisibleNavigationTarget(page, { labels: ["新增远程项目", "Add remote project"], automationIds: ["Acp.RemoteDirectories.Add"] });
  await clickVisibleNavigationTarget(page, { labels: ["新增远程项目", "Add remote project"], automationIds: ["Acp.RemoteDirectories.Add"] });
  await waitForBodyText(page, /显示名称|Project name|ACP 工作路径|ACP working path/, "remote directory editor");

  const fields = await page.evaluate(collectVisibleTextInputPoints);
  const editableFields = fields.filter(field => field.top >= 120);
  if (editableFields.length < 2) {
    throw new Error(`Expected two remote directory editor fields. Fields=${JSON.stringify(fields)}`);
  }

  await typeIntoField(page, editableFields[0], displayName);
  await typeIntoField(page, editableFields[1], remotePath);
  await clickVisibleNavigationTarget(page, { labels: ["保存", "Save"], automationIds: ["Acp.RemoteDirectories.Save"] });
  await expectRemoteDirectoryPresence(page, displayName, remotePath, "saved remote directory");
}

async function expectRemoteDirectoryPresence(page, displayName, remotePath, label) {
  await waitForBodyText(page, new RegExp(escapeRegExp(displayName)), `${label} name`);
  await waitForBodyText(page, new RegExp(escapeRegExp(remotePath)), `${label} path`);
}

async function expectPersistedProfileAfterReload(page, profileName) {
  let lastError;

  for (let attempt = 1; attempt <= 2; attempt += 1) {
    try {
      await page.reload({ waitUntil: "domcontentloaded", timeout: 60_000 });
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

async function fillProfileEditorTextBoxes(page, profileName, serverUrl) {
  const fields = await page.evaluate(() =>
    Array.from(document.querySelectorAll("textarea"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return {
          top: rect.top,
          left: rect.left,
          width: rect.width,
          height: rect.height,
          value: element.value ?? "",
          visible: rect.width > 0
            && rect.height > 0
            && style.display !== "none"
            && style.visibility !== "hidden"
            && rect.left >= -1
            && rect.top >= 120
            && rect.left <= innerWidth
            && rect.top <= innerHeight
        };
      })
      .filter(candidate => candidate.visible)
      .sort((left, right) => (left.top - right.top) || (left.left - right.left)));

  if (fields.length < 2) {
    throw new Error(`Expected at least two profile editor text boxes. Fields: ${JSON.stringify(fields)}`);
  }

  await typeIntoField(page, fields[0], profileName);
  await typeIntoField(page, fields[1], serverUrl);
}

async function typeIntoField(page, field, value) {
  await page.mouse.click(field.left + (field.width / 2), field.top + (field.height / 2));
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.type(value);
  await page.keyboard.press("Tab");
  await page.waitForTimeout(150);
}

async function clickProfileConnectionToggle(page, profileName) {
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

async function ensureGlobalAcpEnabled(page) {
  const state = await page.evaluate(readControlEnabledState, {
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
    const debug = await page.evaluate(() => ({
      checked: readGlobalAcpToggleState(),
      interactive: collectVisibleInteractiveDebug(),
      body: (document.body?.innerText ?? "").slice(0, 2_000)
    }));
    throw new Error(
      `Global ACP toggle remained disabled after click. State=${JSON.stringify(state)} Debug=${JSON.stringify(debug)} `
      + `Cause=${error?.message ?? error}`);
  }
}

async function waitForInitializeWithDiagnostics(acpServer, page, profileName) {
  try {
    return await acpServer.waitForInitialize();
  } catch (error) {
    const debug = await page.evaluate(name => ({
      body: (document.body?.innerText ?? "").slice(0, 2_000),
      globalAcpEnabled: readGlobalAcpToggleState(),
      rowState: readProfileConnectionRowState(name),
      interactive: collectVisibleInteractiveDebug()
    }), profileName);
    throw new Error(
      `Timed out waiting for ACP initialize request. PageDebug=${JSON.stringify(debug)}. `
      + `Cause=${error?.message ?? error}`);
  }
}

async function createSessionAndSendPromptFromStart(
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
    profileName);
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

async function expectControlDoesNotEscapePage(page, options, stayOnPagePattern) {
  const beforeUrl = page.url();
  const state = await page.evaluate(readControlEnabledState, options);
  const point = state.found && Number.isFinite(state.x) && Number.isFinite(state.y)
    ? { x: state.x, y: state.y }
    : null;
  if (!point) {
    throw new Error(`Expected control was not found for escape check: ${JSON.stringify(options)} state=${JSON.stringify(state)}`);
  }

  await page.mouse.click(point.x, point.y);
  try {
    await waitForBodyText(
      page,
      /当前平台暂不支持打开本地文件或目录|Opening local files or folders is not supported on this platform/,
      "unsupported platform dialog",
      2_000);
    await dismissDialogIfPresent(page);
    return;
  } catch {
  }

  if (page.url() !== beforeUrl) {
    throw new Error(`Expected control ${JSON.stringify(options)} to stay on the current page, but url changed to ${page.url()}.`);
  }

  await waitForBodyText(page, stayOnPagePattern, "data storage page after external open attempt", 5_000);
}

function expectNoAdvertisedFileSystemCapability(initializeRequest) {
  const clientCapabilities = initializeRequest?.params?.clientCapabilities;
  if (!clientCapabilities || typeof clientCapabilities !== "object") {
    throw new Error(`Initialize request did not include clientCapabilities: ${JSON.stringify(initializeRequest)}`);
  }

  if (Object.prototype.hasOwnProperty.call(clientCapabilities, "fs")) {
    throw new Error(`WASM client must not advertise ACP fs capability: ${JSON.stringify(clientCapabilities)}`);
  }

  if (clientCapabilities.terminal === true) {
    throw new Error(`WASM client must not advertise ACP terminal capability: ${JSON.stringify(clientCapabilities)}`);
  }
}

async function clickTopNavigationOverflow(page) {
  await page.waitForFunction(findTopNavigationOverflowPoint, null, { timeout: 30_000 });
  const point = await page.evaluate(findTopNavigationOverflowPoint);

  if (!point) {
    const candidates = await page.evaluate(collectTopNavigationButtonCandidateDebug);
    throw new Error(`Settings overflow button was not visible. Candidates: ${JSON.stringify(candidates)}`);
  }

  await page.mouse.click(point.x, point.y);
}

async function scrollToVisibleNavigationTarget(page, options) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, options)) {
    return;
  }

  const scrolled = await page.evaluate(input => {
    const labels = input.labels ?? [];
    const automationIds = input.automationIds ?? [];
    const normalize = value => (value ?? "").trim().toLowerCase();
    const target = Array.from(document.querySelectorAll("body *"))
      .find(element => {
        const text = (element.textContent ?? "").trim();
        const aria = element.getAttribute("aria-label") ?? "";
        const automationId =
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "";
        return automationIds.includes(aria)
          || automationIds.includes(automationId)
          || labels.map(normalize).includes(normalize(text))
          || labels.map(normalize).includes(normalize(aria));
      });

    target?.scrollIntoView({ block: "center", inline: "nearest" });
    return Boolean(target);
  }, options);

  if (!scrolled) {
    return;
  }

  await page.waitForFunction(findVisibleNavigationTargetPoint, options, { timeout: 5_000 });
}

async function selectComboBoxItem(page, selectorAutomationId, expectedVisibleName, options = {}) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    await clickVisibleControl(page, { labels: [], automationIds: [selectorAutomationId] });
    try {
      await page.waitForFunction(
        findVisibleNavigationTargetPoint,
        { labels: [expectedVisibleName], automationIds: [] },
        { timeout: Math.min(3_000, Math.max(250, deadline - Date.now())) });
      break;
    } catch (error) {
      lastError = error;
      await page.keyboard.press("Escape").catch(() => {});
      await page.waitForTimeout(500);
    }
  }

  if (!await page.evaluate(findVisibleNavigationTargetPoint, { labels: [expectedVisibleName], automationIds: [] })) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
    };
    throw new Error(
      `ComboBox '${selectorAutomationId}' did not expose item '${expectedVisibleName}'. `
      + `Debug=${JSON.stringify(debug)} Cause=${lastError?.message ?? lastError}`);
  }

  if (options.keyboardSelectVisibleItem === true) {
    const itemIndex = await page.evaluate(findVisibleComboBoxItemIndex, expectedVisibleName);
    if (itemIndex < 0) {
      throw new Error(`ComboBox '${selectorAutomationId}' item '${expectedVisibleName}' disappeared before keyboard selection.`);
    }

    await page.keyboard.press("Home");
    for (let i = 0; i < itemIndex; i += 1) {
      await page.keyboard.press("ArrowDown");
    }
    await page.keyboard.press("Enter");
  } else {
    await clickVisibleNavigationTarget(page, { labels: [expectedVisibleName], automationIds: [] });
  }

  if (options.verifySelectionText === false) {
    return;
  }

  await page.waitForFunction(
    input => {
      const control = window.__salmoneggSmoke.findVisibleControl({ automationIds: [input.selectorAutomationId] }, [], [input.selectorAutomationId]);
      if ((control?.textContent ?? "").includes(input.expectedVisibleName)
        || (control?.getAttribute("aria-label") ?? "").includes(input.expectedVisibleName)) {
        return true;
      }

      const selectorIndexByAutomationId = new Map([
        ["StartView.AgentSelector", 0],
        ["StartView.ModeSelector", 1],
        ["StartView.ProjectSelector", 2]
      ]);
      const selectorIndex = selectorIndexByAutomationId.get(input.selectorAutomationId);
      if (selectorIndex === undefined) {
        return false;
      }

      const comboBoxes = Array.from(document.querySelectorAll("body *"))
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          const className = element.className?.toString?.() ?? "";
          return {
            element,
            rect,
            className,
            role: element.getAttribute("role") ?? "",
            display: style.display,
            visibility: style.visibility
          };
        })
        .filter(candidate =>
          (candidate.role === "combobox" || candidate.className.toLowerCase().includes("combobox"))
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.display !== "none"
          && candidate.visibility !== "hidden"
          && candidate.rect.left >= -1
          && candidate.rect.top >= innerHeight * 0.55
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight)
        .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));
      return (comboBoxes[selectorIndex]?.element.textContent ?? "").includes(input.expectedVisibleName);
    },
    { selectorAutomationId, expectedVisibleName },
    { timeout: 10_000 });
}

function findVisibleComboBoxItemIndex(expectedVisibleName) {
  const items = Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const className = element.className?.toString?.() ?? "";
      return {
        text: (element.textContent ?? "").trim(),
        rect,
        className,
        role: element.getAttribute("role") ?? "",
        display: style.display,
        visibility: style.visibility
      };
    })
    .filter(candidate =>
      (candidate.role === "option" || candidate.className.toLowerCase().includes("comboboxitem"))
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight)
    .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));

  return items.findIndex(item => item.text === expectedVisibleName);
}

async function clickVisibleControl(page, options) {
  const point = await page.evaluate(findVisibleControlPoint, options)
    ?? await page.evaluate(findStartComposerSelectorFallbackPoint, options);
  if (!point) {
    const debug = {
      interactive: await page.evaluate(collectVisibleInteractiveDebug),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
    };
    throw new Error(`No visible control found for ${JSON.stringify(options)}. Candidates=${JSON.stringify(debug)}`);
  }

  await page.mouse.click(point.x, point.y);
}

async function typeIntoAutomationTextBox(page, automationId, value) {
  const point = await page.evaluate(findVisibleControlPoint, { labels: [], automationIds: [automationId] });
  if (!point) {
    const inputs = await page.evaluate(collectVisibleTextInputPoints);
    throw new Error(`No visible text box found for automation id '${automationId}'. Inputs=${JSON.stringify(inputs)}`);
  }

  await page.mouse.click(point.x, point.y);
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.type(value);
}

async function clickStartComposerSendButton(page) {
  const point = await page.evaluate(() => {
    const buttons = Array.from(document.querySelectorAll("button,[role='button'],.uno-button"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return {
          element,
          rect,
          text: (element.textContent ?? "").trim(),
          aria: element.getAttribute("aria-label") ?? "",
          className: element.className?.toString?.() ?? "",
          display: style.display,
          visibility: style.visibility,
          disabled:
            element.disabled === true
            || element.getAttribute("disabled") != null
            || element.getAttribute("aria-disabled") === "true"
            || element.className?.toString?.().toLowerCase().includes("disabled")
        };
      })
      .filter(candidate =>
        candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.display !== "none"
        && candidate.visibility !== "hidden"
        && !candidate.disabled
        && candidate.rect.top >= innerHeight * 0.75
        && candidate.rect.left >= 0
        && candidate.rect.left <= innerWidth
        && candidate.rect.top <= innerHeight)
      .sort((left, right) => right.rect.right - left.rect.right);

    const target = buttons[0];
    if (!target) {
      return null;
    }

    return {
      x: target.rect.left + target.rect.width / 2,
      y: target.rect.top + target.rect.height / 2
    };
  });

  if (!point) {
    const debug = await page.evaluate(collectVisibleInteractiveDebug);
    throw new Error(`Start composer send button was not visible. Debug=${JSON.stringify(debug)}`);
  }

  await page.mouse.click(point.x, point.y);
}

async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  await page.waitForFunction(
    source => new RegExp(source).test(document.body?.innerText ?? ""),
    pattern.source,
    { timeout: timeoutMs });

  const bodyText = await page.locator("body").innerText();
  if (!pattern.test(bodyText)) {
    throw new Error(`Expected ${label} text was not visible.`);
  }
}

async function clickVisibleNavigationTargetUntilBodyText(page, options, pattern, label) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    try {
      await clickVisibleNavigationTarget(page, options);
      await waitForBodyText(page, pattern, label, Math.min(1_500, Math.max(250, deadline - Date.now())));
      return;
    } catch (error) {
      lastError = error;
      await page.waitForTimeout(250);
    }
  }

  const bodyText = await page.locator("body").innerText().catch(() => "");
  throw new Error(
    `Expected ${label} text was not visible after clicking navigation target. `
    + `Last error: ${lastError?.message ?? lastError}. Body: ${bodyText.slice(0, 1_000)}`);
}

async function clickVisibleNavigationTarget(page, options) {
  const point = await page.evaluate(findVisibleNavigationTargetPoint, options);

  if (!point) {
    const candidates = await page.evaluate(collectVisibleNavigationTargetDebug);
    const labels = options.labels ?? [];
    const automationIds = options.automationIds ?? [];
    throw new Error(
      `No visible navigation item found for labels: ${labels.join(", ")} automationIds: ${automationIds.join(", ")}. `
      + `Candidates: ${JSON.stringify(candidates)}`);
  }

  await page.mouse.click(point.x, point.y);
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

async function ensureVisibleNavigationTarget(page, targetOptions, openerOptions) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, targetOptions)) {
    return;
  }

  await clickVisibleNavigationTarget(page, openerOptions);
  await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 30_000 });
}

function findVisibleNavigationTargetPoint(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const nodes = Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const text = (element.textContent ?? "").trim();
      const aria = element.getAttribute("aria-label") ?? "";
      const automationId =
        element.getAttribute("data-automation-id")
        ?? element.getAttribute("data-automationid")
        ?? element.getAttribute("automationid")
        ?? "";

      return {
        element,
        rect,
        text,
        aria,
        automationId,
        display: style.display,
        visibility: style.visibility,
        automationMatch: automationIds.includes(aria) || automationIds.includes(automationId),
        textMatch: labels.includes(text) || labels.includes(aria)
      };
    })
    .filter(candidate =>
      (candidate.automationMatch || candidate.textMatch)
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight);

  nodes.sort((left, right) => {
    if (left.automationMatch !== right.automationMatch) {
      return left.automationMatch ? -1 : 1;
    }

    return (left.rect.width * left.rect.height) - (right.rect.width * right.rect.height);
  });

  const target = nodes[0]?.element;
  if (!target) {
    return null;
  }

  const clickable =
    target.closest(".uno-navigationviewitem")
    ?? target.closest(".uno-comboboxitem")
    ?? target.closest(".uno-button")
    ?? target.closest("[role='button']")
    ?? target.closest("button")
    ?? target;
  const clickableRect = clickable.getBoundingClientRect();
  const rect = clickableRect.width > 0 && clickableRect.height > 0
    ? clickableRect
    : target.getBoundingClientRect();

  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function findVisibleControlPoint(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return null;
  }

  const inputElement = control.matches("input,textarea")
    ? control
    : control.querySelector("input,textarea") ?? control;
  const rect = inputElement.getBoundingClientRect();
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function findStartComposerSelectorFallbackPoint(input) {
  const automationIds = input.automationIds ?? [];
  const selectorIndexByAutomationId = new Map([
    ["StartView.AgentSelector", 0],
    ["StartView.ModeSelector", 1],
    ["StartView.ProjectSelector", 2]
  ]);
  const targetAutomationId = automationIds.find(id => selectorIndexByAutomationId.has(id));
  if (!targetAutomationId) {
    return null;
  }

  const comboBoxes = Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const className = element.className?.toString?.() ?? "";
      return {
        element,
        rect,
        className,
        role: element.getAttribute("role") ?? "",
        display: style.display,
        visibility: style.visibility
      };
    })
    .filter(candidate =>
      (candidate.role === "combobox" || candidate.className.toLowerCase().includes("combobox"))
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight
      && candidate.rect.top > innerHeight * 0.55)
    .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));
  const target = comboBoxes[selectorIndexByAutomationId.get(targetAutomationId)];
  if (!target) {
    return null;
  }

  return {
    x: target.rect.left + target.rect.width / 2,
    y: target.rect.top + target.rect.height / 2
  };
}

function collectVisibleComboBoxDebug() {
  return Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const className = element.className?.toString?.() ?? "";
      return {
        element,
        rect,
        className,
        role: element.getAttribute("role") ?? "",
        text: (element.textContent ?? "").trim(),
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        display: style.display,
        visibility: style.visibility
      };
    })
    .filter(candidate =>
      (candidate.role === "combobox" || candidate.className.toLowerCase().includes("combobox"))
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight)
    .map(candidate => ({
      text: candidate.text,
      aria: candidate.aria,
      automationId: candidate.automationId,
      role: candidate.role,
      className: candidate.className,
      rect: {
        left: Math.round(candidate.rect.left),
        top: Math.round(candidate.rect.top),
        width: Math.round(candidate.rect.width),
        height: Math.round(candidate.rect.height)
      }
    }))
    .slice(0, 40);
}

function readControlEnabledState(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return { found: false, enabled: false };
  }

  const clickable =
    control.closest("button")
    ?? control.closest("[role='button']")
    ?? control.closest("[role='switch']")
    ?? control.closest(".uno-button")
    ?? control;
  const disabled =
    clickable.disabled === true
    || clickable.getAttribute("disabled") != null
    || clickable.getAttribute("aria-disabled") === "true"
    || clickable.className?.toString?.().toLowerCase().includes("disabled");

  return {
    found: true,
    enabled: !disabled,
    text: (clickable.textContent ?? "").trim(),
    aria: clickable.getAttribute("aria-label") ?? "",
    x: (clickable.className?.toString?.().toLowerCase().includes("toggleswitch")
      ? window.__salmoneggSmoke.resolveToggleClickPoint(clickable)?.x
      : clickable.getBoundingClientRect().left + clickable.getBoundingClientRect().width / 2) ?? null,
    y: (clickable.className?.toString?.().toLowerCase().includes("toggleswitch")
      ? window.__salmoneggSmoke.resolveToggleClickPoint(clickable)?.y
      : clickable.getBoundingClientRect().top + clickable.getBoundingClientRect().height / 2) ?? null,
    automationId:
      clickable.getAttribute("data-automation-id")
      ?? clickable.getAttribute("data-automationid")
      ?? clickable.getAttribute("automationid")
      ?? ""
  };
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

async function dismissDialogIfPresent(page) {
  const closeLabels = {
    labels: ["确定", "OK"],
    automationIds: []
  };

  const point = await page.evaluate(findVisibleNavigationTargetPoint, closeLabels);
  if (!point) {
    return;
  }

  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(300);
}

function getControlValueByAutomationId(automationId) {
  const control = window.__salmoneggSmoke.findVisibleControl({ automationIds: [automationId] }, [], [automationId]);
  if (!control) {
    return null;
  }

  const input = control.matches("input,textarea")
    ? control
    : control.querySelector("input,textarea");
  return input?.value
    ?? control.getAttribute("aria-valuenow")
    ?? (control.textContent ?? "").trim();
}

function getFirstVisibleTextInputValue() {
  const input = Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
    .find(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      return rect.width > 0
        && rect.height > 0
        && style.display !== "none"
        && style.visibility !== "hidden"
        && !["button", "checkbox", "radio", "submit"].includes(type)
        && rect.left >= 0
        && rect.top >= 0
        && rect.left <= innerWidth
        && rect.top <= innerHeight;
    });

  return input?.value ?? input?.textContent ?? null;
}

function collectVisibleTextInputPoints() {
  return Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      return {
        x: rect.left + rect.width / 2,
        y: rect.top + rect.height / 2,
        top: rect.top,
        left: rect.left,
        width: rect.width,
        height: rect.height,
        text: (element.textContent ?? "").trim(),
        value: element.value ?? "",
        aria: element.getAttribute("aria-label") ?? "",
        role: element.getAttribute("role") ?? "",
        type,
        placeholder: element.getAttribute("placeholder") ?? "",
        visible: rect.width > 0
          && rect.height > 0
          && style.display !== "none"
          && style.visibility !== "hidden"
          && !["button", "checkbox", "radio", "submit"].includes(type)
          && rect.left >= 0
          && rect.top >= 0
          && rect.left <= innerWidth
          && rect.top <= innerHeight
      };
    })
    .filter(candidate => candidate.visible)
    .sort((left, right) => (left.top - right.top) || (left.left - right.left));
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

function findTopNavigationOverflowPoint() {
  const explicitTarget = window.__salmoneggSmoke.collectTopNavigationButtonCandidates().find(candidate =>
    candidate.text === "\uE10C"
    || candidate.text === "\uE712"
    || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.aria)
    || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.title));
  const target = explicitTarget?.element;
  if (!target) {
    return null;
  }

  const clickable = target.closest(".uno-button") ?? target;
  const rect = clickable.getBoundingClientRect();
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function collectTopNavigationButtonCandidateDebug() {
  return window.__salmoneggSmoke.collectTopNavigationButtonCandidates().map(candidate => ({
    text: candidate.text,
    aria: candidate.aria,
    title: candidate.title,
    role: candidate.role,
    className: candidate.className,
    rect: candidate.rect
  }));
}

function collectVisibleNavigationTargetDebug() {
  return Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      return {
        text: (element.textContent ?? "").trim().slice(0, 120),
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        role: element.getAttribute("role") ?? "",
        className: element.className?.toString?.() ?? "",
        rect: {
          left: Math.round(rect.left),
          top: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height)
        }
      };
    })
    .filter(candidate =>
      candidate.rect.width > 0
      && candidate.rect.height > 0
      && (candidate.text || candidate.aria || candidate.automationId || candidate.role))
    .slice(0, 80);
}

function collectVisibleInteractiveDebug() {
  return Array.from(document.querySelectorAll("button,input,[role='button'],[role='switch'],[aria-checked],.uno-button,.uno-toggleswitch"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      return {
        text: (element.textContent ?? "").trim().slice(0, 120),
        aria: element.getAttribute("aria-label") ?? "",
        role: element.getAttribute("role") ?? "",
        checked: element.getAttribute("aria-checked") ?? "",
        className: element.className?.toString?.() ?? "",
        rect: {
          left: Math.round(rect.left),
          top: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height)
        }
      };
    })
    .filter(candidate => candidate.rect.width > 0 && candidate.rect.height > 0)
    .slice(0, 120);
}

async function startAcpWebSocketServer() {
  let initializeRequest;
  let sessionNewRequest;
  let sessionPromptRequest;
  let resolveInitialize;
  let resolveSessionNew;
  let resolveSessionPrompt;
  const initializePromise = new Promise(resolve => {
    resolveInitialize = resolve;
  });
  const sessionNewPromise = new Promise(resolve => {
    resolveSessionNew = resolve;
  });
  const sessionPromptPromise = new Promise(resolve => {
    resolveSessionPrompt = resolve;
  });
  const sockets = new Set();
  const sessionId = "wasm-full-chain-session-01";

  const server = createServer();
  server.on("upgrade", (request, socket) => {
    const key = request.headers["sec-websocket-key"];
    if (!key) {
      socket.destroy();
      return;
    }

    const accept = createHash("sha1")
      .update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`)
      .digest("base64");

    socket.write([
      "HTTP/1.1 101 Switching Protocols",
      "Upgrade: websocket",
      "Connection: Upgrade",
      `Sec-WebSocket-Accept: ${accept}`,
      "",
      ""
    ].join("\r\n"));

    sockets.add(socket);
    let buffer = Buffer.alloc(0);
    socket.on("data", chunk => {
      buffer = Buffer.concat([buffer, chunk]);
      const result = readWebSocketTextFrames(buffer);
      buffer = result.remaining;

      for (const text of result.messages) {
        const message = JSON.parse(text);
        if (message.method === "initialize") {
          initializeRequest = message;
          resolveInitialize(message);
          writeJsonRpc(socket, {
            jsonrpc: "2.0",
            id: message.id,
            result: {
              protocolVersion: 1,
              agentInfo: {
                name: "wasm-smoke-agent",
                title: "WASM Smoke Agent",
                version: "1.0.0"
              },
              agentCapabilities: {}
            }
          });
          continue;
        }

        if (message.method === "session/new") {
          sessionNewRequest = message;
          resolveSessionNew(message);
          writeJsonRpc(socket, {
            jsonrpc: "2.0",
            id: message.id,
            result: {
              sessionId,
              modes: {
                currentModeId: "planner",
                availableModes: [
                  {
                    id: "agent",
                    name: "Agent 01",
                    description: "General conversation mode"
                  },
                  {
                    id: "planner",
                    name: "Planner 01",
                    description: "Structured planning mode"
                  }
                ]
              },
              configOptions: [
                {
                  id: "mode",
                  name: "Mode",
                  description: "Conversation mode",
                  type: "select",
                  currentValue: "planner",
                  options: [
                    {
                      value: "agent",
                      name: "Agent 01"
                    },
                    {
                      value: "planner",
                      name: "Planner 01"
                    }
                  ]
                }
              ]
            }
          });
          writeSessionUpdate(socket, sessionId, {
            sessionUpdate: "session_info_update",
            title: "WASM full chain session"
          });
          continue;
        }

        if (message.method === "session/prompt") {
          sessionPromptRequest = message;
          resolveSessionPrompt(message);
          writeSessionUpdate(socket, sessionId, {
            sessionUpdate: "agent_message_chunk",
            content: {
              type: "text",
              text: fullChainAgentReplyText
            }
          });
          writeJsonRpc(socket, {
            jsonrpc: "2.0",
            id: message.id,
            result: {
              stopReason: "end_turn",
              userMessageId: message.params?.messageId ?? null
            }
          });
        }
      }
    });
    socket.on("close", () => sockets.delete(socket));
    socket.on("error", () => sockets.delete(socket));
  });

  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  const port = typeof address === "object" && address ? address.port : 0;

  return {
    url: `ws://127.0.0.1:${port}/acp`,
    waitForInitialize: async () => {
      if (initializeRequest) {
        return initializeRequest;
      }

      let timeoutId;
      const timeout = new Promise((_, reject) => {
        timeoutId = setTimeout(() => reject(new Error("Timed out waiting for ACP initialize request.")), 30_000);
      });

      try {
        return await Promise.race([initializePromise, timeout]);
      } finally {
        if (timeoutId) {
          clearTimeout(timeoutId);
        }
      }
    },
    waitForSessionNew: async () => {
      if (sessionNewRequest) {
        return sessionNewRequest;
      }

      return await waitWithTimeout(sessionNewPromise, "Timed out waiting for ACP session/new request.", 30_000);
    },
    waitForSessionPrompt: async () => {
      if (sessionPromptRequest) {
        return sessionPromptRequest;
      }

      return await waitWithTimeout(sessionPromptPromise, "Timed out waiting for ACP session/prompt request.", 30_000);
    },
    close: async () => {
      for (const socket of sockets) {
        socket.destroy();
      }

      await new Promise(resolve => server.close(resolve));
    }
  };
}

async function waitWithTimeout(promise, message, timeoutMs) {
  let timeoutId;
  const timeout = new Promise((_, reject) => {
    timeoutId = setTimeout(() => reject(new Error(message)), timeoutMs);
  });

  try {
    return await Promise.race([promise, timeout]);
  } finally {
    if (timeoutId) {
      clearTimeout(timeoutId);
    }
  }
}

function writeSessionUpdate(socket, sessionId, update) {
  writeJsonRpc(socket, {
    jsonrpc: "2.0",
    method: "session/update",
    params: {
      sessionId,
      update
    }
  });
}

function writeJsonRpc(socket, message) {
  socket.write(encodeWebSocketTextFrame(JSON.stringify(message)));
}

function readWebSocketTextFrames(buffer) {
  const messages = [];
  let offset = 0;

  while (buffer.length - offset >= 2) {
    const first = buffer[offset];
    const second = buffer[offset + 1];
    const opcode = first & 0x0f;
    const masked = (second & 0x80) !== 0;
    let payloadLength = second & 0x7f;
    let headerLength = 2;

    if (payloadLength === 126) {
      if (buffer.length - offset < 4) {
        break;
      }

      payloadLength = buffer.readUInt16BE(offset + 2);
      headerLength = 4;
    } else if (payloadLength === 127) {
      if (buffer.length - offset < 10) {
        break;
      }

      const high = buffer.readUInt32BE(offset + 2);
      const low = buffer.readUInt32BE(offset + 6);
      payloadLength = high * 2 ** 32 + low;
      headerLength = 10;
    }

    const maskLength = masked ? 4 : 0;
    const frameLength = headerLength + maskLength + payloadLength;
    if (buffer.length - offset < frameLength) {
      break;
    }

    let payload = buffer.subarray(offset + headerLength + maskLength, offset + frameLength);
    if (masked) {
      const mask = buffer.subarray(offset + headerLength, offset + headerLength + 4);
      payload = Buffer.from(payload.map((byte, index) => byte ^ mask[index % 4]));
    }

    if (opcode === 0x1) {
      messages.push(payload.toString("utf8"));
    }

    offset += frameLength;
  }

  return {
    messages,
    remaining: buffer.subarray(offset)
  };
}

function encodeWebSocketTextFrame(text) {
  const payload = Buffer.from(text, "utf8");
  if (payload.length < 126) {
    return Buffer.concat([Buffer.from([0x81, payload.length]), payload]);
  }

  if (payload.length <= 0xffff) {
    const header = Buffer.alloc(4);
    header[0] = 0x81;
    header[1] = 126;
    header.writeUInt16BE(payload.length, 2);
    return Buffer.concat([header, payload]);
  }

  const header = Buffer.alloc(10);
  header[0] = 0x81;
  header[1] = 127;
  header.writeUInt32BE(0, 2);
  header.writeUInt32BE(payload.length, 6);
  return Buffer.concat([header, payload]);
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
