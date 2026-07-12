import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  clearBrowserOriginStorage,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  readNumericControlValue,
  setNumericControlValue,
  readAppSettingsPersistenceDebug,
  selectAlternateCacheRetentionValue,
  setToggleSwitchValue,
  expectToggleSwitchValue,
  expectControlEnabledState,
  selectComboBoxItem,
  expectComboBoxSelectionText,
  clickVisibleNavigationTargetUntilBodyText,
  clickVisibleControl,
  scrollToVisibleControl,
  typeIntoAutomationTextBox,
  typeIntoVisibleTextField,
  waitForBodyText,
  readLocalTextFile
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection
} from "./wasm-smoke-lib/settings-shell.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-settings-persistence-smoke.mjs");
const appSettingsPath = "/local/SalmonEgg/config/app.yaml";
const mcpSettingsPath = "/local/SalmonEgg/config/mcp.yaml";

const sections = {
  general: {
    target: { labels: ["常规", "General"], automationIds: ["SettingsNav.General"] },
    bodyPattern: /开机自启动|Launch on startup|Language|语言/,
    label: "general settings page"
  },
  appearance: {
    target: { labels: ["外观", "Appearance"], automationIds: ["SettingsNav.Appearance"] },
    bodyPattern: /界面主题|Theme|应用主题|App theme|背景材质|Backdrop/,
    label: "appearance settings page"
  },
  dataStorage: {
    target: { labels: ["数据与存储", "Data storage", "Data"], automationIds: ["SettingsNav.DataStorage"] },
    bodyPattern: /保存本地历史|Save local history|缓存保留天数|Cache retention/,
    label: "data storage settings page"
  },
  shortcuts: {
    target: { labels: ["快捷键", "Shortcuts"], automationIds: ["SettingsNav.Shortcuts"] },
    bodyPattern: /启用快捷键|Enable shortcuts|Keyboard shortcuts|自定义快捷键/,
    label: "shortcuts settings page"
  },
  acp: {
    target: { labels: ["ACP Agent", "ACP / Agent", "Agent (ACP)"], automationIds: ["SettingsNav.AgentAcp"] },
    bodyPattern: /Enable ACP agents|启用 ACP Agent|Connection eviction|Hydration/,
    label: "ACP settings page"
  },
  mcp: {
    target: { labels: ["MCP"], automationIds: ["SettingsNav.Mcp"] },
    bodyPattern: /Service configuration|服务配置|Local services use stdio|本地服务使用 stdio|New|新建/,
    label: "MCP settings page"
  }
};

const controls = {
  generalAutoStart: { labels: ["开机自启动", "Launch on startup"], automationIds: ["GeneralSettings.AutoStart"] },
  generalMinimizeToTray: { labels: ["关闭到系统托盘", "Minimize to tray"], automationIds: ["GeneralSettings.MinimizeToTray"] },
  appearanceAnimation: { labels: ["动画效果", "Animations"], automationIds: ["Appearance.Animation"] },
  dataStorageSaveLocalHistory: {
    labels: ["保存本地历史", "Save local history"],
    automationIds: ["DataStorage.SaveLocalHistory"]
  },
  shortcutsEnabled: { labels: ["启用快捷键", "Enable shortcuts"], automationIds: ["Shortcuts.Enabled"] },
  acpEnabled: { labels: ["启用 ACP Agent", "Enable ACP agents"], automationIds: ["Acp.Global.Enabled"] },
  mcpServerEnabled: { labels: ["启用", "Enabled"], automationIds: ["Mcp.Server.Enabled"] }
};

const dataStorageCacheRetentionControl = {
  labels: ["缓存保留天数", "Cache retention (days)"],
  automationIds: ["DataStorage.CacheRetention"]
};
const startNavigationTarget = {
  labels: ["开始", "Start"],
  automationIds: ["MainNav.Start"]
};
const browser = await chromium.launch({ headless: true });

