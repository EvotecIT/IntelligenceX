import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexResponseOptionsTests: XCTestCase {
    func testDefaultsPreserveExistingRequestParameters() async throws {
        let queue = ResponseQueue(responses: [completed()])
        let (client, _) = makeClient(queue)
        _ = try await IXCodexConversation(client: client).run(input: [.text("Hello")], instructions: "Reply.")
        let recordedRequests = await queue.requests
        let request = try XCTUnwrap(recordedRequests.first)
        let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
        XCTAssertEqual(body["text"]?["verbosity"]?.stringValue, "medium")
        XCTAssertEqual(body["reasoning"]?["summary"]?.stringValue, "auto")
        XCTAssertNil(body["service_tier"])
    }

    func testOptionsSurviveAuthSchemaRetriesAndToolContinuation() async throws {
        let queue = ResponseQueue(responses: [
            .json(401, ["error": ["message": "Unauthorized"]]),
            .json(200, ["access_token": "renewed", "refresh_token": "renewed-refresh", "expires_in": 3600]),
            .json(400, ["error": ["message": "Unknown parameter: tools[0].function.parameters"]]),
            completed(tool: true, tier: "priority"), completed(tier: "default"),
        ])
        let (client, _) = makeClient(queue)
        let result = try await IXCodexConversation(client: client).run(
            input: [.text("Read state")], instructions: "Use the read tool.",
            tools: [.init(name: "read_state", description: "Read only.", parameters: .object([
                "type": .string("object"), "properties": .object([:]),
                "required": .array([]), "additionalProperties": .bool(false)]))],
            executor: IXClosureCodexToolExecutor { call in .success(callID: call.id, message: "Ready") },
            responseOptions: .init(textVerbosity: .low, reasoningSummary: nil, serviceTier: .priority))
        let requests = await queue.requests.filter { $0.url?.path.hasSuffix("/responses") == true }
        XCTAssertEqual(requests.count, 4)
        for request in requests {
            let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
            XCTAssertEqual(body["text"]?["verbosity"]?.stringValue, "low")
            XCTAssertNil(body["reasoning"]?["summary"])
            XCTAssertEqual(body["reasoning"]?["effort"]?.stringValue, "low")
            XCTAssertEqual(body["service_tier"]?.stringValue, "priority")
        }
        XCTAssertEqual(result.serviceTiers, ["priority", "default"])
        XCTAssertEqual(result.turn.serviceTier, "default")
    }

    func testCatalogCapabilitiesApplyWithoutAnotherDiscoveryRequest() async throws {
        let queue = ResponseQueue(responses: [catalog(), completed()])
        let (client, _) = makeClient(queue)
        let models = try await client.models()
        let model = try XCTUnwrap(models.first)
        XCTAssertEqual(model.supportsImageInput, false)
        XCTAssertEqual(model.serviceTiers.map(\.id), ["priority"])
        XCTAssertEqual(model.serviceTiers.first?.name, "Fast")
        _ = try await IXCodexConversation(client: client).run(input: [.text("Hello")],
            instructions: "Reply.", model: "text-model",
            responseOptions: .init(textVerbosity: .high, serviceTier: .priority))
        let requests = await queue.requests
        XCTAssertEqual(requests.count, 2)
        let body = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        XCTAssertNil(body["text"])
        XCTAssertNil(body["reasoning"]?["summary"])
        XCTAssertEqual(body["service_tier"]?.stringValue, "priority")
    }

    func testCatalogCapabilitiesDoNotCarryIntoAnotherAccount() async throws {
        let queue = ResponseQueue(responses: [catalog(), completed()])
        let store = IXMemoryCodexCredentialStore(bundle: bundle(account: "first"))
        let http = IXClosureHTTPClient { request in try await queue.next(request) }
        let auth = IXCodexAuthSession(credentialStore: store, httpClient: http)
        let client = IXCodexClient(authSession: auth, httpClient: http)
        _ = try await client.models()
        try await auth.signOut()
        await store.save(bundle(account: "second"))
        _ = try await IXCodexConversation(client: client).run(input: [.text("Hello")],
            instructions: "Reply.", model: "text-model")
        let requests = await queue.requests
        let body = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        XCTAssertEqual(body["text"]?["verbosity"]?.stringValue, "medium")
        XCTAssertEqual(body["reasoning"]?["summary"]?.stringValue, "auto")
    }

    func testLocalCompletionKeepsReportedTier() async throws {
        let queue = ResponseQueue(responses: [completed(tool: true, tier: "priority")])
        let (client, _) = makeClient(queue)
        let result = try await IXCodexConversation(client: client).run(input: [.text("Read")],
            instructions: "Use the tool.", tools: [.init(name: "read_state", description: "Read.",
                parameters: .object(["type": .string("object"), "properties": .object([:])]))],
            executor: IXClosureCodexToolExecutor { call in .success(callID: call.id, message: "Ready") },
            completeToolRound: { _, _ in "Ready" })
        XCTAssertEqual(result.turn.text, "Ready")
        XCTAssertEqual(result.serviceTiers, ["priority"])
    }

    private func makeClient(_ queue: ResponseQueue) -> (IXCodexClient, IXCodexAuthSession) {
        let http = IXClosureHTTPClient { request in try await queue.next(request) }
        let auth = IXCodexAuthSession(credentialStore: IXMemoryCodexCredentialStore(bundle: bundle()), httpClient: http)
        return (IXCodexClient(authSession: auth, httpClient: http), auth)
    }

    private func bundle(account: String = "account") -> IXCodexAuthBundle {
        .init(accessToken: "access", refreshToken: "refresh", expiresAt: .distantFuture, accountID: account)
    }

    private func catalog() -> IXHTTPResponse {
        .json(200, ["models": [["slug": "text-model", "visibility": "list",
            "input_modalities": ["text"], "support_verbosity": false,
            "supports_reasoning_summary_parameter": false,
            "service_tiers": [["id": "priority", "name": "Fast", "description": "Increased usage"]]]]])
    }

    private func completed(tool: Bool = false, tier: String? = nil) -> IXHTTPResponse {
        let output: [[String: Any]] = tool
            ? [["type": "function_call", "call_id": "call", "name": "read_state", "arguments": "{}"]]
            : [["type": "message", "content": [["type": "output_text", "text": "Ready"]]]]
        var response: [String: Any] = ["id": "response", "status": "completed", "output": output]
        if let tier { response["service_tier"] = tier }
        let event: [String: Any] = ["type": "response.completed", "response": response]
        let data = try! JSONSerialization.data(withJSONObject: event)
        return IXHTTPResponse(statusCode: 200, body: Data(("data: " + String(decoding: data, as: UTF8.self) + "\n\ndata: [DONE]\n\n").utf8))
    }
}
