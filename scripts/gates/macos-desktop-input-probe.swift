import AppKit
import CoreGraphics
import Foundation

// This process owns its native window. Input comes through CGEvent rather than NSTextField.stringValue.
final class DesktopProbe: NSObject, NSApplicationDelegate, NSTextFieldDelegate {
    private let output: URL
    private var window: NSWindow?
    private var input: NSTextField?
    private var timer: Timer?
    private var delivered = false
    private var finished = false
    private let started = Date()

    init(output: URL) {
        self.output = output
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        writeResult(passed: false, phase: "window-create")
        let form = NSWindow(contentRect: NSRect(x: 120, y: 160, width: 500, height: 160),
                            styleMask: [.titled, .closable], backing: .buffered, defer: false)
        form.title = "SalmonEgg native desktop acceptance"
        let field = NSTextField(frame: NSRect(x: 30, y: 55, width: 440, height: 28))
        field.delegate = self
        form.contentView?.addSubview(field)
        self.window = form
        self.input = field
        form.makeKeyAndOrderFront(nil)
        NSApplication.shared.activate(ignoringOtherApps: true)
        form.makeFirstResponder(field)
        writeResult(passed: false, phase: "native-input")
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    func controlTextDidChange(_ notification: Notification) {
        if input?.stringValue == "x" {
            finish(passed: true, phase: "complete")
        }
    }

    private func tick() {
        if finished { return }
        if !delivered && window?.isKeyWindow == true {
            delivered = true
            guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 7, keyDown: true),
                  let up = CGEvent(keyboardEventSource: nil, virtualKey: 7, keyDown: false) else {
                finish(passed: false, phase: "event-allocation")
                return
            }
            down.post(tap: .cghidEventTap)
            up.post(tap: .cghidEventTap)
        }
        if Date().timeIntervalSince(started) >= 15 {
            finish(passed: false, phase: "input-timeout")
        }
    }

    private func finish(passed: Bool, phase: String) {
        if finished { return }
        finished = true
        timer?.invalidate()
        writeResult(passed: passed, phase: phase)
        window?.close()
        NSApplication.shared.stop(nil)
        exit(passed ? 0 : 1)
    }

    private func writeResult(passed: Bool, phase: String) {
        let result: [String: Any] = [
            "processId": ProcessInfo.processInfo.processIdentifier,
            "phase": phase,
            "eventPosted": delivered,
            "nativeTextReceived": input?.stringValue == "x",
            "eventPostingAccess": CGPreflightPostEventAccess(),
            "screenCount": NSScreen.screens.count,
            "privacySettingsChanged": false,
            "passed": passed
        ]
        do {
            let data = try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: output, options: .atomic)
            print(String(decoding: data, as: UTF8.self))
        } catch {
            fputs("Unable to write native desktop evidence: \(error)\n", stderr)
            exit(2)
        }
    }
}

guard CommandLine.arguments.count == 2 else {
    fputs("Expected one result JSON path.\n", stderr)
    exit(2)
}
let app = NSApplication.shared
app.setActivationPolicy(.regular)
let probe = DesktopProbe(output: URL(fileURLWithPath: CommandLine.arguments[1]))
app.delegate = probe
app.run()
