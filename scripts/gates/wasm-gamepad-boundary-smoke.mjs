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
const gamepadStart = { labels: [], automationIds: ["Diagnostics.GamepadStart"] };
const gamepadRefresh = { labels: [], automationIds: ["Diagnostics.GamepadRefresh"] };
const supportedStatusPattern = /not started|stopped|monitoring|未启动|已停止|正在监测/i;
const standardGamepadProjectionScript = `
(() => {
  const state = {
    connected: false,
    mapping: "standard",
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
        id: "SalmonEgg Smoke Standard Gamepad",
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

async function verifyInjectedStandardGamepadNativeControlBridge() {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await context.addInitScript({ content: standardGamepadProjectionScript });
    await openDiagnosticsGamepadSection(page);
    await page.evaluate(() => {
      globalThis.__salmoneggSmokeGamepad.setState({
        connected: true,
        pressedButtons: [],
        axes: [0, 0, 0, 0]
      });
    });

    const beforeMove = await focusControlByAutomationId(page, "Diagnostics.GamepadRefresh", "diagnostics refresh");
    await setInjectedGamepadButtons(page, [13]);
    const afterMove = await waitForDifferentFocusedElement(page, beforeMove, "DPadDown diagnostics focus move");
    await setInjectedGamepadButtons(page, []);

    if (afterMove.isBody || !afterMove.visible) {
      throw new Error(`DPadDown moved focus to an invalid target. Snapshot=${JSON.stringify(afterMove)}`);
    }

    await focusControlByAutomationId(page, "Diagnostics.GamepadRefresh", "diagnostics refresh before activate");
    await setInjectedGamepadButtons(page, [0]);
    await waitForControlText(
      page,
      { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
      /Activate/,
      "gamepad Activate native control invocation",
      5_000);
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
  const startState = await waitForControlState(page, gamepadStart, `${label} start action`);
  if (!Number.isFinite(startState.x) || !Number.isFinite(startState.y)) {
    throw new Error(`BrowserWasm gamepad start action did not expose a stable point. State=${JSON.stringify(startState)}`);
  }

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
  const state = await waitForControlState(page, options, label);
  const text = (state.text || state.aria || "").trim();
  if (!pattern.test(text)) {
    throw new Error(`Unexpected ${label}. Text=${JSON.stringify(text)} State=${JSON.stringify(state)}`);
  }
}

async function waitForControlText(page, options, pattern, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
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

  for (let attempt = 0; attempt < 8; attempt += 1) {
    const state = await readControlState(page, gamepadStart);
    if (state.found) {
      return;
    }

    await scrollToVisibleControl(page, gamepadStart);
    await page.mouse.wheel(0, 900);
    await page.waitForTimeout(250);
  }

  const state = await readControlState(page, gamepadStart);
  throw new Error(`Diagnostics gamepad section was not reachable in BrowserWasm. State=${JSON.stringify(state)}`);
}
