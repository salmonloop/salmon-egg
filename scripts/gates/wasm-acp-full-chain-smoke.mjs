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
  navigateToSettingsSection,
  createWebSocketProfile,
  expectProfilePresence,
  createRemoteDirectory,
  expectPersistedProfileAfterReload,
  expectRemoteDirectoryPresence,
  ensureGlobalAcpEnabled,
  clickProfileConnectionToggle,
  waitForInitializeWithDiagnostics,
  createSessionAndSendPromptFromStart
} from "./wasm-smoke-lib/settings-ui.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-acp-full-chain-smoke.mjs");
const profileName = `WASM full chain ${Date.now()}`;
const remoteDirectoryName = `WASM remote project ${Date.now()}`;
const remoteDirectoryPath = `/remote/wasm-full-chain-${Date.now()}`;
const fullChainPromptText = `WASM full chain prompt ${Date.now()}`;
const fullChainAgentReplyText = `WASM full chain agent reply ${Date.now()}`;
const browser = await chromium.launch({ headless: true });
const acpServer = await startAcpWebSocketServer({
  agentReplyText: fullChainAgentReplyText,
  sessionTitle: "WASM full chain session"
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
    await expectProfilePresence(page, profileName, "saved ACP profile");
    await createRemoteDirectory(page, remoteDirectoryName, remoteDirectoryPath);
    await page.waitForTimeout(1_500);

    await expectPersistedProfileAfterReload(page, baseUrl, profileName);
    await expectRemoteDirectoryPresence(page, remoteDirectoryName, remoteDirectoryPath, "persisted remote directory");

    await ensureGlobalAcpEnabled(page);
    await clickProfileConnectionToggle(page, profileName);
    await waitForInitializeWithDiagnostics(acpServer, page, profileName);
    await createSessionAndSendPromptFromStart(
      page,
      acpServer,
      profileName,
      remoteDirectoryName,
      remoteDirectoryPath,
      fullChainPromptText,
      fullChainAgentReplyText);

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM ACP full-chain smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
  await acpServer.close();
}
