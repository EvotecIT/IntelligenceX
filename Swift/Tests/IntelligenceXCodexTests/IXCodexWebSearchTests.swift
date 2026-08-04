import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexWebSearchTests: XCTestCase {
    func testConversationSerializesHostedSearchPolicy() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: webSearchSSE(response: [
                "id": "response-search",
                "status": "completed",
                "output": [[
                    "type": "message",
                    "content": [[
                        "type": "output_text",
                        "text": "Grounded answer",
                    ]],
                ]],
            ])),
        ])
        let conversation = IXCodexConversation(client: webSearchClient(state))
        let unrelatedTool = IXCodexToolDefinition(
            name: "inspect_local_state",
            description: "Inspect local state.",
            parameters: .object([
                "type": .string("object"),
                "properties": .object([:]),
                "additionalProperties": .bool(false),
            ])
        )

        _ = try await conversation.run(
            input: [.text("What changed today?")],
            instructions: "Use current public information.",
            tools: [unrelatedTool],
            webSearch: .init(
                contextSize: .high,
                allowsLiveInternetAccess: false,
                requiresSearch: true
            )
        )

        let requests = await state.requests
        let request = try XCTUnwrap(requests.first)
        let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
        let tools = try XCTUnwrap(body["tools"]?.arrayValue)
        XCTAssertEqual(tools.count, 2)
        XCTAssertEqual(tools[0]["type"]?.stringValue, "web_search")
        XCTAssertEqual(tools[0]["search_context_size"]?.stringValue, "high")
        XCTAssertEqual(tools[0]["external_web_access"]?.boolValue, false)
        XCTAssertEqual(
            body["tool_choice"],
            .object(["type": .string("web_search")])
        )
        XCTAssertEqual(body["parallel_tool_calls"]?.boolValue, true)
        XCTAssertTrue(
            body["include"]?.arrayValue?.contains(
                .string("web_search_call.action.sources")
            ) == true
        )
    }

    func testConversationReturnsCitationsAndSearchActivity() async throws {
        let state = ResponseQueue(responses: [
            IXHTTPResponse(statusCode: 200, body: webSearchSSE(response: [
                "id": "response-search",
                "status": "completed",
                "output": [
                    [
                        "type": "web_search_call",
                        "id": "search-1",
                        "status": "completed",
                        "action": [
                            "type": "search",
                            "queries": ["CasaRay public information"],
                            "sources": [
                                ["url": "https://example.com/source"],
                                ["url": "file:///private/source"],
                            ],
                        ],
                    ],
                    [
                        "type": "message",
                        "content": [[
                            "type": "output_text",
                            "text": "Grounded answer",
                            "annotations": [
                                [
                                    "type": "url_citation",
                                    "title": "Public source",
                                    "url": "https://example.com/source",
                                    "start_index": 0,
                                    "end_index": 8,
                                ],
                                [
                                    "type": "url_citation",
                                    "title": "Unsafe source",
                                    "url": "javascript:alert(1)",
                                ],
                            ],
                        ]],
                    ],
                ],
            ])),
        ])
        let conversation = IXCodexConversation(client: webSearchClient(state))

        let result = try await conversation.run(
            input: [.text("Search for this")],
            instructions: "Use web search.",
            webSearch: .init()
        )

        XCTAssertEqual(result.turn.text, "Grounded answer")
        XCTAssertEqual(result.turn.citations, [
            .init(
                title: "Public source",
                url: try XCTUnwrap(URL(string: "https://example.com/source")),
                startIndex: 0,
                endIndex: 8
            ),
        ])
        XCTAssertEqual(result.turn.webSearchActivities.count, 1)
        XCTAssertEqual(
            result.turn.webSearchActivities[0].queries,
            ["CasaRay public information"]
        )
        XCTAssertEqual(
            result.turn.webSearchActivities[0].sourceURLs.map(\.absoluteString),
            ["https://example.com/source"]
        )
    }

    private func webSearchClient(_ state: ResponseQueue) -> IXCodexClient {
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

    private func webSearchSSE(response: Any) -> Data {
        let responseData = try! JSONSerialization.data(withJSONObject: response)
        let responseText = String(data: responseData, encoding: .utf8)!
        return Data(
            "data: {\"type\":\"response.completed\",\"response\":\(responseText)}\n\ndata: [DONE]\n\n".utf8
        )
    }
}
