export function supportsExternalElicitation() {
    return typeof window.open === "function" && typeof navigator.userActivation?.isActive === "boolean";
}

export function openExternalElicitation(value) {
    if (!supportsExternalElicitation() || !navigator.userActivation.isActive) {
        return false;
    }

    const target = new URL(value);
    const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(target.hostname);
    if (target.origin === window.location.origin || target.username || target.password ||
        (target.protocol !== "https:" && !(target.protocol === "http:" && loopback))) {
        return false;
    }

    // Native noopener creates an unrelated browsing context; assigning opener=null after opening
    // would still allow named-window access after a redirect back to our origin. With noopener,
    // browsers return null for both success and a blocked popup. True means dispatched, not opened.
    window.open(target.href, "_blank", "noopener,noreferrer");
    return true;
}

export function observeDocumentActivity(sink) {
    let disposed = false;
    let lastActive;
    const report = () => {
        if (disposed) return;
        const active = document.visibilityState === "visible" && document.hasFocus();
        if (active === lastActive) return;
        lastActive = active;
        sink(active);
    };
    document.addEventListener("visibilitychange", report);
    window.addEventListener("focus", report);
    window.addEventListener("blur", report);
    report();
    return {
        dispose() {
            if (disposed) return;
            disposed = true;
            document.removeEventListener("visibilitychange", report);
            window.removeEventListener("focus", report);
            window.removeEventListener("blur", report);
        }
    };
}

export function stopObservingDocumentActivity(subscription) {
    subscription.dispose();
}

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
