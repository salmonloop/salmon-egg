import Foundation
import Vision
import XCTest

@MainActor
final class ElicitationTests: XCTestCase {
    private let product = XCUIApplication(bundleIdentifier: "com.companyname.salmonegg")
    private var control: URL!
    private var recognizedLines: [String] = []

    override func setUpWithError() throws {
        continueAfterFailure = false
        control = try XCTUnwrap(URL(string: ProcessInfo.processInfo.environment["SALMONEGG_IOS_CONTROL_URL"] ?? ""))
        // The harness launches the installed product with console capture before attaching XCTest.
        product.activate()
    }

    override func tearDownWithError() throws {
        let hierarchy = XCTAttachment(string: product.debugDescription)
        hierarchy.name = "Product accessibility hierarchy"
        hierarchy.lifetime = .keepAlways
        add(hierarchy)
        let screenshot = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        screenshot.name = "Installed product screen"
        screenshot.lifetime = .keepAlways
        add(screenshot)
        let recognized = XCTAttachment(string: recognizedLines.joined(separator: "\n"))
        recognized.name = "Last recognized visible labels"
        recognized.lifetime = .keepAlways
        add(recognized)
        product.terminate()
    }

    func testInstalledProductRequiresConsentAndKeepsBrowserPrivate() async throws {
        // UIKit's current Uno container peers hide nested elements from XCTest. Read the actual
        // screen with Apple's Vision, then deliver a native tap; no product state or test-ID mode.
        if try textBounds("Add project") == nil {
            let sidebar = product.buttons["Toggle sidebar"]
            if sidebar.waitForExistence(timeout: 10) && sidebar.isHittable { sidebar.tap() }
        }
        try await tapVisibleText("Consent session")
        try await eventually("The authoritative session was not loaded") { try await self.state()["loaded"] as? Bool == true }
        let initialState = try await state()
        let capabilities = try XCTUnwrap(initialState["capabilities"] as? [String: Any])
        let elicitation = try XCTUnwrap(capabilities["elicitation"] as? [String: Any])
        XCTAssertNotNil(elicitation["url"])
        XCTAssertNotNil(elicitation["form"])

        try await instruct("url-decline")
        try await expectUrlCard()
        try await assertNoNavigation()
        try tap(product.buttons["Decline"].firstMatch)
        try await expectResponse("native-url-decline", action: "decline")

        try await instruct("url-cancel")
        try await expectUrlCard()
        try tap(product.buttons["Cancel"].firstMatch)
        try await expectResponse("native-url-cancel", action: "cancel")
        try await assertNoNavigation()

        try await instruct("url-open")
        try await expectUrlCard()
        try await assertNoNavigation()
        try tap(product.buttons["Open in browser"].firstMatch)
        try await expectResponse("native-url-open", action: "accept")
        let safari = XCUIApplication(bundleIdentifier: "com.apple.mobilesafari")
        try await eventually("The system browser did not become foreground") { safari.state == .runningForeground }
        try await eventually("The real Safari page did not report isolation") { try await self.reports().count == 1 }

        product.activate()
        try tap(product.buttons["Open again"].firstMatch)
        try await eventually("Reopening did not reach the external page") { try await self.reports().count == 2 }
        product.activate()
        let accepted = try await responses().filter { $0["id"] as? String == "native-url-open" }
        XCTAssertEqual(accepted.count, 1)
        try await instruct("complete", id: "unknown-id")
        XCTAssertTrue(product.buttons["Open again"].firstMatch.waitForExistence(timeout: 5))
        try await instruct("complete")
        try await eventually("The completed card remained open") { !self.product.buttons["Open again"].firstMatch.exists }
        try await instruct("complete")

        try await instruct("form-accept")
        let field = product.textFields["Acceptance answer"].firstMatch
        try tap(field)
        field.typeText("native-form-answer")
        try tap(product.buttons["Submit"].firstMatch)
        try await expectResponse("native-form-accept", action: "accept")
        let formResponses = try await responses()
        let form = try XCTUnwrap(formResponses.first { $0["id"] as? String == "native-form-accept" })
        let content = (form["result"] as? [String: Any])?["content"] as? [String: Any]
        XCTAssertEqual(content?["answer"] as? String, "native-form-answer")

        try await instruct("url-expire")
        try await expectUrlCard()
        let expiringState = try await state()
        let expiringUrl = try XCTUnwrap(expiringState["url"] as? String)
        try await instruct("disconnect")
        try await eventually("A disconnected request retained its URL") {
            !self.product.staticTexts[expiringUrl].firstMatch.exists
        }
        for report in try await reports() {
            XCTAssertEqual(report["openerNull"] as? Bool, true)
            XCTAssertEqual(report["referrer"] as? String, "")
            XCTAssertEqual(report["privateValue"] as? String, "mobile-page-private-canary")
        }
        print("IOS_ELICITATION_ACCEPTANCE_PASS native_input=true external_safari=true url_responses=3 form_responses=1 browser_visits=2")
    }

