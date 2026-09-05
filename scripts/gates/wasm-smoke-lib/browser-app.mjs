// The page-side half of ui-affordances.mjs: the semantic matcher/activator lives here because
// `page.evaluate` serializes callbacks without their closures, so everything that runs in the
// page shares this one implementation instead of six drifting copies of the same scan. Injected
// once per context; the runtime is a pure DOM reader/writer, so it is safe to register before the
// semantic tree exists.
const semanticRuntimeScript = `
(() => {
  const semanticRoot = () => document.getElementById("uno-semantics-root");

  const describeNode = element => {
    const ariaState = name => {
      const value = element.getAttribute(name);
      return value === "true" ? true : value === "false" ? false : null;
    };
    const rect = element.getBoundingClientRect();
    return {
      id: element.id,
      role: element.getAttribute("role") ?? element.tagName.toLowerCase(),
      automationId: (element.getAttribute("xamlautomationid") ?? "").trim(),
      aria: (element.getAttribute("aria-label") ?? "").trim(),
      text: (element.textContent ?? "").trim().slice(0, 160),
      hidden: element.hidden === true,
      disabled: element.disabled === true || element.getAttribute("aria-disabled") === "true",
      checked: ariaState("aria-checked"),
      selected: ariaState("aria-selected"),
      expanded: ariaState("aria-expanded"),
      value: typeof element.value === "string" ? element.value : null,
      rect: {
        left: Math.round(rect.left),
        top: Math.round(rect.top),
        width: Math.round(rect.width),
        height: Math.round(rect.height)
      }
    };
  };

  const normalize = value => (value ?? "").trim().toLowerCase();

  // Prefer a laid-out candidate. Templated rows (a ListView's item template, an editor that is only
  // realized for the row being edited) put several nodes carrying the SAME automation id into the
  // tree; the ones belonging to unrealized rows report a placeholder rect a few pixels wide at the
  // origin. They are indistinguishable by id, they are not hidden, and driving one does nothing a
  // user could see - typing into it lands in a control that is not on screen and never reaches the
  // ViewModel. Taking the first match is therefore a coin flip; taking the laid-out one is the
  // control the user is actually looking at.
  const laidOutMinimum = 12;
  const looksLaidOut = element => {
    const rect = element.getBoundingClientRect();
    return rect.width >= laidOutMinimum && rect.height >= laidOutMinimum;
  };

  const matchNode = input => {
    const automationIds = (input.automationIds ?? []).map(normalize).filter(Boolean);
    const labels = (input.labels ?? []).map(normalize).filter(Boolean);
    const role = (input.role ?? "").trim().toLowerCase();
    const roleOf = element => (element.getAttribute("role") ?? element.tagName.toLowerCase()).toLowerCase();
    const nodes = semanticRoot()?.querySelectorAll("[id^='uno-semantics-']");
    if (!nodes) {
      return null;
    }

    const idMatches = [];
    const labelMatches = [];
    for (const element of nodes) {
      if (element.hidden) {
        continue;
      }

      // Optional role pin: a label match must not settle for an inner text node whose
      // textContent happens to equal the wanted name before the named control's own
      // aria-label has landed (the hero suggestion cards race exactly that way - the
      // title TextBlock renders before the button's Name binding resolves).
      if (role !== "" && roleOf(element) !== role) {
        continue;
      }

      // aria-label carries the accessible name, which for nameless controls is the automation id
      // (the suite relies on that for e.g. StartView.PromptBox); xamlautomationid is the id
      // proper. Check both against the caller's ids - either hit is authoritative.
      const automationId = normalize(element.getAttribute("xamlautomationid"));
      const aria = normalize(element.getAttribute("aria-label"));
      if (automationIds.length > 0
        && ((automationId !== "" && automationIds.includes(automationId))
          || (aria !== "" && automationIds.includes(aria)))) {
        idMatches.push(element);
        continue;
      }

      if (labels.length > 0
        && (labels.includes(aria) || labels.includes(normalize(element.textContent)))) {
        labelMatches.push(element);
      }
    }

    return idMatches.find(looksLaidOut)
      ?? idMatches[0]
      ?? labelMatches.find(looksLaidOut)
      ?? labelMatches[0]
      ?? null;
  };

  const activate = input => {
    const element = matchNode(input);
    if (!element) {
      return { matched: false, activated: false, state: null };
    }

    const state = describeNode(element);
    if (state.disabled) {
      return { matched: true, activated: false, state };
    }

    // Uno programs the peer action (Invoke / Toggle / Selection / ExpandCollapse) onto each
    // node's click handler without hit testing, so a programmatic click IS the activation.
    element.click();
    return { matched: true, activated: true, state };
  };

  const findEditable = element => (element.matches("input,textarea")
    ? element
    : element.querySelector("input,textarea")) ?? null;

  const setInput = (input, value) => {
    const element = matchNode(input);
    if (!element) {
      return { matched: false, editable: false, disabled: false, state: null };
    }

    const editable = findEditable(element);
    if (!editable) {
      return { matched: true, editable: false, disabled: false, state: describeNode(element) };
    }

    const state = describeNode(editable);
    if (state.disabled) {
      return { matched: true, editable: true, disabled: true, state };
    }

    editable.focus();
    editable.value = value;
    try {
      editable.setSelectionRange(value.length, value.length);
    } catch {
      // Some input types reject selection updates; the value assignment already took.
    }

    // The semantic input forwards 'input' events to the managed text box (OnTextInput), and
    // Uno's key handling suppresses native insertion for canvas input - assigning the value and
    // dispatching the event is the supported path.
    editable.dispatchEvent(new Event("input", { bubbles: true }));
    return { matched: true, editable: true, disabled: false, state };
  };

  // Skia collapsed combo boxes mirror no selection text at all: the value only exists as the
  // popup's highlighted option while the dropdown is open. The readable item names surface as
  // fresh clean-label nodes outside the popup subtree, in document order matching the popup's
  // option nodes - but only on the FIRST open of a dropdown: on reopen Uno reuses the same
  // option nodes and never rebuilds the clean-label mirror. So the first aligned open seeds a
  // posinset-to-label cache per automation id, and every later open reads labels back through
  // the option nodes' aria-posinset. The count alignment is the ordering proof; a mismatch
  // means the mirror has not caught up (or the popup is a half-open ghost) and the caller must
  // retry. The cache lives in the page, so a shell reload (language switch) clears it.
  const comboBoxLabelCache = new Map();
  const comboBoxOpenState = (automationId, beforeIds) => {
    const combo = matchNode({ automationIds: [automationId], labels: [] });
    if (!combo) {
      return null;
    }

    const expanded = combo.getAttribute("aria-expanded") === "true";
    const popupId = combo.getAttribute("aria-controls");
    const popup = (popupId && document.getElementById(popupId))
      ?? semanticRoot().querySelector("[role='listbox']")
      ?? null;
    const optionNodes = popup ? Array.from(popup.children) : [];
    const activeId = combo.getAttribute("aria-activedescendant");
    const activeIndex = activeId ? optionNodes.findIndex(node => node.id === activeId) : -1;

    const freshLabels = Array.from(semanticRoot().querySelectorAll("[aria-label]"))
      .filter(node => !beforeIds.includes(node.id)
        && !node.hidden
        && node.getAttribute("aria-label") !== "Popup"
        && !(popup && (popup === node || popup.contains(node))))
      .map(node => node.getAttribute("aria-label"));

    if (optionNodes.length > 0 && freshLabels.length === optionNodes.length) {
      const byPos = new Map();
      optionNodes.forEach((node, index) => {
        byPos.set(node.getAttribute("aria-posinset") ?? String(index + 1), freshLabels[index]);
      });
      comboBoxLabelCache.set(automationId, byPos);
    }

    const cached = comboBoxLabelCache.get(automationId);
    const cacheUsable = cached !== undefined && cached.size === optionNodes.length;
    const itemLabels = freshLabels.length === optionNodes.length
      ? freshLabels
      : optionNodes.map(node => cached?.get(node.getAttribute("aria-posinset")) ?? null);

    return {
      expanded,
      optionCount: optionNodes.length,
      activeIndex,
      itemLabels,
      aligned: expanded
        && optionNodes.length > 0
        && (freshLabels.length === optionNodes.length || cacheUsable)
        && itemLabels.every(label => typeof label === "string")
    };
  };

  // Counting matches, not just finding one: a saved row appearing twice is only observable as a
  // count, and matchNode deliberately returns the first hit.
  const countMatches = input => {
    const automationIds = (input.automationIds ?? []).map(normalize).filter(Boolean);
    const labels = (input.labels ?? []).map(normalize).filter(Boolean);
    const nodes = semanticRoot()?.querySelectorAll("[id^='uno-semantics-']") ?? [];
    let count = 0;
    for (const element of nodes) {
      if (element.hidden) {
        continue;
      }

      const automationId = normalize(element.getAttribute("xamlautomationid"));
      const aria = normalize(element.getAttribute("aria-label"));
      const matchesId = automationIds.length > 0
        && ((automationId !== "" && automationIds.includes(automationId))
          || (aria !== "" && automationIds.includes(aria)));
      const matchesLabel = automationIds.length === 0
        && labels.length > 0
        && ((aria !== "" && labels.includes(aria))
          || labels.includes(normalize(element.textContent)));
      if (matchesId || matchesLabel) {
        count += 1;
      }
    }

    return count;
  };

  const comboBoxLabeledIds = () => Array.from(semanticRoot().querySelectorAll("[aria-label]"))
    .map(node => node.id);

  // Resolve the real <input> a control writes through, so the driver can put a keyboard on it.
  // Returning the element id rather than the element itself is forced by page.evaluate: DOM nodes do
  // not survive the round trip.
  const resolveEditableField = input => {
    // A CSS selector is accepted as an escape hatch for controls whose automation id never reaches
    // the accessibility view. The composer is the standing case: its id is applied through an x:Bind
    // on AutomationProperties.AutomationId, and the exported node carries no id at all, so there is
    // nothing to match on - while the box itself is unmistakable in the DOM.
    const element = input.selector
      ? document.querySelector(input.selector)
      : matchNode(input);
    if (!element) {
      return { matched: false, id: null, disabled: false, state: null };
    }

    const state = describeNode(element);
    const editable = findEditable(element);
    return { matched: true, id: editable?.id ?? null, disabled: state.disabled, state };
  };

  const focusedSnapshot = () => {
    const element = document.activeElement;
    if (!element || element === document.body) {
      return { visible: false, isBody: true };
    }

    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return {
      tag: element.tagName,
      text: (element.textContent ?? "").trim().slice(0, 120),
      aria: element.getAttribute("aria-label") ?? "",
      automationId: element.getAttribute("xamlautomationid") ?? "",
      role: element.getAttribute("role") ?? "",
      visible:
        rect.width > 0
        && rect.height > 0
        && style.display !== "none"
        && style.visibility !== "hidden"
        && rect.left < innerWidth
        && rect.right > 0
        && rect.top < innerHeight
        && rect.bottom > 0,
      isBody: false,
      rect: {
        left: Math.round(rect.left),
        top: Math.round(rect.top),
        width: Math.round(rect.width),
        height: Math.round(rect.height)
      }
    };
  };

  const readLocalTextFile = filePath => {
    const result = { path: filePath, content: null, error: null };
    try {
      const fs = globalThis.FS;
      if (!fs) {
        result.error = "globalThis.FS unavailable";
        return result;
      }

      result.content = fs.readFile(filePath, { encoding: "utf8" });
      return result;
    } catch (error) {
      result.error = error?.message ?? String(error);
      return result;
    }
  };

  const persistenceDebug = input => {
    const element = matchNode(input.controlOptions);
    const editable = element ? findEditable(element) : null;
    const visibleValue = editable?.value
      ?? element?.getAttribute("aria-valuenow")
      ?? (element?.textContent ?? "").trim();

    return {
      visibleValue: element ? visibleValue : null,
      ...readLocalTextFile(input.path)
    };
  };

  // Real DOM focus on the semantic node. Uno forwards focus into the managed visual tree, which
  // is the precondition for keyboard choreography (F4, arrows, Enter) on combos and lists.
  const focusControl = input => {
    const element = matchNode(input);
    if (!element) {
      return false;
    }

    element.focus();
    return document.activeElement === element;
  };

  const stateWithLegacyPointers = element => {
    const state = describeNode(element);
    return {
      found: true,
      enabled: !state.disabled,
      ...state,
      // Semantic nodes sit at the layout position of their control; the center doubles as a
      // legacy-style point for the callers that still read one.
      x: state.rect.left + state.rect.width / 2,
      y: state.rect.top + state.rect.height / 2
    };
  };

  window.__salmoneggSmoke.semantic = {
    describe: input => {
      const element = matchNode(input);
      return element ? stateWithLegacyPointers(element) : null;
    },
    activate,
    setInput,
    resolveEditableField,
    comboBoxOpenState,
    comboBoxLabeledIds,
    countMatches,
    focusControl,
    focusedSnapshot,
    readLocalTextFile,
    persistenceDebug,
    collectDebug: () => Array.from(semanticRoot()?.querySelectorAll("[id^='uno-semantics-']") ?? [])
      .map(describeNode)
      .filter(node => !node.hidden)
      .slice(0, 200)
  };
})();
`;

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
            ?? element.getAttribute("xamlautomationid")
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
  await waitForSplashLoaderGone(page);
}

