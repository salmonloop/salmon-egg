// Run against this build's bundled module; this gate checks browser facts, not the .NET read receipt.
// Linux requires an isolated DISPLAY with a window manager, xdotool and xprop. No synthetic DOM
// visibility properties are used; a browser that cannot expose native hidden state fails the gate.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import http from "node:http";
import { execFileSync } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { chromium } from "playwright";

const modulePath = process.argv[2];
assert.ok(modulePath, "Expected this build's salmon-egg-wasm-shell.js path");
const source = readFileSync(modulePath, "utf8");
console.log("Built document activity module:", {
    path: modulePath,
    sha256: createHash("sha256").update(source).digest("hex")
});
const server = http.createServer((request, response) => {
    response.writeHead(200, { "Content-Type": request.url === "/module.js" ? "text/javascript" : "text/html" });
    response.end(request.url === "/module.js" ? source : "<!doctype html><title>Document activity gate</title><button>Focus</button>");
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
let page;
const nativePages = [];
try {
    browser = await chromium.launch({
        headless: false,
        executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined
    });
    // Playwright enables focus emulation on its page sessions. A second CDP session cannot undo
    // that session's override. Keep these native targets outside Playwright-managed contexts;
    // the public browser CDP API still owns their lifetime without changing browser/DOM facts.
    const browserCdp = await browser.newBrowserCDPSession();
    const { browserContextId } = await browserCdp.send("Target.createBrowserContext");
    const origin = `http://127.0.0.1:${server.address().port}`;
    page = await createNativePage(browserCdp, browserContextId, origin);
    nativePages.push(page);
    await page.evaluate(async () => {
        window.activityModule = await import("/module.js");
        window.activityEvents = [];
        window.activitySubscription = window.activityModule.observeDocumentActivity(active => window.activityEvents.push(active));
    });
    await browserCdp.send("Target.activateTarget", { targetId: page.targetId });
    await page.waitForFunction(() => window.activityEvents.at(-1) === true);
    const firstWindow = await browserCdp.send("Browser.getWindowForTarget", { targetId: page.targetId });
    const other = await createNativePage(browserCdp, browserContextId, origin);
    nativePages.push(other);
    const otherWindow = await browserCdp.send("Browser.getWindowForTarget", { targetId: other.targetId });
    console.log("Browser window identities:", { first: firstWindow.windowId, other: otherWindow.windowId });
    const windows = execFileSync("xdotool", ["search", "--sync", "--onlyvisible", "--name", "Document activity gate"],
        { timeout: 5000, encoding: "utf8" }).trim().split(/\s+/);
    assert.equal(windows.length, 1, "The isolated display must expose exactly one fixture browser window.");
    const windowId = windows[0];
    const switchTab = async target => {
        execFileSync("xdotool", ["windowactivate", "--sync", windowId], { timeout: 5000 });
        execFileSync("xdotool", ["key", "--clearmodifiers", target === page ? "ctrl+1" : "ctrl+2"], { timeout: 5000 });
    };
    assert.equal(otherWindow.windowId, firstWindow.windowId, "The fixture must create two tabs in one native browser window.");
    await switchTab(other);
    await page.waitForFunction(() => document.visibilityState === "hidden" && !document.hasFocus() && window.activityEvents.at(-1) === false);
    await switchTab(page);
    await page.waitForFunction(() => document.visibilityState === "visible" && document.hasFocus() && window.activityEvents.at(-1) === true);
    assert.deepEqual(await page.evaluate(() => window.activityEvents.slice(-3)), [true, false, true]);
    console.log("Native focus gate passed: active, inactive, active.");

    execFileSync("xdotool", ["key", "--clearmodifiers", "ctrl+l"], { timeout: 5000 });
    await page.waitForFunction(() => document.visibilityState === "visible" && !document.hasFocus() && window.activityEvents.at(-1) === false);
    execFileSync("xdotool", ["key", "--clearmodifiers", "Escape"], { timeout: 5000 });
    await page.waitForFunction(() => document.visibilityState === "visible" && document.hasFocus() && window.activityEvents.at(-1) === true);
    console.log("Native visible-page blur/focus gate passed.");

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
    await browserCdp.send("Browser.setWindowBounds", { windowId: firstWindow.windowId, bounds: { windowState: "normal" } });
    await browserCdp.send("Target.activateTarget", { targetId: page.targetId });
    await page.waitForFunction(() => document.visibilityState === "visible" && document.hasFocus() && window.activityEvents.at(-1) === true);
    await page.evaluate(() => window.activityModule.stopObservingDocumentActivity(window.activitySubscription));
    await browserCdp.send("Target.disposeBrowserContext", { browserContextId });
    console.log("WASM document activity module passed: native hide/show, focus and idempotent listener release.");
} catch (error) {
    if (page) {
        console.error("Native document activity at failure:", await page.evaluate(() => ({
            visibility: document.visibilityState,
            focused: document.hasFocus(),
            events: window.activityEvents
        })).catch(() => "page unavailable"));
    }
    throw error;
} finally {
    for (const nativePage of nativePages) nativePage.dispose();
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
}

async function createNativePage(cdp, browserContextId, url) {
    const { targetId } = await cdp.send("Target.createTarget", { url, browserContextId });
    const { sessionId } = await cdp.send("Target.attachToTarget", { targetId, flatten: false });
    let nextId = 0;
    const pending = new Map();
    const receive = event => {
        if (event.sessionId !== sessionId) return;
        const message = JSON.parse(event.message);
        const request = pending.get(message.id);
        if (!request) return;
        pending.delete(message.id);
        clearTimeout(request.timeout);
        if (message.error) request.reject(new Error(JSON.stringify(message.error)));
        else request.resolve(message.result);
    };
    cdp.on("Target.receivedMessageFromTarget", receive);
    const evaluate = callback => new Promise((resolve, reject) => {
        const id = ++nextId;
        const timeout = setTimeout(() => {
            pending.delete(id);
            reject(new Error("Native Runtime.evaluate timed out"));
        }, 10000);
        pending.set(id, { resolve, reject, timeout });
        cdp.send("Target.sendMessageToTarget", {
            sessionId,
            message: JSON.stringify({ id, method: "Runtime.evaluate", params: {
                expression: `(${callback.toString()})()`, awaitPromise: true, returnByValue: true
            } })
        }).catch(error => {
            pending.delete(id);
            clearTimeout(timeout);
            reject(error);
        });
    }).then(response => {
        assert.ok(!response.exceptionDetails, JSON.stringify(response.exceptionDetails));
        return response.result.value;
    });
    const nativePage = {
        targetId,
        evaluate,
        async waitForFunction(predicate) {
            const deadline = Date.now() + 10000;
            while (!await evaluate(predicate)) {
                assert.ok(Date.now() < deadline, `Native browser condition timed out: ${predicate}`);
                await delay(50);
            }
        },
        dispose() {
            cdp.off("Target.receivedMessageFromTarget", receive);
            for (const request of pending.values()) {
                clearTimeout(request.timeout);
                request.reject(new Error("Native target disposed"));
            }
            pending.clear();
        }
    };
    try {
        await nativePage.waitForFunction(() => document.readyState === "complete" && document.title === "Document activity gate");
    } catch (error) {
        nativePage.dispose();
        throw error;
    }
    return nativePage;
}
