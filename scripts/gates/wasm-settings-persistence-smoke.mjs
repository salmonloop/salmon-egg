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
  focusNumericControl,
  readAppSettingsPersistenceDebug,
  selectAlternateCacheRetentionValue,
  setToggleSwitchValue,
  expectToggleSwitchValue,
  expectControlEnabledState,
  selectComboBoxItem,
  expectComboBoxSelectionText,
  clickVisibleControl,
  countVisibleControls,
  waitForControlState,
  scrollToVisibleControl,
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
    target: { labels: ["数据与存储", "Data & Storage", "Data storage", "Data"], automationIds: ["SettingsNav.DataStorage"] },
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
    bodyPattern: /Enable ACP agents|启用 ACP Agent|Connection eviction|Hydration|Session load completion handling|Advanced loading policy/,
    label: "ACP settings page"
  },
  mcp: {
    target: { labels: ["MCP"], automationIds: ["SettingsNav.Mcp"] },
    // "New" used to be in this alternation, and it matches the navigation shell itself, so the
    // arrival check passed while the page had not changed at all - every later step then ran against
    // whichever section was still showing. Pinned to copy only this page renders.
    bodyPattern: /Enable MCP services as needed|按需启用 MCP 服务|Service configuration|服务配置/,
    label: "MCP settings page"
  }
};

const controls = {
  generalAutoStart: { labels: ["开机自启动", "Launch on startup"], automationIds: ["GeneralSettings.AutoStart"] },
  generalMinimizeToTray: { labels: ["关闭到系统托盘", "Minimize to tray"], automationIds: ["GeneralSettings.MinimizeToTray"] },
  generalLanguage: { labels: ["语言", "Language"], automationIds: ["GeneralSettings.Language"] },
  appearanceAnimation: { labels: ["动画效果", "Animations"], automationIds: ["Appearance.Animation"] },
  dataStorageSaveLocalHistory: {
    labels: ["保存本地历史", "Save local history"],
    automationIds: ["DataStorage.SaveLocalHistory"]
  },
  shortcutsEnabled: { labels: ["启用快捷键", "Enable shortcuts"], automationIds: ["Shortcuts.Enabled"] },
  mcpServerEnabled: { labels: ["启用", "Enabled"], automationIds: ["Mcp.Server.Enabled"] }
};

const mcpEditorPanel = { labels: [], automationIds: ["Mcp.Editor.Panel"] };
const mcpAddServerControl = {
  labels: ["新建", "New"],
  automationIds: ["Mcp.AddServer"]
};
const dataStorageCacheRetentionControl = {
  labels: ["缓存保留天数", "Cache retention (days)"],
  automationIds: ["DataStorage.CacheRetention"]
};
const browser = await chromium.launch({ headless: true });

