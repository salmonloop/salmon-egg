import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  focusVisibleControl,
  readControlState,
  scrollToVisibleControl,
  waitForBodyText,
  waitForFocusedElementSnapshot
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection,
  clickTopNavigationOverflowTargetUntilBodyText
} from "./wasm-smoke-lib/settings-shell.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-focus-boundary-smoke.mjs");
const diagnosticsPagePattern = /Diagnostics and logs|诊断与日志|Live logs|日志|Gamepad input|手柄输入/;
const gamepadStart = { labels: [], automationIds: ["Diagnostics.GamepadStart", "DiagnosticsGamepadStartButton"] };
const gamepadRefresh = { labels: [], automationIds: ["Diagnostics.GamepadRefresh", "DiagnosticsGamepadRefreshButton"] };
const browser = await chromium.launch({ headless: true });

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["诊断与日志", "Diagnostics & Logs", "Diagnostics"], automationIds: ["SettingsNav.Diagnostics"] },
      diagnosticsPagePattern,
      "diagnostics settings page");

    await page.setViewportSize({ width: 390, height: 844 });
    await waitForBodyText(page, diagnosticsPagePattern, "diagnostics settings page at mobile viewport");
    await clickTopNavigationOverflowTargetUntilBodyText(
      page,
      { labels: ["诊断与日志", "Diagnostics & Logs", "Diagnostics"], automationIds: ["SettingsNav.Diagnostics"] },
      diagnosticsPagePattern,
      "diagnostics settings page from mobile overflow");
    await page.setViewportSize({ width: 1280, height: 900 });

    await scrollToVisibleControl(page, gamepadStart);
    const startState = await readControlState(page, gamepadStart);
    if (!startState.found) {
      throw new Error(`Diagnostics gamepad start control was not visible. State=${JSON.stringify(startState)}`);
    }

    const initialFocusTarget = startState.enabled ? gamepadStart : gamepadRefresh;
    const initialSnapshot = await focusVisibleControl(page, initialFocusTarget, "diagnostics gamepad focus target");

    await page.keyboard.press("Tab");
    const nextSnapshot = await waitForFocusedElementSnapshot(page, "next diagnostics focus target");
    if (sameFocusTarget(initialSnapshot, nextSnapshot)) {
      throw new Error(`Tab did not advance diagnostics focus. Before=${JSON.stringify(initialSnapshot)} After=${JSON.stringify(nextSnapshot)}`);
    }

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM focus boundary smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}

function sameFocusTarget(left, right) {
  return left.automationId === right.automationId
    && left.aria === right.aria
    && left.text === right.text
    && left.rect.left === right.rect.left
    && left.rect.top === right.rect.top;
}
