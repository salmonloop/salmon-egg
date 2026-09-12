import AppKit
import ApplicationServices
import Foundation

func attribute(_ element: AXUIElement, _ name: CFString) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name, &value) == .success ? value : nil
}

func children(_ element: AXUIElement) -> [AXUIElement] {
    attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
}

func isFrontmost(_ pid: pid_t) -> Bool {
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
          let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]]
        else { return false }
    // NSWorkspace changes before the app-switcher animation finishes. Native z-order must
    // also expose the actual application window before mouse input can reach its content.
    let front = windows.first { ($0[kCGWindowLayer as String] as? Int) == 0 }
    return (front?[kCGWindowOwnerPID as String] as? Int) == Int(pid)
}

func activate(_ pid: pid_t, _ titlebarPoint: CGPoint) -> Bool {
    if isFrontmost(pid) { return true }
    let frontmost = NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? ""
    if ["com.apple.Safari", "com.google.Chrome", "org.mozilla.firefox", "com.microsoft.edgemac"].contains(frontmost) {
        // Opening the system browser makes it frontmost. Return to the preceding product with
        // the native application switcher, rather than clicking a now-covered product window.
        let down = CGEvent(keyboardEventSource: nil, virtualKey: 48, keyDown: true)
        let up = CGEvent(keyboardEventSource: nil, virtualKey: 48, keyDown: false)
        down?.flags = .maskCommand
        up?.flags = .maskCommand
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
        CGEvent(keyboardEventSource: nil, virtualKey: 55, keyDown: false)?.post(tap: .cghidEventTap)
    } else {
    // An external helper cannot force cooperative app activation on recent macOS. A real
    // titlebar click is the user's native way to bring an inactive product window forward.
    CGEvent(mouseEventSource: nil, mouseType: .mouseMoved, mouseCursorPosition: titlebarPoint, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown, mouseCursorPosition: titlebarPoint, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp, mouseCursorPosition: titlebarPoint, mouseButton: .left)?.post(tap: .cghidEventTap)
    }
    let deadline = Date().addingTimeInterval(5)
    while Date() < deadline {
        if isFrontmost(pid) { return true }
        RunLoop.current.run(until: Date().addingTimeInterval(0.02))
    }
    fputs("Native application did not become foreground after native application activation.\n", stderr)
    return false
}

func findButton(_ element: AXUIElement, _ title: String, _ depth: Int = 0) -> AXUIElement? {
    if depth > 30 { return nil }
    let role = attribute(element, kAXRoleAttribute as CFString) as? String
    let label = attribute(element, kAXTitleAttribute as CFString) as? String
        ?? attribute(element, kAXDescriptionAttribute as CFString) as? String
    let enabled = attribute(element, kAXEnabledAttribute as CFString) as? Bool ?? false
    if role == kAXButtonRole as String && label == title && enabled { return element }
    for child in children(element) {
        if let found = findButton(child, title, depth + 1) { return found }
    }
    return nil
}

func describeTree(_ element: AXUIElement, _ depth: Int = 0) -> [[String: Any]] {
    if depth > 20 { return [] }
    let role = attribute(element, kAXRoleAttribute as CFString) as? String ?? "unknown"
    var result: [[String: Any]] = []
    if role == kAXButtonRole as String || depth < 3 {
        // Buttons have public action labels. Do not capture field values, messages or URLs.
        result.append(["depth": depth, "role": role,
                       "title": attribute(element, kAXTitleAttribute as CFString) as? String ?? "",
                       "description": attribute(element, kAXDescriptionAttribute as CFString) as? String ?? "",
                       "enabled": attribute(element, kAXEnabledAttribute as CFString) as? Bool ?? false])
        if let raw = attribute(element, kAXPositionAttribute as CFString), CFGetTypeID(raw) == AXValueGetTypeID() {
            var point = CGPoint.zero
            if AXValueGetValue(raw as! AXValue, .cgPoint, &point) { result[result.count - 1]["position"] = [point.x, point.y] }
        }
        if let raw = attribute(element, kAXSizeAttribute as CFString), CFGetTypeID(raw) == AXValueGetTypeID() {
            var size = CGSize.zero
            if AXValueGetValue(raw as! AXValue, .cgSize, &size) { result[result.count - 1]["size"] = [size.width, size.height] }
        }
    }
    for child in children(element) { result.append(contentsOf: describeTree(child, depth + 1)) }
    return result
}

