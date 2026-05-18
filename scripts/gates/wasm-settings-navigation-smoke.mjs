import { chromium } from "playwright";

const baseUrl = normalizeBaseUrl(process.argv[2]);
const fatalConsolePattern =
  /ArgumentOutOfRange|NativeDispatcher unhandled exception|NavigationView\.GetItemFromIndex|System\.ArgumentOutOfRangeException/;

const browser = await chromium.launch({ headless: true });

try {
  const fatalConsoleMessages = [];
  const page = await browser.newPage({
    viewport: { width: 1280, height: 900 },
    deviceScaleFactor: 1
  });

  page.on("console", message => {
    const text = message.text();
    if (fatalConsolePattern.test(text)) {
      fatalConsoleMessages.push({ type: message.type(), text });
    }
  });

  page.on("pageerror", error => {
    const text = error.stack ?? error.message;
    if (fatalConsolePattern.test(text)) {
      fatalConsoleMessages.push({ type: "pageerror", text });
    }
  });

  await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
  await page.waitForSelector('[aria-label="StartView.Title"]', { timeout: 60_000 });

  await clickVisibleText(page, ["设置", "Settings"]);
  await page.waitForTimeout(3_000);

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(2_000);

  await clickTopNavigationOverflow(page);
  await page.waitForTimeout(2_000);

  const overflowText = await page.locator("body").innerText();
  if (!/数据与存储|快捷键|诊断与日志|Data|Shortcuts|Diagnostics/.test(overflowText)) {
    throw new Error("Settings overflow menu did not expose expected section labels.");
  }

  await clickVisibleText(page, ["诊断与日志", "Diagnostics"]);
  await page.waitForTimeout(3_000);

  const bodyText = await page.locator("body").innerText();
  if (!/Diagnostics and logs|诊断与日志|Live logs|日志/.test(bodyText)) {
    throw new Error("Diagnostics settings page did not render after overflow navigation.");
  }

  if (fatalConsoleMessages.length > 0) {
    throw new Error(`Fatal console errors detected: ${JSON.stringify(fatalConsoleMessages, null, 2)}`);
  }

  console.log("WASM settings navigation smoke passed");
} finally {
  await browser.close();
}

function normalizeBaseUrl(value) {
  if (!value || !value.trim()) {
    throw new Error("usage: wasm-settings-navigation-smoke.mjs <base-url>");
  }

  return value.endsWith("/") ? value : `${value}/`;
}

async function clickTopNavigationOverflow(page) {
  const point = await page.evaluate(() => {
    const candidates = Array.from(document.querySelectorAll("body *"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        return {
          element,
          rect,
          text: (element.textContent ?? "").trim()
        };
      })
      .filter(candidate =>
        candidate.text === "\uE10C"
        && candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.rect.left >= 0
        && candidate.rect.right <= innerWidth
        && candidate.rect.top >= 40
        && candidate.rect.top <= 140);

    const target = candidates[0]?.element;
    if (!target) {
      return null;
    }

    const clickable = target.closest(".uno-button") ?? target;
    const rect = clickable.getBoundingClientRect();
    return {
      x: rect.left + rect.width / 2,
      y: rect.top + rect.height / 2
    };
  });

  if (!point) {
    throw new Error("Settings overflow button was not visible at mobile viewport.");
  }

  await page.mouse.click(point.x, point.y);
}

async function clickVisibleText(page, labels) {
  const point = await page.evaluate(inputLabels => {
    const nodes = Array.from(document.querySelectorAll("body *"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        return {
          element,
          rect,
          text: (element.textContent ?? "").trim()
        };
      })
      .filter(candidate =>
        inputLabels.includes(candidate.text)
        && candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.rect.left >= -1
        && candidate.rect.top >= -1
        && candidate.rect.left <= innerWidth
        && candidate.rect.top <= innerHeight);

    const target = nodes[nodes.length - 1]?.element;
    if (!target) {
      return null;
    }

    const clickable = target.closest(".uno-navigationviewitem") ?? target;
    const rect = clickable.getBoundingClientRect();
    return {
      x: rect.left + rect.width / 2,
      y: rect.top + rect.height / 2
    };
  }, labels);

  if (!point) {
    throw new Error(`No visible navigation item found for labels: ${labels.join(", ")}`);
  }

  await page.mouse.click(point.x, point.y);
}
