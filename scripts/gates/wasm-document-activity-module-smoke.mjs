// Run against this build's bundled module; this gate checks browser facts, not the .NET read receipt.
// Linux requires an isolated DISPLAY with a window manager, xdotool and xprop. No synthetic DOM
// visibility properties are used; a browser that cannot expose native hidden state fails the gate.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import http from "node:http";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";

const modulePath = process.argv[2];
assert.ok(modulePath, "Expected this build's salmon-egg-wasm-shell.js path");
const source = readFileSync(modulePath, "utf8");
const server = http.createServer((request, response) => {
    response.writeHead(200, { "Content-Type": request.url === "/module.js" ? "text/javascript" : "text/html" });
    response.end(request.url === "/module.js" ? source : "<!doctype html><title>Document activity gate</title><button>Focus</button>");
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
let page;
try {
    browser = await chromium.launch({
        headless: false,
        executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined
    });
    const context = await browser.newContext();
    const origin = `http://127.0.0.1:${server.address().port}`;
    page = await context.newPage();
    page.setDefaultTimeout(10000);
    await page.goto(origin);
    await page.evaluate(async () => {
        window.activityModule = await import("/module.js");
        window.activityEvents = [];
        window.activitySubscription = window.activityModule.observeDocumentActivity(active => window.activityEvents.push(active));
    });
    await page.bringToFront();
    await page.waitForFunction(() => window.activityEvents.at(-1) === true);
    const firstTarget = await context.newCDPSession(page);
    // Playwright enables Chromium focus emulation for every page. Disable that testing override
    // so visibility/hasFocus below come from the native tabs, not the automation environment.
    await firstTarget.send("Emulation.setFocusEmulationEnabled", { enabled: false });
    const firstWindow = await firstTarget.send("Browser.getWindowForTarget");
    const other = await context.newPage();
    await other.goto(origin);
    const otherTarget = await context.newCDPSession(other);
    await otherTarget.send("Emulation.setFocusEmulationEnabled", { enabled: false });
    const otherWindow = await otherTarget.send("Browser.getWindowForTarget");
    console.log("Browser window identities:", { first: firstWindow.windowId, other: otherWindow.windowId });
    const windows = execFileSync("xdotool", ["search", "--onlyvisible", "--name", "Document activity gate"],
        { timeout: 5000, encoding: "utf8" }).trim().split(/\s+/);
    assert.equal(windows.length, 1, "The isolated display must expose exactly one fixture browser window.");
    const windowId = windows[0];
    const switchTab = async target => {
        execFileSync("xdotool", ["windowactivate", "--sync", windowId], { timeout: 5000 });
        execFileSync("xdotool", ["key", "--clearmodifiers", target === page ? "ctrl+1" : "ctrl+2"], { timeout: 5000 });
    };
    assert.equal(otherWindow.windowId, firstWindow.windowId, "The fixture must create two tabs in one native browser window.");
    await switchTab(other);
    await page.waitForFunction(() => !document.hasFocus() && window.activityEvents.at(-1) === false);
    await switchTab(page);
    await page.waitForFunction(() => document.hasFocus() && window.activityEvents.at(-1) === true);
    assert.deepEqual(await page.evaluate(() => window.activityEvents.slice(-3)), [true, false, true]);
    console.log("Native focus gate passed: active, inactive, active.");

    const beforeDetach = await page.evaluate(() => {
        window.activityModule.stopObservingDocumentActivity(window.activitySubscription);
        window.activityModule.stopObservingDocumentActivity(window.activitySubscription);
        return window.activityEvents.length;
    });
    await switchTab(other);
    await page.waitForFunction(() => !document.hasFocus());
    await switchTab(page);
    await page.waitForFunction(() => document.hasFocus());
    assert.equal(await page.evaluate(() => window.activityEvents.length), beforeDetach);
    console.log("Native listener release gate passed.");
    await page.evaluate(() => {
        window.activityEvents = [];
        window.activitySubscription = window.activityModule.observeDocumentActivity(active => window.activityEvents.push(active));
    });

    execFileSync("xdotool", ["windowminimize", "--sync", windowId], { timeout: 5000 });
    console.log("Minimized native window:", execFileSync("xprop", ["-id", windowId, "_NET_WM_STATE"],
        { timeout: 5000, encoding: "utf8" }).trim());
    await page.waitForFunction(() => document.visibilityState === "hidden" && window.activityEvents.at(-1) === false);
    execFileSync("xdotool", ["windowmap", "--sync", windowId, "windowactivate", "--sync"], { timeout: 5000 });
    await page.waitForFunction(() => document.visibilityState === "visible" && window.activityEvents.at(-1) === true);
    await page.evaluate(() => window.activityModule.stopObservingDocumentActivity(window.activitySubscription));
    await context.close();
    console.log("WASM document activity module passed: native hide/show, focus and idempotent listener release.");
} catch (error) {
    if (page && !page.isClosed()) {
        console.error("Native document activity at failure:", await page.evaluate(() => ({
            visibility: document.visibilityState,
            focused: document.hasFocus(),
            events: window.activityEvents
        })).catch(() => "page unavailable"));
    }
    throw error;
} finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
}
