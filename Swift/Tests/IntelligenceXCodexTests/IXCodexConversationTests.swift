import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexConversationTests: XCTestCase {
    func testMalformedHostedFunctionArgumentsNeverReachTheExecutor() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-malformed-tool",
                "status": "completed",
                "output": [[
                    "type": "function_call",
                    "call_id": "call-malformed",
                    "name": "control_home",
                    "arguments": "{\"entity_id\":",
                ]],
            ])),
        ])
        let counter = ToolExecutionCounter()
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(
                input: [.text("Turn it on")],
                instructions: "Use tools.",
                tools: [.init(
                    name: "control_home",
                    description: "Control a device.",
                    parameters: .object(["type": .string("object")])
                )],
                executor: IXClosureCodexToolExecutor { call in
                    await counter.record()
                    return .success(callID: call.id, message: "Changed")
                }
            )
            XCTFail("Malformed function arguments must fail closed")
        } catch let IXCodexError.malformedToolCall(detail) {
            XCTAssertTrue(detail.contains("control_home"))
        }
        let executionCount = await counter.count
        XCTAssertEqual(executionCount, 0)
    }

    func testCustomToolPreservesArbitraryRawInput() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-custom-tool",
                "status": "completed",
                "output": [[
                    "type": "custom_tool_call",
                    "call_id": "call-custom",
                    "name": "freeform",
                    "input": "plain text, not JSON",
                ]],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-custom-finished",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Done"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        let result = try await conversation.run(
            input: [.text("Use the freeform tool")],
            instructions: "Use tools.",
            tools: [.init(
                name: "freeform",
                description: "Accept free-form input.",
                parameters: .object(["type": .string("object")])
            )],
            executor: IXClosureCodexToolExecutor { call in
                XCTAssertEqual(call.kind, .custom)
                XCTAssertEqual(
                    call.arguments["raw"]?.stringValue,
                    "plain text, not JSON"
                )
                return .success(callID: call.id, message: "Accepted")
            }
        )

        XCTAssertEqual(result.turn.text, "Done")
    }

    func testConfirmationRequiredToolFailsClosedWithoutApprovalHandler() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-tool",
                "status": "completed",
                "output": [[
                    "type": "function_call",
                    "call_id": "call-risky",
                    "name": "open_gate",
                    "arguments": "{}",
                ]],
            ])),
        ])
        let counter = ToolExecutionCounter()
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(
                input: [.text("Open it")],
                instructions: "Use tools.",
                tools: [.init(
                    name: "open_gate",
                    description: "Open a gate.",
                    parameters: .object(["type": .string("object")]),
                    requiresConfirmation: true
                )],
                executor: IXClosureCodexToolExecutor { call in
                    await counter.record()
                    return .success(callID: call.id, message: "Opened")
                }
            )
            XCTFail("A confirmation-required tool must fail closed")
        } catch let IXCodexError.toolConfirmationRequired(names) {
            XCTAssertEqual(names, ["open_gate"])
        }
        let executionCount = await counter.count
        XCTAssertEqual(executionCount, 0)
    }

    func testNegativeToolRoundLimitIsRejectedWithoutRequest() async throws {
        let state = ResponseQueue(responses: [])
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(
                input: [.text("Hello")],
                instructions: "Help.",
                maximumToolRounds: -1
            )
            XCTFail("A negative limit must be rejected")
        } catch let IXCodexError.invalidResponse(message) {
            XCTAssertTrue(message.contains("maximumToolRounds"))
        }
        let requests = await state.requests
        XCTAssertTrue(requests.isEmpty)
    }

    func testStreamingTransportDeliversTextDeltasInOrder() async throws {
        let body = Data("""
        data: {"type":"response.output_text.delta","delta":"Hello "}

        data: {"type":"response.refusal.delta","delta":"home"}

        data: {"type":"response.completed","response":{"id":"streamed","status":"completed","output":[]}}

        data: [DONE]

        """.utf8)
        let http = StreamingHTTPClient(chunks: [
            Data(body.prefix(37)),
            Data(body.dropFirst(37).prefix(51)),
            Data(body.dropFirst(88)),
        ])
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account"
            )),
            httpClient: http
        )
        let deltas = TextDeltaRecorder()
        let conversation = IXCodexConversation(client: IXCodexClient(
            authSession: auth,
            httpClient: http
        ))

        let result = try await conversation.run(
            input: [.text("Hello")],
            instructions: "Help.",
            onTextDelta: { delta in await deltas.append(delta) }
        )

        XCTAssertEqual(result.turn.text, "Hello home")
        let recordedDeltas = await deltas.values
        XCTAssertEqual(recordedDeltas, ["Hello ", "home"])
    }

    func testRestoredTranscriptIsReplayedBeforeTheNewPrompt() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-restored",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [[
                        "type": "output_text",
                        "text": "Both rooms are now on",
                    ]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        await conversation.restoreTranscript([
            .init(
                role: .user,
                text: "Check the kitchen",
                images: [
                    .init(
                        id: "saved-image",
                        data: Data([0x01, 0x02]),
                        mimeType: "image/png"
                    ),
                ]
            ),
            .init(role: .assistant, text: "The kitchen is off"),
        ])

        _ = try await conversation.run(
            input: [.text("Turn on the kitchen and living room")],
            instructions: "Help."
        )

        let requests = await state.requests
        let body = try IXJSONValue.decode(
            try XCTUnwrap(requests.first?.httpBody)
        )
        let input = try XCTUnwrap(body["input"]?.arrayValue)
        XCTAssertEqual(input.count, 3)
        XCTAssertEqual(input[0]["role"]?.stringValue, "user")
        XCTAssertEqual(
            input[0]["content"]?.arrayValue?.first?["text"]?.stringValue,
            "Check the kitchen"
        )
        XCTAssertEqual(
            input[0]["content"]?.arrayValue?[1]["type"]?.stringValue,
            "input_image"
        )
        XCTAssertEqual(input[1]["role"]?.stringValue, "assistant")
        XCTAssertEqual(
            input[1]["content"]?.arrayValue?.first?["type"]?.stringValue,
            "output_text"
        )
        XCTAssertEqual(
            input[2]["content"]?.arrayValue?.first?["text"]?.stringValue,
            "Turn on the kitchen and living room"
        )
    }

    func testConversationAggregatesReasoningUsageAcrossToolRounds() async throws {
        let first = sse(response: [
            "id": "response-tool",
            "status": "completed",
            "usage": [
                "input_tokens": 120,
                "output_tokens": 30,
                "total_tokens": 150,
                "output_tokens_details": ["reasoning_tokens": 20],
            ],
            "output": [[
                "type": "function_call",
                "call_id": "call-usage",
                "name": "inspect",
                "arguments": "{}",
            ]],
        ])
        let second = sse(response: [
            "id": "response-answer",
            "status": "completed",
            "usage": [
                "input_tokens": 160,
                "output_tokens": 40,
                "total_tokens": 200,
                "output_tokens_details": ["reasoning_tokens": 12],
            ],
            "output": [[
                "type": "message",
                "content": [["type": "output_text", "text": "Done"]],
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: first),
            IXHTTPResponse(statusCode: 200, body: second),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        let result = try await conversation.run(
            input: [.text("Inspect")],
            instructions: "Use tools.",
            tools: [.init(
                name: "inspect",
                description: "Inspect.",
                parameters: .object(["type": .string("object")])
            )],
            executor: IXClosureCodexToolExecutor { call in
                .success(callID: call.id, message: "Ready")
            }
        )

        XCTAssertEqual(result.usage?.inputTokens, 280)
        XCTAssertEqual(result.usage?.outputTokens, 70)
        XCTAssertEqual(result.usage?.reasoningTokens, 32)
        XCTAssertEqual(result.usage?.totalTokens, 350)
    }

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
        XCTAssertEqual(input.last?["type"]?.stringValue, "function_call_output")
        XCTAssertEqual(input.last?["call_id"]?.stringValue, "call-1")
        XCTAssertEqual(requests.first?.value(forHTTPHeaderField: "chatgpt-account-id"), "account")
        XCTAssertNil(secondBody["previous_response_id"])
    }

    func testConversationCanNarrowToolsAfterCompletedRound() async throws {
        let first = sse(response: [
            "id": "response-query",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-query",
                "name": "query_home",
                "arguments": "{}",
            ]],
        ])
        let second = sse(response: [
            "id": "response-answer",
            "status": "completed",
            "output": [[
                "type": "message",
                "content": [["type": "output_text", "text": "The light is off"]],
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: first),
            IXHTTPResponse(statusCode: 200, body: second),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let query = IXCodexToolDefinition(
            name: "query_home",
            description: "Read state.",
            parameters: .object(["type": .string("object")])
        )
        let control = IXCodexToolDefinition(
            name: "control_home",
            description: "Change state.",
            parameters: .object(["type": .string("object")])
        )

        let result = try await conversation.run(
            input: [.text("Is the light on?")],
            instructions: "Use tools.",
            tools: [query, control],
            executor: IXClosureCodexToolExecutor { call in
                .success(callID: call.id, message: "Off")
            },
            continuationTools: { calls, results, availableTools in
                XCTAssertEqual(calls.map(\.name), ["query_home"])
                XCTAssertEqual(results.map(\.callID), ["call-query"])
                XCTAssertEqual(availableTools, [query, control])
                return [query]
            }
        )

        XCTAssertEqual(result.turn.text, "The light is off")
        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        let firstBody = try IXJSONValue.decode(
            try XCTUnwrap(requests.first?.httpBody)
        )
        let secondBody = try IXJSONValue.decode(
            try XCTUnwrap(requests.last?.httpBody)
        )
        XCTAssertEqual(firstBody["tools"]?.arrayValue?.count, 2)
        let continuationTools = try XCTUnwrap(
            secondBody["tools"]?.arrayValue
        )
        XCTAssertEqual(continuationTools.count, 1)
        let continuationTool = try XCTUnwrap(continuationTools.first)
        XCTAssertEqual(
            continuationTool["name"]?.stringValue ??
                continuationTool["function"]?["name"]?.stringValue,
            "query_home"
        )
    }

    func testConversationCanCompleteAHostedToolRoundLocally() async throws {
        let first = sse(response: [
            "id": "response-action",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-action",
                "name": "control_home",
                "arguments": "{}",
            ]],
        ])
        let second = sse(response: [
            "id": "response-follow-up",
            "status": "completed",
            "output": [[
                "type": "message",
                "content": [["type": "output_text", "text": "Still on"]],
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: first),
            IXHTTPResponse(statusCode: 200, body: second),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let result = try await conversation.run(
            input: [.text("Turn on both rooms")],
            instructions: "Act first.",
            tools: [.init(
                name: "control_home",
                description: "Change state.",
                parameters: .object(["type": .string("object")])
            )],
            executor: IXClosureCodexToolExecutor { call in
                .success(callID: call.id, message: "Confirmed")
            },
            completeToolRound: { calls, results in
                XCTAssertEqual(calls.map(\.name), ["control_home"])
                XCTAssertEqual(results.map(\.callID), ["call-action"])
                return "  ✓ 2  "
            }
        )

        XCTAssertEqual(result.turn.text, "✓ 2")
        XCTAssertEqual(result.toolCalls.map(\.id), ["call-action"])
        let firstRequestCount = await state.requests.count
        XCTAssertEqual(firstRequestCount, 1)

        _ = try await conversation.run(
            input: [.text("Are they still on?")],
            instructions: "Act first."
        )
        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        let body = try IXJSONValue.decode(
            try XCTUnwrap(requests.last?.httpBody)
        )
        let input = try XCTUnwrap(body["input"]?.arrayValue)
        XCTAssertEqual(input.count, 5)
        XCTAssertEqual(input[3]["role"]?.stringValue, "assistant")
        XCTAssertEqual(
            input[3]["content"]?.arrayValue?.first?["text"]?.stringValue,
            "✓ 2"
        )
        XCTAssertEqual(
            input[4]["content"]?.arrayValue?.first?["text"]?.stringValue,
            "Are they still on?"
        )
    }

    func testResetInvalidatesCancellationIgnoringLocalCompletion() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-tool",
                "status": "completed",
                "output": [[
                    "type": "function_call",
                    "call_id": "call-reset-completion",
                    "name": "control_home",
                    "arguments": "{}",
                ]],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-new",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "New"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let completion = CancellationIgnoringTextGate(value: "Stale")
        let oldRun = Task {
            try await conversation.run(
                input: [.text("Old")],
                instructions: "Act first.",
                tools: [.init(
                    name: "control_home",
                    description: "Change state.",
                    parameters: .object(["type": .string("object")])
                )],
                executor: IXClosureCodexToolExecutor { call in
                    .success(callID: call.id, message: "Changed")
                },
                completeToolRound: { _, _ in
                    await completion.waitAndReturn()
                }
            )
        }

        await completion.waitUntilStarted()
        await conversation.reset()
        await completion.release()
        do {
            _ = try await oldRun.value
            XCTFail("Reset should invalidate the local completion")
        } catch is CancellationError {
        }

        let result = try await conversation.run(
            input: [.text("New prompt")],
            instructions: "Help."
        )
        XCTAssertEqual(result.turn.text, "New")
        let requests = await state.requests
        let body = try IXJSONValue.decode(
            try XCTUnwrap(requests.last?.httpBody)
        )
        let input = try XCTUnwrap(body["input"]?.arrayValue)
        XCTAssertEqual(input.count, 1)
        XCTAssertEqual(
            input.first?["content"]?.arrayValue?.first?["text"]?.stringValue,
            "New prompt"
        )
    }

    func testResetDuringLocalCompletionDeltaDoesNotReturnStaleSuccess()
        async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-tool",
                "status": "completed",
                "output": [[
                    "type": "function_call",
                    "call_id": "call-reset-delta",
                    "name": "control_home",
                    "arguments": "{}",
                ]],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-new",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "New"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let delta = CancellationIgnoringVoidGate()
        let oldRun = Task {
            try await conversation.run(
                input: [.text("Old")],
                instructions: "Act first.",
                tools: [.init(
                    name: "control_home",
                    description: "Change state.",
                    parameters: .object(["type": .string("object")])
                )],
                executor: IXClosureCodexToolExecutor { call in
                    .success(callID: call.id, message: "Changed")
                },
                completeToolRound: { _, _ in "Stale" },
                onTextDelta: { _ in await delta.wait() }
            )
        }

        await delta.waitUntilStarted()
        await conversation.reset()
        await delta.release()
        do {
            _ = try await oldRun.value
            XCTFail("Reset during the delta callback should invalidate the run")
        } catch is CancellationError {
        }

        let result = try await conversation.run(
            input: [.text("New prompt")],
            instructions: "Help."
        )
        XCTAssertEqual(result.turn.text, "New")
        let requests = await state.requests
        let body = try IXJSONValue.decode(
            try XCTUnwrap(requests.last?.httpBody)
        )
        let input = try XCTUnwrap(body["input"]?.arrayValue)
        XCTAssertEqual(input.count, 1)
        XCTAssertEqual(
            input.first?["content"]?.arrayValue?.first?["text"]?.stringValue,
            "New prompt"
        )
    }

    func testConversationRejectsContinuationToolThatWasNotOffered() async throws {
        let first = sse(response: [
            "id": "response-query",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-query",
                "name": "query_home",
                "arguments": "{}",
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: first),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(
                input: [.text("Inspect")],
                instructions: "Use tools.",
                tools: [.init(
                    name: "query_home",
                    description: "Read state.",
                    parameters: .object(["type": .string("object")])
                )],
                executor: IXClosureCodexToolExecutor { call in
                    .success(callID: call.id, message: "Ready")
                },
                continuationTools: { _, _, _ in
                    [.init(
                        name: "open_gate",
                        description: "Open a gate.",
                        parameters: .object(["type": .string("object")])
                    )]
                }
            )
            XCTFail("A continuation provider must not add new capabilities")
        } catch let IXCodexError.invalidResponse(message) {
            XCTAssertTrue(message.contains("was not offered"))
        }
    }

    func testRemovedContinuationToolNeverReachesApprovalOrExecution() async throws {
        let first = sse(response: [
            "id": "response-query",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-query",
                "name": "query_home",
                "arguments": "{}",
            ]],
        ])
        let second = sse(response: [
            "id": "response-removed-control",
            "status": "completed",
            "output": [[
                "type": "function_call",
                "call_id": "call-open-gate",
                "name": "open_gate",
                "arguments": "{}",
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: first),
            IXHTTPResponse(statusCode: 200, body: second),
        ])
        let executionCounter = ToolExecutionCounter()
        let approvalCounter = ToolExecutionCounter()
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(
                input: [.text("Inspect, then open it if needed")],
                instructions: "Use tools.",
                tools: [
                    .init(
                        name: "query_home",
                        description: "Read state.",
                        parameters: .object(["type": .string("object")])
                    ),
                    .init(
                        name: "open_gate",
                        description: "Open a gate.",
                        parameters: .object(["type": .string("object")]),
                        requiresConfirmation: true
                    ),
                ],
                executor: IXClosureCodexToolExecutor { call in
                    await executionCounter.record()
                    return .success(callID: call.id, message: "Ready")
                },
                approveTools: { _, _ in
                    await approvalCounter.record()
                    return true
                },
                continuationTools: { _, _, _ in [] }
            )
            XCTFail("A removed continuation tool must fail closed")
        } catch let IXCodexError.malformedToolCall(detail) {
            XCTAssertTrue(detail.contains("open_gate"))
            XCTAssertTrue(detail.contains("not offered"))
        }

        let executionCount = await executionCounter.count
        let approvalCount = await approvalCounter.count
        XCTAssertEqual(executionCount, 1)
        XCTAssertEqual(approvalCount, 0)
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
        XCTAssertEqual(parsed.eventTypes, [
            "response.output_text.delta",
            "response.output_text.delta",
            "response.completed",
        ])
    }

    func testSSEParserAssemblesFunctionCallFromAddedAndArgumentEvents() throws {
        let data = Data("""
        data: {"type":"response.output_item.added","item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"get_home_state","arguments":""}}

        data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","delta":"{\\"scope\\":"}

        data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","delta":"\\"summary\\"}"}

        data: {"type":"response.function_call_arguments.done","item_id":"fc_1","name":"get_home_state","arguments":"{\\"scope\\":\\"summary\\"}"}

        data: {"type":"response.output_item.done","item_id":"fc_1"}

        data: {"type":"response.completed","response":{"id":"r1","status":"completed","output":[]}}

        """.utf8)

        let parsed = try IXCodexSSEParser().parse(data)

        XCTAssertEqual(parsed.streamedItems.count, 1)
        XCTAssertEqual(parsed.streamedItems[0]["type"]?.stringValue, "function_call")
        XCTAssertEqual(parsed.streamedItems[0]["call_id"]?.stringValue, "call_1")
        XCTAssertEqual(parsed.streamedItems[0]["arguments"]?.stringValue, "{\"scope\":\"summary\"}")
    }

    func testSSEParserAssemblesCompactFunctionCallWithoutNestedItem() throws {
        let data = Data("""
        data: {"type":"response.output_item.added","item_id":"fc_compact"}

        data: {"type":"response.function_call_arguments.delta","item_id":"fc_compact","delta":"{}"}

        data: {"type":"response.function_call_arguments.done","item_id":"fc_compact","name":"get_home_state","arguments":"{}"}

        data: {"type":"response.output_item.done","item_id":"fc_compact"}

        data: {"type":"response.completed","response":{"id":"r1","status":"completed","output":[]}}

        """.utf8)

        let parsed = try IXCodexSSEParser().parse(data)

        XCTAssertEqual(parsed.streamedItems.count, 1)
        XCTAssertEqual(parsed.streamedItems[0]["type"]?.stringValue, "function_call")
        XCTAssertEqual(parsed.streamedItems[0]["call_id"]?.stringValue, "fc_compact")
        XCTAssertEqual(parsed.streamedItems[0]["name"]?.stringValue, "get_home_state")
        XCTAssertEqual(parsed.streamedItems[0]["arguments"]?.stringValue, "{}")
    }

    func testConversationUsesStreamedToolItemWhenCompletedOutputIsEmpty() async throws {
        let streamedTool = Data("""
        data: {"type":"response.output_item.added","item":{"id":"fc_stream","type":"function_call","call_id":"call_stream","name":"get_home_state","arguments":""}}

        data: {"type":"response.function_call_arguments.done","item_id":"fc_stream","arguments":"{}"}

        data: {"type":"response.output_item.done","item":{"id":"fc_stream","type":"function_call","call_id":"call_stream","name":"get_home_state","arguments":"{}"}}

        data: {"type":"response.completed","response":{"id":"r1","status":"completed","output":[]}}

        """.utf8)
        let final = sse(response: [
            "id": "r2",
            "status": "completed",
            "output": [[
                "type": "message",
                "content": [["type": "output_text", "text": "Healthy"]],
            ]],
        ])
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: streamedTool),
            IXHTTPResponse(statusCode: 200, body: final),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let executor = IXClosureCodexToolExecutor { call in
            XCTAssertEqual(call.name, "get_home_state")
            return .success(callID: call.id, message: "Healthy")
        }

        let result = try await conversation.run(
            input: [.text("Health?")],
            instructions: "Use tools.",
            tools: [.init(
                name: "get_home_state",
                description: "Inspect state.",
                parameters: .object(["type": .string("object")])
            )],
            executor: executor
        )

        XCTAssertEqual(result.turn.text, "Healthy")
        XCTAssertEqual(result.toolCalls.map(\.id), ["call_stream"])
    }

    func testConversationRejectsSilentEmptyResponseWithEventDiagnostics() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: Data("""
            data: {"type":"response.reasoning_summary_text.delta","delta":"Checking"}

            data: {"type":"response.completed","response":{"id":"r1","status":"completed","output":[]}}

            data: [DONE]

            """.utf8)),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(input: [.text("Hello")], instructions: "Help.")
            XCTFail("Expected an empty-output error")
        } catch let error as IXCodexError {
            XCTAssertTrue(
                error.localizedDescription.contains("response.reasoning_summary_text.delta"),
                "Unexpected error: \(error)"
            )
        }
    }

    func testConversationRejectsWhitespaceOnlyAssistantOutput() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-empty",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": " \n\t "]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(input: [.text("Hello")], instructions: "Help.")
            XCTFail("Expected a whitespace-only output error")
        } catch let error as IXCodexError {
            XCTAssertTrue(
                error.localizedDescription.contains("no usable assistant output"),
                "Unexpected error: \(error)"
            )
        }
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
                parameters: .object([
                    "type": .string("object"),
                    "properties": .object([:]),
                    "required": .array([]),
                    "additionalProperties": .bool(false),
                ]),
                strict: true
            )]
        )

        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        let first = try IXJSONValue.decode(try XCTUnwrap(requests[0].httpBody))
        let second = try IXJSONValue.decode(try XCTUnwrap(requests[1].httpBody))
        XCTAssertNotNil(first["tools"]?.arrayValue?.first?["function"]?["parameters"])
        XCTAssertNotNil(second["tools"]?.arrayValue?.first?["function"]?["input_schema"])
        XCTAssertEqual(
            first["tools"]?.arrayValue?.first?["function"]?["strict"]?.boolValue,
            true
        )
        XCTAssertEqual(
            second["tools"]?.arrayValue?.first?["function"]?["strict"]?.boolValue,
            true
        )
    }

    func testAcceptedToolSchemaIsReusedWithoutRepeatedRejectedRequests() async throws {
        let rejected = IXHTTPResponse.json(400, [
            "error": [
                "message": "Unknown parameter: tools[0].function.parameters",
                "param": "tools[0].function.parameters",
            ],
        ])
        let successfulTurn = IXHTTPResponse(statusCode: 200, body: sse(response: [
            "id": "response",
            "status": "completed",
            "output": [[
                "type": "message",
                "content": [["type": "output_text", "text": "Ready"]],
            ]],
        ]))
        let state = ResponseQueue(responses: [rejected, successfulTurn, successfulTurn])
        let client = makeClient(state)
        let tool = IXCodexToolDefinition(
            name: "inspect",
            description: "Inspect state.",
            parameters: .object(["type": .string("object")])
        )

        _ = try await IXCodexConversation(client: client).run(
            input: [.text("First")],
            instructions: "Use tools.",
            tools: [tool]
        )
        _ = try await IXCodexConversation(client: client).run(
            input: [.text("Second")],
            instructions: "Use tools.",
            tools: [tool]
        )

        let requests = await state.requests
        XCTAssertEqual(requests.count, 3)
        let reused = try IXJSONValue.decode(try XCTUnwrap(requests.last?.httpBody))
        XCTAssertNotNil(reused["tools"]?.arrayValue?.first?["function"]?["input_schema"])
    }

    func testUnsupportedDefaultModelFallsBackWithoutLosingInput() async throws {
        let state = ResponseQueue(responses: [
            .json(400, ["error": ["message": "The model is not supported for this ChatGPT account"]]),
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

    func testExplicitUnsupportedModelDoesNotSilentlyFallBack() async throws {
        let state = ResponseQueue(responses: [
            .json(400, ["error": ["message": "The model is not supported for this ChatGPT account"]]),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        do {
            _ = try await conversation.run(
                input: [.text("Hello")],
                instructions: "Help.",
                model: "chosen-model"
            )
            XCTFail("An explicitly selected model must not silently change")
        } catch {
        }

        let requests = await state.requests
        XCTAssertEqual(requests.count, 1)
        let body = try IXJSONValue.decode(try XCTUnwrap(requests.first?.httpBody))
        XCTAssertEqual(body["model"]?.stringValue, "chosen-model")
    }

    func testToolCallsFromOneModelTurnAreOfferedAsOneBatch() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-tools",
                "status": "completed",
                "output": [
                    [
                        "type": "function_call",
                        "call_id": "call-one",
                        "name": "change",
                        "arguments": #"{"device":"one"}"#,
                    ],
                    [
                        "type": "function_call",
                        "call_id": "call-two",
                        "name": "change",
                        "arguments": #"{"device":"two"}"#,
                    ],
                ],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-answer",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Done"]],
                ]],
            ])),
        ])
        let executor = BatchRecordingToolExecutor()
        let conversation = IXCodexConversation(client: makeClient(state))

        _ = try await conversation.run(
            input: [.text("Change both")],
            instructions: "Use tools.",
            tools: [
                .init(
                    name: "change",
                    description: "Change a device.",
                    parameters: .object(["type": .string("object")])
                ),
            ],
            executor: executor
        )

        let batches = await executor.batches
        XCTAssertEqual(batches, [["call-one", "call-two"]])
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

    func testModelCatalogIncludesSupportedReasoningEfforts() async throws {
        let state = ResponseQueue(responses: [
            .json(200, ["models": [[
                "id": "gpt-home-internal",
                "slug": "gpt-home",
                "display_name": "GPT Home",
                "description": "Home reasoning model",
                "default_reasoning_level": "medium",
                "supported_reasoning_levels": [
                    ["effort": "low", "description": "Fast"],
                    ["effort": "medium", "description": "Balanced"],
                    ["effort": "high", "description": "Deep"],
                ],
            ], [
                "slug": "codex-auto-review",
                "display_name": "Codex Auto Review",
                "visibility": "hide",
            ]]]),
        ])
        let client = makeClient(state)

        let models = try await client.models()

        XCTAssertEqual(models.count, 1)
        XCTAssertEqual(models[0].id, "gpt-home")
        XCTAssertEqual(models[0].displayName, "GPT Home")
        XCTAssertEqual(models[0].defaultReasoningEffort, .medium)
        XCTAssertEqual(models[0].supportedReasoningEfforts.map(\.effort), [.low, .medium, .high])
        let requests = await state.requests
        let request = try XCTUnwrap(requests.first)
        let components = try XCTUnwrap(URLComponents(
            url: try XCTUnwrap(request.url),
            resolvingAgainstBaseURL: false
        ))
        XCTAssertEqual(
            components.queryItems?.first(where: { $0.name == "client_version" })?.value,
            "0.146.0-alpha.3.1"
        )
    }

    func testModelCatalogPrefersRunnableSlugOverInternalID() async throws {
        let state = ResponseQueue(responses: [
            .json(200, ["models": [[
                "id": "gpt-5-5-instant",
                "slug": "gpt-5.6-sol",
                "display_name": "GPT-5.6-Sol",
            ]]]),
        ])

        let models = try await makeClient(state).models()

        XCTAssertEqual(models.map(\.id), ["gpt-5.6-sol"])
        XCTAssertEqual(models.map(\.displayName), ["GPT-5.6-Sol"])
    }

    func testModelCatalogDoesNotRetryFallbackURLsAfterCancellation()
        async throws {
        let requestCounter = RequestCountRecorder()
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account"
            ))
        )
        let client = IXCodexClient(
            authSession: auth,
            httpClient: IXClosureHTTPClient { _ in
                await requestCounter.record()
                throw CancellationError()
            }
        )

        do {
            _ = try await client.models()
            XCTFail("Cancellation must stop model endpoint fallback")
        } catch is CancellationError {
        }
        let requestCount = await requestCounter.count
        XCTAssertEqual(requestCount, 1)
    }

    func testSelectedReasoningEffortIsSentOnEveryRequest() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-reasoning",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Ready"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        _ = try await conversation.run(
            input: [.text("Analyze")],
            instructions: "Help.",
            model: "gpt-home",
            reasoningEffort: .high
        )

        let requests = await state.requests
        let request = try XCTUnwrap(requests.first)
        let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
        XCTAssertEqual(body["model"]?.stringValue, "gpt-home")
        XCTAssertEqual(body["reasoning"]?["effort"]?.stringValue, "high")
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
        XCTAssertTrue(recoveryInput.contains { $0["type"]?.stringValue == "function_call" })
        XCTAssertTrue(recoveryInput.contains { $0["type"]?.stringValue == "function_call_output" })
    }

    func testCallerCancellationCheckpointsAnAlreadyExecutedToolBeforeStopping() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "tool-response",
                "status": "completed",
                "output": [[
                    "type": "function_call",
                    "call_id": "call-side-effect",
                    "name": "toggle_light",
                    "arguments": "{}",
                ]],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "recovered-response",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Still changed once."]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))
        let executor = CancellationIgnoringToolGate()
        let firstRun = Task {
            try await conversation.run(
                input: [.text("Toggle")],
                instructions: "Help.",
                tools: [IXCodexToolDefinition(
                    name: "toggle_light",
                    description: "Toggle",
                    parameters: .object(["type": .string("object")])
                )],
                executor: executor
            )
        }

        await executor.waitUntilStarted()
        firstRun.cancel()
        await executor.release()
        do {
            _ = try await firstRun.value
            XCTFail("Caller cancellation should stop the model continuation")
        } catch is CancellationError {
        }

        let recovered = try await conversation.run(
            input: [.text("What happened?")],
            instructions: "Help."
        )

        XCTAssertEqual(recovered.turn.text, "Still changed once.")
        let executionCount = await executor.executionCount
        XCTAssertEqual(executionCount, 1)
        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        let recoveredBody = try IXJSONValue.decode(
            try XCTUnwrap(requests.last?.httpBody)
        )
        let replay = try XCTUnwrap(recoveredBody["input"]?.arrayValue)
        XCTAssertTrue(replay.contains { item in
            item["type"]?.stringValue == "function_call_output" &&
                item["call_id"]?.stringValue == "call-side-effect"
        })
    }

    func testLongConversationUsesRemoteCompactionBeforeContinuing() async throws {
        let retainedItem: [String: Any] = [
            "type": "message",
            "role": "user",
            "content": [[
                "type": "input_text",
                "text": "Retained recent context",
            ]],
        ]
        let compactedItem: [String: Any] = [
            "type": "compaction",
            "id": "compact-1",
            "encrypted_content": "opaque-checkpoint",
        ]
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "compaction-response",
                "status": "completed",
                "output": [compactedItem, retainedItem],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-after-compaction",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Continued"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(
            client: makeClient(state),
            maximumHistoryItemsBeforeCompaction: 8
        )
        await conversation.restoreTranscript((0..<8).map { index in
            IXCodexTranscriptMessage(
                role: index.isMultiple(of: 2) ? .user : .assistant,
                text: "Message \(index)"
            )
        })

        let result = try await conversation.run(
            input: [.text("Continue")],
            instructions: "Help."
        )

        XCTAssertEqual(result.turn.text, "Continued")
        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
        XCTAssertEqual(requests[0].url?.path, "/backend-api/codex/responses/compact")
        let compactBody = try IXJSONValue.decode(
            try XCTUnwrap(requests[0].httpBody)
        )
        XCTAssertEqual(compactBody["input"]?.arrayValue?.count, 9)
        let responseBody = try IXJSONValue.decode(
            try XCTUnwrap(requests[1].httpBody)
        )
        XCTAssertEqual(responseBody["input"]?.arrayValue?.count, 2)
        XCTAssertEqual(
            responseBody["input"]?.arrayValue?.first?["type"]?.stringValue,
            "compaction"
        )
        XCTAssertEqual(
            responseBody["input"]?.arrayValue?.last?["type"]?.stringValue,
            "message"
        )
    }

    func testResetDuringRemoteCompactionDoesNotSendAStaleFollowUp() async throws {
        let gate = ConversationResponseGate(
            first: IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "compaction-response",
                "status": "completed",
                "output": [[
                    "type": "compaction",
                    "id": "compact-reset",
                    "encrypted_content": "opaque-reset-checkpoint",
                ]],
            ])),
            later: IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "unexpected-response",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Unexpected"]],
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
            httpClient: IXClosureHTTPClient { request in
                try await gate.send(request)
            }
        )
        let conversation = IXCodexConversation(
            client: IXCodexClient(
                authSession: auth,
                httpClient: IXClosureHTTPClient { request in
                    try await gate.send(request)
                }
            ),
            maximumHistoryItemsBeforeCompaction: 8
        )
        await conversation.restoreTranscript((0..<8).map { index in
            IXCodexTranscriptMessage(
                role: index.isMultiple(of: 2) ? .user : .assistant,
                text: "Message \(index)"
            )
        })
        let run = Task {
            try await conversation.run(
                input: [.text("Continue")],
                instructions: "Help."
            )
        }
        await gate.waitUntilFirstRequest()
        await conversation.reset()
        await gate.releaseFirst()

        do {
            _ = try await run.value
            XCTFail("Reset should invalidate a turn awaiting compaction")
        } catch is CancellationError {
        }
        let requests = await gate.requests
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(
            requests.first?.url?.path,
            "/backend-api/codex/responses/compact"
        )
    }

    func testResetDuringContextLimitResponseDoesNotStartCompaction() async throws {
        let gate = ConversationResponseGate(
            first: .json(400, [
                "error": [
                    "message": "context_length_exceeded: maximum context length reached",
                ],
            ]),
            later: IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "unexpected-compaction",
                "status": "completed",
                "output": [[
                    "type": "compaction",
                    "id": "compact-unexpected",
                    "encrypted_content": "must-not-be-sent",
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
            httpClient: IXClosureHTTPClient { request in
                try await gate.send(request)
            }
        )
        let conversation = IXCodexConversation(client: IXCodexClient(
            authSession: auth,
            httpClient: IXClosureHTTPClient { request in
                try await gate.send(request)
            }
        ))
        let run = Task {
            try await conversation.run(
                input: [.text("Continue")],
                instructions: "Help."
            )
        }
        await gate.waitUntilFirstRequest()
        await conversation.reset()
        await gate.releaseFirst()

        do {
            _ = try await run.value
            XCTFail("Reset should prevent reactive compaction")
        } catch is CancellationError {
        }
        let requests = await gate.requests
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(
            requests.first?.url?.path,
            "/backend-api/codex/responses"
        )
    }

    func testContextLimitFailureCompactsAndRetriesTheSameTurn() async throws {
        let state = ResponseQueue(responses: [
            .json(400, [
                "error": [
                    "message": "context_length_exceeded: maximum context length reached",
                ],
            ]),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "compaction-response",
                "status": "completed",
                "output": [[
                    "type": "compaction",
                    "id": "compact-retry",
                    "encrypted_content": "opaque-retry-checkpoint",
                ]],
            ])),
            IXHTTPResponse(statusCode: 200, body: sse(response: [
                "id": "response-after-retry",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [["type": "output_text", "text": "Recovered"]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: makeClient(state))

        let result = try await conversation.run(
            input: [.text("Continue")],
            instructions: "Help."
        )

        XCTAssertEqual(result.turn.text, "Recovered")
        let requests = await state.requests
        XCTAssertEqual(requests.map { $0.url?.path }, [
            "/backend-api/codex/responses",
            "/backend-api/codex/responses/compact",
            "/backend-api/codex/responses",
        ])
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

actor RequestCountRecorder {
    private(set) var count = 0

    func record() {
        count += 1
    }
}

actor TextDeltaRecorder {
    private(set) var values: [String] = []

    func append(_ value: String) {
        values.append(value)
    }
}

final class StreamingHTTPClient: IXHTTPStreamingClient, @unchecked Sendable {
    private let chunks: [Data]

    init(chunks: [Data]) {
        self.chunks = chunks
    }

    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        IXHTTPResponse(statusCode: 200, body: chunks.reduce(into: Data()) {
            $0.append($1)
        })
    }

    func stream(_ request: URLRequest) async throws -> IXHTTPStreamingResponse {
        let chunks = chunks
        return IXHTTPStreamingResponse(
            statusCode: 200,
            body: AsyncThrowingStream { continuation in
                for chunk in chunks { continuation.yield(chunk) }
                continuation.finish()
            }
        )
    }
}

actor BatchRecordingToolExecutor: IXCodexToolExecuting {
    private(set) var batches: [[String]] = []

    func execute(_ call: IXCodexToolCall) async -> IXCodexToolResult {
        .success(callID: call.id, message: "Done")
    }

    func execute(_ calls: [IXCodexToolCall]) async -> [IXCodexToolResult] {
        batches.append(calls.map(\.id))
        return calls.map { .success(callID: $0.id, message: "Done") }
    }
}

actor CancellationIgnoringToolGate: IXCodexToolExecuting {
    private var startedContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?
    private(set) var executionCount = 0

    func execute(_ call: IXCodexToolCall) async -> IXCodexToolResult {
        executionCount += 1
        startedContinuation?.resume()
        startedContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        return .success(callID: call.id, message: "Changed")
    }

    func waitUntilStarted() async {
        guard executionCount == 0 else { return }
        await withCheckedContinuation { startedContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

actor CancellationIgnoringTextGate {
    private let value: String
    private var started = false
    private var startedContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    init(value: String) {
        self.value = value
    }

    func waitAndReturn() async -> String {
        started = true
        startedContinuation?.resume()
        startedContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        return value
    }

    func waitUntilStarted() async {
        guard !started else { return }
        await withCheckedContinuation { startedContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

actor CancellationIgnoringVoidGate {
    private var started = false
    private var startedContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    func wait() async {
        started = true
        startedContinuation?.resume()
        startedContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
    }

    func waitUntilStarted() async {
        guard !started else { return }
        await withCheckedContinuation { startedContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
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
