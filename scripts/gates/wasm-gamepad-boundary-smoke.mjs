import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  clickVisibleControl,
  readControlState,
  scrollToVisibleControl,
  waitForBodyText,
  waitForControlState,
  waitForFocusedElementSnapshot
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection
} from "./wasm-smoke-lib/settings-shell.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-gamepad-boundary-smoke.mjs");
const diagnosticsPagePattern = /Diagnostics and logs|诊断与日志|Gamepad input|手柄输入/;
const gamepadStart = { labels: ["Start monitor", "开始监测"], automationIds: ["Diagnostics.GamepadStart"] };
const gamepadRefresh = { labels: ["Refresh once", "刷新一次"], automationIds: ["Diagnostics.GamepadRefresh"] };
const supportedStatusPattern = /not started|stopped|monitoring|未启动|已停止|正在监测/i;
const standardGamepadProjectionScript = `
(() => {
  const state = {
    connected: false,
    mapping: "standard",
    id: "SalmonEgg Smoke Standard Gamepad",
    pressedButtons: new Set(),
    axes: [0, 0, 0, 0],
    timestamp: 1
  };

  function createButtons() {
    return Array.from({ length: 16 }, (_, index) => {
      const pressed = state.pressedButtons.has(index);
      return {
        pressed,
        touched: pressed,
        value: pressed ? 1 : 0
      };
    });
  }

  Object.defineProperty(globalThis.navigator, "getGamepads", {
    configurable: true,
    value() {
      if (!state.connected) {
        return [];
      }

      state.timestamp += 1;
      return [{
        id: state.id,
        index: 0,
        connected: true,
        mapping: state.mapping,
        timestamp: state.timestamp,
        buttons: createButtons(),
        axes: state.axes.slice(),
        hapticActuators: [],
        vibrationActuator: null
      }];
    }
  });

  globalThis.__salmoneggSmokeGamepad = {
    setState(nextState) {
      state.connected = Boolean(nextState?.connected);
      state.mapping = typeof nextState?.mapping === "string" ? nextState.mapping : "standard";
      if (typeof nextState?.id === "string" && nextState.id.trim().length > 0) {
        state.id = nextState.id;
      }
      state.pressedButtons = new Set(nextState?.pressedButtons ?? []);
      state.axes = Array.isArray(nextState?.axes) ? nextState.axes.slice(0, 4) : [0, 0, 0, 0];
    }
  };
})();
`;
const browser = await chromium.launch({ headless: true });

try {
  await verifyNativeBrowserNoDeviceProjection();
  await verifyInjectedNonStandardGamepadIsNotMisread();
  await verifyInjectedStandardGamepadProjection();
  await verifyInjectedMultiBrandGamepadIdentityProjection();
  await verifyInjectedStandardGamepadNativeControlBridge();
  console.log("WASM gamepad boundary smoke passed");
} finally {
  await browser.close();
}

async function verifyNativeBrowserNoDeviceProjection() {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openDiagnosticsGamepadSection(page);
    await clickVisibleControl(page, gamepadRefresh);
    await page.waitForTimeout(250);
    const projection = await expectSupportedNoGamepadProjection(page, "native BrowserWasm gamepad diagnostics");

    await page.mouse.click(projection.startState.x, projection.startState.y);
    await page.waitForTimeout(350);
    await expectSupportedNoGamepadProjection(page, "native BrowserWasm gamepad diagnostics after monitoring start");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
  } finally {
    await context.close();
  }
}

async function verifyInjectedNonStandardGamepadIsNotMisread() {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await context.addInitScript({ content: standardGamepadProjectionScript });
    await openDiagnosticsGamepadSection(page);
    await page.evaluate(() => {
      globalThis.__salmoneggSmokeGamepad.setState({
        connected: true,
        mapping: "",
        pressedButtons: [0, 13],
        axes: [1, -1, 0, 0]
      });
    });

    await clickVisibleControl(page, gamepadRefresh);
    await page.waitForTimeout(250);
    await expectConnectedNonStandardGamepadWithoutActiveProjection(
      page,
      "injected non-standard Gamepad API projection");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
  } finally {
    await context.close();
  }
}

async function verifyInjectedStandardGamepadProjection() {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await context.addInitScript({ content: standardGamepadProjectionScript });
    await openDiagnosticsGamepadSection(page);
    await page.evaluate(() => {
      globalThis.__salmoneggSmokeGamepad.setState({
        connected: true,
        pressedButtons: [0, 13],
        axes: [0.25, 0.5, 0, 0]
      });
    });

    await clickVisibleControl(page, gamepadRefresh);
    await page.waitForTimeout(250);
    await expectActiveStandardGamepadProjection(page, "injected standard Gamepad API projection");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
  } finally {
    await context.close();
  }
}

