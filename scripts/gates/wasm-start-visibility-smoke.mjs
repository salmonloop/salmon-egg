import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  clickVisibleControl,
  waitForBodyText,
  waitForControlState
} from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-start-visibility-smoke.mjs");
const browser = await chromium.launch({ headless: true });

const expectedSuggestions = [
  {
    automationId: "StartView.Suggestion.ReportGuidance",
    title: ["举报 AI 内容", "Report AI content"]
  },
  {
    automationId: "StartView.Suggestion.RecommendTasks",
    title: ["推荐开发任务", "Recommend tasks"]
  },
  {
    automationId: "StartView.Suggestion.ResolveErrors",
    title: ["解决最近报错", "Resolve recent errors"]
  }
];

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);

    // Each hero card must be present in the semantic DOM under its AutomationId and carry its
    // localized title as the accessible name - exactly what a screen reader announces. This
    // smoke used to additionally assert title/subtitle alpha and opacity against the pre-6.7
    // DOM; the semantic tree mirrors structure, not styling, so visible-here means "present,
    // unhidden, named". (waitForControlState only reports nodes that are not hidden.)
    for (const suggestion of expectedSuggestions) {
      const state = await waitForControlState(
        page,
        { automationIds: [suggestion.automationId], labels: [] },
        suggestion.automationId);
      if (!suggestion.title.includes(state.aria)) {
        throw new Error(
          `${suggestion.automationId} accessible name was '${state.aria}', `
          + `expected one of ${JSON.stringify(suggestion.title)}.`);
      }
    }

    // Activating the report card explains itself instead of sending anything: the tip copy is
    // the behaviour, and it only exists after the card is activated.
    await clickVisibleControl(
      page,
      { automationIds: ["StartView.Suggestion.ReportGuidance"], labels: [] });
    await waitForBodyText(
      page,
      /这张提示卡本身只是说明，不会发送举报|This tip card only explains the path and cannot send a report/,
      "report guidance tip");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM start visibility smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}
