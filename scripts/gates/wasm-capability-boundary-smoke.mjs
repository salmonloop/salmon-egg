import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  clearBrowserOriginStorage,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import { startAcpWebSocketServer } from "./wasm-smoke-lib/acp-test-server.mjs";
import {
  readControlState,
  scrollToVisibleControl,
  waitForControlState,
  expectControlDoesNotEscapePage
} from "./wasm-smoke-lib/ui-affordances.mjs";
import {
  navigateToSettingsSection
} from "./wasm-smoke-lib/settings-shell.mjs";
import {
  createWebSocketProfile,
  ensureAcpProfilesReady,
  clickProfileConnectionToggle,
  waitForInitializeWithDiagnostics
} from "./wasm-smoke-lib/acp-ui-fixture.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-capability-boundary-smoke.mjs");
const profileName = `WASM capability ${Date.now()}`;
const browser = await chromium.launch({ headless: true });
const acpServer = await startAcpWebSocketServer({
  agentReplyText: `WASM capability agent reply ${Date.now()}`,
  sessionTitle: "WASM capability session"
});

try {
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(
      page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP 连接配置|ACP connection profiles/,
      "ACP Agent settings page");

    await createWebSocketProfile(page, profileName, acpServer.url);
    await ensureAcpProfilesReady(page);
    await clickProfileConnectionToggle(page, profileName);
    const initializeRequest = await waitForInitializeWithDiagnostics(acpServer, page, profileName);
    expectNoAdvertisedFileSystemCapability(initializeRequest);

    await navigateToSettingsSection(
      page,
      { labels: ["数据与存储", "Data storage", "Data"], automationIds: ["SettingsNav.DataStorage"] },
      /数据与存储|Data storage|Save local history|缓存保留天数|Cache retention/,
      "data storage settings page");

    await scrollToVisibleControl(
      page,
      { labels: ["打开缓存目录", "Open cache folder"], automationIds: ["DataStorage.OpenCacheFolder"] });
    const cacheFolderState = await waitForControlState(
      page,
      { labels: ["打开缓存目录", "Open cache folder"], automationIds: ["DataStorage.OpenCacheFolder"] },
      "cache-folder affordance");
    if (cacheFolderState.enabled) {
      await expectControlDoesNotEscapePage(
        page,
        { labels: ["打开缓存目录", "Open cache folder"], automationIds: ["DataStorage.OpenCacheFolder"] },
        /数据与存储|Data storage|Save local history|缓存保留天数|Cache retention/);
    }

    await scrollToVisibleControl(
      page,
      { labels: ["打开导出目录", "Open exports folder"], automationIds: [] });
    const exportsState = await waitForControlState(
      page,
      { labels: ["打开导出目录", "Open exports folder"], automationIds: [] },
      "exports-folder affordance");
    if (exportsState.enabled) {
      await expectControlDoesNotEscapePage(
        page,
        { labels: ["打开导出目录", "Open exports folder"], automationIds: [] },
        /数据与存储|Data storage|Save local history|缓存保留天数|Cache retention/);
    }

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM capability boundary smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
  await acpServer.close();
}

function expectNoAdvertisedFileSystemCapability(initializeRequest) {
  const clientCapabilities = initializeRequest?.params?.clientCapabilities;
  if (!clientCapabilities || typeof clientCapabilities !== "object") {
    throw new Error(`Initialize request did not include clientCapabilities: ${JSON.stringify(initializeRequest)}`);
  }

  if (Object.prototype.hasOwnProperty.call(clientCapabilities, "fs")) {
    throw new Error(`WASM client must not advertise ACP fs capability: ${JSON.stringify(clientCapabilities)}`);
  }

  if (clientCapabilities.terminal === true) {
    throw new Error(`WASM client must not advertise ACP terminal capability: ${JSON.stringify(clientCapabilities)}`);
  }
}