async function verifyInjectedMultiBrandGamepadIdentityProjection() {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await context.addInitScript({ content: standardGamepadProjectionScript });
    await openDiagnosticsGamepadSection(page);

    const brands = [
      {
        label: "Xbox Wireless Controller identity",
        id: "Xbox Wireless Controller (STANDARD GAMEPAD Vendor: 045e Product: 0b13)",
        name: "Xbox Wireless Controller",
        vid: "045E",
        pid: "0B13",
        layoutPattern: /layout\s+(Standard|标准)/
      },
      {
        label: "DualSense identity",
        id: "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)",
        name: "DualSense Wireless Controller",
        vid: "054C",
        pid: "0CE6",
        layoutPattern: /layout\s+(Standard|标准)/
      },
      {
        label: "Switch Pro Controller identity",
        id: "Pro Controller (STANDARD GAMEPAD Vendor: 057e Product: 2009)",
        name: "Pro Controller",
        vid: "057E",
        pid: "2009",
        layoutPattern: /layout\s+(Nintendo|任天堂)/
      }
    ];

    for (const brand of brands) {
      await page.evaluate(id => {
        globalThis.__salmoneggSmokeGamepad.setState({
          connected: true,
          mapping: "standard",
          id,
          pressedButtons: [0],
          axes: [0, 0, 0, 0]
        });
      }, brand.id);

      await clickVisibleControl(page, gamepadRefresh);
      await page.waitForTimeout(250);

      await expectControlText(
        page,
        { labels: [], automationIds: ["Diagnostics.GamepadStandardCount"] },
        /^1$/,
        `${brand.label} standard gamepad count`);

      // Format: "#0 {name} VID {vid} PID {pid}; layout {layout}; ..."
      // Identity is diagnostics/layout labeling only; face semantics stay position-based.
      const detailsPattern = new RegExp(
        `#0\\s+${escapeRegExp(brand.name)}\\s+VID\\s+${brand.vid}\\s+PID\\s+${brand.pid};\\s*${brand.layoutPattern.source}`,
        "i");
      await expectControlText(
        page,
        { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
        detailsPattern,
        `${brand.label} standard details`);
    }

    assertNoFatalConsoleMessages(fatalConsoleMessages);
  } finally {
    await context.close();
  }
}

function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

async function verifyInjectedStandardGamepadNativeControlBridge() {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await context.addInitScript({ content: standardGamepadProjectionScript });
    await openDiagnosticsGamepadSection(page);
    await page.evaluate(() => {
      globalThis.__salmoneggSmokeGamepad.setState({
        connected: true,
        mapping: "standard",
        pressedButtons: [],
        axes: [0, 0, 0, 0]
      });
    });

    // Keep diagnostics polling so ActiveInputs updates while injected buttons stay pressed.
    await scrollToVisibleControl(page, gamepadStart, 15_000);
    const startState = await waitForControlState(page, gamepadStart, "native bridge start action");
    if (startState.enabled) {
      await clickVisibleControl(page, gamepadStart);
      await page.waitForTimeout(300);
    }

    const beforeMove = await focusControlByAutomationId(page, "Diagnostics.GamepadRefresh", "diagnostics refresh");
    await setInjectedGamepadButtons(page, [13]);
    const afterMove = await waitForDifferentFocusedElement(page, beforeMove, "DPadDown diagnostics focus move");
    await setInjectedGamepadButtons(page, []);

    if (afterMove.isBody || !afterMove.visible) {
      throw new Error(`DPadDown moved focus to an invalid target. Snapshot=${JSON.stringify(afterMove)}`);
    }

    await focusControlByAutomationId(page, "Diagnostics.GamepadRefresh", "diagnostics refresh before activate");
    await setInjectedGamepadButtons(page, [0]);
    await clickVisibleControl(page, gamepadRefresh);
    await waitForControlText(
      page,
      { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
      /Activate/,
      "gamepad Activate native control invocation",
      15_000);
    await setInjectedGamepadButtons(page, []);

    assertNoFatalConsoleMessages(fatalConsoleMessages);
  } finally {
    await context.close();
  }
}

