import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright";
import {
  normalizeBaseUrl, clearBrowserOriginStorage, createInstrumentedContext, openApp, assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import { navigateToSettingsSection } from "./wasm-smoke-lib/settings-shell.mjs";
import { createWebSocketProfile, createRemoteDirectory, ensureAcpProfilesReady, clickProfileConnectionToggle }
  from "./wasm-smoke-lib/acp-ui-fixture.mjs";
import {
  selectComboBoxItem, clickVisibleNavigationTarget, typeIntoVisibleTextField,
  clickStartComposerSendButton, waitForSemanticText, collectVisibleInteractiveDebug, waitForControlEnabledState
} from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-official-v2-agent-smoke.mjs");
const agentUrl = process.env.SALMONEGG_OFFICIAL_V2_WS_URL;
assert.ok(agentUrl, "Supply the released bridge URL fronting the unmodified official Rust V2 Agent.");
const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined });
const frames = [];
const marker = `official-v2-gui-${Date.now()}`;
const profile = `Official V2 ${Date.now()}`;
const directory = `V2 project ${Date.now()}`;
const cwd = process.env.SALMONEGG_OFFICIAL_V2_CWD || "/tmp";
const artifacts = process.env.WASM_SMOKE_ARTIFACTS_DIR;

function record(direction, event) {
  const text = typeof event.payload === "string" ? event.payload : event.payload.toString("utf8");
  try {
    const parsed = JSON.parse(text);
    for (const frame of Array.isArray(parsed) ? parsed : [parsed]) frames.push({ direction, frame });
  } catch {
    throw new Error("The official bridge sent a non-JSON ACP frame.");
  }
}

async function awaitFrame(predicate, label) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    const match = frames.find(predicate);
    if (match) return match.frame;
    await new Promise(resolve => setTimeout(resolve, 20));
  }
  throw new Error(`No ${label} was observed on the actual browser WebSocket.`);
}

try {
  await clearBrowserOriginStorage(browser, baseUrl);
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);
  page.on("websocket", socket => {
    if (socket.url() === agentUrl) {
      socket.on("framesent", frame => record("client", frame));
      socket.on("framereceived", frame => record("agent", frame));
    }
  });
  try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(page,
      { labels: ["ACP Agent", "ACP / Agent"], automationIds: ["SettingsNav.AgentAcp"] },
      /ACP Agent|ACP connection profiles/, "ACP Agent settings");
    await createWebSocketProfile(page, profile, agentUrl);
    await createRemoteDirectory(page, directory, cwd);
    await ensureAcpProfilesReady(page);
    await clickProfileConnectionToggle(page, profile);
    const initialize = await awaitFrame(item => item.direction === "client" && item.frame.method === "initialize", "V2 offer");
    assert.equal(initialize.params.protocolVersion, 2, "Only the explicitly opted-in artifact may enter this gate.");
    assert.ok(initialize.params.info);
    assert.equal(initialize.params.clientInfo, undefined);
    const initialized = await awaitFrame(item => item.direction === "agent" && item.frame.id === initialize.id, "negotiated V2 response");
    assert.equal(initialized.result.protocolVersion, 2);

    await clickVisibleNavigationTarget(page, { labels: ["Start", "开始"], automationIds: ["MainNav.Start"] });
    await selectComboBoxItem(page, "AgentSelectorHost", profile, { verifySelectionText: false, keyboardSelectVisibleItem: true });
    await selectComboBoxItem(page, "ProjectSelectorHost", directory, { verifySelectionText: false, keyboardSelectVisibleItem: true });
    const created = await awaitFrame(item => item.direction === "client" && item.frame.method === "session/new", "session creation");
    assert.equal(created.params.cwd, cwd);
    await awaitFrame(item => item.direction === "agent" && item.frame.id === created.id, "created session response");
    await typeIntoVisibleTextField(page, { selector: "#uno-semantics-root textarea" }, marker, "V2 prompt");
    await clickStartComposerSendButton(page);
    const prompt = await awaitFrame(item => item.direction === "client" && item.frame.method === "session/prompt", "prompt");
    const accepted = await awaitFrame(item => item.direction === "agent" && item.frame.id === prompt.id, "prompt acknowledgement");
    assert.equal(accepted.result.stopReason, undefined, "V2 acknowledgement is not a terminal prompt result.");
    const idle = await awaitFrame(item => item.direction === "agent" && item.frame.method === "session/update"
      && item.frame.params.update.sessionUpdate === "state_update" && item.frame.params.update.state === "idle", "ending idle");
    assert.equal(idle.params.update.stopReason, "end_turn");
    await waitForSemanticText(page, new RegExp(`Echo: ${marker}`), "official Agent response visible in ChatView", 30_000);
    await waitForControlEnabledState(page, { automationIds: ["ChatInputArea.Input"] }, true, "input enabled after authoritative idle", 30_000);
    assert.equal(frames.filter(item => item.direction === "client" && item.frame.method === "session/prompt").length, 1);
    assert.equal(frames.filter(item => item.direction === "client" && item.frame.method === "session/load").length, 0);
    assertNoFatalConsoleMessages(fatalConsoleMessages);
    if (artifacts) {
      await mkdir(artifacts, { recursive: true });
      await writeFile(`${artifacts}/result.json`, JSON.stringify({ passed: true, agent: "official Rust simple_agent_v2", llm: false,
        negotiatedVersion: 2, marker, accepted: true, idle: true, inputEnabled: true }, null, 2));
      await writeFile(`${artifacts}/wire.json`, JSON.stringify(frames, null, 2));
      await page.screenshot({ path: `${artifacts}/final.png` });
    }
    console.log("Official ACP V2 Agent WASM application gate passed");
  } catch (error) {
    if (artifacts) {
      await mkdir(artifacts, { recursive: true });
      await page.screenshot({ path: `${artifacts}/failure.png` });
      await writeFile(`${artifacts}/failure.json`, JSON.stringify({ error: String(error), frames,
        controls: await page.evaluate(collectVisibleInteractiveDebug), fatalConsoleMessages }, null, 2));
    }
    throw error;
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}
