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
