export async function waitForBodyText(page, pattern, label, timeoutMs = 30_000) {
  // `waitForFunction` confirms the pattern is in body text, then a *separate*
  // `locator().innerText()` re-reads. Those two reads straddle the LoadAsync
  // Clear-then-refill window (ObservableCollection Reset momentarily empties
  // the body), so the re-read can come back empty despite the poll just
  // succeeding. Retry the whole poll+read cycle until the re-read is stable.
  const deadline = Date.now() + timeoutMs;
  let lastBodyText = "";
  while (Date.now() < deadline) {
    await page.waitForFunction(
      source => new RegExp(source).test(document.body?.innerText ?? ""),
      pattern.source,
      { timeout: Math.min(5_000, Math.max(250, deadline - Date.now())) });

    lastBodyText = await page.locator("body").innerText();
    if (pattern.test(lastBodyText)) {
      return;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Expected ${label} text was not visible. Last body: ${lastBodyText.slice(0, 500)}`);
}

export async function clickVisibleNavigationTargetUntilBodyText(page, options, pattern, label, activationOptions = {}) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    try {
      await clickVisibleNavigationTarget(page, options);
      await waitForBodyText(page, pattern, label, Math.min(5_000, Math.max(250, deadline - Date.now())));
      return;
    } catch (error) {
      lastError = error;
      if (activationOptions.keyboardFallback === true) {
        try {
          await activateVisibleNavigationTargetByKeyboard(page, options);
          await waitForBodyText(page, pattern, label, Math.min(5_000, Math.max(250, deadline - Date.now())));
          return;
        } catch (fallbackError) {
          lastError = fallbackError;
        }
      }

      await page.waitForTimeout(250);
    }
  }

  const bodyText = await page.locator("body").innerText().catch(() => "");
  const candidates = await page.evaluate(collectVisibleNavigationTargetMatchDebug, options).catch(error => [`debug error: ${error?.message ?? error}`]);
  throw new Error(
    `Expected ${label} text was not visible after clicking navigation target. `
    + `Last error: ${lastError?.message ?? lastError}. Body: ${bodyText.slice(0, 1_000)} `
    + `Candidates=${JSON.stringify(candidates)}`);
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

async function activateVisibleNavigationTargetByKeyboard(page, options) {
  const focused = await page.evaluate(focusVisibleNavigationTargetInPage, options);
  if (!focused) {
    throw new Error(`Navigation target could not be focused for keyboard activation: ${JSON.stringify(options)}`);
  }

  await page.keyboard.press("Enter");
}

export async function ensureVisibleNavigationTarget(page, targetOptions, openerOptions) {
  if (await page.evaluate(findVisibleNavigationTargetPoint, targetOptions)) {
    return;
  }

  await clickVisibleNavigationTarget(page, openerOptions);
  try {
    await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 3_000 });
    return;
  } catch {
  }

  await activateVisibleNavigationTargetByKeyboard(page, openerOptions);
  try {
    await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 3_000 });
    return;
  } catch {
  }

  const clicked = await page.evaluate(clickVisibleNavigationTargetInPage, openerOptions);
  if (!clicked) {
    throw new Error(`Navigation opener could not be activated: ${JSON.stringify(openerOptions)}`);
  }

  await page.waitForFunction(findVisibleNavigationTargetPoint, targetOptions, { timeout: 30_000 });
}

function focusVisibleNavigationTargetInPage(input) {
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
  const focusable =
    target?.closest(".uno-navigationviewitem")
    ?? target?.closest(".uno-button")
    ?? target?.closest("[role='button']")
    ?? target?.closest("button")
    ?? target;
  if (typeof focusable?.focus !== "function") {
    return false;
  }

  focusable.focus();
  return document.activeElement === focusable || focusable.contains(document.activeElement);
}

function clickVisibleNavigationTargetInPage(input) {
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

  const target = nodes[0]?.element ?? null;
  const clickable =
    target?.closest(".uno-navigationviewitem")
    ?? target?.closest(".uno-button")
    ?? target?.closest("[role='button']")
    ?? target?.closest("button")
    ?? target;
  if (typeof clickable?.click !== "function") {
    return false;
  }

  clickable.click();
  return true;
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

export async function readToggleSwitchValue(page, controlOptions, label, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;

  while (Date.now() < deadline) {
    lastState = await page.evaluate(readToggleSwitchStateInPage, controlOptions);
    if (lastState?.found && typeof lastState.checked === "boolean") {
      return lastState.checked;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out reading toggle ${label}. State=${JSON.stringify(lastState)}`);
}

export async function setToggleSwitchValue(page, controlOptions, expectedValue, label) {
  let state = await page.evaluate(readToggleSwitchStateInPage, controlOptions);
  if (!state?.found || !state?.enabled || typeof state.checked !== "boolean") {
    throw new Error(`Expected enabled toggle for ${label}. State=${JSON.stringify(state)}`);
  }

  if (state.checked === expectedValue) {
    return;
  }

  if (state.hasPointerTarget === false) {
    await pressToggleSwitchByKeyboard(page, controlOptions, label);
  } else {
    if (!Number.isFinite(state.x) || !Number.isFinite(state.y)) {
      throw new Error(`Toggle ${label} did not expose a clickable point. State=${JSON.stringify(state)}`);
    }

    await page.mouse.click(state.x, state.y);
  }

  const deadline = Date.now() + 10_000;
  let usedKeyboardFallback = state.hasPointerTarget === false;
  while (Date.now() < deadline) {
    state = await page.evaluate(readToggleSwitchStateInPage, controlOptions);
    if (state?.found && state.checked === expectedValue) {
      return;
    }

    if (state?.found
      && state.checked !== expectedValue
      && state.hasPointerTarget !== false
      && !usedKeyboardFallback
      && deadline - Date.now() < 5_000) {
      usedKeyboardFallback = true;
      await pressToggleSwitchByKeyboard(page, controlOptions, label);
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Toggle ${label} did not change to ${expectedValue}. State=${JSON.stringify(state)}`);
}

export async function expectToggleSwitchValue(page, controlOptions, expectedValue, label) {
  const actualValue = await readToggleSwitchValue(page, controlOptions, label);
  if (actualValue !== expectedValue) {
    throw new Error(`Expected toggle ${label} to be ${expectedValue}, got ${actualValue}.`);
  }
}

export async function expectControlEnabledState(page, controlOptions, expectedEnabled, label) {
  const state = await waitForControlState(page, controlOptions, label);
  if (state.enabled !== expectedEnabled) {
    throw new Error(`Expected ${label} enabled=${expectedEnabled}. State=${JSON.stringify(state)}`);
  }
}

export async function scrollToVisibleControl(page, options, timeoutMs = 10_000) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    if (await page.evaluate(findVisibleControlPoint, options)) {
      return true;
    }

    const scrolledToKnownTarget = await page.evaluate(scrollKnownControlCandidateIntoView, options);
    if (scrolledToKnownTarget) {
      await page.waitForTimeout(250);
      if (await page.evaluate(findVisibleControlPoint, options)) {
        return true;
      }
    }

    const scrolledContainer = await page.evaluate(scrollNearestScrollableContainerDown);
    await page.waitForTimeout(250);
  }

  return Boolean(await page.evaluate(findVisibleControlPoint, options));
}

function scrollKnownControlCandidateIntoView(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const normalize = value => (value ?? "").trim().toLowerCase();
  const normalizedLabels = labels.map(normalize).filter(Boolean);
  const normalizedAutomationIds = automationIds.map(normalize).filter(Boolean);
  const target = Array.from(document.querySelectorAll("body *"))
    .find(element => {
      const text = (element.textContent ?? "").trim();
      const aria = element.getAttribute("aria-label") ?? "";
      const automationId =
        element.getAttribute("data-automation-id")
        ?? element.getAttribute("data-automationid")
        ?? element.getAttribute("automationid")
        ?? "";
      return normalizedAutomationIds.includes(normalize(aria))
        || normalizedAutomationIds.includes(normalize(automationId))
        || normalizedLabels.includes(normalize(text))
        || normalizedLabels.includes(normalize(aria));
    });

  target?.scrollIntoView({ block: "center", inline: "nearest" });
  return Boolean(target);
}

function scrollNearestScrollableContainerDown() {
  const scrollableCandidates = Array.from(document.querySelectorAll("body *"))
    .map(element => {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {
        element,
        rect,
        overflowY: style.overflowY,
        canScroll: element.scrollHeight > element.clientHeight + 4,
        remaining: element.scrollHeight - element.clientHeight - element.scrollTop
      };
    })
    .filter(candidate =>
      candidate.canScroll
      && candidate.remaining > 2
      && candidate.rect.width > 0
      && candidate.rect.height > 0
      && candidate.rect.left < innerWidth
      && candidate.rect.right > 0
      && candidate.rect.top < innerHeight
      && candidate.rect.bottom > 0
      && candidate.overflowY !== "hidden"
      && candidate.overflowY !== "clip")
    .sort((left, right) => {
      const leftArea = left.rect.width * left.rect.height;
      const rightArea = right.rect.width * right.rect.height;
      return rightArea - leftArea;
    });

  const target = scrollableCandidates[0]?.element;
  if (target) {
    target.scrollTop = Math.min(target.scrollTop + Math.max(320, Math.floor(target.clientHeight * 0.75)), target.scrollHeight);
    return true;
  }

  const beforeY = window.scrollY;
  window.scrollBy({ top: Math.max(320, Math.floor(innerHeight * 0.75)), left: 0, behavior: "instant" });
  return window.scrollY !== beforeY;
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

export async function typeIntoVisibleTextField(page, options, value, label, timeoutMs = 10_000) {
  const deadline = Date.now() + timeoutMs;
  let point = null;

  while (Date.now() < deadline) {
    point = await page.evaluate(findVisibleTextInputPoint, options)
      ?? await page.evaluate(findVisibleControlPoint, options);
    if (point) {
      break;
    }

    await page.waitForTimeout(100);
  }

  if (!point) {
    const inputs = await page.evaluate(collectVisibleTextInputPoints);
    throw new Error(`No visible text field found for ${label}. Options=${JSON.stringify(options)} Inputs=${JSON.stringify(inputs)}`);
  }

  await typeIntoField(page, point, value);
}

export async function selectComboBoxItem(page, selectorAutomationId, expectedVisibleName, options = {}) {
  const expectedVisibleNames = Array.isArray(expectedVisibleName)
    ? expectedVisibleName
    : [expectedVisibleName];
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    const controlPoint = await findComboBoxControlPoint(page, selectorAutomationId);
    if (!controlPoint) {
      const debug = {
        comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
        navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
      };
      throw new Error(`No visible ComboBox found for '${selectorAutomationId}'. Debug=${JSON.stringify(debug)}`);
    }

    await page.mouse.click(controlPoint.x, controlPoint.y);
    try {
      await page.waitForFunction(
        input => {
          const expectedNames = input.expectedVisibleNames ?? [];
          return window.__salmoneggSmoke.collectVisibleComboBoxItems()
            .some(item => expectedNames.some(name => item.text === name || item.text.includes(name)));
        },
        { expectedVisibleNames },
        { timeout: Math.min(3_000, Math.max(250, deadline - Date.now())) });
      break;
    } catch (error) {
      lastError = error;
      await page.keyboard.press("Escape").catch(() => {});
      await page.waitForTimeout(500);
    }
  }

  if (!await page.evaluate(
    input => window.__salmoneggSmoke.collectVisibleComboBoxItems()
      .some(item => input.expectedVisibleNames.some(name => item.text === name || item.text.includes(name))),
    { expectedVisibleNames })) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
    };
    throw new Error(
      `ComboBox '${selectorAutomationId}' did not expose any item from ${JSON.stringify(expectedVisibleNames)}. `
      + `Debug=${JSON.stringify(debug)} Cause=${lastError?.message ?? lastError}`);
  }

  if (options.keyboardSelectVisibleItem === true) {
    const itemIndex = await page.evaluate(findVisibleComboBoxItemIndexByNames, expectedVisibleNames);
    if (itemIndex < 0) {
      throw new Error(`ComboBox '${selectorAutomationId}' items ${JSON.stringify(expectedVisibleNames)} disappeared before keyboard selection.`);
    }

    await commitVisibleComboBoxItemWithKeyboard(page, itemIndex);
    if (options.verifySelectionText !== false
      && !await waitForComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, 3_000)) {
      await selectComboBoxItemWithMouseFallback(page, selectorAutomationId, expectedVisibleNames);
    }
  } else {
    const point = await page.evaluate(findVisibleComboBoxItemPointByNames, expectedVisibleNames);
    if (!point) {
      throw new Error(`ComboBox '${selectorAutomationId}' items ${JSON.stringify(expectedVisibleNames)} disappeared before mouse selection.`);
    }

    await page.mouse.click(point.x, point.y);
  }

  if (options.verifySelectionText === false) {
    if (options.ensureSelectionWithFallback === true) {
      await selectComboBoxItemWithLocatorFallback(page, selectorAutomationId, expectedVisibleNames);
    }

    return;
  }

  if (!await waitForComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, 10_000)) {
    await selectComboBoxItemWithLocatorFallback(page, selectorAutomationId, expectedVisibleNames);
  }

  if (!await waitForComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, 2_000)) {
    await selectComboBoxItemWithKeyboardFallback(page, selectorAutomationId, expectedVisibleNames);
  }

  if (!await waitForComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, 5_000)) {
    const debug = {
      body: (await page.locator("body").innerText().catch(() => "")).slice(0, 2_000),
      comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
      selectedText: await page.evaluate(readComboBoxSelectionTextInPage, selectorAutomationId),
      interactive: await page.evaluate(collectVisibleInteractiveDebug)
    };
    throw new Error(
      `ComboBox '${selectorAutomationId}' did not show selected item ${JSON.stringify(expectedVisibleNames)}. `
      + `Debug=${JSON.stringify(debug)}`);
  }
}

async function findComboBoxControlPoint(page, selectorAutomationId) {
  return await page.evaluate(findVisibleControlPoint, { labels: [], automationIds: [selectorAutomationId] })
    ?? await page.evaluate(findStartComposerSelectorFallbackPoint, { labels: [], automationIds: [selectorAutomationId] })
    ?? await page.evaluate(findVisibleComboBoxControlPointBySelectorFallback, selectorAutomationId);
}

async function selectComboBoxItemWithLocatorFallback(page, selectorAutomationId, expectedVisibleNames) {
  const controlPoint = await findComboBoxControlPoint(page, selectorAutomationId);
  if (!controlPoint) {
    return;
  }

  await page.mouse.click(controlPoint.x, controlPoint.y);
  for (const expectedName of expectedVisibleNames) {
    const item = page.locator(".uno-comboboxitem:visible", { hasText: expectedName }).last();
    try {
      await item.click({ timeout: 3_000, force: true });
      return;
    } catch {
    }

    const text = page.getByText(expectedName, { exact: true }).last();
    try {
      await text.click({ timeout: 3_000, force: true });
      return;
    } catch {
    }
  }
}

async function selectComboBoxItemWithMouseFallback(page, selectorAutomationId, expectedVisibleNames) {
  const controlPoint = await findComboBoxControlPoint(page, selectorAutomationId);
  if (!controlPoint) {
    return;
  }

  await page.mouse.click(controlPoint.x, controlPoint.y);
  await page.waitForFunction(
    input => window.__salmoneggSmoke.collectVisibleComboBoxItems()
      .some(item => input.expectedVisibleNames.some(name => item.text === name || item.text.includes(name))),
    { expectedVisibleNames },
    { timeout: 3_000 }).catch(() => {});
  const point = await page.evaluate(findVisibleComboBoxItemPointByNames, expectedVisibleNames);
  if (point) {
    await page.mouse.click(point.x, point.y);
  }
}

async function selectComboBoxItemWithKeyboardFallback(page, selectorAutomationId, expectedVisibleNames) {
  const controlPoint = await findComboBoxControlPoint(page, selectorAutomationId);
  if (!controlPoint) {
    return;
  }

  await page.mouse.click(controlPoint.x, controlPoint.y);
  await page.waitForFunction(
    input => window.__salmoneggSmoke.collectVisibleComboBoxItems()
      .some(item => input.expectedVisibleNames.some(name => item.text === name || item.text.includes(name))),
    { expectedVisibleNames },
    { timeout: 3_000 }).catch(() => {});

  const itemIndex = await page.evaluate(findVisibleComboBoxItemIndexByNames, expectedVisibleNames);
  if (itemIndex < 0) {
    await page.keyboard.press("Escape").catch(() => {});
    return;
  }

  await commitVisibleComboBoxItemWithKeyboard(page, itemIndex);
}

async function commitVisibleComboBoxItemWithKeyboard(page, itemIndex) {
  await page.keyboard.press("Home");
  for (let index = 0; index < itemIndex; index += 1) {
    await page.keyboard.press("ArrowDown");
  }

  for (const key of ["Enter", "Enter", "Space"]) {
    await page.keyboard.press(key);
    if (await waitForVisibleComboBoxItemsClosed(page, 1_000)) {
      await page.waitForTimeout(100);
      return;
    }
  }
}

async function waitForComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const selectedText = await page.evaluate(readComboBoxSelectionTextInPage, selectorAutomationId);
    if (typeof selectedText === "string" && expectedVisibleNames.some(name => selectedText.includes(name))) {
      return true;
    }

    await page.waitForTimeout(100);
  }

  return false;
}

async function waitForVisibleComboBoxItemsClosed(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const visibleItems = await page.evaluate(() => window.__salmoneggSmoke.collectVisibleComboBoxItems().length);
    if (visibleItems === 0) {
      return true;
    }

    await page.waitForTimeout(100);
  }

  return false;
}

export async function selectComboBoxItemByIndex(
  page,
  selectorAutomationId,
  itemIndex,
  expectedVisibleNames,
  options = {}) {
  const deadline = Date.now() + 30_000;
  let lastError;
  let selected = false;

  while (Date.now() < deadline) {
    const controlPoint = await page.evaluate(findVisibleControlPoint, { labels: [], automationIds: [selectorAutomationId] })
      ?? await page.evaluate(findVisibleComboBoxControlPointBySelectorFallback, selectorAutomationId);
    if (!controlPoint) {
      const debug = {
        comboBoxes: await page.evaluate(collectVisibleComboBoxDebug),
        navigation: await page.evaluate(collectVisibleNavigationTargetDebug)
      };
      throw new Error(`No visible ComboBox found for '${selectorAutomationId}'. Debug=${JSON.stringify(debug)}`);
    }

    await page.mouse.click(controlPoint.x, controlPoint.y);
    try {
      await page.waitForFunction(
        index => window.__salmoneggSmoke.collectVisibleComboBoxItems().length > index,
        itemIndex,
        { timeout: Math.min(3_000, Math.max(250, deadline - Date.now())) });
      const point = await page.evaluate(findVisibleComboBoxItemPointByIndex, itemIndex);
      if (!point) {
        throw new Error(`ComboBox '${selectorAutomationId}' did not expose item index ${itemIndex}.`);
      }

      await page.mouse.click(point.x, point.y);
      selected = true;
      break;
    } catch (error) {
      lastError = error;
      await page.keyboard.press("Escape").catch(() => {});
      await page.waitForTimeout(500);
    }
  }

  if (!selected) {
    throw new Error(
      `ComboBox '${selectorAutomationId}' did not expose item index ${itemIndex}. `
      + `Cause=${lastError?.message ?? lastError}`);
  }

  if (options.verifySelectionText === false) {
    return;
  }

  await expectComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, selectorAutomationId);
}

export async function readComboBoxSelectionText(page, selectorAutomationId, label) {
  const deadline = Date.now() + 30_000;
  let lastText = null;

  while (Date.now() < deadline) {
    lastText = await page.evaluate(readComboBoxSelectionTextInPage, selectorAutomationId);
    if (typeof lastText === "string" && lastText.trim().length > 0) {
      return lastText.trim();
    }

    await page.waitForTimeout(100);
  }

  throw new Error(`Timed out reading ComboBox ${label}. LastText=${JSON.stringify(lastText)}`);
}

export async function expectComboBoxSelectionText(page, selectorAutomationId, expectedVisibleNames, label) {
  const expectedNames = Array.isArray(expectedVisibleNames)
    ? expectedVisibleNames
    : [expectedVisibleNames];
  const deadline = Date.now() + 10_000;
  let observedText = null;

  while (Date.now() < deadline) {
    observedText = await readComboBoxSelectionText(page, selectorAutomationId, label);
    if (expectedNames.some(name => observedText.includes(name))) {
      return;
    }

    await page.waitForTimeout(100);
  }

  throw new Error(
    `ComboBox ${label} did not show one of ${JSON.stringify(expectedNames)}. `
    + `Observed=${JSON.stringify(observedText)}`);
}

export async function readLocalTextFile(page, path) {
  return await page.evaluate(readLocalTextFileInPage, path);
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

function collectVisibleNavigationTargetMatchDebug(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  return Array.from(document.querySelectorAll("body *"))
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
      const clickable =
        element.closest(".uno-navigationviewitem")
        ?? element.closest(".uno-comboboxitem")
        ?? element.closest(".uno-button")
        ?? element.closest("[role='button']")
        ?? element.closest("button")
        ?? element;
      const clickableRect = clickable.getBoundingClientRect();
      return {
        text: text.slice(0, 160),
        aria,
        automationId,
        role: element.getAttribute("role") ?? "",
        className: element.className?.toString?.() ?? "",
        rect: {
          left: Math.round(rect.left),
          top: Math.round(rect.top),
          width: Math.round(rect.width),
          height: Math.round(rect.height)
        },
        clickableText: (clickable.textContent ?? "").trim().slice(0, 160),
        clickableAria: clickable.getAttribute("aria-label") ?? "",
        clickableRole: clickable.getAttribute("role") ?? "",
        clickableClassName: clickable.className?.toString?.() ?? "",
        clickableRect: {
          left: Math.round(clickableRect.left),
          top: Math.round(clickableRect.top),
          width: Math.round(clickableRect.width),
          height: Math.round(clickableRect.height)
        },
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
      && candidate.rect.top <= innerHeight)
    .sort((left, right) => {
      if (left.automationMatch !== right.automationMatch) {
        return left.automationMatch ? -1 : 1;
      }

      return (left.rect.width * left.rect.height) - (right.rect.width * right.rect.height);
    });
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

async function pressToggleSwitchByKeyboard(page, controlOptions, label) {
  for (let index = 0; index < 40; index += 1) {
    if (await page.evaluate(isFocusedControlInPage, controlOptions)) {
      await page.keyboard.press("Space");
      return;
    }

    await page.keyboard.press("Tab");
    await page.waitForTimeout(50);
  }

  throw new Error(`Could not focus toggle ${label} by keyboard. Options=${JSON.stringify(controlOptions)}`);
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
  const x = rect.left + rect.width / 2;
  const y = rect.top + rect.height / 2;

  // findVisibleControl admits a control whose top edge is on screen even when its center is not,
  // so a row straddling the fold yields a point below the viewport. Clicking it silently lands on
  // nothing: focus never moves and the caller times out waiting for it. Report no point instead,
  // so scrollToVisibleControl keeps scrolling rather than accepting an unclickable target.
  if (x < 0 || y < 0 || x > innerWidth || y > innerHeight) {
    return null;
  }

  return { x, y };
}

function isFocusedControlInPage(input) {
  const activeElement = document.activeElement;
  if (!activeElement) {
    return false;
  }

  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  return Boolean(control && (activeElement === control || control.contains(activeElement)));
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
      const textBoxContainer = element.closest(".uno-textbox,.uno-passwordbox,.uno-autosuggestbox");
      return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
        containerText: (textBoxContainer?.textContent ?? "").trim(),
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
        || labels.some(label => normalize(candidate.containerText).includes(label))
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
  const items = window.__salmoneggSmoke.collectVisibleComboBoxItems();
  return items.findIndex(item => item.text === expectedVisibleName);
}

function findVisibleComboBoxItemIndexByNames(expectedVisibleNames) {
  const items = window.__salmoneggSmoke.collectVisibleComboBoxItems();
  return items.findIndex(item => expectedVisibleNames.some(name => item.text === name || item.text.includes(name)));
}

function findVisibleComboBoxItemPointByIndex(itemIndex) {
  const item = window.__salmoneggSmoke.collectVisibleComboBoxItems()[itemIndex];
  if (!item) {
    return null;
  }

  const rect = item.clickRect ?? item.rect;
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function findVisibleComboBoxItemPointByNames(expectedVisibleNames) {
  const item = window.__salmoneggSmoke.collectVisibleComboBoxItems()
    .find(candidate => expectedVisibleNames.some(name => candidate.text === name || candidate.text.includes(name)));
  if (!item) {
    return null;
  }

  const rect = item.clickRect ?? item.rect;
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function findVisibleComboBoxControlPointBySelectorFallback(selectorAutomationId) {
  const selectorIndexByAutomationId = new Map([
    ["StartView.AgentSelector", 0],
    ["StartView.ModeSelector", 1],
    ["StartView.ProjectSelector", 2],
    ["Appearance.Theme", 0],
    ["Appearance.Backdrop", 1],
    ["GeneralSettings.Language", 0]
  ]);
  const selectorIndex = selectorIndexByAutomationId.get(selectorAutomationId);
  if (selectorIndex === undefined) {
    return null;
  }

  const comboBox = window.__salmoneggSmoke.collectVisibleComboBoxControls()[selectorIndex] ?? null;
  if (!comboBox) {
    return null;
  }

  return {
    x: comboBox.rect.left + comboBox.rect.width / 2,
    y: comboBox.rect.top + comboBox.rect.height / 2
  };
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
  const clickableStyle = getComputedStyle(clickable);
  const disabled =
    clickable.disabled === true
    || clickable.getAttribute("disabled") != null
    || clickable.getAttribute("aria-disabled") === "true"
    || clickable.className?.toString?.().toLowerCase().includes("disabled")
    || clickableStyle.pointerEvents === "none";

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

function readToggleSwitchStateInPage(input) {
  const labels = input.labels ?? [];
  const automationIds = input.automationIds ?? [];
  const control = window.__salmoneggSmoke.findVisibleControl(input, labels, automationIds);
  if (!control) {
    return { found: false, enabled: false, checked: null };
  }

  const resolveEffectiveOpacityInline = (element, boundary) => {
    let opacity = 1;
    for (let current = element; current && current !== document.body; current = current.parentElement) {
      const style = getComputedStyle(current);
      if (style.display === "none" || style.visibility === "hidden") {
        return 0;
      }

      const parsedOpacity = Number.parseFloat(style.opacity);
      if (Number.isFinite(parsedOpacity)) {
        opacity *= parsedOpacity;
      }

      if (current === boundary) {
        break;
      }
    }

    return opacity;
  };
  const resolveVisibleToggleLabelCheckedInline = toggle => {
    const activeLabels = Array.from(toggle.querySelectorAll("*"))
      .map(element => ({
        text: (element.textContent ?? "").trim(),
        opacity: resolveEffectiveOpacityInline(element, toggle)
      }))
      .filter(candidate => candidate.text.length > 0 && candidate.opacity > 0.5);

    const trueLabels = ["On", "Enabled", "开", "启用", "开启", "打开"];
    const falseLabels = ["Off", "Disabled", "关", "停用", "禁用", "关闭"];
    if (activeLabels.some(candidate => trueLabels.includes(candidate.text))) {
      return true;
    }

    if (activeLabels.some(candidate => falseLabels.includes(candidate.text))) {
      return false;
    }

    return null;
  };
  const resolveToggleCheckedInline = toggle => {
    if (typeof toggle.checked === "boolean") {
      return toggle.checked;
    }

    const ariaChecked = toggle.getAttribute("aria-checked");
    if (ariaChecked === "true") {
      return true;
    }

    if (ariaChecked === "false") {
      return false;
    }

    const descendant = toggle.querySelector("input[type='checkbox'],[aria-checked]");
    if (!descendant) {
      return resolveVisibleToggleLabelCheckedInline(toggle);
    }

    if (typeof descendant.checked === "boolean") {
      return descendant.checked;
    }

    const descendantAriaChecked = descendant.getAttribute("aria-checked");
    if (descendantAriaChecked === "true") {
      return true;
    }

    if (descendantAriaChecked === "false") {
      return false;
    }

    return resolveVisibleToggleLabelCheckedInline(toggle);
  };
  const resolveToggleElementInline = controlElement => {
    const candidates = [
      controlElement.matches("input[type='checkbox'],[role='switch'],[aria-checked],.uno-toggleswitch") ? controlElement : null,
      controlElement.closest("input[type='checkbox'],[role='switch'],[aria-checked],.uno-toggleswitch"),
      controlElement.querySelector("input[type='checkbox'],[role='switch'],[aria-checked],.uno-toggleswitch")
    ].filter(Boolean);

    return candidates.find(candidate => typeof resolveToggleCheckedInline(candidate) === "boolean")
      ?? candidates[0]
      ?? null;
  };
  const isDisabledInline = element => {
    for (let current = element; current && current !== document.body; current = current.parentElement) {
      if (current.disabled === true
        || current.getAttribute("disabled") != null
        || current.getAttribute("aria-disabled") === "true"
        || /(^|\s)(disabled|is-disabled|uno-disabled)(\s|$)/i.test(current.className?.toString?.() ?? "")) {
        return true;
      }
    }

    return false;
  };
  const hasPointerTargetInline = element => [element, ...element.querySelectorAll("*")]
    .some(candidate => {
      const rect = candidate.getBoundingClientRect();
      const style = getComputedStyle(candidate);
      return style.pointerEvents !== "none"
        && style.display !== "none"
        && style.visibility !== "hidden"
        && rect.width > 0
        && rect.height > 0;
    });

  const toggle = resolveToggleElementInline(control);
  if (!toggle) {
    return { found: true, enabled: false, checked: null, text: (control.textContent ?? "").trim() };
  }

  const disabled = isDisabledInline(toggle);
  const hasPointerTarget = hasPointerTargetInline(toggle);
  const rect = toggle.getBoundingClientRect();
  const point = window.__salmoneggSmoke.resolveToggleClickPoint(toggle)
    ?? (rect.width > 0 && rect.height > 0
      ? { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
      : null);

  return {
    found: true,
    enabled: !disabled,
    hasPointerTarget,
    checked: resolveToggleCheckedInline(toggle),
    text: (toggle.textContent ?? "").trim(),
    aria: toggle.getAttribute("aria-label") ?? "",
    x: point?.x ?? null,
    y: point?.y ?? null,
    automationId:
      toggle.getAttribute("data-automation-id")
      ?? toggle.getAttribute("data-automationid")
      ?? toggle.getAttribute("automationid")
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

function readComboBoxSelectionTextInPage(selectorAutomationId) {
  const resolveVisibleComboBoxControlBySelectorFallbackInline = automationId => {
    const selectorIndexByAutomationId = new Map([
      ["StartView.AgentSelector", 0],
      ["StartView.ModeSelector", 1],
      ["StartView.ProjectSelector", 2],
      ["Appearance.Theme", 0],
      ["Appearance.Backdrop", 1],
      ["GeneralSettings.Language", 0]
    ]);
    const selectorIndex = selectorIndexByAutomationId.get(automationId);
    if (selectorIndex === undefined) {
      return null;
    }

    return window.__salmoneggSmoke.collectVisibleComboBoxControls()[selectorIndex] ?? null;
  };

  const control = window.__salmoneggSmoke.findVisibleControl(
    { automationIds: [selectorAutomationId] },
    [],
    [selectorAutomationId])
    ?? resolveVisibleComboBoxControlBySelectorFallbackInline(selectorAutomationId)?.element;
  if (!control) {
    return null;
  }

  return (control.textContent ?? "").trim()
    || control.getAttribute("aria-label")
    || null;
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

function readLocalTextFileInPage(filePath) {
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
