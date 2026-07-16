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


function sameFocusTarget(left, right) {
  return left.automationId === right.automationId
    && left.aria === right.aria
    && left.text === right.text
    && left.rect.left === right.rect.left
    && left.rect.top === right.rect.top;
}