try {
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await verifyUnsupportedGeneralSettings(page);
    await changeAppearanceSettings(page);
    const updatedCacheRetention = await changeDataStorageSettings(page);
    await changeShortcutsSettings(page);
    await changeAcpSettings(page);
    await changeMcpSettings(page);

    await waitForLocalFileContains(
      page,
      appSettingsPath,
      [
        "theme: Dark",
        "is_animation_enabled: false",
        "backdrop: Acrylic",
        "save_local_history: false",
        `cache_retention_days: ${updatedCacheRetention}`,
        "keyboard_shortcuts_enabled: false",
        "acp_enabled: false"
      ],
      "app settings YAML");
    await waitForLocalFileContains(
      page,
      mcpSettingsPath,
      [
        "transport: stdio",
        "name: new-mcp-server",
        "enabled: false",
        "command: /usr/bin/node"
      ],
      "MCP settings YAML");

    await page.reload({ waitUntil: "domcontentloaded", timeout: 60_000 });
    await openApp(page, baseUrl);
    await verifyUnsupportedGeneralSettings(page, "after reload");
    await verifyAppearanceSettings(page, "after reload");
    await verifyDataStorageSettings(page, updatedCacheRetention, "after reload");
    await verifyShortcutsSettings(page, "after reload");
    await verifyAcpSettings(page, "after reload");
    await verifyMcpSettings(page, "after reload");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM settings persistence smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}

async function verifyUnsupportedGeneralSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.general.target,
    sections.general.bodyPattern,
    `${sections.general.label} ${suffix}`.trim());
  await expectControlEnabledState(
    page,
    controls.generalAutoStart,
    false,
    `launch on startup ${suffix}`.trim());
  await expectControlEnabledState(
    page,
    controls.generalMinimizeToTray,
    false,
    `minimize to tray ${suffix}`.trim());
  await waitForBodyText(
    page,
    /当前平台不支持语言覆盖|Language override is not supported on this platform/,
    `language unsupported notice ${suffix}`.trim());
}

async function changeAppearanceSettings(page) {
  await verifyStartComposerTextColorTracksAppearanceTheme(page);
  await navigateToSettingsSection(
    page,
    sections.appearance.target,
    sections.appearance.bodyPattern,
    sections.appearance.label);
  await selectComboBoxItem(page, "Appearance.Theme", ["深色", "Dark"], { keyboardSelectVisibleItem: true });
  await setToggleSwitchValue(page, controls.appearanceAnimation, false, "animations");
  await selectComboBoxItem(page, "Appearance.Backdrop", ["Acrylic"], { keyboardSelectVisibleItem: true });
  await verifyAppearanceSettings(page, "after edit");
}

async function verifyStartComposerTextColorTracksAppearanceTheme(page) {
  await selectAppearanceTheme(page, ["浅色", "Light"], "light");
  const lightProjection = await readStartPromptTextProjection(page, "light");

  await selectAppearanceTheme(page, ["深色", "Dark"], "dark");
  const darkProjection = await waitForStartPromptTextColorChange(page, lightProjection.color, "dark");

  const lightColor = parseCssColor(lightProjection.color);
  const darkColor = parseCssColor(darkProjection.color);
  const lightLuminance = relativeLuminance(lightColor);
  const darkLuminance = relativeLuminance(darkColor);
  const distance = colorDistance(lightColor, darkColor);

  if (distance < 30 || darkLuminance <= lightLuminance + 40) {
    throw new Error(
      `Start prompt text color did not track the appearance theme. `
      + `Light=${JSON.stringify(lightProjection)} Dark=${JSON.stringify(darkProjection)} `
      + `Distance=${distance.toFixed(2)} LightLuminance=${lightLuminance.toFixed(2)} DarkLuminance=${darkLuminance.toFixed(2)}`);
  }
}

async function selectAppearanceTheme(page, visibleNames, label) {
  await navigateToSettingsSection(
    page,
    sections.appearance.target,
    sections.appearance.bodyPattern,
    `appearance settings page for ${label} theme`);
  await selectComboBoxItem(page, "Appearance.Theme", visibleNames, { keyboardSelectVisibleItem: true });
}

async function waitForStartPromptTextColorChange(page, previousColor, label) {
  const deadline = Date.now() + 10_000;
  let projection = null;

  while (Date.now() < deadline) {
    projection = await readStartPromptTextProjection(page, label);
    if (projection.color !== previousColor) {
      return projection;
    }

    await page.waitForTimeout(150);
  }

  throw new Error(
    `Start prompt text color did not change after selecting ${label} theme. `
    + `Previous=${previousColor} Projection=${JSON.stringify(projection)}`);
}

