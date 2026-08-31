#!/usr/bin/env python3
"""Send a real WM_DELETE_WINDOW ClientMessage to the SalmonEgg X11 window.

Why not `xdotool windowclose`: it sends XDestroyWindow, which makes Uno's X11
presenter segfault (rc=139, BadWindow) — a crash that masquerades as a fast
exit. The titlebar close button sends WM_PROTOCOLS/WM_DELETE_WINDOW via
XSendEvent; that is the message this script delivers, and the only one that
exercises the real close path (issue #126 gate).
"""
import argparse
import ctypes
import sys
import time

IS_VIEWABLE = 2
XA_CARDINAL = 6
ANY_PROPERTY_TYPE = 0
CURRENT_TIME = 0
NO_EVENT_MASK = 0
MIN_WINDOW_WIDTH = 200
MIN_WINDOW_HEIGHT = 200


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


class XClientMessageEvent(ctypes.Structure):
    _fields_ = [
        ("type", ctypes.c_int),
        ("serial", ctypes.c_ulong),
        ("send_event", ctypes.c_bool),
        ("display", ctypes.c_void_p),
        ("window", ctypes.c_ulong),
        ("message_type", ctypes.c_ulong),
        ("format", ctypes.c_int),
        ("data", ctypes.c_long * 5),
    ]


class XEvent(ctypes.Union):
    _fields_ = [
        ("type", ctypes.c_long),
        ("xclient", XClientMessageEvent),
    ]


def fetch_window_pid(x11, display, window, atom):
    if atom == 0:
        return None

    actual_type = ctypes.c_ulong()
    actual_format = ctypes.c_int()
    item_count = ctypes.c_ulong()
    bytes_after = ctypes.c_ulong()
    prop = ctypes.POINTER(ctypes.c_ubyte)()

    status = x11.XGetWindowProperty(
        display, window, atom, 0, 1, 0, ANY_PROPERTY_TYPE,
        ctypes.byref(actual_type), ctypes.byref(actual_format),
        ctypes.byref(item_count), ctypes.byref(bytes_after), ctypes.byref(prop),
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
            display, current,
            ctypes.byref(root_return), ctypes.byref(parent_return),
            ctypes.byref(children), ctypes.byref(child_count),
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


def fetch_window_name(x11, display, window):
    name = ctypes.c_char_p()
    if x11.XFetchName(display, window, ctypes.byref(name)) == 0 or not name.value:
        return ""
    try:
        return name.value.decode("utf-8", "replace")
    finally:
        x11.XFree(name)


def find_target_window(x11, display, target_pid):
    # 这个 Uno X11 窗口实测不携带可读的 _NET_WM_PID（老 probe 同样显示 pid=<unknown>），
    # 所以必须像 smoke probe 一样以窗口名匹配为主、pid 匹配为加分项。
    pid_atom = x11.XInternAtom(display, b"_NET_WM_PID", 1)
    root = x11.XDefaultRootWindow(display)
    best = None
    for window in enumerate_windows(x11, display, root):
        attributes = XWindowAttributes()
        if x11.XGetWindowAttributes(display, window, ctypes.byref(attributes)) == 0:
            continue
        if attributes.map_state != IS_VIEWABLE:
            continue
        if attributes.width < MIN_WINDOW_WIDTH or attributes.height < MIN_WINDOW_HEIGHT:
            continue
        name = fetch_window_name(x11, display, window)
        window_pid = fetch_window_pid(x11, display, window, pid_atom)
        is_name_match = "SalmonEgg" in name or "Salmon Egg" in name
        is_pid_match = window_pid == target_pid
        if not is_name_match and not is_pid_match:
            continue
        score = attributes.width * attributes.height
        if is_pid_match:
            score += 10_000_000_000
        if is_name_match:
            score += 1_000_000_000
        if best is None or score > best[0]:
            best = (score, window)
    return best[1] if best else None


def send_close_request(x11, display, window):
    wm_protocols = x11.XInternAtom(display, b"WM_PROTOCOLS", 1)
    wm_delete_window = x11.XInternAtom(display, b"WM_DELETE_WINDOW", 1)
    if not wm_protocols or not wm_delete_window:
        return False

    event = XEvent()
    event.xclient.type = 33  # ClientMessage
    event.xclient.window = window
    event.xclient.message_type = wm_protocols
    event.xclient.format = 32
    event.xclient.data[0] = wm_delete_window
    event.xclient.data[1] = CURRENT_TIME

    status = x11.XSendEvent(display, window, False, NO_EVENT_MASK, ctypes.byref(event))
    x11.XFlush(display)
    return status != 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--display", required=True)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--timeout", type=float, default=20.0)
    args = parser.parse_args()

    x11 = ctypes.CDLL("libX11.so.6")
    # argtypes 必须逐个声明：不声明时 ctypes 把 64 位指针按 32 位 int 传参，直接段错误。
    x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
    x11.XOpenDisplay.restype = ctypes.c_void_p
    x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
    x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
    x11.XDefaultRootWindow.restype = ctypes.c_ulong
    x11.XQueryTree.argtypes = [
        ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)),
        ctypes.POINTER(ctypes.c_uint),
    ]
    x11.XQueryTree.restype = ctypes.c_int
    x11.XGetWindowAttributes.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(XWindowAttributes)]
    x11.XGetWindowAttributes.restype = ctypes.c_int
    x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
    x11.XInternAtom.restype = ctypes.c_ulong
    x11.XGetWindowProperty.argtypes = [
        ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_long, ctypes.c_long,
        ctypes.c_int, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong),
        ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.POINTER(ctypes.c_ubyte)),
    ]
    x11.XGetWindowProperty.restype = ctypes.c_int
    x11.XFree.argtypes = [ctypes.c_void_p]
    x11.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_char_p)]
    x11.XFetchName.restype = ctypes.c_int
    x11.XSendEvent.argtypes = [
        ctypes.c_void_p, ctypes.c_ulong, ctypes.c_bool, ctypes.c_long,
        ctypes.POINTER(XEvent),
    ]
    x11.XSendEvent.restype = ctypes.c_int
    x11.XFlush.argtypes = [ctypes.c_void_p]

    display = x11.XOpenDisplay(args.display.encode())
    if not display:
        print(f"Unable to open X display {args.display}", file=sys.stderr)
        return 1

    try:
        deadline = time.monotonic() + args.timeout
        window = None
        while time.monotonic() < deadline and window is None:
            window = find_target_window(x11, display, args.pid)
            if window is None:
                time.sleep(0.2)

        if window is None:
            print(f"No viewable window owned by pid {args.pid}", file=sys.stderr)
            return 1

        if not send_close_request(x11, display, window):
            print("XSendEvent failed", file=sys.stderr)
            return 1

        print(f"Sent WM_DELETE_WINDOW to window {window:#x} (pid {args.pid})")
        return 0
    finally:
        x11.XCloseDisplay(ctypes.c_void_p(display))


if __name__ == "__main__":
    sys.exit(main())