    private func tap(_ element: XCUIElement) throws {
        XCTAssertTrue(element.waitForExistence(timeout: 20), "The required native control is absent")
        let ready = XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true AND hittable == true"), object: element)
        XCTAssertEqual(XCTWaiter.wait(for: [ready], timeout: 20), .completed, "The native control is not interactable")
        element.tap()
    }

    private func textBounds(_ text: String) throws -> CGRect? {
        let screenshot = XCUIScreen.main.screenshot()
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.recognitionLanguages = ["en-US"]
        request.usesLanguageCorrection = false
        try VNImageRequestHandler(data: screenshot.pngRepresentation).perform([request])
        recognizedLines = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }
        let matches = (request.results ?? []).compactMap { observation -> CGRect? in
            guard let candidate = observation.topCandidates(1).first,
                  let range = candidate.string.range(of: text),
                  let bounds = try? candidate.boundingBox(for: range) else { return nil }
            return bounds.boundingBox
        }
        guard matches.count == 1, let rectangle = matches.first else { return nil }
        return CGRect(x: rectangle.minX, y: 1 - rectangle.maxY, width: rectangle.width, height: rectangle.height)
    }

    private func tapVisibleText(_ text: String) async throws {
        try await eventually("The visible native label is absent or ambiguous: " + text) {
            try self.textBounds(text) != nil
        }
        let rectangle = try XCTUnwrap(textBounds(text))
        let evidence = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        evidence.name = "Before native tap: " + text
        evidence.lifetime = .keepAlways
        add(evidence)
        product.coordinate(withNormalizedOffset: CGVector(dx: rectangle.midX, dy: rectangle.midY)).tap()
    }

    private func expectUrlCard() async throws {
        let current = try await state()
        let expected = try XCTUnwrap(current["url"] as? String)
        let fullUrl = product.staticTexts[expected].firstMatch
        XCTAssertTrue(fullUrl.waitForExistence(timeout: 20))
        XCTAssertTrue(fullUrl.label == expected, "The native card must show the full address")
        XCTAssertEqual(product.staticTexts["127.0.0.1"].firstMatch.label, "127.0.0.1")
    }

    private func assertNoNavigation() async throws {
        let current = try await state()
        XCTAssertEqual((current["visits"] as? [Any])?.count, 0, "The product navigated before consent")
    }

    private func expectResponse(_ id: String, action: String) async throws {
        try await eventually("No matching ACP response") {
            try await self.responses().contains { $0["id"] as? String == id }
        }
        let current = try await responses()
        let response = try XCTUnwrap(current.first { $0["id"] as? String == id })
        let result = try XCTUnwrap(response["result"] as? [String: Any])
        XCTAssertEqual(result["action"] as? String, action)
        if id.hasPrefix("native-url-") { XCTAssertNil(result["content"]) }
    }

    private func responses() async throws -> [[String: Any]] { try await state()["responses"] as? [[String: Any]] ?? [] }
    private func reports() async throws -> [[String: Any]] { try await state()["reports"] as? [[String: Any]] ?? [] }

    private func state() async throws -> [String: Any] {
        let (data, response) = try await URLSession.shared.data(from: control.appendingPathComponent("state"))
        XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
        let value = try JSONSerialization.jsonObject(with: data)
        return try XCTUnwrap(value as? [String: Any])
    }

    private func instruct(_ action: String, id: String? = nil) async throws {
        var request = URLRequest(url: control.appendingPathComponent("control"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: ["action": action, "id": id].compactMapValues { $0 })
        let (_, response) = try await URLSession.shared.data(for: request)
        XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
    }

    private func eventually(_ message: String, _ condition: () async throws -> Bool) async throws {
        let deadline = Date().addingTimeInterval(30)
        while Date() < deadline {
            if try await condition() { return }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        XCTFail(message)
    }
}
