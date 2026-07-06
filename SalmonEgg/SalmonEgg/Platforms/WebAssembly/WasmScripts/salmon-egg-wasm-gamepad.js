export function dispatchKeyboardNavigation(key, code) {
    if (!document?.body || typeof KeyboardEvent !== "function") {
        return false;
    }

    const target = document.activeElement && document.activeElement !== document.body
        ? document.activeElement
        : document.body;

    const options = {
        key,
        code,
        bubbles: true,
        cancelable: true,
        composed: true
    };

    target.dispatchEvent(new KeyboardEvent("keydown", options));
    target.dispatchEvent(new KeyboardEvent("keyup", options));
    return true;
}
