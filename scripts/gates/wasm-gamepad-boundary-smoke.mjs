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
  await verifyInjectedMultiBrandFaceAndTriggerSemanticsProjection();
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
        family: "Xbox",
        layoutPattern: /layout\s+(Standard|标准)/
      },
      {
        label: "DualSense identity",
        id: "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)",
        name: "DualSense Wireless Controller",
        vid: "054C",
        pid: "0CE6",
        family: "Sony",
        layoutPattern: /layout\s+(Standard|标准)/
      },
      {
        label: "Switch Pro Controller identity",
        id: "Pro Controller (STANDARD GAMEPAD Vendor: 057e Product: 2009)",
        name: "Pro Controller",
        vid: "057E",
        pid: "2009",
        family: "Nintendo",
        layoutPattern: /layout\s+(Nintendo|任天堂)/
      },
      {
        label: "Firefox-style DualSense identity",
        id: "054c-0ce6-DualSense Wireless Controller",
        name: "DualSense Wireless Controller",
        vid: "054C",
        pid: "0CE6",
        family: "Sony",
        layoutPattern: /layout\s+(Standard|标准)/
      },
      {
        label: "Firefox-style Xbox identity",
        id: "045e-0b13-Xbox Wireless Controller",
        name: "Xbox Wireless Controller",
        vid: "045E",
        pid: "0B13",
        family: "Xbox",
        layoutPattern: /layout\s+(Standard|标准)/
      },
      {
        label: "Firefox-style Switch Pro identity",
        id: "057e-2009-Pro Controller",
        name: "Pro Controller",
        vid: "057E",
        pid: "2009",
        family: "Nintendo",
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

      // Format: "#0 {name} VID {vid} PID {pid}; family {family}; layout {layout}; ..."
      // Identity/family/layout are diagnostics labeling only; face semantics stay position-based.
      const detailsPattern = new RegExp(
        `#0\\s+${escapeRegExp(brand.name)}\\s+VID\\s+${brand.vid}\\s+PID\\s+${brand.pid};\\s*family\\s+${escapeRegExp(brand.family)};\\s*${brand.layoutPattern.source}`,
        "i");
      await expectControlText(
        page,
        { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
        detailsPattern,
        `${brand.label} standard details`);

      // Standard mapping slot A is position-based Activate for all brands under W3C mapping.
      await expectControlText(
        page,
        { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
        /pressed\s+A/i,
        `${brand.label} pressed standard face A`);
      await expectControlText(
        page,
        { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
        /Activate/,
        `${brand.label} active Activate intent`);
    }

    assertNoFatalConsoleMessages(fatalConsoleMessages);
  } finally {
    await context.close();
  }
}


async function verifyInjectedMultiBrandFaceAndTriggerSemanticsProjection() {
  // W3C standard mapping is position-based for every brand under mapping:"standard".
  // Identity only labels diagnostics; face/trigger intents must stay brand-neutral slots.
  const brands = [
    {
      label: "Xbox standard face/trigger semantics",
      id: "Xbox Wireless Controller (STANDARD GAMEPAD Vendor: 045e Product: 0b13)"
    },
    {
      label: "DualSense standard face/trigger semantics",
      id: "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)"
    },
    {
      label: "Switch Pro standard face/trigger semantics",
      id: "Pro Controller (STANDARD GAMEPAD Vendor: 057e Product: 2009)"
    }
  ];

  const cases = [
    {
      pressedButtons: [0],
      activePattern: /Activate/,
      pressedPattern: /pressed\s+A/i,
      readingPattern: /LT 0\.00, RT 0\.00/,
      label: "bottom face -> Activate"
    },
    {
      pressedButtons: [1],
      activePattern: /Back/,
      pressedPattern: /pressed\s+B/i,
      readingPattern: /LT 0\.00, RT 0\.00/,
      label: "east face -> Back"
    },
    {
      pressedButtons: [2],
      activePattern: /^(None|无)$/,
      pressedPattern: /pressed\s+X/i,
      readingPattern: /LT 0\.00, RT 0\.00/,
      label: "west face -> no app semantic"
    },
    {
      pressedButtons: [3],
      activePattern: /ToggleVoiceInput/,
      pressedPattern: /pressed\s+Y/i,
      readingPattern: /LT 0\.00, RT 0\.00/,
      label: "north face -> ToggleVoiceInput"
    },
    {
      pressedButtons: [6],
      activePattern: /PageUp/,
      pressedPattern: /pressed\s+LeftTrigger/i,
      readingPattern: /LT 1\.00, RT 0\.00/,
      label: "left trigger -> PageUp"
    },
    {
      pressedButtons: [7],
      activePattern: /PageDown/,
      pressedPattern: /pressed\s+RightTrigger/i,
      readingPattern: /LT 0\.00, RT 1\.00/,
      label: "right trigger -> PageDown"
    }
  ];

  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await context.addInitScript({ content: standardGamepadProjectionScript });
    await openDiagnosticsGamepadSection(page);

    for (const brand of brands) {
      for (const sample of cases) {
        await page.evaluate(({ id, pressedButtons }) => {
          globalThis.__salmoneggSmokeGamepad.setState({
            connected: true,
            mapping: "standard",
            id,
            pressedButtons,
            axes: [0, 0, 0, 0]
          });
        }, { id: brand.id, pressedButtons: sample.pressedButtons });

        await clickVisibleControl(page, gamepadRefresh);
        await page.waitForTimeout(200);

        await expectControlText(
          page,
          { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
          sample.activePattern,
          `${brand.label} ${sample.label} active inputs`);
        await expectControlText(
          page,
          { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
          sample.pressedPattern,
          `${brand.label} ${sample.label} pressed details`);
        await expectControlText(
          page,
          { labels: [], automationIds: ["Diagnostics.GamepadThumbstick"] },
          sample.readingPattern,
          `${brand.label} ${sample.label} reading LT/RT`);
        await expectControlText(
          page,
          { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
          sample.readingPattern,
          `${brand.label} ${sample.label} standard details reading LT/RT`);
      }
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
  // Architecture (post shell-fallback removal): BrowserWasm gamepad polling is an
  // authoritative fact/diagnostics source. Native Uno focus ownership stays with
  // keyboard/XYFocus and control consumers; smoke must not require a second global
  // shell focus bridge that reintroduces double-dispatch risk.
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
    const startState = await waitForControlState(page, gamepadStart, "diagnostics monitor start action");
    if (startState.enabled) {
      await clickVisibleControl(page, gamepadStart);
      await page.waitForTimeout(300);
    }

    // Standard-mapping DPadDown (index 13) must project as MoveDown intent facts.
    await setInjectedGamepadButtons(page, [13]);
    await clickVisibleControl(page, gamepadRefresh);
    await waitForControlText(
      page,
      { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
      /MoveDown/,
      "gamepad DPadDown active MoveDown intent",
      15_000);
    await expectControlText(
      page,
      { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
      /pressed\s+DPadDown/i,
      "gamepad DPadDown pressed standard slot");
    await setInjectedGamepadButtons(page, []);

    // Standard-mapping A (index 0) must project Activate intent facts for consumers.
    await setInjectedGamepadButtons(page, [0]);
    await clickVisibleControl(page, gamepadRefresh);
    await waitForControlText(
      page,
      { labels: [], automationIds: ["Diagnostics.GamepadActiveInputs"] },
      /Activate/,
      "gamepad Activate active intent",
      15_000);
    await expectControlText(
      page,
      { labels: [], automationIds: ["Diagnostics.GamepadStandardDetails"] },
      /pressed\s+A/i,
      "gamepad Activate pressed standard slot");
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
    /X 0\.00, Y 0\.00; LT 0\.00, RT 0\.00/,
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
    /X 0\.25, Y -0\.50; LT 0\.00, RT 0\.00/,
    `${label} thumbstick`);
  await waitForBodyText(page, diagnosticsPagePattern, `${label} page still visible`);
}

async function expectControlText(page, options, pattern, label) {
  const state = await waitForControlText(page, options, pattern, label, 15_000);
  return state;
}

async function waitForControlText(page, options, pattern, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    await scrollToVisibleControl(page, options, 2_000).catch(() => false);
    lastState = await readControlState(page, options);
    const text = (lastState?.text || lastState?.aria || "").trim();
    if (lastState?.found && pattern.test(text)) {
      return lastState;
    }

    // BrowserWasm TextBlocks often keep AutomationId off the DOM (no aria-label).
    // Fall back to leaf text in the expanded Gamepad section for diagnostics projection.
    // Do not require the leaf to already be in the viewport; scroll it into view first.
    const fallback = await page.evaluate(({ patternSource, flags }) => {
      const re = new RegExp(patternSource, flags);
      const isLaidOut = element => {
        const rect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return rect.width > 0
          && rect.height > 0
          && style.display !== "none"
          && style.visibility !== "hidden"
          && Number(style.opacity || "1") > 0;
      };

      const start = document.querySelector('[aria-label="Diagnostics.GamepadStart"]');
      const scope =
        start?.closest(".uno-expander")
        ?? start?.closest("[class*='Expander']")
        ?? document.body;
      const leaves = Array.from(scope.querySelectorAll("*"))
        .filter(element => element.children.length === 0)
        .filter(isLaidOut);

      for (const element of leaves) {
        const text = (element.textContent || "").replace(/\s+/g, " ").trim();
        if (!text || !re.test(text)) {
          continue;
        }

        element.scrollIntoView({ block: "center", inline: "nearest" });
        const rect = element.getBoundingClientRect();
        return {
          found: true,
          enabled: true,
          text,
          aria: element.getAttribute("aria-label") || "",
          x: rect.left + rect.width / 2,
          y: rect.top + rect.height / 2,
          via: "leaf-text-fallback"
        };
      }

      return { found: false, enabled: false };
    }, { patternSource: pattern.source, flags: pattern.flags });

    if (fallback?.found) {
      return fallback;
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


async function revealGamepadDiagnosticsSection(page) {
  await waitForBodyText(page, diagnosticsPagePattern, "diagnostics settings page before gamepad reveal");

  const headerTargets = {
    labels: ["Gamepad input", "手柄输入", "Compatibility monitor", "兼容性监测"],
    automationIds: ["Diagnostics.GamepadMonitorHeader"]
  };

  for (let attempt = 0; attempt < 20; attempt += 1) {
    const state = await readControlState(page, gamepadStart);
    if (state.found) {
      return;
    }

    // Uno Expander does not reliably expand from synthetic element.click() in
    // BrowserWasm. Use a real Playwright mouse click on the Gamepad expander
    // toggle (or the nearest ExpanderToggleButton whose text mentions gamepad).
    const togglePoint = await page.evaluate(() => {
      const normalize = value => (value ?? "").replace(/\s+/g, " ").trim().toLowerCase();
      const start = document.querySelector('[aria-label="Diagnostics.GamepadStart"]');
      const expander =
        start?.closest(".uno-expander")
        ?? start?.closest("[class*='Expander']")
        ?? start?.closest("details")
        ?? null;
      const ownedToggle =
        expander?.querySelector('[aria-label="ExpanderToggleButton"], button, [role="button"], .uno-expanderheader, summary')
        ?? null;
      if (ownedToggle) {
        const rect = ownedToggle.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) {
          return {
            x: rect.left + rect.width / 2,
            y: rect.top + rect.height / 2,
            source: "owned-toggle"
          };
        }
      }

      const toggles = Array.from(
        document.querySelectorAll('[aria-label="ExpanderToggleButton"], button, [role="button"], summary'));
      for (const toggle of toggles) {
        const text = normalize(toggle.textContent);
        if (text.includes("gamepad") || text.includes("手柄") || text.includes("compatibility monitor") || text.includes("兼容性监测")) {
          const rect = toggle.getBoundingClientRect();
          if (rect.width > 0 && rect.height > 0
            && rect.left >= -1
            && rect.top >= -1
            && rect.left <= innerWidth
            && rect.top <= innerHeight) {
            return {
              x: rect.left + rect.width / 2,
              y: rect.top + rect.height / 2,
              source: "text-toggle"
            };
          }
        }
      }

      return null;
    });

    if (togglePoint) {
      await page.mouse.click(togglePoint.x, togglePoint.y);
      await page.waitForTimeout(500);
      const afterToggle = await readControlState(page, gamepadStart);
      if (afterToggle.found) {
        return;
      }
    }

    await scrollToVisibleControl(page, headerTargets);
    await scrollToVisibleControl(page, gamepadStart);
    await page.mouse.wheel(0, 700);
    await page.waitForTimeout(300);
  }

  const state = await readControlState(page, gamepadStart);
  throw new Error(`Diagnostics gamepad section was not reachable in BrowserWasm. State=${JSON.stringify(state)}`);
}
