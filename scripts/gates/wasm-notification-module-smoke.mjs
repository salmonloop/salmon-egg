// Verifies the browser Notification bridge the WASM notification service depends on.
//
// The module is pure browser interaction, so a managed unit test would have to stub the very API
// under test. This loads the module built into the current WASM output over http, in a real browser,
// with notification permission genuinely granted or denied per case, and asserts the branches the
// managed service switches on: the permission strings, the "unsupported" sentinel when the API is
// absent, per-turn tags as the replace key, and a throwing constructor surfacing as false.
//
// Usage: node wasm-notification-module-smoke.mjs <path-to-salmon-egg-wasm-notifications.js>
import { chromium } from 'playwright';
import { readFileSync } from 'node:fs';
import http from 'node:http';

const MODULE_PATH = process.argv[2];
const moduleSource = readFileSync(MODULE_PATH, 'utf8');

// Serve the module over http so it is a real ES module import, as in the app.
const server = http.createServer((req, res) => {
  if (req.url === '/mod.js') {
    res.writeHead(200, { 'Content-Type': 'text/javascript' });
    res.end(moduleSource);
    return;
  }
  res.writeHead(200, { 'Content-Type': 'text/html' });
  res.end('<!doctype html><html><body>notification module host</body></html>');
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

const failures = [];
function check(label, actual, expected) {
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`${ok ? 'PASS' : 'FAIL'} ${label}: ${JSON.stringify(actual)}${ok ? '' : ` (expected ${JSON.stringify(expected)})`}`);
  if (!ok) failures.push(label);
}

async function withPage(permissions, fn) {
  // The headless *shell* build always reports notification permission as denied regardless of the
  // grant, so the granted path needs the full headless Chromium.
  const browser = await chromium.launch({ channel: 'chromium' });
  const context = await browser.newContext({ permissions });
  await context.grantPermissions(permissions, { origin });
  const page = await context.newPage();
  await page.goto(origin);
  // Record constructed notifications so the tag (replace key) is observable.
  await page.addInitScript(() => {});
  try {
    return await fn(page);
  } finally {
    await browser.close();
  }
}

// Case 1: permission granted. getPermission must report granted and showNotification must succeed
// and pass the notification id through as the browser tag.
const granted = await withPage(['notifications'], async page => {
  return page.evaluate(async origin => {
    const mod = await import(`${origin}/mod.js`);
    const constructed = [];
    const RealNotification = window.Notification;
    // Wrap rather than replace so `typeof Notification === "function"` and .permission stay real.
    class Recording extends RealNotification {
      constructor(title, options) {
        super(title, options);
        constructed.push({ title, body: options?.body, tag: options?.tag });
      }
      static get permission() { return RealNotification.permission; }
      static requestPermission(...a) { return RealNotification.requestPermission(...a); }
    }
    window.Notification = Recording;
    const permission = mod.getPermission();
    const first = mod.showNotification('turn:conv-1:turn-1', 'Task completed', 'First turn.');
    const repeat = mod.showNotification('turn:conv-1:turn-1', 'Task completed', 'First turn.');
    const second = mod.showNotification('turn:conv-1:turn-2', 'Task completed', 'Second turn.');
    const requested = await mod.requestPermission();
    window.Notification = RealNotification;
    return { permission, first, repeat, second, requested, constructed };
  }, origin);
});
check('granted: getPermission', granted.permission, 'granted');
check('granted: showNotification first turn', granted.first, true);
check('granted: showNotification same turn again', granted.repeat, true);
check('granted: showNotification second turn', granted.second, true);
check('granted: requestPermission after a decision', granted.requested, 'granted');
check('granted: notification tags are the per-turn ids',
  granted.constructed.map(n => n.tag),
  ['turn:conv-1:turn-1', 'turn:conv-1:turn-1', 'turn:conv-1:turn-2']);
check('granted: bodies pass through',
  granted.constructed.map(n => n.body),
  ['First turn.', 'First turn.', 'Second turn.']);

// Case 2: permission denied. showNotification must refuse rather than throw.
const denied = await withPage([], async page => {
  return page.evaluate(async origin => {
    const mod = await import(`${origin}/mod.js`);
    return {
      permission: mod.getPermission(),
      shown: mod.showNotification('turn:conv-1:turn-1', 'Task completed', 'Body.'),
    };
  }, origin);
});
check('denied: getPermission is not granted', denied.permission !== 'granted', true);
check('denied: showNotification refuses', denied.shown, false);

// Case 3: the Notification API is absent entirely (older/embedded browsers).
const unsupported = await withPage([], async page => {
  return page.evaluate(async origin => {
    const mod = await import(`${origin}/mod.js`);
    const real = window.Notification;
    delete window.Notification;
    try {
      return {
        permission: mod.getPermission(),
        requested: await mod.requestPermission(),
        shown: mod.showNotification('turn:conv-1:turn-1', 'Task completed', 'Body.'),
      };
    } finally {
      window.Notification = real;
    }
  }, origin);
});
check('unsupported: getPermission reports the sentinel', unsupported.permission, 'unsupported');
check('unsupported: requestPermission reports the sentinel', unsupported.requested, 'unsupported');
check('unsupported: showNotification refuses', unsupported.shown, false);

// Case 4: a constructor that throws (Chrome on Android requires a service worker) must be reported
// as a failure rather than escaping as an exception.
const throwing = await withPage(['notifications'], async page => {
  return page.evaluate(async origin => {
    const mod = await import(`${origin}/mod.js`);
    const real = window.Notification;
    function Exploding() { throw new TypeError('Illegal constructor.'); }
    Object.defineProperty(Exploding, 'permission', { get: () => 'granted' });
    window.Notification = Exploding;
    try {
      return { shown: mod.showNotification('turn:conv-1:turn-1', 'Task completed', 'Body.') };
    } finally {
      window.Notification = real;
    }
  }, origin);
});
check('throwing constructor: reported as failure, not an exception', throwing.shown, false);

server.close();
if (failures.length > 0) {
  console.error(`\n${failures.length} check(s) failed: ${failures.join(', ')}`);
  process.exit(1);
}
console.log('\nAll browser notification module checks passed.');
