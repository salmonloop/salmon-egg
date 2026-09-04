import {
  clickVisibleNavigationTargetUntilBodyText,
  clickVisibleControl,
  collectVisibleNavigationTargetDebug,
  ensureVisibleNavigationTarget,
  scrollToVisibleControl,
  waitForBodyText,
  waitForControlState
} from "./ui-affordances.mjs";

// Reach a settings section and confirm arrival. Callers name the destination; this function chooses
// the route - activate the section entry when the list is showing it, otherwise open the overflow
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
    "settings shell");

  if (!await scrollToVisibleControl(page, sectionTarget, 3_000)) {
    await clickTopNavigationOverflow(page);
    await waitForControlState(page, sectionTarget, describeTarget(sectionTarget), 10_000);
  }

  await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
  await waitForSectionActive(page, sectionTarget);
}

function describeTarget(options) {
  const ids = options.automationIds ?? [];
  const labels = options.labels ?? [];
  return ids.length > 0 ? `automation id '${ids.join("', '")}'` : `label '${labels.join("', '")}'`;
}

// The section is active once the shell marks it so. Names come from AutomationProperties.Name, which is
// localized, so accept any label the caller listed; createInstrumentedContext pins the locale, so in
// practice one branch is live and the rest are documentation. The semantic DOM mirrors the selection
// state (`aria-selected` on navigation items), which is exactly the state this predicate looks for.
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
  await clickTopNavigationOverflow(page);
  // The overflow panel expands asynchronously; activation polls for the target in the semantic DOM,
  // so no separate wait is needed before this.
  await clickVisibleNavigationTargetUntilBodyText(page, targetOptions, pattern, label);
}

// Open the top navigation's overflow affordance ("..."). The semantic route matches it by accessible
// name, which is the contract a screen reader consumes; keying on the Segoe Fluent private-use glyphs
//  /  or on Uno's internal `.uno-button` class - which the rectangle fallback below still
// tolerates - promised nothing and remains only as a last resort. The rectangle fallback exists at
// all because the overflow button is generated from Uno's template rather than by the app: its name
// is not in our resw, and whether the semantic tree mirrors it at every pane width is not yet proven
// end to end. Once the semantic route is confirmed on CI, the fallback and the candidate collector
// behind it can go.
export async function clickTopNavigationOverflow(page) {
  const semanticNodes = await page.evaluate(collectVisibleNavigationTargetDebug);
  const overflowNode = semanticNodes.find(node =>
    /more|overflow|更多|溢出/i.test((node.aria ?? "").trim())
    || /more|overflow|更多|溢出/i.test((node.text ?? "").trim()));
  if (overflowNode?.aria) {
    await clickVisibleControl(page, { labels: [overflowNode.aria.trim()], automationIds: [] });
    return;
  }

  await page.waitForFunction(findTopNavigationOverflowPoint, null, { timeout: 30_000 });
  const point = await page.evaluate(findTopNavigationOverflowPoint);

  if (!point) {
    const candidates = await page.evaluate(collectTopNavigationButtonCandidateDebug);
    throw new Error(
      `Settings overflow button was neither in the semantic DOM nor visible as a top button. `
      + `Semantic nodes: ${JSON.stringify(semanticNodes)}. Button candidates: ${JSON.stringify(candidates)}`);
  }

  await page.mouse.click(point.x, point.y);
}

// Rectangle fallback: find the overflow button the way the old full-DOM scanner did, by its
// accessible name inside the top strip of the viewport. See the comment on clickTopNavigationOverflow
// for why this path survives.
function findTopNavigationOverflowPoint() {
  const target = window.__salmoneggSmoke.collectTopNavigationButtonCandidates().find(candidate =>
    /^(more|overflow|更多)$/i.test((candidate.aria ?? "").trim())
    || /^(more|overflow|更多)$/i.test((candidate.title ?? "").trim()))?.element;
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
