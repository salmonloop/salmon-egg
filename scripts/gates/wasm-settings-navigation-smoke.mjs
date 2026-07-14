import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  waitForBodyText
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection,
  clickTopNavigationOverflowTargetUntilBodyText
} from "./wasm-smoke-lib/settings-shell.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-settings-navigation-smoke.mjs");
const browser = await chromium.launch({ headless: true });

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["设置", "Settings"], automationIds: ["SettingsItem"] },
      /常规|General|外观|Appearance|ACP \/ Agent/,
      "settings shell");

    await page.setViewportSize({ width: 390, height: 844 });
    await waitForBodyText(page, /常规|General|外观|Appearance|ACP \/ Agent/, "settings shell at mobile viewport");

    await clickTopNavigationOverflowTargetUntilBodyText(
      page,
      { labels: ["诊断与日志", "Diagnostics & Logs", "Diagnostics"], automationIds: ["SettingsNav.Diagnostics"] },
      /Diagnostics and logs|诊断与日志|Live logs|日志/,
      "diagnostics settings page");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM settings navigation smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}
