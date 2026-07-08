#!/usr/bin/env python3
import argparse
import ctypes
import os
import sys
import time


IS_VIEWABLE = 2
XA_CARDINAL = 6
ANY_PROPERTY_TYPE = 0
Z_PIXMAP = 2
REVERT_TO_PARENT = 2
CURRENT_TIME = 0
XK_TAB = 0xFF09


class XWindowAttributes(ctypes.Structure):
    _fields_ = [
        ("x", ctypes.c_int),
        ("y", ctypes.c_int),
        ("width", ctypes.c_int),
        ("height", ctypes.c_int),
        ("border_width", ctypes.c_int),
        ("depth", ctypes.c_int),
        ("visual", ctypes.c_void_p),
        ("root", ctypes.c_ulong),
        ("class", ctypes.c_int),
        ("bit_gravity", ctypes.c_int),
        ("win_gravity", ctypes.c_int),
        ("backing_store", ctypes.c_int),
        ("backing_planes", ctypes.c_ulong),
        ("backing_pixel", ctypes.c_ulong),
        ("save_under", ctypes.c_int),
        ("colormap", ctypes.c_ulong),
        ("map_installed", ctypes.c_int),
        ("map_state", ctypes.c_int),
        ("all_event_masks", ctypes.c_long),
        ("your_event_mask", ctypes.c_long),
        ("do_not_propagate_mask", ctypes.c_long),
        ("override_redirect", ctypes.c_int),
        ("screen", ctypes.c_void_p),
    ]


def configure_x11():
    x11 = ctypes.CDLL("libX11.so.6")

    x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
    x11.XOpenDisplay.restype = ctypes.c_void_p
    x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
    x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
    x11.XDefaultRootWindow.restype = ctypes.c_ulong
    x11.XQueryTree.argtypes = [
        ctypes.c_void_p,
        ctypes.c_ulong,
        ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)),
        ctypes.POINTER(ctypes.c_uint),
    ]
    x11.XQueryTree.restype = ctypes.c_int
    x11.XGetWindowAttributes.argtypes = [
        ctypes.c_void_p,
        ctypes.c_ulong,
        ctypes.POINTER(XWindowAttributes),
    ]
    x11.XGetWindowAttributes.restype = ctypes.c_int
    x11.XFetchName.argtypes = [
        ctypes.c_void_p,
        ctypes.c_ulong,
        ctypes.POINTER(ctypes.c_char_p),
    ]
    x11.XFetchName.restype = ctypes.c_int
    x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
    x11.XInternAtom.restype = ctypes.c_ulong
    x11.XGetWindowProperty.argtypes = [
        ctypes.c_void_p,
        ctypes.c_ulong,
        ctypes.c_ulong,
        ctypes.c_long,
        ctypes.c_long,
        ctypes.c_int,
        ctypes.c_ulong,
        ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_int),
        ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.POINTER(ctypes.c_ubyte)),
    ]
    x11.XGetWindowProperty.restype = ctypes.c_int
    x11.XFree.argtypes = [ctypes.c_void_p]
    x11.XGetImage.argtypes = [
        ctypes.c_void_p,
        ctypes.c_ulong,
        ctypes.c_int,
        ctypes.c_int,
        ctypes.c_uint,
        ctypes.c_uint,
        ctypes.c_ulong,
        ctypes.c_int,
    ]
    x11.XGetImage.restype = ctypes.c_void_p
    x11.XGetPixel.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
    x11.XGetPixel.restype = ctypes.c_ulong
    x11.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
    x11.XSetInputFocus.argtypes = [
        ctypes.c_void_p,
        ctypes.c_ulong,
        ctypes.c_int,
        ctypes.c_ulong,
    ]
    x11.XGetInputFocus.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_int),
    ]
    x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
    x11.XKeysymToKeycode.restype = ctypes.c_uint
    try:
        x11.XDestroyImage.argtypes = [ctypes.c_void_p]
    except AttributeError:
        pass
    return x11


