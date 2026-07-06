export async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  await page.waitForFunction(
    source => new RegExp(source).test(document.body?.innerText ?? ""),
    pattern.source,
    { timeout: timeoutMs });

  const bodyText = await page.locator("body").innerText();
  if (!pattern.test(bodyText)) {
    throw new Error(`Expected ${label} text was not visible.`);
  }
}

export async function clickVisibleNavigationTargetUntilBodyText(page, options, pattern, label) {
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

export async function clickVisibleNavigationTarget(page, options) {
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

export async function ensureVisibleNavigationTarget(page, targetOptions, openerOptions) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, targetOptions)) {
    return;
  }

  await clickVisibleNavigationTarget(page, openerOptions);
  await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 30_000 });
}

export async function scrollToVisibleNavigationTarget(page, options) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, options)) {
    return;
  }

  const scrolled = await page.evaluate(input => {
    const labels = input.labels ?? [];
    const automationIds = input.automationIds ?? [];
    const normalize = value => (value ?? "").trim().toLowerCase();
    const target = Array.from(document.querySelectorAll("body *"))
      .find(element => {
        const text = (element.textContent ?? "").trim();
        const aria = element.getAttribute("aria-label") ?? "";
        const automationId =
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "";
        return automationIds.includes(aria)
          || automationIds.includes(automationId)
          || labels.map(normalize).includes(normalize(text))
          || labels.map(normalize).includes(normalize(aria));
      });

    target?.scrollIntoView({ block: "center", inline: "nearest" });
    return Boolean(target);
  }, options);

  if (!scrolled) {
    return;
  }

  await page.waitForFunction(findVisibleNavigationTargetPoint, options, { timeout: 5_000 });
}