try {
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await verifyGeneralSettings(page);
    await changeLanguage(page);
    await changeAppearanceSettings(page);
    const updatedCacheRetention = await changeDataStorageSettings(page);
    await changeShortcutsSettings(page);
    await verifyAcpSettings(page);
    await changeMcpSettings(page);

    await waitForLocalFileContains(
      page,
      appSettingsPath,
      [
        "theme: Dark",
        "language: en-US",
        "is_animation_enabled: false",
        "backdrop: Acrylic",
        "save_local_history: false",
        `cache_retention_days: ${updatedCacheRetention}`,
        "keyboard_shortcuts_enabled: false"
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
    await verifyGeneralSettings(page, "after reload");
    await verifyLanguageSelection(page, "after reload");
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

async function verifyGeneralSettings(page, suffix = "") {
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
  if (!suffix) {
    await expectComboBoxSelectionText(
      page,
      "GeneralSettings.Language",
      ["System", "跟随系统"],
      "initial language selection");
  }
}

async function changeLanguage(page) {
  await navigateToSettingsSection(
    page,
    sections.general.target,
    sections.general.bodyPattern,
    "general settings page for language change");
  await selectComboBoxItem(
    page,
    "GeneralSettings.Language",
    ["English"],
    { keyboardSelectVisibleItem: true, verifySelectionText: false });
  // The authoritative shell selection is preserved across a language reload. The
  // current Settings page is therefore the observable proof that x:Uid content was
  // recreated in English; navigation is not expected to jump to Start.
  await waitForBodyText(
    page,
    /Manage startup, window behavior, and UI language\.|Launch on startup/,
    "English settings page after shell reload");
  await verifyLanguageSelection(page, "after edit");
}

async function verifyLanguageSelection(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.general.target,
    sections.general.bodyPattern,
    `general settings language ${suffix}`.trim());
  await expectComboBoxSelectionText(
    page,
    "GeneralSettings.Language",
    ["English"],
    `language selection ${suffix}`.trim());
}

async function changeAppearanceSettings(page) {
  // Composer text color on Skia is painted into a canvas: the semantic tree mirrors
  // structure, not styling, so no DOM-observable color assertion can exist here.
  // Theme behavior is covered by the persisted selection below (combo text plus the
  // "theme: Dark" yaml snapshot checked later in this smoke).
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
  // Focus first: the editor input only exists in the accessibility view with the page mounted,
  // and the focused-state contrast check below must observe the editor while it holds focus.
  await focusNumericControl(
    page,
    dataStorageCacheRetentionControl,
    "cache retention focused contrast");
  await verifyVisibleSettingsTextInputsResolveDarkThemeForeground(
    page,
    "focused data storage cache retention",
    { requireFocused: true });
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

async function verifyAcpSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.acp.target,
    sections.acp.bodyPattern,
    `${sections.acp.label} ${suffix}`.trim());
  await waitForControlState(
    page,
    { labels: ["刷新", "Refresh"], automationIds: ["Acp.Profiles.Refresh"] },
    `ACP profiles refresh ${suffix}`.trim());
}

async function changeMcpSettings(page) {
  await navigateToSettingsSection(
    page,
    sections.mcp.target,
    sections.mcp.bodyPattern,
    sections.mcp.label);
  // The page's own affordance is the arrival proof that cannot be satisfied by the shell's text.
  await waitForControlState(page, mcpAddServerControl, "MCP page");
  // Matched through the semantic tree by automation id: the accessible name is the localized button
  // text ("New"), so a locator keyed on the id as an aria-label matches nothing.
  //
  // Retried against the editor appearing rather than against the click's own return value. The
  // page's semantic nodes show up before its layout and ViewModel are ready - the button is briefly
  // reported at 16x6 in the top-left corner - and a command that is not ready yet drops the
  // activation without the node ever reporting itself disabled, so the click "succeeds" and nothing
  // opens. Pressing again is what a user does, and the editor becoming visible is the only honest
  // proof it worked: its field labels reach the DOM solely as the inputs' accessible names, never as
  // text, so no body-text wait can stand in for it on Skia.
  await openMcpServerEditor(page);
  await verifyVisibleSettingsTextInputsResolveDarkThemeForeground(page, "MCP server editor");
  await typeIntoVisibleTextField(
    page,
    { labels: ["mcp-filesystem", "Launch command", "启动命令", "Command", "命令"], automationIds: [] },
    "/usr/bin/node",
    "MCP server command");
  // Uno WASM does not activate this bound command through locator.click(), so use the proven control helper.
  await scrollToVisibleControl(page, { labels: ["保存", "Save"], automationIds: ["Mcp.SaveServer"] });
  await clickVisibleControl(page, { labels: ["保存", "Save"], automationIds: ["Mcp.SaveServer"] });
  // Saving must add exactly one row - a duplicate would mean the editor saved twice, which the row
  // count is the only way to notice. Counted in the semantic tree for the same reason as above.
  await waitForControlState(page, controls.mcpServerEnabled, "saved MCP server row");
  const savedServerRows = await countVisibleControls(page, controls.mcpServerEnabled);
  if (savedServerRows !== 1) {
    throw new Error(`Expected one saved MCP server row, found ${savedServerRows}.`);
  }
  await waitForBodyText(page, /new-mcp-server/, "saved MCP server");
  await setToggleSwitchValue(page, controls.mcpServerEnabled, false, "MCP server enabled");
  await verifyMcpSettings(page, "after edit");
}

async function openMcpServerEditor(page) {
  const mcpEditorAttempts = 3;
  for (let attempt = 1; attempt <= mcpEditorAttempts; attempt += 1) {
    await clickVisibleControl(page, mcpAddServerControl);
    if (await scrollToVisibleControl(page, mcpEditorPanel, 8_000)) {
      return;
    }
  }

  throw new Error(
    `The MCP server editor did not open after ${mcpEditorAttempts} activations of the New affordance.`);
}

async function verifyMcpSettings(page, suffix = "") {
  await navigateToSettingsSection(
    page,
    sections.mcp.target,
    sections.mcp.bodyPattern,
    `${sections.mcp.label} ${suffix}`.trim());
  // The section's body pattern matches static chrome (the "New" button), so navigation reports success
  // while the page's async load is still in flight. That load clears the row collection before refilling
  // it, so the server name is briefly absent from the body text. Wait for the row control itself, the way
  // the save path above does, before asserting on text.
  await waitForControlState(page, mcpAddServerControl, `MCP page ${suffix}`.trim());
  await waitForControlState(page, controls.mcpServerEnabled, `MCP server row control ${suffix}`.trim());
  await waitForBodyText(page, /new-mcp-server/, `MCP server row ${suffix}`.trim());
  try {
    await expectToggleSwitchValue(page, controls.mcpServerEnabled, false, `MCP server enabled ${suffix}`.trim());
  } catch (error) {
    // A wrong toggle state is ambiguous on its own: it can mean the change was never persisted, or
    // that more than one row is present and the first one is a different server. Name which.
    const rows = await countVisibleControls(page, controls.mcpServerEnabled);
    const file = await readLocalTextFile(page, mcpSettingsPath);
    throw new Error(
      `${error.message} Rows=${rows} McpYaml=${JSON.stringify(file)}`,
      { cause: error });
  }
}

async function verifyVisibleSettingsTextInputsResolveDarkThemeForeground(page, label, options = {}) {
  const projections = await page.evaluate(() => {
    const parseColor = value => {
      const match = String(value ?? "").match(/rgba?\(([^)]+)\)/i);
      if (!match) {
        return null;
      }

      const parts = match[1].split(",").map(part => Number(part.trim()));
      return {
        r: parts[0] ?? 0,
        g: parts[1] ?? 0,
        b: parts[2] ?? 0,
        a: parts.length >= 4 ? parts[3] : 1
      };
    };
    const composite = (foreground, background) => {
      const alpha = foreground.a + (background.a * (1 - foreground.a));
      if (alpha <= 0) {
        return { r: 0, g: 0, b: 0, a: 0 };
      }

      return {
        r: ((foreground.r * foreground.a)
          + (background.r * background.a * (1 - foreground.a))) / alpha,
        g: ((foreground.g * foreground.a)
          + (background.g * background.a * (1 - foreground.a))) / alpha,
        b: ((foreground.b * foreground.a)
          + (background.b * background.a * (1 - foreground.a))) / alpha,
        a: alpha
      };
    };
    const findEffectiveBackground = element => {
      const layers = [];
      let current = element;
      while (current) {
        const backgroundColor = getComputedStyle(current).backgroundColor;
        const parsed = parseColor(backgroundColor);
        if (parsed && parsed.a > 0) {
          layers.push({
            color: parsed,
            source: current.className?.toString?.() || current.tagName
          });
        }

        current = current.parentElement;
      }

      if (layers.length === 0) {
        return null;
      }

      let effective = layers[layers.length - 1].color;
      for (let index = layers.length - 2; index >= 0; index -= 1) {
        effective = composite(layers[index].color, effective);
      }

      return effective.a >= 0.99
        ? {
            color: effective,
            sources: layers.map(layer => layer.source)
          }
        : null;
    };
    return Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
      .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      const textBoxContainer = element.closest(".uno-textbox,.uno-passwordbox");
      const background = findEffectiveBackground(element);
      return {
        color: style.color,
        backgroundColor: background?.color ?? null,
        backgroundSources: background?.sources ?? [],
        opacity: Number(style.opacity || "1"),
        focused: document.activeElement === element,
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
      .filter(projection => projection.visible);
  });

  if (projections.length === 0) {
    throw new Error(`No visible settings text inputs found for ${label}.`);
  }

  if (options.requireFocused === true
      && !projections.some(projection => projection.focused)) {
    throw new Error(
      `No visible settings text input held focus for ${label}. Projections=${JSON.stringify(projections)}`);
  }

  const failures = projections
    .map(projection => ({
      ...projection,
      parsedColor: parseCssColor(projection.color),
      parsedBackgroundColor: projection.backgroundColor
    }))
    .map(projection => ({
      ...projection,
      contrastRatio: projection.parsedBackgroundColor
        ? cssContrastRatio(
            compositeCssColor(
              {
                ...projection.parsedColor,
                a: projection.parsedColor.a * projection.opacity
              },
              projection.parsedBackgroundColor),
            projection.parsedBackgroundColor)
        : 0
    }))
    .filter(projection => projection.opacity <= 0.1
      || projection.parsedColor.a <= 0.1
      || projection.parsedBackgroundColor === null
      || projection.contrastRatio < 4.5);

  if (failures.length > 0) {
    throw new Error(
      `Settings text input foreground did not meet readable contrast for ${label}. `
      + `Failures=${JSON.stringify(failures)} All=${JSON.stringify(projections)}`);
  }
}

function compositeCssColor(foreground, background) {
  const alpha = foreground.a + (background.a * (1 - foreground.a));
  if (alpha <= 0) {
    return { r: 0, g: 0, b: 0, a: 0 };
  }

  return {
    r: ((foreground.r * foreground.a)
      + (background.r * background.a * (1 - foreground.a))) / alpha,
    g: ((foreground.g * foreground.a)
      + (background.g * background.a * (1 - foreground.a))) / alpha,
    b: ((foreground.b * foreground.a)
      + (background.b * background.a * (1 - foreground.a))) / alpha,
    a: alpha
  };
}

function cssContrastRatio(foreground, background) {
  const channel = value => {
    const normalized = value / 255;
    return normalized <= 0.04045
      ? normalized / 12.92
      : ((normalized + 0.055) / 1.055) ** 2.4;
  };
  const luminance = color =>
    (0.2126 * channel(color.r))
    + (0.7152 * channel(color.g))
    + (0.0722 * channel(color.b));
  const foregroundLuminance = luminance(foreground);
  const backgroundLuminance = luminance(background);
  const lighter = Math.max(foregroundLuminance, backgroundLuminance);
  const darker = Math.min(foregroundLuminance, backgroundLuminance);
  return (lighter + 0.05) / (darker + 0.05);
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