def configure_xtst():
    xtst = ctypes.CDLL("libXtst.so.6")
    xtst.XTestQueryExtension.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(ctypes.c_int),
        ctypes.POINTER(ctypes.c_int),
        ctypes.POINTER(ctypes.c_int),
        ctypes.POINTER(ctypes.c_int),
    ]
    xtst.XTestQueryExtension.restype = ctypes.c_int
    xtst.XTestFakeKeyEvent.argtypes = [
        ctypes.c_void_p,
        ctypes.c_uint,
        ctypes.c_int,
        ctypes.c_ulong,
    ]
    xtst.XTestFakeKeyEvent.restype = ctypes.c_int
    return xtst


def fetch_name(x11, display, window):
    raw = ctypes.c_char_p()
    if x11.XFetchName(display, window, ctypes.byref(raw)) == 0 or not raw.value:
        return ""

    try:
        return raw.value.decode("utf-8", errors="replace")
    finally:
        x11.XFree(raw)


def fetch_window_pid(x11, display, window, atom):
    if atom == 0:
        return None

    actual_type = ctypes.c_ulong()
    actual_format = ctypes.c_int()
    item_count = ctypes.c_ulong()
    bytes_after = ctypes.c_ulong()
    prop = ctypes.POINTER(ctypes.c_ubyte)()

    status = x11.XGetWindowProperty(
        display,
        window,
        atom,
        0,
        1,
        0,
        ANY_PROPERTY_TYPE,
        ctypes.byref(actual_type),
        ctypes.byref(actual_format),
        ctypes.byref(item_count),
        ctypes.byref(bytes_after),
        ctypes.byref(prop),
    )
    if status != 0 or not prop:
        return None

    try:
        if actual_type.value != XA_CARDINAL or actual_format.value != 32 or item_count.value < 1:
            return None

        return ctypes.cast(prop, ctypes.POINTER(ctypes.c_ulong))[0]
    finally:
        x11.XFree(prop)


def enumerate_windows(x11, display, root):
    result = []
    stack = [root]
    while stack:
        current = stack.pop()
        root_return = ctypes.c_ulong()
        parent_return = ctypes.c_ulong()
        children = ctypes.POINTER(ctypes.c_ulong)()
        child_count = ctypes.c_uint()
        if x11.XQueryTree(
            display,
            current,
            ctypes.byref(root_return),
            ctypes.byref(parent_return),
            ctypes.byref(children),
            ctypes.byref(child_count),
        ) == 0:
            continue

        try:
            for index in range(child_count.value):
                child = children[index]
                result.append(child)
                stack.append(child)
        finally:
            if children:
                x11.XFree(children)

    return result


def get_viewable_windows(x11, display, root, target_pid, min_width, min_height):
    pid_atom = x11.XInternAtom(display, b"_NET_WM_PID", 1)
    candidates = []
    for window in enumerate_windows(x11, display, root):
        attributes = XWindowAttributes()
        if x11.XGetWindowAttributes(display, window, ctypes.byref(attributes)) == 0:
            continue

        if attributes.map_state != IS_VIEWABLE:
            continue

        if attributes.width < min_width or attributes.height < min_height:
            continue

        name = fetch_name(x11, display, window)
        window_pid = fetch_window_pid(x11, display, window, pid_atom)
        score = attributes.width * attributes.height
        if window_pid == target_pid:
            score += 10_000_000_000
        if "SalmonEgg" in name or "Salmon Egg" in name:
            score += 1_000_000_000

        candidates.append((score, window, attributes, name, window_pid))

    candidates.sort(key=lambda item: item[0], reverse=True)
    return candidates


def sample_distinct_pixels(x11, display, window, width, height):
    image = x11.XGetImage(
        display,
        window,
        0,
        0,
        width,
        height,
        ctypes.c_ulong(-1).value,
        Z_PIXMAP,
    )
    if not image:
        return 0

    try:
        samples = set()
        columns = min(15, max(1, width))
        rows = min(15, max(1, height))
        for row in range(rows):
            y = min(height - 1, int((row + 0.5) * height / rows))
            for column in range(columns):
                x = min(width - 1, int((column + 0.5) * width / columns))
                samples.add(int(x11.XGetPixel(image, x, y)))
                if len(samples) >= 3:
                    return len(samples)

        return len(samples)
    finally:
        if hasattr(x11, "XDestroyImage"):
            x11.XDestroyImage(image)