async function readStartPromptTextProjection(page, label) {
  await clickVisibleNavigationTargetUntilBodyText(
    page,
    startNavigationTarget,
    /Salmon Egg|推荐开发任务|Recommend tasks/,
    `start page for ${label} composer theme`);
  await typeIntoAutomationTextBox(page, "StartView.PromptBox", `wasm ${label} theme text`);
  await page.waitForTimeout(250);

  const projection = await page.evaluate(automationId => {
    const control = window.__salmoneggSmoke.findVisibleControl({ automationIds: [automationId] }, [], [automationId]);
    const textInput = control?.matches("input,textarea,[contenteditable='true']")
      ? control
      : control?.querySelector("input,textarea,[contenteditable='true']");
    if (!control || !textInput) {
      return {
        found: false,
        controlText: control?.textContent ?? "",
        controlAria: control?.getAttribute("aria-label") ?? ""
      };
    }

    const style = getComputedStyle(textInput);
    const rect = textInput.getBoundingClientRect();
    return {
      found: true,
      color: style.color,
      backgroundColor: style.backgroundColor,
      opacity: Number(style.opacity || "1"),
      value: textInput.value ?? textInput.textContent ?? "",
      rect: {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height
      }
    };
  }, "StartView.PromptBox");

  if (!projection.found || projection.rect.width <= 0 || projection.rect.height <= 0) {
    throw new Error(`Start prompt input was not visible for ${label}. Projection=${JSON.stringify(projection)}`);
  }

  const color = parseCssColor(projection.color);
  if (color.a <= 0.1 || projection.opacity <= 0.1) {
    throw new Error(`Start prompt input text resolved transparent for ${label}. Projection=${JSON.stringify(projection)}`);
  }

  return projection;
}

function parseCssColor(value) {
  const match = String(value ?? "").match(/rgba?\(([^)]+)\)/i);
  if (!match) {
    throw new Error(`Unsupported CSS color format: ${value}`);
  }

  const parts = match[1].split(",").map(part => Number(part.trim()));
  return {
    r: parts[0] ?? 0,
    g: parts[1] ?? 0,
    b: parts[2] ?? 0,
    a: parts.length >= 4 ? parts[3] : 1
  };
}

function relativeLuminance(color) {
  return (0.2126 * color.r) + (0.7152 * color.g) + (0.0722 * color.b);
}

function colorDistance(left, right) {
  return Math.hypot(left.r - right.r, left.g - right.g, left.b - right.b);
}

async function verifyAppearanceSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.appearance.target,
    sections.appearance.bodyPattern,
    `${sections.appearance.label} ${suffix}`.trim());
  await expectComboBoxSelectionText(page, "Appearance.Theme", ["深色", "Dark"], `theme ${suffix}`.trim());
  await expectToggleSwitchValue(page, controls.appearanceAnimation, false, `animations ${suffix}`.trim());
  await expectComboBoxSelectionText(page, "Appearance.Backdrop", ["Acrylic"], `backdrop ${suffix}`.trim());
}

async function changeDataStorageSettings(page) {
  await navigateToSettingsSection(
    page,
    sections.dataStorage.target,
    sections.dataStorage.bodyPattern,
    sections.dataStorage.label);
  await setToggleSwitchValue(
    page,
    controls.dataStorageSaveLocalHistory,
    false,
    "save local history");

  const initialValue = await readNumericControlValue(
    page,
    dataStorageCacheRetentionControl,
    "cache retention before edit");
  const updatedValue = selectAlternateCacheRetentionValue(initialValue);
  await setNumericControlValue(
    page,
    dataStorageCacheRetentionControl,
    updatedValue,
    "cache retention");
  await verifyVisibleSettingsTextInputsResolveDarkThemeForeground(page, "data storage cache retention");
  await verifyDataStorageSettings(page, updatedValue, "after edit");
  return updatedValue;
}

async function verifyDataStorageSettings(page, updatedValue, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.dataStorage.target,
    sections.dataStorage.bodyPattern,
    `${sections.dataStorage.label} ${suffix}`.trim());
  await expectToggleSwitchValue(
    page,
    controls.dataStorageSaveLocalHistory,
    false,
    `save local history ${suffix}`.trim());
  const persistedValue = await readNumericControlValue(
    page,
    dataStorageCacheRetentionControl,
    `cache retention ${suffix}`.trim());
  if (persistedValue !== updatedValue) {
    const debug = await readAppSettingsPersistenceDebug(page, {
      controlOptions: dataStorageCacheRetentionControl,
      path: appSettingsPath
    });
    throw new Error(
      `Cache retention did not persist across reload. `
      + `Expected ${updatedValue}, got ${persistedValue}. `
      + `StorageDebug=${JSON.stringify(debug)}`);
  }
}

async function changeShortcutsSettings(page) {
  await navigateToSettingsSection(
    page,
    sections.shortcuts.target,
    sections.shortcuts.bodyPattern,
    sections.shortcuts.label);
  await setToggleSwitchValue(page, controls.shortcutsEnabled, false, "keyboard shortcuts");
  await verifyShortcutsSettings(page, "after edit");
}

