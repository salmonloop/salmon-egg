// Exercise the bundled external-navigation module with real user activation and popup isolation.
// Supply the module from this build's WASM output, never the source tree.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import http from "node:http";
import { chromium } from "playwright";

const modulePath = process.argv[2];
assert.ok(modulePath, "Expected path to built salmon-egg-wasm-shell.js");
const source = readFileSync(modulePath, "utf8");
let visits = 0;
let receivedReferrer;
const targetServer = http.createServer((request, response) => {
  if (request.url === "/redirect-to-app") {
    response.writeHead(302, { Location: `${appOrigin}/private-return` });
    response.end();
    return;
  }
  if (request.url.startsWith("/authorize")) {
    visits++;
    receivedReferrer = request.headers.referer;
  }
  response.writeHead(200, { "Content-Type": "text/html" });
  response.end("<!doctype html><title>Private external step</title><input id='secret' value='private-page-canary'>");
});
const appServer = http.createServer((request, response) => {
  if (request.url === "/module.js") {
    response.writeHead(200, { "Content-Type": "text/javascript", "Access-Control-Allow-Origin": "*" });
    response.end(source);
    return;
  }
  if (request.url === "/private-return") {
    response.writeHead(200, { "Content-Type": "text/html" });
    response.end("<!doctype html><script>window.name='private-return-context'</script><input id='secret' value='redirect-private-canary'>");
    return;
  }
  if (request.url === "/sandbox-container") {
    response.writeHead(200, { "Content-Type": "text/html" });
    response.end("<!doctype html><iframe id='blocker' sandbox='allow-scripts' src='/blocked-wrapper'></iframe>");
    return;
  }
  response.writeHead(200, { "Content-Type": "text/html" });
  response.end(`<!doctype html><button id='consent'>Open in browser</button><script type='module'>
    import * as launcher from '/module.js';
    window.launcher = launcher;
    window.targetUrl = ${JSON.stringify(`${targetOrigin}/authorize?canary=private-url`)};
    window.automaticOpenResult = launcher.openExternalElicitation(window.targetUrl);
    document.querySelector('#consent').onclick = () => window.openResult = launcher.openExternalElicitation(window.targetUrl);
    </script>`);
});
await new Promise(resolve => targetServer.listen(0, "127.0.0.1", resolve));
await new Promise(resolve => appServer.listen(0, "127.0.0.1", resolve));
const targetOrigin = `http://127.0.0.1:${targetServer.address().port}`;
const appOrigin = `http://127.0.0.1:${appServer.address().port}`;
let browser;
try {
  browser = await chromium.launch({
    headless: true,
    ignoreDefaultArgs: ["--disable-popup-blocking"],
    executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined
  });
  const context = await browser.newContext();
  const page = await context.newPage();
  // CDP Runtime.evaluate can grant user activation, so exercise the automatic call from page load.
  await page.goto(appOrigin, { waitUntil: "networkidle" });
  assert.equal(visits, 0, "Loading and rendering must not prefetch a URL.");
  assert.equal(await page.evaluate(() => window.automaticOpenResult), false,
    "A script without a real click must not navigate.");
  assert.equal(visits, 0);
  const popupPromise = context.waitForEvent("page");
  await page.locator("#consent").click();
  const popup = await popupPromise;
  await popup.waitForURL(`${targetOrigin}/authorize?canary=private-url`);
  assert.equal(await page.evaluate(() => window.openResult), true);
  assert.equal(await popup.evaluate(() => window.opener), null, "External page must not retain an opener.");
  assert.equal(await popup.evaluate(() => document.referrer), "", "External page must receive no referrer.");
  assert.equal(receivedReferrer, undefined);
  assert.equal(await page.evaluate(() => document.querySelector("#secret")), null);
  assert.equal(visits, 1);
  await popup.close();

  // A redirect back to the application origin must not restore access by a named window.
  await page.evaluate(url => { window.targetUrl = url; }, `${targetOrigin}/redirect-to-app`);
  const redirectedPromise = context.waitForEvent("page");
  await page.locator("#consent").click();
  const redirected = await redirectedPromise;
  await redirected.waitForURL(`${appOrigin}/private-return`);
  assert.equal(await redirected.evaluate(() => window.opener), null);
  assert.equal(await page.evaluate(() => {
    const candidate = window.open("", "private-return-context");
    try {
      return candidate?.document.querySelector("#secret")?.value ?? null;
    } finally {
      candidate?.close();
    }
  }), null, "Returning to the app origin must not expose the user's external page input.");
  await redirected.close();

  // Same-origin pages must not become model-accessible application contexts.
  await page.evaluate(url => { window.targetUrl = url; }, `${appOrigin}/authorize`);
  await page.locator("#consent").click();
  assert.equal(await page.evaluate(() => window.openResult), false);
  assert.equal(context.pages().length, 1);

  // Native sandbox policy blocks popups even with trusted user activation. noopener deliberately
  // hides the resulting WindowProxy in both cases, so the contract can only promise dispatch.
  await page.goto(`${appOrigin}/sandbox-container`, { waitUntil: "networkidle" });
  const sandbox = page.frameLocator("#blocker");
  await sandbox.locator("#consent").click();
  const blockedFrame = page.frames().find(frame => frame.url().endsWith("/blocked-wrapper"));
  assert.ok(blockedFrame);
  assert.equal(await blockedFrame.evaluate(() => window.openResult), true,
    "A successful dispatch must not pretend to report whether a popup opened.");
  assert.equal(context.pages().length, 1, "The native sandbox must actually block the popup.");
  assert.equal(visits, 1);
  await context.close();
  console.log("WASM external URL module passed: trusted consent, no automatic open, no opener/referrer, same-origin rejection and redirect isolation, native blocked popup reports dispatch only.");
} finally {
  await browser?.close();
  await Promise.all([
    new Promise(resolve => appServer.close(resolve)),
    new Promise(resolve => targetServer.close(resolve))
  ]);
}