def is_window_or_descendant(x11, display, ancestor, candidate):
    if candidate == ancestor:
        return True

    return candidate in enumerate_windows(x11, display, ancestor)


def describe_focus_target(x11, display):
    focus = ctypes.c_ulong()
    revert_to = ctypes.c_int()
    x11.XGetInputFocus(display, ctypes.byref(focus), ctypes.byref(revert_to))
    return focus.value, revert_to.value


def verify_focus_and_keyboard_input(x11, display, window):
    xtst = configure_xtst()
    event_base = ctypes.c_int()
    error_base = ctypes.c_int()
    major_version = ctypes.c_int()
    minor_version = ctypes.c_int()
    if xtst.XTestQueryExtension(
        display,
        ctypes.byref(event_base),
        ctypes.byref(error_base),
        ctypes.byref(major_version),
        ctypes.byref(minor_version),
    ) == 0:
        return False, "XTEST extension is unavailable"

    x11.XSetInputFocus(display, window, REVERT_TO_PARENT, CURRENT_TIME)
    x11.XSync(display, 0)

    focus, _ = describe_focus_target(x11, display)
    if not is_window_or_descendant(x11, display, window, focus):
        return False, f"focus=0x{focus:x}"

    keycode = x11.XKeysymToKeycode(display, XK_TAB)
    if keycode == 0:
        return False, "Tab keysym did not resolve to an X11 keycode"

    if xtst.XTestFakeKeyEvent(display, keycode, 1, 0) == 0:
        return False, "XTest key press injection failed"

    if xtst.XTestFakeKeyEvent(display, keycode, 0, 0) == 0:
        return False, "XTest key release injection failed"

    x11.XSync(display, 0)
    focus, _ = describe_focus_target(x11, display)
    if not is_window_or_descendant(x11, display, window, focus):
        return False, f"focus after key input=0x{focus:x}"

    return True, f"focus=0x{focus:x} xTestKeycode={keycode}"


def probe(args):
    os.environ["DISPLAY"] = args.display
    x11 = configure_x11()
    display = x11.XOpenDisplay(args.display.encode("utf-8"))
    if not display:
        raise RuntimeError(f"Unable to open X display {args.display}.")

    try:
        root = x11.XDefaultRootWindow(display)
        deadline = time.monotonic() + args.timeout
        last_description = "no candidate windows"
        while time.monotonic() < deadline:
            x11.XSync(display, 0)
            candidates = get_viewable_windows(
                x11,
                display,
                root,
                args.pid,
                args.min_width,
                args.min_height,
            )
            if candidates:
                _, window, attributes, name, window_pid = candidates[0]
                distinct_pixels = sample_distinct_pixels(
                    x11,
                    display,
                    window,
                    attributes.width,
                    attributes.height,
                )
                last_description = (
                    f"window=0x{window:x} pid={window_pid or '<unknown>'} "
                    f"name='{name or '<unnamed>'}' size={attributes.width}x{attributes.height} "
                    f"distinctPixels={distinct_pixels}"
                )
                if distinct_pixels >= args.min_distinct_pixels:
                    if args.require_focus_input:
                        input_ok, input_description = verify_focus_and_keyboard_input(
                            x11,
                            display,
                            window,
                        )
                        last_description = f"{last_description} {input_description}"
                        if not input_ok:
                            time.sleep(0.2)
                            continue

                    print(last_description)
                    return 0

            time.sleep(0.2)

        print(f"X11 window probe failed: {last_description}", file=sys.stderr)
        return 1
    finally:
        x11.XCloseDisplay(display)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--display", required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--timeout", type=float, default=15)
    parser.add_argument("--min-width", type=int, default=320)
    parser.add_argument("--min-height", type=int, default=240)
    parser.add_argument("--min-distinct-pixels", type=int, default=2)
    parser.add_argument("--require-focus-input", action="store_true")
    args = parser.parse_args()
    return probe(args)


if __name__ == "__main__":
    raise SystemExit(main())
