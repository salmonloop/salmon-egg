export const fatalConsolePattern =
  /ArgumentOutOfRange|NativeDispatcher unhandled exception|NavigationView\.GetItemFromIndex|System\.ArgumentOutOfRangeException|Unhandled exception/i;

// Console output is the only witness when the app never reaches first paint. Keyed off the page so
// helpers can read it without threading an extra parameter through every smoke script.
const pageDiagnostics = new WeakMap();
const recentConsoleLimit = 60;

export const domHelperScript = `
(() => {
  window.__salmoneggSmoke = {
    resolveToggleClickPoint(toggleElement) {
      const interactiveDescendants = [toggleElement, ...toggleElement.querySelectorAll("*")]
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          return {
            rect,
            pointerEvents: style.pointerEvents,
            element
          };
        })
        .filter(candidate =>
          candidate.pointerEvents !== "none"
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.rect.left >= 0
          && candidate.rect.top >= 0
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight)
        .sort((left, right) => {
          const leftArea = left.rect.width * left.rect.height;
          const rightArea = right.rect.width * right.rect.height;
          return rightArea - leftArea;
        });

      const target = interactiveDescendants[0];
      if (!target) {
        const rect = toggleElement.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0
          ? { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
          : null;
      }

      return {
        x: target.rect.left + target.rect.width / 2,
        y: target.rect.top + target.rect.height / 2
      };
    },

    findVisibleControl(input, labels, automationIds) {
      const normalize = value => (value ?? "").trim().toLowerCase();
      const normalizedLabels = labels.map(normalize).filter(Boolean);
      const normalizedAutomationIds = automationIds.map(normalize).filter(Boolean);
      const nodes = Array.from(document.querySelectorAll("body *"))
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          const text = (element.textContent ?? "").trim();
          const aria = element.getAttribute("aria-label") ?? "";
          const normalizedText = normalize(text);
          const normalizedAria = normalize(aria);
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
            interactive:
              element.matches("button,input,textarea,[role='button'],[role='switch'],[role='combobox']")
              || element.className?.toString?.().toLowerCase().includes("button")
              || element.className?.toString?.().toLowerCase().includes("toggleswitch")
              || element.className?.toString?.().toLowerCase().includes("combobox"),
            automationMatch:
              normalizedAutomationIds.includes(normalizedAria)
              || normalizedAutomationIds.includes(normalize(automationId)),
            exactTextMatch:
              normalizedLabels.includes(normalizedText)
              || normalizedLabels.includes(normalizedAria),
            textMatch:
              normalizedLabels.includes(normalizedText)
              || (normalizedText.length <= 160
                && (
                  element.matches("button,input,textarea,[role='button'],[role='switch'],[role='combobox']")
                  || element.className?.toString?.().toLowerCase().includes("button")
                  || element.className?.toString?.().toLowerCase().includes("toggleswitch")
                  || element.className?.toString?.().toLowerCase().includes("combobox"))
                && normalizedLabels.some(label => normalizedText.includes(label)))
              || normalizedLabels.some(label => normalizedAria.includes(label))
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

        if (left.interactive !== right.interactive) {
          return left.interactive ? -1 : 1;
        }

        if (left.exactTextMatch !== right.exactTextMatch) {
          return left.exactTextMatch ? -1 : 1;
        }

        return (left.rect.width * left.rect.height) - (right.rect.width * right.rect.height);
      });

      return nodes[0]?.element ?? null;
    },

    collectTopNavigationButtonCandidates() {
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
            || candidate.text === "\\uE10C"
            || candidate.text === "\\uE712"
            || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.aria)
            || /more|overflow|ellipsis|更多|溢出|展开/i.test(candidate.title)))
        .sort((left, right) => right.rect.right - left.rect.right);
    },

    collectVisibleComboBoxItems() {
      const seenClickableItems = new Set();
      return Array.from(document.querySelectorAll("body *"))
        .map(element => {
          const clickable = element.closest(".uno-comboboxitem,[role='option']") ?? element;
          const rect = element.getBoundingClientRect();
          const clickRect = clickable.getBoundingClientRect();
          const style = getComputedStyle(element);
          const className = element.className?.toString?.() ?? "";
          return {
            element,
            clickable,
            text: (element.textContent ?? "").trim(),
            rect,
            clickRect,
            className,
            role: element.getAttribute("role") ?? "",
            display: style.display,
            visibility: style.visibility
          };
        })
        .filter(candidate =>
          (candidate.role === "option" || candidate.className.toLowerCase().includes("comboboxitem"))
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.display !== "none"
          && candidate.visibility !== "hidden"
          && candidate.rect.left >= -1
          && candidate.rect.top >= -1
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight
          && !seenClickableItems.has(candidate.clickable)
          && (seenClickableItems.add(candidate.clickable) || true))
        .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));
    },

    collectVisibleComboBoxControls() {
      return Array.from(document.querySelectorAll("body *"))
        .map(element => {
          const rect = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          const className = element.className?.toString?.() ?? "";
          return {
            element,
            rect,
            className,
            role: element.getAttribute("role") ?? "",
            display: style.display,
            visibility: style.visibility
          };
        })
        .filter(candidate =>
          (candidate.role === "combobox" || candidate.className.toLowerCase().includes("combobox"))
          && candidate.role !== "option"
          && !candidate.className.toLowerCase().includes("comboboxitem")
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.display !== "none"
          && candidate.visibility !== "hidden"
          && candidate.rect.left >= -1
          && candidate.rect.top >= -1
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight)
        .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));
    }
  };
})();
`;

