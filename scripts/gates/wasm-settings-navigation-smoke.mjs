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

  const settingsNavigationTarget = {
    labels: ["设置", "Settings"],
    automationIds: ["SettingsItem"]
  };

  await ensureVisibleNavigationTarget(page, settingsNavigationTarget, {
    labels: [],
    automationIds: ["TitleBar.ToggleSidebar"]
  });
  await clickVisibleNavigationTargetUntilBodyText(
    page,
    settingsNavigationTarget,
    /常规|General|外观|Appearance|ACP \/ Agent/,
    "settings shell");

  await page.setViewportSize({ width: 390, height: 844 });
  await waitForBodyText(page, /常规|General|外观|Appearance|ACP \/ Agent/, "settings shell at mobile viewport");

  await clickTopNavigationOverflow(page);
  await waitForBodyText(page, /数据与存储|快捷键|诊断与日志|Data|Shortcuts|Diagnostics/, "settings overflow menu");

  const overflowText = await page.locator("body").innerText();
  if (!/数据与存储|快捷键|诊断与日志|Data|Shortcuts|Diagnostics/.test(overflowText)) {
    throw new Error("Settings overflow menu did not expose expected section labels.");
  }

  await clickVisibleNavigationTargetUntilBodyText(
    page,
    {
      labels: ["诊断与日志", "Diagnostics"],
      automationIds: ["SettingsNav.Diagnostics"]
    },
    /Diagnostics and logs|诊断与日志|Live logs|日志/,
    "diagnostics settings page");

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
  await page.waitForFunction(findTopNavigationOverflowPoint, null, { timeout: 30_000 });
  const point = await page.evaluate(findTopNavigationOverflowPoint);

  if (!point) {
    const candidates = await page.evaluate(collectTopNavigationButtonCandidateDebug);
    throw new Error(`Settings overflow button was not visible at mobile viewport. Candidates: ${JSON.stringify(candidates)}`);
  }

  await page.mouse.click(point.x, point.y);
}

async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  await page.waitForFunction(
    source => new RegExp(source).test(document.body?.innerText ?? ""),
    pattern.source,
    { timeout: timeoutMs });

  const bodyText = await page.locator("body").innerText();
  if (!pattern.test(bodyText)) {
    throw new Error(`Expected ${label} text was not visible.`);
  }
}

async function clickVisibleNavigationTargetUntilBodyText(page, options, pattern, label) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    try {
      await clickVisibleNavigationTarget(page, options);
      await waitForBodyText(page, pattern, label, Math.min(1_500, Math.max(250, deadline - Date.now())));
      return;
    } catch (error) {
      lastError = error;
      await page.waitForTimeout(250);
    }
  }

  const bodyText = await page.locator("body").innerText().catch(() => "");
  throw new Error(
    `Expected ${label} text was not visible after clicking navigation target. `
    + `Last error: ${lastError?.message ?? lastError}. Body: ${bodyText.slice(0, 1_000)}`);
}

