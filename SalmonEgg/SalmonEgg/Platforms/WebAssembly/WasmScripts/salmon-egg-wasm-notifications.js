// Browser Notification API bridge. Kept deliberately thin: every decision that is not a raw browser
// fact (whether to notify at all, per-turn identity, wording) belongs to the shared layers.

// "default" means the user has not been asked yet, so a prompt is still possible.
export function getPermission() {
    if (typeof Notification !== "function") {
        return "unsupported";
    }

    return Notification.permission;
}

export async function requestPermission() {
    if (typeof Notification !== "function") {
        return "unsupported";
    }

    if (Notification.permission !== "default") {
        // Asking again after a decision is a no-op in every browser, and Safari rejects it outright.
        return Notification.permission;
    }

    try {
        return await Notification.requestPermission();
    } catch {
        // Older Safari only supports the callback form of requestPermission.
        return await new Promise(resolve => {
            try {
                Notification.requestPermission(resolve);
            } catch {
                resolve("denied");
            }
        });
    }
}

export function showNotification(notificationId, title, body) {
    if (typeof Notification !== "function" || Notification.permission !== "granted") {
        return false;
    }

    try {
        // The tag is the browser's own replace key: re-notifying one turn replaces its notification
        // instead of stacking a second one.
        new Notification(title, { body, tag: notificationId });
        return true;
    } catch {
        // Chrome on Android throws for page-created Notifications and requires a service worker.
        return false;
    }
}
