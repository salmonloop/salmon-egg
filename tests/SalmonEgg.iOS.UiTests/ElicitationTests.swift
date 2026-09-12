import Foundation
import XCTest

@MainActor
final class ElicitationTests: XCTestCase {
    private let product = XCUIApplication(bundleIdentifier: "com.companyname.salmonegg")
    private var control: URL!

    override func setUpWithError() throws {
        continueAfterFailure = false
        control = try XCTUnwrap(URL(string: ProcessInfo.processInfo.environment["SALMONEGG_IOS_CONTROL_URL"] ?? ""))
        product.launchEnvironment["SALMONEGG_GUI"] = "1"
        product.launch()
    }

    override func tearDownWithError() throws {
        product.terminate()
    }

    func testInstalledProductRequiresConsentAndKeepsBrowserPrivate() async throws {
        let session = product.descendants(matching: .any)["MainNav.Session.native-elicitation-conversation"].firstMatch
        if !session.waitForExistence(timeout: 30) || !session.isHittable {
            let sidebar = product.buttons["TitleBar.ToggleSidebar"]
            if sidebar.waitForExistence(timeout: 10) && sidebar.isHittable { sidebar.tap() }
            let project = product.descendants(matching: .any)["MainNav.Project.remote-directory:native-elicitation-directory"].firstMatch
            if project.waitForExistence(timeout: 10) && project.isHittable { project.tap() }
        }
        try tap(session)
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
        try await instruct("disconnect")
        try await eventually("A disconnected request retained its URL") {
            !self.product.staticTexts["Elicitation.FullUrl"].firstMatch.exists
                || self.product.staticTexts["Elicitation.FullUrl"].firstMatch.label.isEmpty
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

    private func expectUrlCard() async throws {
        let fullUrl = product.staticTexts["Elicitation.FullUrl"].firstMatch
        XCTAssertTrue(fullUrl.waitForExistence(timeout: 20))
        let current = try await state()
        let expected = try XCTUnwrap(current["url"] as? String)
        XCTAssertTrue(fullUrl.label == expected, "The native card must show the full address")
        XCTAssertEqual(product.staticTexts["Elicitation.UrlHost"].firstMatch.label, "127.0.0.1")
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
