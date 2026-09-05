import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";
import {
  clickVisibleControl,
  clickVisibleControlWithTrustedPointer,
  readControlState,
  scrollToVisibleControl,
  waitForControlEnabledState,
  waitForControlState
} from "./wasm-smoke-lib/ui-affordances.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-start-visibility-smoke.mjs");
const browser = await chromium.launch({ headless: true });

const expectedSuggestions = [
  { title: ["举报 AI 内容", "Report AI content"] },
  { title: ["推荐开发任务", "Recommend tasks"] },
  { title: ["解决最近报错", "Resolve recent errors"] }
];

const reportCard = { labels: ["举报 AI 内容", "Report AI content"], automationIds: [], role: "button" };
const noticeAcknowledgement = { labels: ["确定", "OK"], automationIds: [], role: "button" };

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

    // Activating the report card must answer the user with a modal notice they then acknowledge -
    // that round-trip is the behaviour, and it is asserted the way a user experiences it, not by
    // reading the notice's copy: Skia paints the ContentDialog into the canvas without mirroring
    // it into the DOM/semantic tree, so its text is structurally unobservable here and any
    // text-based assertion would be pinning a renderer detail. What *is* observable - on this
    // renderer and on a DOM one - is modality: while the notice is up the semantic tree reports
    // the page's controls disabled, the notice's own OK button appears in it, and acknowledging
    // that button hands control back. The activation itself goes through the node's real center:
    // the semantic activate path (element.click()) does not reach the XAML command - the same
    // BrowserWasm gap the expander and the gamepad refresh button already documented - while a
    // raw locator can never pass Playwright's actionability check against a semantic node (no
    // baked `role` attribute, pointer-events: none). A trusted pointer at the center Uno reports
    // is the user's actual gesture path through the canvas hit test.
    await activateReportCardUntilNoticeOpens(page, "report suggestion card");
    await waitForControlEnabledState(page, reportCard, false, "report suggestion card behind the notice");

    // The OK button's automation peer finishes wiring shortly after the node first surfaces, so an
    // invoke fired at first sight can be swallowed. Acknowledging is retried against the visible
    // effect (the page coming back) rather than against the invoke's own return value, which is
    // always true even when the dialog stays up.
    const acknowledgeDeadline = Date.now() + 30_000;
    const isCardEnabled = async () => (await readControlState(page, reportCard)).enabled;
    while (!(await isCardEnabled())) {
      if (Date.now() > acknowledgeDeadline) {
        throw new Error("The notice acknowledgement did not return the page within 30s.");
      }

      // Only press while the button is still there. The notice can be gone a beat before the page
      // reports itself enabled again, and pressing into that gap fails on a control that has already
      // done its job - which reads as "OK never appeared" and hides what actually happened.
      if (await scrollToVisibleControl(page, noticeAcknowledgement, 1_000)) {
        await clickVisibleControl(page, noticeAcknowledgement);
      }

      await page.waitForTimeout(500);
    }

    // The affordance must survive its own use: a second activation reopens the notice instead of
    // leaving the card dead after one acknowledgement.
    await activateReportCardUntilNoticeOpens(page, "report suggestion card again");

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM start visibility smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}

// Retried against the notice appearing, not against the click reporting success. A trusted pointer
// goes through the canvas hit test, and that can miss: the card's reported centre is right, the click
// is delivered, and the command still does not run - measured on CI, where one run in several opened
// no notice at all while the same commit passed locally twice and on the previous CI run. Pressing
// again is what a user does. The card's own enabled state decides whether to press: once the notice
// is up the page reports its controls disabled, so a card that has gone disabled means the notice is
// on its way and the only thing left to do is wait for it.
async function activateReportCardUntilNoticeOpens(page, label) {
  const attempts = 3;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    if (await scrollToVisibleControl(page, noticeAcknowledgement, 1_000)) {
      return;
    }

    if ((await readControlState(page, reportCard)).enabled) {
      await clickVisibleControlWithTrustedPointer(page, reportCard, label);
    }

    if (await scrollToVisibleControl(page, noticeAcknowledgement, 8_000)) {
      return;
    }
  }

  throw new Error(
    `Activating ${label} did not open its notice in ${attempts} attempts. `
    + `Card=${JSON.stringify(await readControlState(page, reportCard))}`);
}