// The bootstrap keeps the `.uno-loader` splash mounted as `#loading` and unmounts it only from a
// MutationObserver on #uno-body's child list (uno-bootstrap.js `initProgress`). The canvas, the
// aria-live regions and the semantics root all land in #uno-body during boot, so the observer fires
// within about a second of first paint - but openApp resolves the moment the semantic shell labels
// appear, which can still be inside that window (measured: the splash was up for ~750ms after the
// start cards were already queryable). While it is up it covers the whole viewport with
// `pointer-events: auto` at z-index 5000, so a real pointer click aimed at the canvas lands on the
// splash and is silently swallowed - the click reports success, nothing behind it reacts, and the
// step times out on a body-text wait with no signal of what ate the click. Wait for the splash to
// detach so pointer-driven steps start from a page a user could actually reach.
//
// If it is still mounted this deep into boot the observer never fired, which is itself a defect a
// real user would see as a splash that never leaves; surface that instead of tearing it out here.
async function waitForSplashLoaderGone(page) {
  try {
    await page.waitForFunction(
      () => !document.getElementById("loading"),
      undefined,
      { timeout: 15_000, polling: 250 });
  } catch (error) {
    const splash = await page.evaluate(() => {
      const element = document.getElementById("loading");
      if (!element) {
        return null;
      }
      const rect = element.getBoundingClientRect();
      return {
        rect: `${rect.width}x${rect.height}@${rect.left},${rect.top}`,
        pointerEvents: getComputedStyle(element).pointerEvents,
        zIndex: getComputedStyle(element).zIndex
      };
    });
    throw new Error(
      `The bootstrap splash loader (#loading) never unmounted, so every real pointer click `
      + `lands on it instead of the app. Splash state=${JSON.stringify(splash)}`, { cause: error });
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
  await context.addInitScript({ content: semanticRuntimeScript });
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
