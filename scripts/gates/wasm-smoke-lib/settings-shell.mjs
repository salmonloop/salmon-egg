import {
  clickVisibleNavigationTarget,
  clickVisibleNavigationTargetUntilBodyText,
  collectVisibleNavigationTargetDebug,
  ensureVisibleNavigationTarget,
  findVisibleNavigationTargetPoint,
  waitForBodyText
} from "./ui-affordances.mjs";

// Reach a settings section and confirm arrival. Callers name the destination; this function chooses
// the route - click the section entry when the list is showing it, otherwise open the overflow
// affordance first. It used to also resize the window to 390x844 to *force* the overflow route and
// restore the size in a `finally`, while the settings-navigation smoke performed the same resize and
// overflow click again; two viewport mutations made the order load-bearing and neither belonged to the
// behaviour being checked. The caller now owns its viewport.
//
// Arrival is confirmed on the section reporting itself active - selected tab, heading, or aria-current -
// which is the state the navigation is supposed to produce. `bodyPattern` is still honoured for callers
// that assert on rendered copy, but it is no longer the only evidence: a prose alternation is satisfied
// by any one of its branches appearing anywhere, so it cannot distinguish "arrived" from "that word is
// also on this page".
//
// Activation stays rect-based because Uno's semantic nodes are a visually hidden overlay beside the
// canvas, so Playwright's actionability checks would refuse to click them. That is a property of the
// renderer. Keying on the icon glyph and on Uno's `.uno-button` class - which the overflow lookup used
// to do - was not; those promise nothing and are gone.
export async function navigateToSettingsSection(page, sectionTarget, bodyPattern, label) {
  const settingsNavigationTarget = {
    labels: ["设置", "Settings"],
    automationIds: ["SettingsItem"]
  };

  await ensureVisibleNavigationTarget(page, settingsNavigationTarget, {
    labels: ["Toggle sidebar"],
    automationIds: ["TitleBar.ToggleSidebar"]
  });
  await clickVisibleNavigationTargetUntilBodyText(
    page,
    settingsNavigationTarget,
    /常规|General|外观|Appearance|ACP Agent|ACP \/ Agent/,
    "settings shell",
    { keyboardFallback: true });

  if (!await page.evaluate(findVisibleNavigationTargetPoint, sectionTarget)) {
    await clickTopNavigationOverflow(page);
    await page.waitForFunction(findVisibleNavigationTargetPoint, sectionTarget, { timeout: 10_000 });
  }

  await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
  await waitForSectionActive(page, sectionTarget);
}

// The section is active once the shell marks it so. Names come from AutomationProperties.Name, which is
// localized, so accept any label the caller listed; createInstrumentedContext pins the locale, so in
// practice one branch is live and the rest are documentation.
async function waitForSectionActive(page, sectionTarget) {
  const names = (sectionTarget.labels ?? []).concat(sectionTarget.automationIds ?? []);
  if (names.length === 0) {
    return;
  }

  await page.waitForFunction(
    expected => Array.from(document.querySelectorAll("[aria-label]")).some(element => {
      const name = (element.getAttribute("aria-label") ?? "").trim();
      if (!expected.includes(name)) {
        return false;
      }

      const role = element.getAttribute("role") ?? "";
      return element.getAttribute("aria-selected") === "true"
        || element.getAttribute("aria-current") !== null
        || role.startsWith("heading")
        || /^h[1-6]$/i.test(element.tagName);
    }),
    names,
    { timeout: 15_000 })
    .catch(() => {
      throw new Error(
        `Settings section did not report itself active. Expected one of ${JSON.stringify(names)} to be a `
        + "selected tab, a heading, or aria-current in the settings shell.");
    });
}

export async function clickTopNavigationOverflowTargetUntilBodyText(page, targetOptions, pattern, label) {
  const deadline = Date.now() + 30_000;
  let lastError;

  while (Date.now() < deadline) {
    try {
      await clickTopNavigationOverflow(page);
      await page.waitForFunction(
        findVisibleNavigationTargetPoint,
        targetOptions,
        { timeout: Math.min(1_500, Math.max(250, deadline - Date.now())) });
      const point = await page.evaluate(findVisibleNavigationTargetPoint, targetOptions);
      if (!point) {
        throw new Error(`Target disappeared before click: ${JSON.stringify(targetOptions)}`);
      }

      await page.mouse.click(point.x, point.y);
      await waitForBodyText(page, pattern, label, Math.min(3_000, Math.max(250, deadline - Date.now())));
      return;
    } catch (error) {
      lastError = error;
      await page.keyboard.press("Escape").catch(() => {});
      await page.waitForTimeout(250);
    }
  }

  const candidates = await page.evaluate(collectVisibleNavigationTargetDebug);
  throw new Error(
    `Settings overflow menu did not activate target ${JSON.stringify(targetOptions)}. `
    + `Last error: ${lastError?.message ?? lastError}. Candidates=${JSON.stringify(candidates)}`);
}

export async function clickTopNavigationOverflow(page) {
  await page.waitForFunction(findTopNavigationOverflowPoint, null, { timeout: 30_000 });
  const point = await page.evaluate(findTopNavigationOverflowPoint);

  if (!point) {
    const candidates = await page.evaluate(collectTopNavigationButtonCandidateDebug);
    throw new Error(`Settings overflow button was not visible. Candidates: ${JSON.stringify(candidates)}`);
  }

  await page.mouse.click(point.x, point.y);
}

// Identify the overflow affordance by its accessible name, which is the contract Uno publishes and a
// screen reader consumes. The previous matcher keyed on the Segoe Fluent private-use glyphs \uE10C /
// \uE712 and on Uno's internal `.uno-button` class: a different icon or a renamed class broke the gate
// with no change in behaviour, and neither is anything the app promises.
function findTopNavigationOverflowPoint() {
  const target = window.__salmoneggSmoke.collectTopNavigationButtonCandidates().find(candidate =>
    /^(more|overflow)$/i.test((candidate.aria ?? "").trim())
    || /^(more|overflow)$/i.test((candidate.title ?? "").trim()))?.element;
  if (!target) {
    return null;
  }

  const rect = target.getBoundingClientRect();
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2
  };
}

function collectTopNavigationButtonCandidateDebug() {
  return window.__salmoneggSmoke.collectTopNavigationButtonCandidates().map(candidate => ({
    text: candidate.text,
    aria: candidate.aria,
    title: candidate.title,
    role: candidate.role,
    className: candidate.className,
    rect: candidate.rect
  }));
}