let arguments = CommandLine.arguments
guard arguments.count >= 3 else { exit(2) }
if arguments[1] == "pids" {
    let values = NSWorkspace.shared.runningApplications.filter {
        ["com.apple.Safari", "com.google.Chrome", "org.mozilla.firefox", "com.microsoft.edgemac"].contains($0.bundleIdentifier ?? "")
    }
        .map { Int($0.processIdentifier) }
    print(String(data: try JSONSerialization.data(withJSONObject: values), encoding: .utf8)!)
    exit(0)
}
if arguments[1] == "describe", let pid = Int32(arguments[2]) {
    let target = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(target, 2)
    let result: [String: Any] = ["trusted": AXIsProcessTrusted(), "pid": Int(pid), "tree": describeTree(target)]
    print(String(data: try JSONSerialization.data(withJSONObject: result), encoding: .utf8)!)
    exit(0)
}
if arguments[1] == "allow-local-network" {
    var candidates: [(AXUIElement, [String])] = []
    for application in NSWorkspace.shared.runningApplications where application.bundleIdentifier == "com.apple.UserNotificationCenter" {
        let root = AXUIElementCreateApplication(application.processIdentifier)
        AXUIElementSetMessagingTimeout(root, 2)
        let windows = attribute(root, kAXWindowsAttribute as CFString) as? [AXUIElement] ?? []
        for window in windows {
            var stack = [window]
            var texts: [String] = []
            while let element = stack.popLast() {
                for name in [kAXTitleAttribute, kAXDescriptionAttribute, kAXValueAttribute] {
                    if let text = attribute(element, name as CFString) as? String { texts.append(text) }
                }
                stack.append(contentsOf: children(element))
            }
            let text = texts.joined(separator: " ").lowercased()
            if text.contains("python") && text.contains("local network") { candidates.append((window, texts)) }
        }
    }
    if candidates.isEmpty { print("No Python local-network permission prompt"); exit(0) }
    guard candidates.count == 1, let button = findButton(candidates[0].0, "Allow"),
          let rawPosition = attribute(button, kAXPositionAttribute as CFString),
          let rawSize = attribute(button, kAXSizeAttribute as CFString) else { exit(7) }
    var point = CGPoint.zero
    var size = CGSize.zero
    guard AXValueGetValue(rawPosition as! AXValue, .cgPoint, &point),
          AXValueGetValue(rawSize as! AXValue, .cgSize, &size) else { exit(8) }
    point.x += size.width / 2
    point.y += size.height / 2
    CGEvent(mouseEventSource: nil, mouseType: .mouseMoved, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    print("Allowed the temporary Python fixture local-network prompt through native UI")
    exit(0)
}
if arguments[1] == "pointer", arguments.count == 6, let pid = Int32(arguments[2]),
   let localX = Double(arguments[3]), let localY = Double(arguments[4]), let height = Double(arguments[5]) {
    let target = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(target, 2)
    guard let windows = attribute(target, kAXWindowsAttribute as CFString) as? [AXUIElement], windows.count == 1,
          let rawPosition = attribute(windows[0], kAXPositionAttribute as CFString),
          let rawSize = attribute(windows[0], kAXSizeAttribute as CFString) else { exit(5) }
    var position = CGPoint.zero
    var size = CGSize.zero
    guard AXValueGetValue(rawPosition as! AXValue, .cgPoint, &position),
          AXValueGetValue(rawSize as! AXValue, .cgSize, &size), size.height >= height else { exit(6) }
    // Uno's native content frame begins below the OS window chrome. The current XamlRoot height
    // and the system AX frame determine that inset; no fixed titlebar pixel value is assumed.
    let point = CGPoint(x: position.x + localX, y: position.y + size.height - height + localY)
    let titlebarPoint = CGPoint(x: position.x + size.width / 2, y: position.y + (size.height - height) / 2)
    guard activate(pid, titlebarPoint) else { exit(9) }
    print("CGEvent target pid=\(pid) x=\(point.x) y=\(point.y) nativeWindow=\(position) size=\(size) contentHeight=\(height)")
    CGEvent(mouseEventSource: nil, mouseType: .mouseMoved, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    print("Native system window bounds and read-only product sample used for CGEvent pointer")
    exit(0)
}
if arguments[1] == "click", arguments.count == 4, let pid = Int32(arguments[2]) {
    let target = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(target, 2)
    guard let button = findButton(target, arguments[3]),
          let rawPosition = attribute(button, kAXPositionAttribute as CFString),
          let rawSize = attribute(button, kAXSizeAttribute as CFString) else {
        fputs("No enabled native AX button with the requested title.\n", stderr)
        exit(3)
    }
    var position = CGPoint.zero
    var size = CGSize.zero
    guard AXValueGetValue(rawPosition as! AXValue, .cgPoint, &position),
          AXValueGetValue(rawSize as! AXValue, .cgSize, &size), size.width > 0, size.height > 0 else { exit(4) }
    NSRunningApplication(processIdentifier: pid)?.activate(options: [])
    let point = CGPoint(x: position.x + size.width / 2, y: position.y + size.height / 2)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    print("Native AX button located and CGEvent pointer delivered")
    exit(0)
}
exit(2)
