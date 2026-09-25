import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexPromptCacheTests: XCTestCase {
    func testPromptCacheKeyDefaultsToConversationSessionID() async throws {
        let queue = ResponseQueue(responses: [completed()])
        let client = makeClient(queue)

        _ = try await IXCodexConversation(client: client, sessionID: "session-1")
            .run(input: [.text("Hello")], instructions: "Reply.")

        let body = try await firstBody(queue)
        XCTAssertEqual(body["prompt_cache_key"]?.stringValue, "session-1")
    }

    func testExplicitPromptCacheKeyIsSharedAcrossConversationsAndToolRounds() async throws {
        let queue = ResponseQueue(responses: [
            completed(tool: true), completed(), completed(),
        ])
        let client = makeClient(queue)
        let options = IXCodexResponseOptions(promptCacheKey: "verifier-policy-v1")

        _ = try await IXCodexConversation(client: client, sessionID: "first").run(
            input: [.text("Read state")],
            instructions: "Use the read tool.",
            tools: [.init(name: "read_state", description: "Read only.", parameters: .object([
                "type": .string("object"), "properties": .object([:]),
                "required": .array([]), "additionalProperties": .bool(false),
            ]))],
            executor: IXClosureCodexToolExecutor { call in
                .success(callID: call.id, message: "Ready")
            },
            responseOptions: options
        )
        _ = try await IXCodexConversation(client: client, sessionID: "second").run(
            input: [.text("Hello")],
            instructions: "Use the read tool.",
            responseOptions: options
        )

        let requests = await queue.requests
        XCTAssertEqual(requests.count, 3)
        for request in requests {
            let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
            XCTAssertEqual(body["prompt_cache_key"]?.stringValue, "verifier-policy-v1")
        }
        // The session header keeps identifying each conversation.
        XCTAssertEqual(
            requests.map { $0.value(forHTTPHeaderField: "session_id") },
            ["first", "first", "second"]
        )
    }

    func testEmptyPromptCacheKeyFallsBackToSessionID() async throws {
        let queue = ResponseQueue(responses: [completed()])
        let client = makeClient(queue)

        _ = try await IXCodexConversation(client: client, sessionID: "session-2").run(
            input: [.text("Hello")],
            instructions: "Reply.",
            responseOptions: .init(promptCacheKey: "")
        )

        let body = try await firstBody(queue)
        XCTAssertEqual(body["prompt_cache_key"]?.stringValue, "session-2")
    }

    func testUsageReportsAndAggregatesCachedInputTokens() async throws {
        let queue = ResponseQueue(responses: [
            completed(tool: true, usage: [
                "input_tokens": 1_200, "output_tokens": 10, "total_tokens": 1_210,
                "input_tokens_details": ["cached_tokens": 1_024],
            ]),
            completed(usage: [
                "input_tokens": 1_300, "output_tokens": 20, "total_tokens": 1_320,
                "input_tokens_details": ["cached_tokens": 1_152],
            ]),
        ])
        let client = makeClient(queue)

        let result = try await IXCodexConversation(client: client).run(
            input: [.text("Read state")],
            instructions: "Use the read tool.",
            tools: [.init(name: "read_state", description: "Read only.", parameters: .object([
                "type": .string("object"), "properties": .object([:]),
            ]))],
            executor: IXClosureCodexToolExecutor { call in
                .success(callID: call.id, message: "Ready")
            }
        )

        XCTAssertEqual(result.turn.usage?.cachedInputTokens, 1_152)
        XCTAssertEqual(result.usage?.inputTokens, 2_500)
        XCTAssertEqual(result.usage?.cachedInputTokens, 2_176)
    }

    func testUsageWithoutCacheDetailsReportsZeroCachedTokens() async throws {
        let queue = ResponseQueue(responses: [
            completed(usage: ["input_tokens": 12, "output_tokens": 3, "total_tokens": 15]),
        ])
        let client = makeClient(queue)

        let result = try await IXCodexConversation(client: client)
            .run(input: [.text("Hello")], instructions: "Reply.")

        XCTAssertEqual(result.usage?.inputTokens, 12)
        XCTAssertEqual(result.usage?.cachedInputTokens, 0)
        XCTAssertEqual(
            IXCodexUsage(inputTokens: 1, outputTokens: 2, reasoningTokens: 0, totalTokens: 3)
                .cachedInputTokens,
            0
        )
    }

    private func makeClient(_ queue: ResponseQueue) -> IXCodexClient {
        let http = IXClosureHTTPClient { request in try await queue.next(request) }
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account"
            )),
            httpClient: http
        )
        return IXCodexClient(authSession: auth, httpClient: http)
    }

    private func firstBody(_ queue: ResponseQueue) async throws -> IXJSONValue {
        let requests = await queue.requests
        return try IXJSONValue.decode(try XCTUnwrap(requests.first?.httpBody))
    }

    private func completed(
        tool: Bool = false,
        usage: [String: Any]? = nil
    ) -> IXHTTPResponse {
        let output: [[String: Any]] = tool
            ? [["type": "function_call", "call_id": "call", "name": "read_state", "arguments": "{}"]]
            : [["type": "message", "content": [["type": "output_text", "text": "Ready"]]]]
        var response: [String: Any] = ["id": "response", "status": "completed", "output": output]
        if let usage { response["usage"] = usage }
        let event: [String: Any] = ["type": "response.completed", "response": response]
        let data = try! JSONSerialization.data(withJSONObject: event)
        return IXHTTPResponse(
            statusCode: 200,
            body: Data(("data: " + String(decoding: data, as: UTF8.self) + "\n\ndata: [DONE]\n\n").utf8)
        )
    }
}
