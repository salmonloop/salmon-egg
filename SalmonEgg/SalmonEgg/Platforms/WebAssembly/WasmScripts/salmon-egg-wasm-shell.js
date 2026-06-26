export async function copyToClipboard(text) {
    const value = text ?? "";
    if (navigator?.clipboard?.writeText) {
        try {
            await navigator.clipboard.writeText(value);
            return true;
        } catch {
            // Some browsers reject Clipboard API writes outside secure or activated contexts.
        }
    }

    return copyWithSelectionFallback(value);
}

export async function readClipboardText() {
    if (!navigator?.clipboard?.readText) {
        return null;
    }

    try {
        const text = await navigator.clipboard.readText();
        return typeof text === "string" ? text : null;
    } catch {
        return null;
    }
}

function copyWithSelectionFallback(text) {
    if (!document?.body || typeof document.execCommand !== "function") {
        return false;
    }

    const textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.setAttribute("readonly", "");
    textArea.style.position = "fixed";
    textArea.style.left = "-9999px";
    textArea.style.top = "0";
    textArea.style.opacity = "0";

    const selection = document.getSelection?.();
    const selectedRange = selection && selection.rangeCount > 0
        ? selection.getRangeAt(0)
        : null;

    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    try {
        return document.execCommand("copy");
    } catch {
        return false;
    } finally {
        textArea.remove();
        if (selectedRange && selection) {
            selection.removeAllRanges();
            selection.addRange(selectedRange);
        }
    }
}