export async function readNumericControlValue(page, controlOptions, label) {
  const deadline = Date.now() + 30_000;
  let lastRawValue = null;

  while (Date.now() < deadline) {
    lastRawValue = await page.evaluate(readControlValueInPage, controlOptions);
    const parsedValue = tryParseInteger(lastRawValue);
    if (parsedValue != null) {
      return parsedValue;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out reading ${label}. LastRawValue=${JSON.stringify(lastRawValue)}`);
}

export async function setNumericControlValue(page, controlOptions, value, label) {
  const state = await page.evaluate(readEditableControlStateInPage, controlOptions);
  if (!state?.found || !state?.enabled || !Number.isFinite(state.x) || !Number.isFinite(state.y)) {
    throw new Error(`Expected editable numeric control for ${label}. State=${JSON.stringify(state)}`);
  }

  await page.mouse.click(state.x, state.y, { clickCount: 3 });
  await page.waitForTimeout(100);
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.press("Backspace");
  await page.keyboard.type(String(value));
  await page.keyboard.press("Tab");

  let observedValue = null;
  const deadline = Date.now() + 5_000;
  while (Date.now() < deadline) {
    observedValue = await readNumericControlValue(page, controlOptions, `${label} after edit`);
    if (observedValue === value) {
      return;
    }

    await page.waitForTimeout(100);
  }

  if (Number.isFinite(observedValue)) {
    await page.mouse.click(state.x, state.y);
    const stepKey = value > observedValue ? "ArrowUp" : "ArrowDown";
    const stepCount = Math.min(Math.abs(value - observedValue), 120);
    for (let i = 0; i < stepCount; i += 1) {
      await page.keyboard.press(stepKey);
      await page.waitForTimeout(25);
    }

    await page.keyboard.press("Tab");
    const stepDeadline = Date.now() + 5_000;
    while (Date.now() < stepDeadline) {
      observedValue = await readNumericControlValue(page, controlOptions, `${label} after step edit`);
      if (observedValue === value) {
        return;
      }

      await page.waitForTimeout(100);
    }
  }

  if (observedValue !== value) {
    throw new Error(`Failed to set ${label}. Expected ${value}, got ${observedValue}.`);
  }
}

export function selectAlternateCacheRetentionValue(currentValue) {
  if (!Number.isFinite(currentValue)) {
    return 7;
  }

  if (currentValue >= 60) {
    return 59;
  }

  if (currentValue <= 1) {
    return 2;
  }

  return currentValue + 1;
}

export async function readAppSettingsPersistenceDebug(page, options) {
  return await page.evaluate(readAppSettingsPersistenceDebugInPage, options);
}

export async function expectControlDoesNotEscapePage(page, options, stayOnPagePattern) {
  const beforeUrl = page.url();
  const state = await readControlState(page, options);
  if (!state.found || !state.enabled) {
    return;
  }

  const point = Number.isFinite(state.x) && Number.isFinite(state.y)
    ? { x: state.x, y: state.y }
    : null;
  if (!point) {
    throw new Error(`Expected control was not found for escape check: ${JSON.stringify(options)} state=${JSON.stringify(state)}`);
  }

  await page.mouse.click(point.x, point.y);
  try {
    await waitForBodyText(
      page,
      /当前平台暂不支持打开本地文件或目录|Opening local files or folders is not supported on this platform/,
      "unsupported platform dialog",
      2_000);
    await dismissDialogIfPresent(page);
    return;
  } catch {
  }

  if (page.url() !== beforeUrl) {
    throw new Error(`Expected control ${JSON.stringify(options)} to stay on the current page, but url changed to ${page.url()}.`);
  }

  await waitForBodyText(page, stayOnPagePattern, "data storage page after external open attempt", 5_000);
}

export async function readControlState(page, options) {
  return await page.evaluate(readControlEnabledStateInPage, options);
}

export async function waitForControlState(page, options, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    lastState = await readControlState(page, options);
    if (lastState?.found) {
      return lastState;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out waiting for ${label}. State=${JSON.stringify(lastState)}`);
}

export async function scrollToVisibleControl(page, options) {
  if (await page.evaluate(findVisibleControlPoint, options)) {
    return;
  }

  const scrolled = await page.evaluate(input => {
    const labels = input.labels ?? [];
    const automationIds = input.automationIds ?? [];
    const normalize = value => (value ?? "").trim().toLowerCase();
    const target = Array.from(document.querySelectorAll("body *"))
      .find(element => {
        const text = (element.textContent ?? "").trim();
        const aria = element.getAttribute("aria-label") ?? "";
        const automationId =
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "";
        return automationIds.includes(aria)
          || automationIds.includes(automationId)
          || labels.map(normalize).includes(normalize(text))
          || labels.map(normalize).includes(normalize(aria));
      });

    target?.scrollIntoView({ block: "center", inline: "nearest" });
    return Boolean(target);
  }, options);

  if (!scrolled) {
    return;
  }

  await page.waitForTimeout(250);
}

export async function clickVisibleControl(page, options) {
  const point = await page.evaluate(findVisibleControlPoint, options)
    ?? await page.evaluate(findStartComposerSelectorFallbackPoint, options);
  if (!point) {
    const debug = {
      interactive: await page.evaluate(collectVisibleInteractiveDebug),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
    };
    throw new Error(`No visible control found for ${JSON.stringify(options)}. Candidates=${JSON.stringify(debug)}`);
  }

  await page.mouse.click(point.x, point.y);
}

export async function typeIntoAutomationTextBox(page, automationId, value) {
  const point = await page.evaluate(findVisibleControlPoint, { labels: [], automationIds: [automationId] });
  if (!point) {
    const inputs = await page.evaluate(collectVisibleTextInputPoints);
    throw new Error(`No visible text box found for automation id '${automationId}'. Inputs=${JSON.stringify(inputs)}`);
  }

  await page.mouse.click(point.x, point.y);
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.type(value);
}

export async function typeIntoVisibleTextField(page, options, value, label) {
  const point = await page.evaluate(findVisibleControlPoint, options)
    ?? await page.evaluate(findVisibleTextInputPoint, options);
  if (!point) {
    const inputs = await page.evaluate(collectVisibleTextInputPoints);
    throw new Error(`No visible text field found for ${label}. Options=${JSON.stringify(options)} Inputs=${JSON.stringify(inputs)}`);
  }

  await typeIntoField(page, point, value);
}

export async function selectComboBoxItem(page, selectorAutomationId, expectedVisibleName, options = {}) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    await clickVisibleControl(page, { labels: [], automationIds: [selectorAutomationId] });
    try {
      await page.waitForFunction(
        findVisibleNavigationTargetPoint,
        { labels: [expectedVisibleName], automationIds: [] },
        { timeout: Math.min(3_000, Math.max(250, deadline - Date.now())) });
      break;
    } catch (error) {
      lastError = error;
      await page.keyboard.press("Escape").catch(() => {});
      await page.waitForTimeout(500);
    }
  }

  if (!await page.evaluate(findVisibleNavigationTargetPoint, { labels: [expectedVisibleName], automationIds: [] })) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
    };
    throw new Error(
      `ComboBox '${selectorAutomationId}' did not expose item '${expectedVisibleName}'. `
      + `Debug=${JSON.stringify(debug)} Cause=${lastError?.message ?? lastError}`);
  }

  if (options.keyboardSelectVisibleItem === true) {
    const itemIndex = await page.evaluate(findVisibleComboBoxItemIndex, expectedVisibleName);
    if (itemIndex < 0) {
      throw new Error(`ComboBox '${selectorAutomationId}' item '${expectedVisibleName}' disappeared before keyboard selection.`);
    }

    await page.keyboard.press("Home");
    for (let i = 0; i < itemIndex; i += 1) {
      await page.keyboard.press("ArrowDown");
    }
    await page.keyboard.press("Enter");
  } else {
    await clickVisibleNavigationTarget(page, { labels: [expectedVisibleName], automationIds: [] });
  }

  if (options.verifySelectionText === false) {
    return;
  }

  await page.waitForFunction(
    input => {
      const control = window.__salmoneggSmoke.findVisibleControl({ automationIds: [input.selectorAutomationId] }, [], [input.selectorAutomationId]);
      if ((control?.textContent ?? "").includes(input.expectedVisibleName)
        || (control?.getAttribute("aria-label") ?? "").includes(input.expectedVisibleName)) {
        return true;
      }

      const selectorIndexByAutomationId = new Map([
        ["StartView.AgentSelector", 0],
        ["StartView.ModeSelector", 1],
        ["StartView.ProjectSelector", 2]
      ]);
      const selectorIndex = selectorIndexByAutomationId.get(input.selectorAutomationId);
      if (selectorIndex === undefined) {
        return false;
      }

      const comboBoxes = Array.from(document.querySelectorAll("body *"))
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
          && candidate.rect.width > 0
          && candidate.rect.height > 0
          && candidate.display !== "none"
          && candidate.visibility !== "hidden"
          && candidate.rect.left >= -1
          && candidate.rect.top >= innerHeight * 0.55
          && candidate.rect.left <= innerWidth
          && candidate.rect.top <= innerHeight)
        .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));
      return (comboBoxes[selectorIndex]?.element.textContent ?? "").includes(input.expectedVisibleName);
    },
    { selectorAutomationId, expectedVisibleName },
    { timeout: 10_000 });
}

