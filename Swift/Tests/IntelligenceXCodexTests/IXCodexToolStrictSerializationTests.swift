import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexToolStrictSerializationTests: XCTestCase {
    func testFunctionWireFallbacksPreserveBothStrictnessValues() async throws {
        for strict in [false, true] {
            let rejection = IXHTTPResponse.json(400, ["error": [
                "message": "Unknown parameter: tools[0].function.parameters",
                "param": "tools[0].function.parameters",
            ]])
            let completed = Data("""
            data: {"type":"response.completed","response":{"id":"r","status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"Ready"}]}]}}

            data: [DONE]

            """.utf8)
            let queue = ResponseQueue(responses: [rejection, rejection, rejection,
                IXHTTPResponse(statusCode: 200, body: completed)])
            let http = IXClosureHTTPClient { request in try await queue.next(request) }
            let auth = IXCodexAuthSession(
                credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                    accessToken: "access", refreshToken: "refresh",
                    expiresAt: .distantFuture, accountID: "account")),
                httpClient: http)
            let parameters: IXJSONValue = .object([
                "type": .string("object"),
                "properties": .object(["query": .object([
                    "type": .array([.string("string"), .string("null")])])]),
                "required": .array(strict ? [.string("query")] : []),
                "additionalProperties": .bool(false),
            ])
            _ = try await IXCodexConversation(client: .init(authSession: auth, httpClient: http)).run(
                input: [.text("Read state")], instructions: "Use tools.",
                tools: [.init(name: "read_state", description: "Read state.",
                    parameters: parameters, strict: strict)])
            let requests = await queue.requests
            XCTAssertEqual(requests.count, 4)
            for (index, request) in requests.enumerated() {
                let payload = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
                let tool = try XCTUnwrap(payload["tools"]?.arrayValue?.first)
                let function = index < 2 ? try XCTUnwrap(tool["function"]) : tool
                XCTAssertEqual(function["strict"]?.boolValue, strict)
                XCTAssertEqual(function[index.isMultiple(of: 2) ? "parameters" : "input_schema"], parameters)
            }
        }
    }
}