async function verifyShortcutsSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.shortcuts.target,
    sections.shortcuts.bodyPattern,
    `${sections.shortcuts.label} ${suffix}`.trim());
  await expectToggleSwitchValue(page, controls.shortcutsEnabled, false, `keyboard shortcuts ${suffix}`.trim());
}

async function changeAcpSettings(page) {
  await navigateToSettingsSection(
    page,
    sections.acp.target,
    sections.acp.bodyPattern,
    sections.acp.label);
  await setToggleSwitchValue(page, controls.acpEnabled, false, "ACP global enabled");
  await verifyAcpSettings(page, "after edit");
}

async function verifyAcpSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.acp.target,
    sections.acp.bodyPattern,
    `${sections.acp.label} ${suffix}`.trim());
  await expectToggleSwitchValue(page, controls.acpEnabled, false, `ACP global enabled ${suffix}`.trim());
}

async function changeMcpSettings(page) {
  await navigateToSettingsSection(
    page,
    sections.mcp.target,
    sections.mcp.bodyPattern,
    sections.mcp.label);
  await clickVisibleControl(page, { labels: ["新建", "New", "添加服务器", "Add server"], automationIds: ["Mcp.AddServer"] });
  await waitForBodyText(page, /Launch command|启动命令/, "MCP server editor");
  await verifyVisibleSettingsTextInputsResolveDarkThemeForeground(page, "MCP server editor");
  await typeIntoVisibleTextField(
    page,
    { labels: ["mcp-filesystem", "Launch command", "启动命令", "Command", "命令"], automationIds: [] },
    "/usr/bin/node",
    "MCP server command");
  await scrollToVisibleControl(page, { labels: ["保存", "Save"], automationIds: ["Mcp.SaveServer"] });
  await clickVisibleControl(page, { labels: ["保存", "Save"], automationIds: ["Mcp.SaveServer"] });
  await waitForBodyText(page, /new-mcp-server/, "saved MCP server");
  await setToggleSwitchValue(page, controls.mcpServerEnabled, false, "MCP server enabled");
  await verifyMcpSettings(page, "after edit");
}

async function verifyMcpSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.mcp.target,
    sections.mcp.bodyPattern,
    `${sections.mcp.label} ${suffix}`.trim());
  await waitForBodyText(page, /new-mcp-server/, `MCP server row ${suffix}`.trim());
  await expectToggleSwitchValue(page, controls.mcpServerEnabled, false, `MCP server enabled ${suffix}`.trim());
}

async function verifyVisibleSettingsTextInputsResolveDarkThemeForeground(page, label) {
  const projections = await page.evaluate(() => Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      const textBoxContainer = element.closest(".uno-textbox,.uno-passwordbox");
      return {
        color: style.color,
        opacity: Number(style.opacity || "1"),
        value: element.value ?? element.textContent ?? "",
        placeholder: element.getAttribute("placeholder") ?? "",
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        className: element.className?.toString?.() ?? "",
        containerClassName: textBoxContainer?.className?.toString?.() ?? "",
        rect: {
          left: rect.left,
          top: rect.top,
          width: rect.width,
          height: rect.height
        },
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
    .filter(projection => projection.visible));

  if (projections.length === 0) {
    throw new Error(`No visible settings text inputs found for ${label}.`);
  }

  const failures = projections
    .map(projection => ({
      ...projection,
      parsedColor: parseCssColor(projection.color)
    }))
    .map(projection => ({
      ...projection,
      luminance: relativeLuminance(projection.parsedColor)
    }))
    .filter(projection => projection.opacity <= 0.1
      || projection.parsedColor.a <= 0.1
      || projection.luminance < 120);

  if (failures.length > 0) {
    throw new Error(
      `Settings text input foreground did not resolve to a readable dark-theme color for ${label}. `
      + `Failures=${JSON.stringify(failures)} All=${JSON.stringify(projections)}`);
  }
}

async function waitForLocalFileContains(page, path, requiredSnippets, label, timeoutMs = 15_000) {
  const deadline = Date.now() + timeoutMs;
  let lastResult = null;

  while (Date.now() < deadline) {
    lastResult = await readLocalTextFile(page, path);
    const content = lastResult?.content ?? "";
    if (!lastResult?.error && requiredSnippets.every(snippet => content.includes(snippet))) {
      return content;
    }

    await page.waitForTimeout(250);
  }

  throw new Error(
    `${label} did not contain expected settings. `
    + `Expected=${JSON.stringify(requiredSnippets)} `
    + `Actual=${JSON.stringify(lastResult)}`);
}