export async function clickStartComposerSendButton(page) {
  const point = await page.evaluate(() => {
    const buttons = Array.from(document.querySelectorAll("button,[role='button'],.uno-button"))
      .map(element => {
        const rect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return {
          element,
          rect,
          text: (element.textContent ?? "").trim(),
          aria: element.getAttribute("aria-label") ?? "",
          className: element.className?.toString?.() ?? "",
          display: style.display,
          visibility: style.visibility,
          disabled:
            element.disabled === true
            || element.getAttribute("disabled") != null
            || element.getAttribute("aria-disabled") === "true"
            || element.className?.toString?.().toLowerCase().includes("disabled")
        };
      })
      .filter(candidate =>
        candidate.rect.width > 0
        && candidate.rect.height > 0
        && candidate.display !== "none"
        && candidate.visibility !== "hidden"
        && !candidate.disabled
        && candidate.rect.top >= innerHeight * 0.75
        && candidate.rect.left >= 0
        && candidate.rect.left <= innerWidth
        && candidate.rect.top <= innerHeight)
      .sort((left, right) => right.rect.right - left.rect.right);

    const target = buttons[0];
    if (!target) {
      return null;
    }

    return {
      x: target.rect.left + target.rect.width / 2,
      y: target.rect.top + target.rect.height / 2
    };
  });

  if (!point) {
    const debug = await page.evaluate(collectVisibleInteractiveDebug);
    throw new Error(`Start composer send button was not visible. Debug=${JSON.stringify(debug)}`);
  }

  await page.mouse.click(point.x, point.y);
}

export async function focusVisibleControl(page, options, label) {
  await scrollToVisibleControl(page, options);
  const point = await page.evaluate(findVisibleControlPoint, options);
  if (!point) {
    const debug = await page.evaluate(collectVisibleInteractiveDebug);
    throw new Error(`No visible control found to focus for ${label}. Options=${JSON.stringify(options)} Debug=${JSON.stringify(debug)}`);
  }

  await page.mouse.click(point.x, point.y);
  const snapshot = await waitForFocusedElementSnapshot(page, `${label} focus`);
  if (!snapshot.visible || snapshot.isBody) {
    throw new Error(`Expected visible focused element for ${label}. Snapshot=${JSON.stringify(snapshot)}`);
  }

  return snapshot;
}

