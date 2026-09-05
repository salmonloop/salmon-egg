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
// Arrival is confirmed on the destination's own copy rendering: the caller supplies `bodyPattern`,
// which the smoke authors pin to strings that exist only on that section's page (usually its
// resw page title). That is the behaviour a user can observe - the content changed to the section
// they picked. The shell's *selection state* is deliberately not consulted: NavigationViewItem goes
// through Uno's generic semantic path (addSemanticElement), which publishes neither `aria-selected`
// nor `aria-current` on Skia WASM, so a predicate waiting for the item to report itself selected is
// a timeout with extra steps. If Uno starts mirroring selection (its dedicated item factories do),
// an explicit selection assertion can come back on top of the body check.
export async function navigateToSettingsSection(page, sectionTarget, bodyPattern, label) {
  const settingsNavigationTarget = {
    labels: ["设置", "Settings"],
    automationIds: ["SettingsItem"]
  };

  // Only enter the shell when we are not already in it. The section entries exist in the semantic
  // tree only while the settings shell is showing, so seeing the wanted one is proof enough - and
  // activating the Settings entry when it is already open costs more than a wasted click: it starts
  // a fresh navigation to the shell's default section, which lands asynchronously and can replace
  // the section page a caller has already navigated to and started using.
  if (!await scrollToVisibleControl(page, sectionTarget, 1_500)) {
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
  }

  await clickSectionUntilItSticks(page, sectionTarget, bodyPattern, label);
}

// The shell hop above lands asynchronously: activating the Settings entry navigates the shell to its
// default section, and that navigation can complete AFTER this section click, replacing the page we
// just asked for. The symptom is brutal to read - the section's own controls appear, the step that
// follows activates one of them, and only its effect goes missing, because by then the shell has
// swapped the page back. So arrival is confirmed, given a beat, and confirmed again; if the shell
// pulled the page out from under us, the section is simply activated again, which a navigation item
// tolerates because activating it twice is idempotent.
const sectionArrivalAttempts = 3;
const sectionSettleMs = 800;

async function clickSectionUntilItSticks(page, sectionTarget, bodyPattern, label) {
  let lastBodyText = "";
  for (let attempt = 1; attempt <= sectionArrivalAttempts; attempt += 1) {
    await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
    await page.waitForTimeout(sectionSettleMs);
    lastBodyText = await page.locator("body").innerText();
    if (bodyPattern.test(lastBodyText)) {
      return;
    }
  }

  throw new Error(
    `Navigated to ${label} but the shell replaced the page again each time `
    + `(${sectionArrivalAttempts} attempts). Last body text=${JSON.stringify(lastBodyText.slice(0, 400))}`);
}

function describeTarget(options) {
  const ids = options.automationIds ?? [];
  const labels = options.labels ?? [];
  return ids.length > 0 ? `automation id '${ids.join("', '")}'` : `label '${labels.join("', '")}'`;
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
