import {
  clickVisibleNavigationTarget,
  clickVisibleNavigationTargetUntilBodyText,
  collectVisibleNavigationTargetDebug,
  ensureVisibleNavigationTarget,
  findVisibleNavigationTargetPoint,
  waitForBodyText
} from "./ui-affordances.mjs";

export async function navigateToSettingsSection(page, sectionTarget, bodyPattern, label) {
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
    /常规|General|外观|Appearance|ACP Agent|ACP \/ Agent/,
    "settings shell");

  if (await page.evaluate(findVisibleNavigationTargetPoint, sectionTarget)) {
    await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
    return;
  }

  await page.setViewportSize({ width: 390, height: 844 });
  await waitForBodyText(page, /常规|General|外观|Appearance|ACP Agent|ACP \/ Agent/, "settings shell at mobile viewport");
  await clickTopNavigationOverflow(page);
  await clickVisibleNavigationTargetUntilBodyText(page, sectionTarget, bodyPattern, label);
  await page.setViewportSize({ width: 1280, height: 900 });
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

function findTopNavigationOverflowPoint() {
  const explicitTarget = window.__salmoneggSmoke.collectTopNavigationButtonCandidates().find(candidate =>
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
  return window.__salmoneggSmoke.collectTopNavigationButtonCandidates().map(candidate => ({
    text: candidate.text,
    aria: candidate.aria,
    title: candidate.title,
    role: candidate.role,
    className: candidate.className,
    rect: candidate.rect
  }));
}