function findTopNavigationOverflowPoint() {
  function collectTopNavigationButtonCandidates() {
    return Array.from(document.querySelectorAll("body *"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        return {
          element,
          rect: {
            left: rect.left,
            top: rect.top,
            right: rect.right,
            bottom: rect.bottom,
            width: rect.width,
            height: rect.height
          },
          text: (element.textContent ?? "").trim(),
          aria: element.getAttribute("aria-label") ?? "",
          title: element.getAttribute("title") ?? "",
          role: element.getAttribute("role") ?? "",
          className: element.className?.toString?.() ?? ""
        };
      })
      .filter(candidate =>
        candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.rect.left >= 0
        && candidate.rect.top >= 16
        && candidate.rect.top <= 96
        && candidate.rect.right <= innerWidth + 1
        && candidate.rect.width <= 80
        && candidate.rect.height <= 80
        && (
          candidate.role === "button"
          || candidate.className.includes("uno-button")
          || candidate.text === "\uE10C"
          || candidate.text === "\uE712"
          || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.aria)
          || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.title)))
      .sort((left, right) => right.rect.right - left.rect.right);
  }

  const explicitTarget = collectTopNavigationButtonCandidates().find(candidate =>
    candidate.text === "\uE10C"
    || candidate.text === "\uE712"
    || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.aria)
    || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.title));
  const target = explicitTarget?.element;
  if (!target) {
    return null;
  }

  const clickable = target.closest(".uno-button") ?? target;
  const rect = clickable.getBoundingClientRect();
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function collectTopNavigationButtonCandidateDebug() {
  function collectTopNavigationButtonCandidates() {
    return Array.from(document.querySelectorAll("body *"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        return {
          element,
          rect: {
            left: rect.left,
            top: rect.top,
            right: rect.right,
            bottom: rect.bottom,
            width: rect.width,
            height: rect.height
          },
          text: (element.textContent ?? "").trim(),
          aria: element.getAttribute("aria-label") ?? "",
          title: element.getAttribute("title") ?? "",
          role: element.getAttribute("role") ?? "",
          className: element.className?.toString?.() ?? ""
        };
      })
      .filter(candidate =>
        candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.rect.left >= 0
        && candidate.rect.top >= 16
        && candidate.rect.top <= 96
        && candidate.rect.right <= innerWidth + 1
        && candidate.rect.width <= 80
        && candidate.rect.height <= 80
        && (
          candidate.role === "button"
          || candidate.className.includes("uno-button")
          || candidate.text === "\uE10C"
          || candidate.text === "\uE712"
          || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.aria)
          || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.title)))
      .sort((left, right) => right.rect.right - left.rect.right);
  }

  return collectTopNavigationButtonCandidates().map(candidate => ({
    text: candidate.text,
    aria: candidate.aria,
    title: candidate.title,
    role: candidate.role,
    className: candidate.className,
    rect: candidate.rect
  }));
}

async function clickVisibleNavigationTarget(page, options) {
  const point = await page.evaluate(findVisibleNavigationTargetPoint, options);

  if (!point) {
    const candidates = await page.evaluate(collectVisibleNavigationTargetDebug);
    const labels = options.labels ?? [];
    const automationIds = options.automationIds ?? [];
    throw new Error(
      `No visible navigation item found for labels: ${labels.join(", ")} automationIds: ${automationIds.join(", ")}. `
      + `Candidates: ${JSON.stringify(candidates)}`);
  }

  await page.mouse.click(point.x, point.y);
}

async function ensureVisibleNavigationTarget(page, targetOptions, openerOptions) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, targetOptions)) {
    return;
  }

  await clickVisibleNavigationTarget(page, openerOptions);
  await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 30_000 });
}

function findVisibleNavigationTargetPoint(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const nodes = Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const text = (element.textContent ?? "").trim();
      const aria = element.getAttribute("aria-label") ?? "";
      const automationId =
        element.getAttribute("data-automation-id")
        ?? element.getAttribute("data-automationid")
        ?? element.getAttribute("automationid")
        ?? "";

      return {
        element,
        rect,
        text,
        aria,
        automationId,
        display: style.display,
        visibility: style.visibility,
        automationMatch: automationIds.includes(aria) || automationIds.includes(automationId),
        textMatch: labels.includes(text) || labels.includes(aria)
      };
    })
    .filter(candidate =>
      (candidate.automationMatch || candidate.textMatch)
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight);

  nodes.sort((left, right) => {
    if (left.automationMatch !== right.automationMatch) {
      return left.automationMatch ? -1 : 1;
    }

    return (left.rect.width * left.rect.height) - (right.rect.width * right.rect.height);
  });

  const target = nodes[0]?.element;
  if (!target) {
    return null;
  }

  const clickable =
    target.closest(".uno-navigationviewitem")
    ?? target.closest("[role='button']")
    ?? target.closest("button")
    ?? target;
  const clickableRect = clickable.getBoundingClientRect();
  const rect = clickableRect.width > 0 && clickableRect.height > 0
    ? clickableRect
    : target.getBoundingClientRect();

  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function collectVisibleNavigationTargetDebug() {
  return Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      return {
        text: (element.textContent ?? "").trim().slice(0, 120),
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        role: element.getAttribute("role") ?? "",
        className: element.className?.toString?.() ?? "",
        rect: {
          left: Math.round(rect.left),
          top: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height)
        }
      };
    })
    .filter(candidate =>
      candidate.rect.width > 0
      && candidate.rect.height > 0
      && (candidate.text || candidate.aria || candidate.automationId || candidate.role))
    .slice(0, 80);
}
