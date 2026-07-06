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
  waitForControlState
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection
} from "./wasm-smoke-lib/settings-shell.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-gamepad-boundary-smoke.mjs");
const diagnosticsPagePattern = /Diagnostics and logs|诊断与日志|Gamepad input|手柄输入/;
const gamepadStart = { labels: [], automationIds: ["Diagnostics.GamepadStart"] };
const gamepadRefresh = { labels: [], automationIds: ["Diagnostics.GamepadRefresh"] };
const browser = await chromium.launch({ headless: true });

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["诊断与日志", "Diagnostics"], automationIds: ["SettingsNav.Diagnostics"] },
      diagnosticsPagePattern,
      "diagnostics settings page");

    await revealGamepadDiagnosticsSection(page);
    const initialProjection = await expectUnsupportedGamepadProjection(page, "initial BrowserWasm gamepad diagnostics");

    await page.mouse.click(initialProjection.startState.x, initialProjection.startState.y);
    await page.waitForTimeout(250);
    await expectUnsupportedGamepadProjection(page, "BrowserWasm gamepad diagnostics after start attempt");

    await clickVisibleControl(page, gamepadRefresh);
    await page.waitForTimeout(250);
    await expectUnsupportedGamepadProjection(page, "refreshed BrowserWasm gamepad diagnostics");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM gamepad boundary smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}

async function expectUnsupportedGamepadProjection(page, label) {
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
    /not supported|不支持/i,
    `${label} unsupported status`);
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

async function expectControlText(page, options, pattern, label) {
  const state = await waitForControlState(page, options, label);
  const text = (state.text || state.aria || "").trim();
  if (!pattern.test(text)) {
    throw new Error(`Unexpected ${label}. Text=${JSON.stringify(text)} State=${JSON.stringify(state)}`);
  }
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
