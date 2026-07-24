import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexConversationTests: XCTestCase {
    func testConversationExecutesToolAndReplaysCanonicalResult() async throws {
        let first = sse(response: [
            "id": "response-1",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-1",
                "name": "inspect_home",
                "arguments": "{\"scope\":\"summary\"}",
            ]],
        ])
        let second = sse(response: [
            "id": "response-2",
            "status": "completed",
            "output": [[
                "type": "message",
                "role": "assistant",
                "content": [["type": "output_text", "text": "Everything looks healthy."]],
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: first),
            IXHTTPResponse(statusCode: 200, body: second),
        ])
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account"
            )),
            httpClient: IXClosureHTTPClient { request in try await state.next(request) }
        )
        let http = IXClosureHTTPClient { request in try await state.next(request) }
        let client = IXCodexClient(authSession: auth, httpClient: http)
        let conversation = IXCodexConversation(client: client)
        let executor = IXClosureCodexToolExecutor { call in
            XCTAssertEqual(call.name, "inspect_home")
            return .success(callID: call.id, message: "Healthy")
        }

        let result = try await conversation.run(
            input: [.text("Is my home healthy?")],
            instructions: "Use tools for current state.",
            tools: [.init(
                name: "inspect_home",
                description: "Read the normalized home state.",
                parameters: .object(["type": .string("object")])
            )],
            executor: executor
        )

        XCTAssertEqual(result.turn.text, "Everything looks healthy.")
        XCTAssertEqual(result.toolCalls.map(\.id), ["call-1"])
        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        let secondBody = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        let input = try XCTUnwrap(secondBody["input"]?.arrayValue)
        XCTAssertEqual(input.last?["type"]?.stringValue, "custom_tool_call_output")
        XCTAssertEqual(input.last?["call_id"]?.stringValue, "call-1")
        XCTAssertEqual(requests.first?.value(forHTTPHeaderField: "chatgpt-account-id"), "account")
        XCTAssertNil(secondBody["previous_response_id"])
    }

    func testSSEParserReadsDeltaAndCompletedOutput() throws {
        let data = Data("""
        data: {"type":"response.output_text.delta","delta":"Hello "}

        data: {"type":"response.output_text.delta","delta":"home"}

        data: {"type":"response.completed","response":{"id":"r1","status":"completed","output":[]}}

        data: [DONE]

        """.utf8)
        let parsed = try IXCodexSSEParser().parse(data)
        XCTAssertEqual(parsed.text, "Hello home")
        XCTAssertEqual(parsed.completedResponse?["id"]?.stringValue, "r1")
    }

    func testToolSchemaFallsBackToNestedInputSchema() async throws {
        let rejected = IXHTTPResponse.json(400, [
            "error": [
                "message": "Unknown parameter: tools[0].function.parameters",
                "param": "tools[0].function.parameters",
            ],
        ])
        let state = ResponseQueue(responses: [
            rejected,
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-1",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Ready"]],
                ]],
            ])),
        ])
        let client = makeClient(state)
        let conversation = IXCodexConversation(client: client)

        _ = try await conversation.run(
            input: [.text("Inspect")],
            instructions: "Use tools.",
            tools: [.init(
                name: "inspect",
                description: "Inspect state.",
                parameters: .object(["type": .string("object")])
            )]
        )

        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        let first = try IXJSONValue.decode(try XCTUnwrap(requests[0].httpBody))
        let second = try IXJSONValue.decode(try XCTUnwrap(requests[1].httpBody))
        XCTAssertNotNil(first["tools"]?.arrayValue?.first?["function"]?["parameters"])
        XCTAssertNotNil(second["tools"]?.arrayValue?.first?["function"]?["input_schema"])
    }

    func testUnsupportedDefaultModelFallsBackWithoutLosingInput() async throws {
        let state = ResponseQueue(responses: [
            .json(400, ["error": ["message": "The model is not supported for this ChatGPT account"]]),
            .json(200, ["models": []]),
            .json(200, ["models": []]),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-2",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Fallback worked"]],
                ]],
            ])),
        ])
        let client = makeClient(state)
        let conversation = IXCodexConversation(client: client)

        let result = try await conversation.run(input: [.text("Hello")], instructions: "Help.")

        XCTAssertEqual(result.turn.text, "Fallback worked")
        let requests = await state.requests
        let retryBody = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        XCTAssertEqual(retryBody["model"]?.stringValue, "gpt-5.5")
        let firstInput = retryBody["input"]?.arrayValue?.first
        let firstContent = firstInput?["content"]?.arrayValue?.first
        XCTAssertEqual(firstContent?["text"]?.stringValue, "Hello")
    }

    func testImageGenerationToolIsExplicitlyAdvertised() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-image",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Ready to create images"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        _ = try await conversation.run(
            input: [.text("Create a floor-plan concept")],
            instructions: "Help.",
            imageGeneration: .init(quality: "high", outputFormat: "png")
        )

        let requests = await state.requests
        let request = try XCTUnwrap(requests.first)
        let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
        let imageTool = body["tools"]?.arrayValue?.first
        XCTAssertEqual(imageTool?["type"]?.stringValue, "image_generation")
        XCTAssertEqual(imageTool?["quality"]?.stringValue, "high")
        XCTAssertEqual(imageTool?["output_format"]?.stringValue, "png")
        XCTAssertNil(body["tool_choice"])
    }

    func testResetInvalidatesInFlightTurnAndDoesNotReplayItsPrompt() async throws {
        let gate = ConversationResponseGate(
            first: IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "old-response",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Old"]],
                ]],
            ])),
            later: IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "new-response",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "New"]],
                ]],
            ]))
        )
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account"
            )),
            httpClient: IXClosureHTTPClient { request in try await gate.send(request) }
        )
        let client = IXCodexClient(
            authSession: auth,
            httpClient: IXClosureHTTPClient { request in try await gate.send(request) }
        )
        let conversation = IXCodexConversation(client: client)
        let oldRun = Task {
            try await conversation.run(input: [.text("Old prompt")], instructions: "Help.")
        }
        await gate.waitUntilFirstRequest()
        await conversation.reset()
        await gate.releaseFirst()
        do {
            _ = try await oldRun.value
            XCTFail("Reset should invalidate the old turn")
        } catch is CancellationError {
        }

        let result = try await conversation.run(input: [.text("New prompt")], instructions: "Help.")

        XCTAssertEqual(result.turn.text, "New")
        let requests = await gate.requests
        let newBody = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        let input = try XCTUnwrap(newBody["input"]?.arrayValue)
        XCTAssertEqual(input.count, 1)
        XCTAssertEqual(input.first?["content"]?.arrayValue?.first?["text"]?.stringValue, "New prompt")
    }

    func testCompletedToolResultSurvivesFailedFollowUpWithoutRepeatingSideEffect() async throws {
        let toolTurn = sse(response: [
            "id": "tool-response",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-side-effect",
                "name": "toggle_light",
                "arguments": "{}",
            ]],
        ])
        let recoveredTurn = sse(response: [
            "id": "recovered-response",
            "status": "completed",
            "output": [[
                "type": "message",
                "content": [["type": "output_text", "text": "The light was changed."]],
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: toolTurn),
            .json(500, ["error": ["message": "temporary failure"]]),
            IXHTTPResponse(statusCode: 200, body: recoveredTurn),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let counter = ToolExecutionCounter()
        let executor = IXClosureCodexToolExecutor { call in
            await counter.record()
            return .success(callID: call.id, message: "Light toggled")
        }

        do {
            _ = try await conversation.run(
                input: [.text("Toggle the lamp")],
                instructions: "Use tools.",
                tools: [.init(name: "toggle_light", description: "Toggle a light", parameters: .object([:])),],
                executor: executor
            )
            XCTFail("The follow-up request should fail")
        } catch {
        }

        let result = try await conversation.run(input: [.text("What happened?")], instructions: "Answer.")

        XCTAssertEqual(result.turn.text, "The light was changed.")
        let executionCount = await counter.count
        XCTAssertEqual(executionCount, 1)
        let requests = await state.requests
        let recoveryBody = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        let recoveryInput = try XCTUnwrap(recoveryBody["input"]?.arrayValue)
        XCTAssertTrue(recoveryInput.contains { $0["type"]?.stringValue == "custom_tool_call" })
        XCTAssertTrue(recoveryInput.contains { $0["type"]?.stringValue == "custom_tool_call_output" })
    }

    private func makeClient(_ state: ResponseQueue) -> IXCodexClient {
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account"
            )),
            httpClient: IXClosureHTTPClient { request in try await state.next(request) }
        )
        return IXCodexClient(
            authSession: auth,
            httpClient: IXClosureHTTPClient { request in try await state.next(request) }
        )
    }

    private func sse(response: Any) -> Data {
        let responseData = try! JSONSerialization.data(withJSONObject: response)
        let responseText = String(data: responseData, encoding: .utf8)!
        return Data("data: {\"type\":\"response.completed\",\"response\":\(responseText)}\n\ndata: [DONE]\n\n".utf8)
    }
}

actor ToolExecutionCounter {
    private(set) var count = 0

    func record() {
        count += 1
    }
}

actor ConversationResponseGate {
    private let first: IXHTTPResponse
    private let later: IXHTTPResponse
    private var firstRelease: CheckedContinuation<Void, Never>?
    private var firstRequest: CheckedContinuation<Void, Never>?
    private(set) var requests: [URLRequest] = []

    init(first: IXHTTPResponse, later: IXHTTPResponse) {
        self.first = first
        self.later = later
    }

    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        requests.append(request)
        guard requests.count == 1 else { return later }
        firstRequest?.resume()
        firstRequest = nil
        await withCheckedContinuation { firstRelease = $0 }
        return first
    }

    func waitUntilFirstRequest() async {
        guard requests.isEmpty else { return }
        await withCheckedContinuation { firstRequest = $0 }
    }

    func releaseFirst() {
        firstRelease?.resume()
        firstRelease = nil
    }
}