async function openDiagnosticsGamepadSection(page) {
  await openApp(page, baseUrl);
  await navigateToSettingsSection(
    page,
    { labels: ["诊断与日志", "Diagnostics & Logs", "Diagnostics"], automationIds: ["SettingsNav.Diagnostics"] },
    diagnosticsPagePattern,
    "diagnostics settings page");

  await revealGamepadDiagnosticsSection(page);
}

async function expectSupportedNoGamepadProjection(page, label) {
  await scrollToVisibleControl(page, gamepadStart, 15_000);
  const startState = await waitForControlState(page, gamepadStart, `${label} start action`);
  if (!Number.isFinite(startState.x) || !Number.isFinite(startState.y)) {
    throw new Error(`BrowserWasm gamepad start action did not expose a stable point. State=${JSON.stringify(startState)}`);
  }

  await scrollToVisibleControl(page, gamepadRefresh, 15_000);
  const refreshState = await waitForControlState(page, gamepadRefresh, `${label} refresh action`);
  if (!refreshState.enabled) {
    throw new Error(`BrowserWasm diagnostics refresh should remain available. State=${JSON.stringify(refreshState)}`);
  }

  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadStatus"] },
    supportedStatusPattern,
    `${label} supported status`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadStandardCount"] },
    /^0$/,
    `${label} standard gamepad count`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadRawCount"] },
    /^0$/,
    `${label} raw controller count`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadInputSource"] },
    /^(None|无)$/,
    `${label} input source`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
    /^(None|无)$/,
    `${label} active input`);
  await waitForBodyText(page, diagnosticsPagePattern, `${label} page still visible`);

  return { startState, refreshState };
}

async function expectConnectedNonStandardGamepadWithoutActiveProjection(page, label) {
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadStatus"] },
    supportedStatusPattern,
    `${label} supported status`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadStandardCount"] },
    /^1$/,
    `${label} standard gamepad count`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadRawCount"] },
    /^0$/,
    `${label} raw controller count`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadInputSource"] },
    /^(None|无)$/,
    `${label} input source`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
    /^(None|无)$/,
    `${label} active input`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadThumbstick"] },
    /X 0\.00, Y 0\.00/,
    `${label} thumbstick`);
  await waitForBodyText(page, diagnosticsPagePattern, `${label} page still visible`);
}

async function expectActiveStandardGamepadProjection(page, label) {
  const startState = await waitForControlState(page, gamepadStart, `${label} start action`);
  if (!startState.enabled) {
    throw new Error(`BrowserWasm gamepad start action should be enabled for supported browser gamepad input. State=${JSON.stringify(startState)}`);
  }

  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadStatus"] },
    supportedStatusPattern,
    `${label} supported status`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadStandardCount"] },
    /^1$/,
    `${label} standard gamepad count`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadRawCount"] },
    /^0$/,
    `${label} raw controller count`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadInputSource"] },
    /^Gamepad$/,
    `${label} input source`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
    /^(?=.*MoveDown)(?=.*Activate).+$/,
    `${label} active input`);
  await expectControlText(
    page,
    { labels: [], automationIds: ["Diagnostics.GamepadThumbstick"] },
    /X 0\.25, Y -0\.50/,
    `${label} thumbstick`);
  await waitForBodyText(page, diagnosticsPagePattern, `${label} page still visible`);
}

async function expectControlText(page, options, pattern, label) {
  await scrollToVisibleControl(page, options, 15_000);
  const state = await waitForControlState(page, options, label, 15_000);
  const text = (state.text || state.aria || "").trim();
  if (!pattern.test(text)) {
    throw new Error(`Unexpected ${label}. Text=${JSON.stringify(text)} State=${JSON.stringify(state)}`);
  }
}

async function waitForControlText(page, options, pattern, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    await scrollToVisibleControl(page, options, 2_000);
    lastState = await readControlState(page, options);
    const text = (lastState?.text || lastState?.aria || "").trim();
    if (lastState?.found && pattern.test(text)) {
      return lastState;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out waiting for ${label}. State=${JSON.stringify(lastState)}`);
}

async function setInjectedGamepadButtons(page, pressedButtons) {
  await page.evaluate(buttons => {
    globalThis.__salmoneggSmokeGamepad.setState({
      connected: true,
      mapping: "standard",
      pressedButtons: buttons,
      axes: [0, 0, 0, 0]
    });
  }, pressedButtons);
}

async function focusControlByAutomationId(page, automationId, label) {
  await scrollToVisibleControl(page, { labels: [], automationIds: [automationId] });
  const focused = await page.evaluate(id => {
    const element = document.querySelector(`[aria-label="${id}"]`);
    if (!element || typeof element.focus !== "function") {
      return false;
    }

    element.focus();
    return document.activeElement === element;
  }, automationId);

  if (!focused) {
    const state = await readControlState(page, { labels: [], automationIds: [automationId] });
    throw new Error(`Could not focus ${label}. State=${JSON.stringify(state)}`);
  }

  return await waitForFocusedElementSnapshot(page, `${label} focus`);
}

async function waitForDifferentFocusedElement(page, beforeSnapshot, label) {
  const deadline = Date.now() + 5_000;
  let lastSnapshot = null;

  while (Date.now() < deadline) {
    lastSnapshot = await waitForFocusedElementSnapshot(page, label, 1_000);
    if (!isSameFocusSnapshot(beforeSnapshot, lastSnapshot)) {
      return lastSnapshot;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out waiting for ${label}. Before=${JSON.stringify(beforeSnapshot)} After=${JSON.stringify(lastSnapshot)}`);
}

