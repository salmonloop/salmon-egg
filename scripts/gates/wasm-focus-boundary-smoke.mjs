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
const gamepadStart = { labels: ["Start monitor", "开始监测"], automationIds: ["Diagnostics.GamepadStart"] };
const gamepadRefresh = { labels: ["Refresh once", "刷新一次"], automationIds: ["Diagnostics.GamepadRefresh"] };
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

    await revealGamepadDiagnosticsSection(page);
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
      // Fallback rectangle scanner: match the id contract, not the name. These buttons carry no
      // AutomationProperties.Name, so their aria-label only *coincidentally* equals the automation
      // id (the nameless fallback); xamlautomationid keeps working whether or not a name is added.
      const start = document.querySelector('[xamlautomationid="Diagnostics.GamepadStart"]');
      const expander =
        start?.closest(".uno-expander")
        ?? start?.closest("[class*='Expander']")
        ?? start?.closest("details")
        ?? null;
      const ownedToggle =
        expander?.querySelector('[xamlautomationid="ExpanderToggleButton"], button, [role="button"], .uno-expanderheader, summary')
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
        document.querySelectorAll('[xamlautomationid="ExpanderToggleButton"], button, [role="button"], summary'));
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


function sameFocusTarget(left, right) {
  return left.automationId === right.automationId
    && left.aria === right.aria
    && left.text === right.text
    && left.rect.left === right.rect.left
    && left.rect.top === right.rect.top;
}