export async function waitForFocusedElementSnapshot(page, label, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  let snapshot = null;

  while (Date.now() < deadline) {
    snapshot = await page.evaluate(readFocusedElementSnapshotInPage);
    if (snapshot?.visible && !snapshot?.isBody) {
      return snapshot;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out waiting for ${label}. Snapshot=${JSON.stringify(snapshot)}`);
}

export function findVisibleNavigationTargetPoint(input) {
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
    ?? target.closest(".uno-comboboxitem")
    ?? target.closest(".uno-button")
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

export function collectVisibleNavigationTargetDebug() {
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

export function collectVisibleInteractiveDebug() {
  return Array.from(document.querySelectorAll("button,input,[role='button'],[role='switch'],[aria-checked],.uno-button,.uno-toggleswitch"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      return {
        text: (element.textContent ?? "").trim().slice(0, 120),
        aria: element.getAttribute("aria-label") ?? "",
        role: element.getAttribute("role") ?? "",
        checked: element.getAttribute("aria-checked") ?? "",
        className: element.className?.toString?.() ?? "",
        rect: {
          left: Math.round(rect.left),
          top: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height)
        }
      };
    })
    .filter(candidate => candidate.rect.width > 0 && candidate.rect.height > 0)
    .slice(0, 120);
}

export function collectVisibleComboBoxDebug() {
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
        text: (element.textContent ?? "").trim(),
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        display: style.display,
        visibility: style.visibility
      };
    })
    .filter(candidate =>
      (candidate.role === "combobox" || candidate.className.toLowerCase().includes("combobox"))
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight)
    .map(candidate => ({
      text: candidate.text,
      aria: candidate.aria,
      automationId: candidate.automationId,
      role: candidate.role,
      className: candidate.className,
      rect: {
        left: Math.round(candidate.rect.left),
        top: Math.round(candidate.rect.top),
        width: Math.round(candidate.rect.width),
        height: Math.round(candidate.rect.height)
      }
    }))
    .slice(0, 40);
}

export function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function tryParseInteger(value) {
  const match = String(value ?? "").match(/-?\d+/);
  return match ? Number.parseInt(match[0], 10) : null;
}

async function typeIntoField(page, field, value) {
  const clickX = Number.isFinite(field.x) ? field.x : field.left + (field.width / 2);
  const clickY = Number.isFinite(field.y) ? field.y : field.top + (field.height / 2);
  await page.mouse.click(clickX, clickY);
  await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
  await page.keyboard.type(value);
  await page.keyboard.press("Tab");
  await page.waitForTimeout(150);
}

async function dismissDialogIfPresent(page) {
  const closeLabels = {
    labels: ["确定", "OK"],
    automationIds: []
  };

  const point = await page.evaluate(findVisibleNavigationTargetPoint, closeLabels);
  if (!point) {
    return;
  }

  await page.mouse.click(point.x, point.y);
  await page.waitForTimeout(300);
}

function findVisibleControlPoint(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return null;
  }

  const inputElement = control.matches("input,textarea")
    ? control
    : control.querySelector("input,textarea") ?? control;
  const rect = inputElement.getBoundingClientRect();
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function findVisibleTextInputPoint(input) {
  const normalize = value => (value ?? "").trim().toLowerCase();
  const labels = (input.labels ?? []).map(normalize).filter(Boolean);
  const automationIds = (input.automationIds ?? []).map(normalize).filter(Boolean);
  const candidates = Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        placeholder: element.getAttribute("placeholder") ?? "",
        visible: rect.width > 0
          && rect.height > 0
          && style.display !== "none"
          && style.visibility !== "hidden"
          && !["button", "checkbox", "radio", "submit"].includes(type)
          && rect.left >= 0
          && rect.top >= 0
          && rect.left <= innerWidth
          && rect.top <= innerHeight
      };
    })
    .filter(candidate => candidate.visible)
    .map(candidate => ({
      ...candidate,
      automationMatch:
        automationIds.includes(normalize(candidate.aria))
        || automationIds.includes(normalize(candidate.automationId)),
      textMatch:
        labels.some(label => normalize(candidate.aria).includes(label))
        || labels.some(label => normalize(candidate.placeholder).includes(label))
    }))
    .filter(candidate => candidate.automationMatch || candidate.textMatch)
    .sort((left, right) => {
      if (left.automationMatch !== right.automationMatch) {
        return left.automationMatch ? -1 : 1;
      }

      return (left.top - right.top) || (left.left - right.left);
    });
  const target = candidates[0];
  if (!target) {
    return null;
  }

  return {
    left: target.left,
    top: target.top,
    width: target.width,
    height: target.height
  };
}

function findStartComposerSelectorFallbackPoint(input) {
  const automationIds = input.automationIds ?? [];
  const selectorIndexByAutomationId = new Map([
    ["StartView.AgentSelector", 0],
    ["StartView.ModeSelector", 1],
    ["StartView.ProjectSelector", 2]
  ]);
  const targetAutomationId = automationIds.find(id => selectorIndexByAutomationId.has(id));
  if (!targetAutomationId) {
    return null;
  }

  const comboBoxes = Array.from(document.querySelectorAll("body *"))
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
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.display !== "none"
      && candidate.visibility !== "hidden"
      && candidate.rect.left >= -1
      && candidate.rect.top >= -1
      && candidate.rect.left <= innerWidth
      && candidate.rect.top <= innerHeight
      && candidate.rect.top > innerHeight * 0.55)
    .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));
  const target = comboBoxes[selectorIndexByAutomationId.get(targetAutomationId)];
  if (!target) {
    return null;
  }

  return {
    x: target.rect.left + target.rect.width / 2,
    y: target.rect.top + target.rect.height / 2
  };
}

function findVisibleComboBoxItemIndex(expectedVisibleName) {
  const items = Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const className = element.className?.toString?.() ?? "";
      return {
        text: (element.textContent ?? "").trim(),
        rect,
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
      && candidate.rect.top <= innerHeight)
    .sort((left, right) => (left.rect.top - right.rect.top) || (left.rect.left - right.rect.left));

  return items.findIndex(item => item.text === expectedVisibleName);
}

function readControlEnabledStateInPage(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return { found: false, enabled: false };
  }

  const resolveNearestCompactRoleButtonInline = element => {
    const candidate = element.closest("[role='button']");
    if (!candidate) {
      return null;
    }

    const rect = candidate.getBoundingClientRect();
    if (rect.width > 420 || rect.height > 160) {
      return null;
    }

    return candidate;
  };
  const clickable =
    control.matches("button,input,[role='switch'],.uno-button,.uno-toggleswitch")
      ? control
      : control.closest("button,input,[role='switch'],.uno-button,.uno-toggleswitch")
        ?? resolveNearestCompactRoleButtonInline(control)
        ?? control;
  const disabled =
    clickable.disabled === true
    || clickable.getAttribute("disabled") != null
    || clickable.getAttribute("aria-disabled") === "true"
    || clickable.className?.toString?.().toLowerCase().includes("disabled");

  const rect = clickable.getBoundingClientRect();
  return {
    found: true,
    enabled: !disabled,
    text: (clickable.textContent ?? "").trim(),
    aria: clickable.getAttribute("aria-label") ?? "",
    x: (clickable.className?.toString?.().toLowerCase().includes("toggleswitch")
      ? window.__salmoneggSmoke.resolveToggleClickPoint(clickable)?.x
      : rect.left + rect.width / 2) ?? null,
    y: (clickable.className?.toString?.().toLowerCase().includes("toggleswitch")
      ? window.__salmoneggSmoke.resolveToggleClickPoint(clickable)?.y
      : rect.top + rect.height / 2) ?? null,
    automationId:
      clickable.getAttribute("data-automation-id")
      ?? clickable.getAttribute("data-automationid")
      ?? clickable.getAttribute("automationid")
      ?? ""
  };
}

function readControlValueInPage(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return null;
  }

  const editable = (() => {
    if (control.matches("input,textarea,[contenteditable='true']")) {
      return control;
    }

    let current = control;
    while (current && current !== document.body) {
      const editableCandidate = current.querySelector("input,textarea,[contenteditable='true']");
      if (editableCandidate) {
        return editableCandidate;
      }

      current = current.parentElement;
    }

    return null;
  })();
  return editable?.value
    ?? control.getAttribute("aria-valuenow")
    ?? editable?.getAttribute("aria-valuenow")
    ?? (control.textContent ?? "").trim();
}

function readEditableControlStateInPage(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return { found: false, enabled: false };
  }

  const editable = (() => {
    if (control.matches("input,textarea,[contenteditable='true']")) {
      return control;
    }

    let current = control;
    while (current && current !== document.body) {
      const editableCandidate = current.querySelector("input,textarea,[contenteditable='true']");
      if (editableCandidate) {
        return editableCandidate;
      }

      current = current.parentElement;
    }

    return null;
  })();
  if (!editable) {
    return { found: false, enabled: false };
  }

  const rect = editable.getBoundingClientRect();
  const disabled =
    editable.disabled === true
    || editable.getAttribute("disabled") != null
    || editable.getAttribute("aria-disabled") === "true";

  return {
    found: true,
    enabled: !disabled,
    value: editable.value ?? "",
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function readAppSettingsPersistenceDebugInPage(input) {
  const controlOptions = input.controlOptions;
  const path = input.path;
  const readControlValueInline = controlInput => {
    const labels = controlInput.labels ?? [];
    const automationIds = controlInput.automationIds ?? [];
    const control = window.__salmoneggSmoke.findVisibleControl(controlInput, labels, automationIds);
    if (!control) {
      return null;
    }

    const editable = (() => {
      if (control.matches("input,textarea,[contenteditable='true']")) {
        return control;
      }

      let current = control;
      while (current && current !== document.body) {
        const editableCandidate = current.querySelector("input,textarea,[contenteditable='true']");
        if (editableCandidate) {
          return editableCandidate;
        }

        current = current.parentElement;
      }

      return null;
    })();
    return editable?.value
      ?? control.getAttribute("aria-valuenow")
      ?? editable?.getAttribute("aria-valuenow")
      ?? (control.textContent ?? "").trim();
  };
  const readLocalTextFileInline = filePath => {
    const result = {
      path: filePath,
      content: null,
      error: null
    };

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

  const appYaml = readLocalTextFileInline(path);
  return {
    visibleValue: readControlValueInline(controlOptions),
    appYaml: appYaml.content,
    appYamlError: appYaml.error
  };
}

function readFocusedElementSnapshotInPage() {
  const element = document.activeElement;
  if (!element) {
    return null;
  }

  const rect = element.getBoundingClientRect();
  const style = getComputedStyle(element);
  return {
    tag: element.tagName,
    text: (element.textContent ?? "").trim().slice(0, 120),
    aria: element.getAttribute("aria-label") ?? "",
    automationId:
      element.getAttribute("data-automation-id")
      ?? element.getAttribute("data-automationid")
      ?? element.getAttribute("automationid")
      ?? "",
    role: element.getAttribute("role") ?? "",
    className: element.className?.toString?.() ?? "",
    visible:
      rect.width > 0
      && rect.height > 0
      && style.display !== "none"
      && style.visibility !== "hidden"
      && rect.left < innerWidth
      && rect.right > 0
      && rect.top < innerHeight
      && rect.bottom > 0,
    isBody: element === document.body,
    rect: {
      left: Math.round(rect.left),
      top: Math.round(rect.top),
      width: Math.round(rect.width),
      height: Math.round(rect.height)
    }
  };
}

function collectVisibleTextInputPoints() {
  return Array.from(document.querySelectorAll("input,textarea,[contenteditable='true']"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      const type = element.getAttribute("type")?.toLowerCase() ?? "";
      return {
        x: rect.left + rect.width / 2,
        y: rect.top + rect.height / 2,
        top: rect.top,
        left: rect.left,
        width: rect.width,
        height: rect.height,
        text: (element.textContent ?? "").trim(),
        value: element.value ?? "",
        aria: element.getAttribute("aria-label") ?? "",
        automationId:
          element.getAttribute("data-automation-id")
          ?? element.getAttribute("data-automationid")
          ?? element.getAttribute("automationid")
          ?? "",
        role: element.getAttribute("role") ?? "",
        type,
        placeholder: element.getAttribute("placeholder") ?? "",
        visible: rect.width > 0
          && rect.height > 0
          && style.display !== "none"
          && style.visibility !== "hidden"
          && !["button", "checkbox", "radio", "submit"].includes(type)
          && rect.left >= 0
          && rect.top >= 0
          && rect.left <= innerWidth
          && rect.top <= innerHeight
      };
    })
    .filter(candidate => candidate.visible)
    .sort((left, right) => (left.top - right.top) || (left.left - right.left));
}
