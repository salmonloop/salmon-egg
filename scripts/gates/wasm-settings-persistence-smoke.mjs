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
  readAppSettingsPersistenceDebug,
  selectAlternateCacheRetentionValue
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection
} from "./wasm-smoke-lib/settings-shell.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-settings-persistence-smoke.mjs");
const dataStorageCacheRetentionControl = {
  labels: ["缓存保留天数", "Cache retention"],
  automationIds: ["DataStorage.CacheRetention"]
};
const browser = await chromium.launch({ headless: true });

try {
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["数据与存储", "Data storage", "Data"], automationIds: ["SettingsNav.DataStorage"] },
      /数据与存储|Data storage|Save local history|缓存保留天数|Cache retention/,
      "data storage settings page");

    const initialValue = await readNumericControlValue(
      page,
      dataStorageCacheRetentionControl,
      "cache retention before edit");
    const updatedValue = selectAlternateCacheRetentionValue(initialValue);

    await setNumericControlValue(
      page,
      dataStorageCacheRetentionControl,
      updatedValue,
      "cache retention");
    // BrowserWasm settings persistence is asynchronous and has no user-visible "saved" signal.
    await page.waitForTimeout(1_500);

    await page.reload({ waitUntil: "domcontentloaded", timeout: 60_000 });
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["数据与存储", "Data storage", "Data"], automationIds: ["SettingsNav.DataStorage"] },
      /数据与存储|Data storage|Save local history|缓存保留天数|Cache retention/,
      "data storage settings page after reload");

    const persistedValue = await readNumericControlValue(
      page,
      dataStorageCacheRetentionControl,
      "cache retention after reload");
    if (persistedValue !== updatedValue) {
      const debug = await readAppSettingsPersistenceDebug(page, {
        controlOptions: dataStorageCacheRetentionControl,
        path: "/local/SalmonEgg/config/app.yaml"
      });
      throw new Error(
        `App settings did not persist across reload. `
        + `Expected ${updatedValue}, got ${persistedValue}. `
        + `StorageDebug=${JSON.stringify(debug)}`);
    }

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM settings persistence smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}
