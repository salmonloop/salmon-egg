export async function navigateToSettingsSection(page, sectionTarget, bodyPattern, label) {
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

export async function clickTopNavigationOverflowTargetUntilBodyText(page, targetOptions, pattern, label) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    try {
      await clickTopNavigationOverflow(page);
      await page.waitForFunction(
        findVisibleNavigationTargetPoint,
        targetOptions,
        { timeout: Math.min(1_500, Math.max(250, deadline - Date.now())) });
      const point = await page.evaluate(findVisibleNavigationTargetPoint, targetOptions);
      if (!point) {
        throw new Error(`Target disappeared before click: ${JSON.stringify(targetOptions)}`);
      }

      await page.mouse.click(point.x, point.y);
      await waitForBodyText(page, pattern, label, Math.min(3_000, Math.max(250, deadline - Date.now())));
      return;
    } catch (error) {
      lastError = error;
      await page.keyboard.press("Escape").catch(() => {});
      await page.waitForTimeout(250);
    }
  }

  const candidates = await page.evaluate(collectVisibleNavigationTargetDebug);
  throw new Error(
    `Settings overflow menu did not activate target ${JSON.stringify(targetOptions)}. `
    + `Last error: ${lastError?.message ?? lastError}. Candidates=${JSON.stringify(candidates)}`);
}

export async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  await page.waitForFunction(
    source => new RegExp(source).test(document.body?.innerText ?? ""),
    pattern.source,
    { timeout: timeoutMs });

  const bodyText = await page.locator("body").innerText();
  if (!pattern.test(bodyText)) {
    throw new Error(`Expected ${label} text was not visible.`);
  }
}

export async function clickVisibleNavigationTargetUntilBodyText(page, options, pattern, label) {
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

export async function clickVisibleNavigationTarget(page, options) {
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

export async function ensureVisibleNavigationTarget(page, targetOptions, openerOptions) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, targetOptions)) {
    return;
  }

  await clickVisibleNavigationTarget(page, openerOptions);
  await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 30_000 });
}