export function normalizeBaseUrl(value, usageName = "wasm smoke") {
  if (!value || !value.trim()) {
    throw new Error(`usage: ${usageName} <base-url>`);
  }

  return value.endsWith("/") ? value : `${value}/`;
}

export async function openApp(page, baseUrl) {
  await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
  await enableSemanticDom(page);
  // StartView.Title is a gradient TextBlock; on BrowserWasm it may not project
  // AutomationId into aria-label. Prefer any stable Start shell marker instead.
  // Cold Mono/Uno first paint on aarch64 Debug can exceed 60s after framework download.
  try {
    await page.waitForSelector(
      [
        '[aria-label="StartView.Title"]',
        '[aria-label="StartView.PromptBox"]',
        '[aria-label="StartView.Suggestion.ReportGuidance"]',
        '[aria-label="StartView.AgentSelector"]',
        '[aria-label="MainNavView"]'
      ].join(", "),
      { timeout: 180_000 });
  } catch (error) {
    throw new Error(`${error.message}\n${await describeUnrenderedPage(page)}`, { cause: error });
  }
}

// Skia paints into a <canvas>, so the accessibility tree is the only DOM Uno mirrors - and it builds
// that lazily. Until a screen reader is detected, or the "Enable accessibility" affordance is activated,
// the body holds only that button plus an "Application content" placeholder and no
// AutomationProperties.Name reaches the DOM at all, which is why every [aria-label] locator waits
// forever. Playwright is not a screen reader, so ask for the tree explicitly.
//
// The affordance carries role="button" and tabindex="0" but renders empty, so it has no hit box:
// dispatch the activation on the element instead of letting Playwright aim a real pointer at it.
// A missing affordance is not a failure - the DOM-rendered heads never had one.
async function enableSemanticDom(page) {
  const toggle = page.locator("#uno-enable-accessibility");
  try {
    await toggle.waitFor({ state: "attached", timeout: 30_000 });
  } catch {
    return;
  }

  await toggle.evaluate(element => {
    element.focus();
    element.click();
    element.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
  });
}