function isSameFocusSnapshot(left, right) {
  return left?.tag === right?.tag
    && left?.text === right?.text
    && left?.aria === right?.aria
    && left?.role === right?.role
    && left?.rect?.left === right?.rect?.left
    && left?.rect?.top === right?.rect?.top
    && left?.rect?.width === right?.rect?.width
    && left?.rect?.height === right?.rect?.height;
}

async function revealGamepadDiagnosticsSection(page) {
  await waitForBodyText(page, diagnosticsPagePattern, "diagnostics settings page before gamepad reveal");

  const headerTargets = {
    labels: ["Gamepad input", "手柄输入", "Compatibility monitor", "兼容性监测"],
    automationIds: ["Diagnostics.GamepadMonitorHeader"]
  };

  for (let attempt = 0; attempt < 16; attempt += 1) {
    const state = await readControlState(page, gamepadStart);
    if (state.found) {
      return;
    }

    await page.evaluate(() => {
      const normalize = value => (value ?? "").replace(/\s+/g, " ").trim().toLowerCase();
      const titles = new Set(["gamepad input", "手柄输入", "compatibility monitor", "兼容性监测"]);

      // Prefer exact leaf text matches so we click the section header, not a large ancestor.
      const leafHeaders = Array.from(document.querySelectorAll("body *"))
        .filter(element => {
          const children = Array.from(element.childNodes);
          const directText = children
            .filter(node => node.nodeType === Node.TEXT_NODE)
            .map(node => normalize(node.textContent))
            .join(" ")
            .trim();
          const text = normalize(element.textContent);
          const aria = normalize(element.getAttribute("aria-label"));
          return titles.has(directText) || titles.has(text) || titles.has(aria);
        })
        .sort((left, right) => {
          const leftArea = left.getBoundingClientRect().width * left.getBoundingClientRect().height;
          const rightArea = right.getBoundingClientRect().width * right.getBoundingClientRect().height;
          return leftArea - rightArea;
        });

      for (const element of leafHeaders) {
        const expander =
          element.closest(".uno-expander")
          ?? element.closest("[class*='Expander']")
          ?? element.closest("details");
        const header =
          expander?.querySelector("button, [role='button'], .uno-expanderheader, summary")
          ?? element.closest("button, [role='button'], summary")
          ?? element;
        header.scrollIntoView({ block: "center", inline: "nearest" });
        if (typeof header.click === "function") {
          header.click();
          return true;
        }
      }

      // Fallback: expand any collapsed expander that is still closed on the diagnostics page.
      const toggles = Array.from(document.querySelectorAll("button, [role='button'], summary"))
        .filter(element => {
          const ariaExpanded = element.getAttribute("aria-expanded");
          return ariaExpanded === "false" || element.tagName.toLowerCase() === "summary";
        });
      for (const toggle of toggles) {
        const text = normalize(toggle.textContent);
        if (text.includes("gamepad") || text.includes("手柄") || text.includes("monitor") || text.includes("监测") || text.includes("diagnostics") || text.includes("诊断") || text.includes("logs") || text.includes("日志") || text.includes("voice") || text.includes("语音")) {
          toggle.scrollIntoView({ block: "center", inline: "nearest" });
          toggle.click();
        }
      }

      return false;
    });

    await scrollToVisibleControl(page, headerTargets);
    await scrollToVisibleControl(page, gamepadStart);
    await page.mouse.wheel(0, 500);
    await page.waitForTimeout(300);
  }

  const state = await readControlState(page, gamepadStart);
  throw new Error(`Diagnostics gamepad section was not reachable in BrowserWasm. State=${JSON.stringify(state)}`);
}