export async function readNumericControlValue(page, controlOptions, label) {
  const deadline = Date.now() + 30_000;
  let lastRawValue = null;

  while (Date.now() < deadline) {
    lastRawValue = await page.evaluate(readControlValue, controlOptions);
    const parsedValue = tryParseInteger(lastRawValue);
    if (parsedValue != null) {
      return parsedValue;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out reading ${label}. LastRawValue=${JSON.stringify(lastRawValue)}`);
}

export async function setNumericControlValue(page, controlOptions, value, label) {
  const state = await page.evaluate(readEditableControlState, controlOptions);
  if (!state?.found || !state?.enabled || !Number.isFinite(state.x) || !Number.isFinite(state.y)) {
    throw new Error(`Expected editable numeric control for ${label}. State=${JSON.stringify(state)}`);
  }

  await page.mouse.click(state.x, state.y);
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.type(String(value));
  await page.keyboard.press("Tab");

  const observedValue = await readNumericControlValue(page, controlOptions, `${label} after edit`);
  if (observedValue !== value) {
    throw new Error(`Failed to set ${label}. Expected ${value}, got ${observedValue}.`);
  }
}

export function selectAlternateCacheRetentionValue(currentValue) {
  if (!Number.isFinite(currentValue)) {
    return 7;
  }

  if (currentValue >= 60) {
    return 59;
  }

  if (currentValue <= 1) {
    return 2;
  }

  return currentValue + 1;
}

export async function readAppSettingsPersistenceDebug(page, options) {
  return await page.evaluate(readAppSettingsPersistenceDebugInPage, options);
}

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

export async function waitForInitializeWithDiagnostics(acpServer, page, profileName) {
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
    { verifySelectionText: false });
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

export async function expectControlDoesNotEscapePage(page, options, stayOnPagePattern) {
  const beforeUrl = page.url();
  const state = await page.evaluate(readControlEnabledState, options);
  if (!state.found) {
    return;
  }

  if (!state.enabled) {
    return;
  }

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

export async function readControlState(page, options) {
  return await page.evaluate(readControlEnabledState, options);
}

export async function waitForControlState(page, options, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    if (lastState?.found) {
      return lastState;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out waiting for ${label}. State=${JSON.stringify(lastState)}`);
}

export async function scrollToVisibleControl(page, options) {
  if (await page.evaluate(findVisibleControlPoint, options)) {
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

  await page.waitForTimeout(250);
}

export function expectNoAdvertisedFileSystemCapability(initializeRequest) {
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

function tryParseInteger(value) {
  const match = String(value ?? "").match(/-?\d+/);
  return match ? Number.parseInt(match[0], 10) : null;
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

async function typeIntoField(page, field, value) {
  const clickX = Number.isFinite(field.x) ? field.x : field.left + (field.width / 2);
  const clickY = Number.isFinite(field.y) ? field.y : field.top + (field.height / 2);
  await page.mouse.click(clickX, clickY);
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.type(value);
  await page.keyboard.press("Tab");
  await page.waitForTimeout(150);
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

async function typeIntoVisibleTextField(page, options, value, label) {
  const point = await page.evaluate(findVisibleControlPoint, options)
    ?? await page.evaluate(findVisibleTextInputPoint, options);
  if (!point) {
    const inputs = await page.evaluate(collectVisibleTextInputPoints);
    throw new Error(`No visible text field found for ${label}. Options=${JSON.stringify(options)} Inputs=${JSON.stringify(inputs)}`);
  }

  await typeIntoField(page, point, value);
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

export function findVisibleNavigationTargetPoint(input) {
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

function findVisibleTextInputPoint(input) {
  const normalize = value => (value ?? "").trim().toLowerCase();
  const labels = (input.labels ?? []).map(normalize).filter(Boolean);
  const automationIds = (input.automationIds ?? []).map(normalize).filter(Boolean);
  const candidates = Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
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
    .map(candidate => ({
      ...candidate,
      automationMatch:
        automationIds.includes(normalize(candidate.aria))
        || automationIds.includes(normalize(candidate.automationId)),
      textMatch:
        labels.some(label => normalize(candidate.aria).includes(label))
        || labels.some(label => normalize(candidate.placeholder).includes(label))
    }))
    .filter(candidate => candidate.automationMatch || candidate.textMatch)
    .sort((left, right) => {
      if (left.automationMatch !== right.automationMatch) {
        return left.automationMatch ? -1 : 1;
      }

      return (left.top - right.top) || (left.left - right.left);
    });
  const target = candidates[0];
  if (!target) {
    return null;
  }

  return {
    left: target.left,
    top: target.top,
    width: target.width,
    height: target.height
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

function readControlValue(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return null;
  }

  const resolveEditableInputInline = start => {
    if (start.matches("input,textarea,[contenteditable='true']")) {
      return start;
    }

    let current = start;
    while (current && current !== document.body) {
      const editableCandidate = current.querySelector("input,textarea,[contenteditable='true']");
      if (editableCandidate) {
        return editableCandidate;
      }

      current = current.parentElement;
    }

    return null;
  };

  const editable = resolveEditableInputInline(control);
  return editable?.value
    ?? control.getAttribute("aria-valuenow")
    ?? editable?.getAttribute("aria-valuenow")
    ?? (control.textContent ?? "").trim();
}

function readEditableControlState(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return { found: false, enabled: false };
  }

  const resolveEditableInputInline = start => {
    if (start.matches("input,textarea,[contenteditable='true']")) {
      return start;
    }

    let current = start;
    while (current && current !== document.body) {
      const editableCandidate = current.querySelector("input,textarea,[contenteditable='true']");
      if (editableCandidate) {
        return editableCandidate;
      }

      current = current.parentElement;
    }

    return null;
  };

  const editable = resolveEditableInputInline(control);
  if (!editable) {
    return { found: false, enabled: false };
  }

  const rect = editable.getBoundingClientRect();
  const disabled =
    editable.disabled === true
    || editable.getAttribute("disabled") != null
    || editable.getAttribute("aria-disabled") === "true";

  return {
    found: true,
    enabled: !disabled,
    value: editable.value ?? "",
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function readAppSettingsPersistenceDebugInPage(input) {
  const controlOptions = input.controlOptions;
  const path = input.path;
  const readControlValueInline = controlInput => {
    const labels = controlInput.labels ?? [];
    const automationIds = controlInput.automationIds ?? [];
    const control = window.__salmoneggSmoke.findVisibleControl(controlInput, labels, automationIds);
    if (!control) {
      return null;
    }

    const resolveEditableInputInline = start => {
      if (start.matches("input,textarea,[contenteditable='true']")) {
        return start;
      }

      let current = start;
      while (current && current !== document.body) {
        const editableCandidate = current.querySelector("input,textarea,[contenteditable='true']");
        if (editableCandidate) {
          return editableCandidate;
        }

        current = current.parentElement;
      }

      return null;
    };

    const editable = resolveEditableInputInline(control);
    return editable?.value
      ?? control.getAttribute("aria-valuenow")
      ?? editable?.getAttribute("aria-valuenow")
      ?? (control.textContent ?? "").trim();
  };
  const readLocalTextFileInline = filePath => {
    const result = {
      path: filePath,
      content: null,
      error: null
    };

    try {
      const fs = globalThis.FS;
      if (!fs) {
        result.error = "globalThis.FS unavailable";
        return result;
      }

      result.content = fs.readFile(filePath, { encoding: "utf8" });
      return result;
    } catch (error) {
      result.error = error?.message ?? String(error);
      return result;
    }
  };

  const appYaml = readLocalTextFileInline(path);
  return {
    visibleValue: readControlValueInline(controlOptions),
    appYaml: appYaml.content,
    appYamlError: appYaml.error
  };
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
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
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

export function collectVisibleNavigationTargetDebug() {
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

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
