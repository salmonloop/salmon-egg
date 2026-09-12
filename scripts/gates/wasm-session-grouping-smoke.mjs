import assert from "node:assert/strict";
import { chromium } from "playwright";
import { mkdir, writeFile } from "node:fs/promises";
import { normalizeBaseUrl, createInstrumentedContext, openApp, assertNoFatalConsoleMessages } from "./wasm-smoke-lib/browser-app.mjs";
import { navigateToSettingsSection } from "./wasm-smoke-lib/settings-shell.mjs";
import { expectComboBoxSelectionText, selectComboBoxItem, waitForPersistedLocalFileContains, collectVisibleInteractiveDebug } from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-session-grouping-smoke.mjs");
const browser = await chromium.launch({
    headless: true,
    executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined
});
const groupingId = "Appearance.SidebarConversationGrouping";
const appearance = { labels: ["Appearance", "外观"], automationIds: ["SettingsNav.Appearance"] };
const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);
try {
    await openApp(page, baseUrl);
    await navigateToSettingsSection(page, appearance, /Sidebar conversation grouping|侧栏会话分组/, "appearance settings");
    await expectComboBoxSelectionText(page, groupingId, ["By project", "按项目"], "default grouping");
    await selectComboBoxItem(page, groupingId, ["By status", "按状态"]);
    await waitForPersistedLocalFileContains(page, "/local/SalmonEgg/config/app.yaml", ["sidebar_conversation_grouping: Status"], "status grouping preference");
    await page.waitForFunction(() => {
        const labels = [...document.querySelectorAll("[aria-label]")].map(node => node.getAttribute("aria-label"));
        return ["Needs attention", "Working", "Other"].every(name => labels.some(label => label?.startsWith(name)));
    });
    await openApp(page, baseUrl);
    await navigateToSettingsSection(page, appearance, /Sidebar conversation grouping|侧栏会话分组/, "reloaded appearance settings");
    await expectComboBoxSelectionText(page, groupingId, ["By status", "按状态"], "persisted grouping");
    await selectComboBoxItem(page, groupingId, ["By project", "按项目"]);
    await waitForPersistedLocalFileContains(page, "/local/SalmonEgg/config/app.yaml", ["sidebar_conversation_grouping: Project"], "project grouping preference");
    assertNoFatalConsoleMessages(fatalConsoleMessages);
    assert.equal(await page.evaluate(() => !!document.getElementById("uno-semantics-root")), true);
    console.log("WASM session grouping passed: native setting, three groups, reload persistence and project mode restore.");
} catch (error) {
    const directory = process.env.WASM_SMOKE_ARTIFACTS_DIR;
    if (directory) {
        await mkdir(directory, { recursive: true });
        await page.screenshot({ path: `${directory}/session-grouping.png` });
        await writeFile(`${directory}/session-grouping.json`, JSON.stringify({
            error: String(error), semantic: await page.evaluate(collectVisibleInteractiveDebug), fatalConsoleMessages
        }, null, 2));
    }
    throw error;
} finally {
    await context.close();
    await browser.close();
}
