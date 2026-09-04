import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  waitForBodyText,
  waitForControlState
} from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-start-visibility-smoke.mjs");
const browser = await chromium.launch({ headless: true });

const expectedSuggestions = [
  { title: ["举报 AI 内容", "Report AI content"] },
  { title: ["推荐开发任务", "Recommend tasks"] },
  { title: ["解决最近报错", "Resolve recent errors"] }
];

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);

    // Each hero card must be present in the semantic DOM as a button carrying its localized
    // title as the accessible name - exactly what a screen reader announces. The match is by
    // name, not by the cards' AutomationId: those ids are x:Bind-fed, and Uno's BrowserWasm
    // semantic mapping bakes the automation id into the semantic element when the node is
    // created - before bindings resolve - and never revises it, so the DOM still carries the
    // button's x:Name fallback (HeroSuggestionButton). Static ids survive that bake; dynamic
    // ones never show. The accessible name, in contrast, is read live, and Windows UIA reads
    // the bound id live too, so the ViewModel ids remain valid for real consumers - they are
    // just unobservable through this tree. This smoke used to additionally assert title/
    // subtitle alpha and opacity against the pre-6.7 DOM; the semantic tree mirrors
    // structure, not styling, so visible-here means "present, unhidden, named".
    // (waitForControlState only reports nodes that are not hidden.)
    for (const suggestion of expectedSuggestions) {
      await waitForControlState(
        page,
        { labels: suggestion.title, automationIds: [], role: "button" },
        suggestion.title.join(" / "));
    }

    // Activating the report card explains itself instead of sending anything: the tip copy is
    // the behaviour, and it only exists after the card is activated. The activation is a real
    // trusted click on the semantic node, not a synthetic element.click(): CI (the role-pin
    // commit) showed the hero card's command never fires from the semantic activate path - the
    // same BrowserWasm gap the expander and the gamepad refresh button already documented -
    // while a trusted click through the same node the screen reader exposes is the closest
    // thing to the user's gesture.
    await page.locator('[role="button"][aria-label="Report AI content"]').click({ timeout: 15_000 });
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
