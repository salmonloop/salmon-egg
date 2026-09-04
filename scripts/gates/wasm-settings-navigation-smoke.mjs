import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import { navigateToSettingsSection } from "./wasm-smoke-lib/settings-shell.mjs";

// Behaviour under test: from a cold start a user can reach the settings shell and, from there, the
// Diagnostics section - at a window narrow enough that the section list has collapsed behind the
// overflow affordance, which is where the route is easiest to break.
//
// The route is not the assertion. navigateToSettingsSection owns choosing between a visible section
// entry and the overflow menu, and confirms arrival on the destination's page title rendering. This
// file used to re-run the viewport change and the overflow click that helper already performed,
// which made the order of two viewport mutations load-bearing; the narrow window is now simply how
// this context is created.
const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-settings-navigation-smoke.mjs");
const browser = await chromium.launch({ headless: true });

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser, {
    viewport: { width: 390, height: 844 }
  });

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
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
