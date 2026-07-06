import { chromium } from "playwright";
import {
  normalizeBaseUrl,
  createInstrumentedContext,
  openApp,
  assertNoFatalConsoleMessages
} from "./wasm-smoke-lib/browser-app.mjs";

const baseUrl = normalizeBaseUrl(process.argv[2], "wasm-start-visibility-smoke.mjs");
const browser = await chromium.launch({ headless: true });

const expectedSuggestions = [
  {
    automationId: "StartView.Suggestion.AnalyzeCodebase",
    title: ["分析代码库", "Analyze codebase"],
    subtitle: ["深入理解项目架构与逻辑", "Understand the project architecture and logic"]
  },
  {
    automationId: "StartView.Suggestion.RecommendTasks",
    title: ["推荐开发任务", "Recommend tasks"],
    subtitle: ["明确接下来该做什么", "Clarify what to work on next"]
  },
  {
    automationId: "StartView.Suggestion.ResolveErrors",
    title: ["解决最近报错", "Resolve recent errors"],
    subtitle: ["提交错误日志让我看看", "Share the error log for analysis"]
  }
];

try {
  const { context, page, fatalConsoleMessages } = await createInstrumentedContext(browser);

  try {
    await openApp(page, baseUrl);
    await page.waitForTimeout(500);

    const projection = await page.evaluate(expected => {
      const normalize = value => (value ?? "").replace(/\u200B/g, "").trim();
      const parseAlpha = color => {
        const rgba = color.match(/rgba?\(([^)]+)\)/i);
        if (!rgba) {
          return 1;
        }

        const parts = rgba[1].split(",").map(part => part.trim());
        return parts.length >= 4 ? Number(parts[3]) : 1;
      };
      const isVisible = element => {
        const style = getComputedStyle(element);
        const rect = element.getBoundingClientRect();
        return rect.width > 0
          && rect.height > 0
          && style.display !== "none"
          && style.visibility !== "hidden"
          && Number(style.opacity || "1") > 0;
      };
      const findVisibleText = (root, candidates) => {
        const normalizedCandidates = candidates.map(normalize);
        const elements = [root, ...root.querySelectorAll("*")];

        for (const element of elements) {
          if (!isVisible(element)) {
            continue;
          }

          const text = normalize(element.textContent);
          if (!normalizedCandidates.some(candidate => candidate && text.includes(candidate))) {
            continue;
          }

          const style = getComputedStyle(element);
          const rect = element.getBoundingClientRect();
          return {
            text,
            color: style.color,
            opacity: Number(style.opacity || "1"),
            alpha: parseAlpha(style.color),
            rect: {
              left: rect.left,
              top: rect.top,
              width: rect.width,
              height: rect.height
            }
          };
        }

        return null;
      };

      return expected.map(item => {
        const button = document.querySelector(`[aria-label="${item.automationId}"]`);
        if (!button) {
          return {
            automationId: item.automationId,
            missing: true
          };
        }

        const rect = button.getBoundingClientRect();
        return {
          automationId: item.automationId,
          buttonText: normalize(button.textContent),
          buttonRect: {
            left: rect.left,
            top: rect.top,
            width: rect.width,
            height: rect.height
          },
          title: findVisibleText(button, item.title),
          subtitle: findVisibleText(button, item.subtitle)
        };
      });
    }, expectedSuggestions);

    const failures = [];
    for (const item of projection) {
      if (item.missing) {
        failures.push(`${item.automationId} was not rendered.`);
        continue;
      }

      if (!item.title) {
        failures.push(`${item.automationId} did not render a visible title. State=${JSON.stringify(item)}`);
      }

      if (!item.subtitle) {
        failures.push(`${item.automationId} did not render a visible subtitle. State=${JSON.stringify(item)}`);
      } else if (item.subtitle.alpha <= 0.05 || item.subtitle.opacity <= 0.05) {
        failures.push(`${item.automationId} subtitle resolved transparent text. State=${JSON.stringify(item.subtitle)}`);
      }
    }

    if (failures.length > 0) {
      throw new Error(`Start suggestion visibility failed:\n${failures.join("\n")}`);
    }

    assertNoFatalConsoleMessages(fatalConsoleMessages);
    console.log("WASM start visibility smoke passed");
  } finally {
    await context.close();
  }
} finally {
  await browser.close();
}