// A bare selector timeout says only that first paint never happened. Everything that explains why - a
// runtime abort, a framework asset that 404s, a JS module that failed to load - has already gone by as
// console output, and assertNoFatalConsoleMessages never runs because openApp threw first. Dump the raw
// tail here so a boot regression is diagnosable from the gate log alone: the WASM head cannot be built
// on every contributor machine, so "reproduce it locally" is not available as a fallback.
async function describeUnrenderedPage(page) {
  const captured = pageDiagnostics.get(page)
    ?? { fatalConsoleMessages: [], recentConsoleMessages: [] };
  const state = await page
    .evaluate(() => ({
      url: location.href,
      title: document.title,
      readyState: document.readyState,
      bodyText: (document.body?.innerText ?? "").slice(0, 400),
      ariaLabels: Array.from(document.querySelectorAll("[aria-label]"))
        .slice(0, 25)
        .map(element => element.getAttribute("aria-label")),
      // Uno's WASM semantic DOM decides per role whether a name becomes aria-label,
      // aria-labelledby or text content, so an empty aria-label set does not mean the
      // element is missing. Report what identifying attributes the page actually carries.
      identifyingAttributes: Array.from(
        new Set(Array.from(document.querySelectorAll("*"))
          .flatMap(element => Array.from(element.attributes).map(attribute => attribute.name))
          .filter(name => name.startsWith("aria-")
            || name.startsWith("data-")
            || name === "role"
            || name === "id"
            || name === "title")))
        .sort(),
      semanticDomSample: (document.body?.outerHTML ?? "").slice(0, 3000)
    }))
    .catch(error => ({ evaluateFailed: error.message }));

  return [
    "--- page state ---",
    JSON.stringify(state, null, 2),
    `--- fatal console (${captured.fatalConsoleMessages.length}) ---`,
    JSON.stringify(captured.fatalConsoleMessages, null, 2),
    `--- console/pageerror tail (${captured.recentConsoleMessages.length}) ---`,
    JSON.stringify(captured.recentConsoleMessages, null, 2)
  ].join("\n");
}

export async function clearBrowserOriginStorage(browser, targetUrl) {
  const context = await browser.newContext();
  const page = await context.newPage();
  const origin = new URL(targetUrl).origin;

  await page.goto(targetUrl, { waitUntil: "domcontentloaded", timeout: 60_000 });
  const cdp = await context.newCDPSession(page);
  await cdp.send("Storage.clearDataForOrigin", {
    origin,
    storageTypes: "indexeddb,local_storage,cache_storage,websql,service_workers"
  });
  await page.evaluate(async () => {
    if (!indexedDB.databases) {
      return;
    }

    const databases = await indexedDB.databases();
    await Promise.all(databases
      .map(database => database.name)
      .filter(Boolean)
      .map(name => new Promise((resolve, reject) => {
        const request = indexedDB.deleteDatabase(name);
        request.onsuccess = () => resolve();
        request.onblocked = () => resolve();
        request.onerror = () => reject(request.error);
      })));
  });

  await context.close();
}

export async function createInstrumentedContext(browser, options = {}) {
  const fatalConsoleMessages = [];
  const context = await browser.newContext({
    viewport: options.viewport ?? { width: 1280, height: 900 },
    deviceScaleFactor: options.deviceScaleFactor ?? 1,
    // Pin the culture. Uno resolves the app language from the browser, and accessible names are
    // localized by design (AutomationProperties.Name comes from resw), so an unpinned locale forces
    // every assertion into a multi-language alternation that passes on any one of its branches -
    // including on the wrong screen. With the locale fixed, a name is a single expected string.
    locale: options.locale ?? "en-US"
  });
  await context.addInitScript({ content: domHelperScript });
  const page = await context.newPage();

  const recentConsoleMessages = [];
  const remember = entry => {
    recentConsoleMessages.push(entry);
    if (recentConsoleMessages.length > recentConsoleLimit) {
      recentConsoleMessages.shift();
    }
  };

  page.on("console", message => {
    const text = message.text();
    remember({ type: message.type(), text });
    if (fatalConsolePattern.test(text)) {
      fatalConsoleMessages.push({ type: message.type(), text });
    }
  });

  page.on("pageerror", error => {
    const text = error.stack ?? error.message;
    remember({ type: "pageerror", text });
    if (fatalConsolePattern.test(text)) {
      fatalConsoleMessages.push({ type: "pageerror", text });
    }
  });

  pageDiagnostics.set(page, { fatalConsoleMessages, recentConsoleMessages });

  return {
    context,
    page,
    fatalConsoleMessages
  };
}

export function assertNoFatalConsoleMessages(fatalConsoleMessages) {
  if (fatalConsoleMessages.length > 0) {
    throw new Error(`Fatal console errors detected: ${JSON.stringify(fatalConsoleMessages, null, 2)}`);
  }
}
