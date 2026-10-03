import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexDeadlineTests: XCTestCase {
    func testContinuousKeepalivesCannotExtendHostedResponseDeadline() async throws {
        let http = KeepaliveHTTP()
        let conversation = IXCodexConversation(client: makeClient(http))
        do {
            _ = try await conversation.run(input: [.text("Read")], instructions: "Answer",
                onTextDelta: { _ in })
            XCTFail("Expected elapsed-time deadline despite keepalives")
        } catch let error as URLError {
            XCTAssertEqual(error.code, .timedOut)
        }
        let chunks = await http.chunkCount()
        XCTAssertGreaterThan(chunks, 0)
        // The failed network round releases the conversation; retrying an
        // ordinary read does not leave it permanently busy.
        await http.finishKeepalives()
        let result = try await conversation.run(input: [.text("Try again")], instructions: "Answer")
        XCTAssertEqual(result.turn.text, "Ready")
    }

    func testNoncooperativeAuthenticationRefreshSharesResponseDeadline() async throws {
        let http = SuspendedHTTP()
        let auth = IXCodexAuthSession(credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
            accessToken: "expired", refreshToken: "refresh", expiresAt: .distantPast, accountID: "account"
        )), httpClient: http)
        let client = IXCodexClient(authSession: auth, httpClient: http, requestTimeoutInterval: 0.1)
        do {
            _ = try await IXCodexConversation(client: client).run(input: [.text("Hello")], instructions: "Answer")
            XCTFail("Expected authentication to be included in elapsed budget")
        } catch let error as URLError {
            XCTAssertEqual(error.code, .timedOut)
        }
        await http.release()
    }

    private func makeClient(_ http: any IXHTTPClient) -> IXCodexClient {
        let auth = IXCodexAuthSession(credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
            accessToken: "access", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account"
        )), httpClient: http)
        return IXCodexClient(authSession: auth, httpClient: http, requestTimeoutInterval: 0.2)
    }

    func testUserApprovalIsOutsideEachHostedRoundDeadline() async throws {
        let queue = ResponseQueue(responses: [
            completed(["id": "r1", "status": "completed", "output": [[
                "type": "function_call", "call_id": "action", "name": "change_virtual_state", "arguments": "{}"
            ]]]),
            completed(["id": "r2", "status": "completed", "output": [[
                "type": "message", "content": [["type": "output_text", "text": "Ready"]]
            ]]])
        ])
        let client = makeClient(IXClosureHTTPClient { try await queue.next($0) })
        let result = try await IXCodexConversation(client: client).run(
            input: [.text("Change the virtual state")], instructions: "Use the tool",
            tools: [.init(name: "change_virtual_state", description: "Virtual action", parameters: .object([
                "type": .string("object"), "properties": .object([:])
            ]), requiresConfirmation: true)],
            executor: IXClosureCodexToolExecutor { .success(callID: $0.id, message: "Ready") },
            approveTools: { _, _ in
                try await Task.sleep(for: .milliseconds(300))
                return true
            }
        )
        XCTAssertEqual(result.turn.text, "Ready")
    }

    private func completed(_ response: [String: Any]) -> IXHTTPResponse {
        let data = try! JSONSerialization.data(withJSONObject: ["type": "response.completed", "response": response])
        return .init(statusCode: 200, body: Data(("data: " + String(decoding: data, as: UTF8.self) + "\n\n").utf8))
    }
}

private actor KeepaliveHTTP: IXHTTPStreamingClient {
    private var chunks = 0
    private var finished = false
    func chunkCount() -> Int { chunks }
    func finishKeepalives() { finished = true }
    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        IXHTTPResponse(statusCode: 200, body: Data("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"r\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Ready\"}]}]}}\n\n".utf8))
    }
    func stream(_ request: URLRequest) async throws -> IXHTTPStreamingResponse {
        let body = AsyncThrowingStream<Data, Error> { continuation in
            let task = Task {
                do {
                    while !Task.isCancelled, !(await self.finished) {
                        continuation.yield(Data(": keepalive\n\n".utf8))
                        await self.countChunk()
                        try await Task.sleep(for: .milliseconds(5))
                    }
                    continuation.finish()
                } catch { continuation.finish(throwing: error) }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
        return .init(statusCode: 200, body: body)
    }
    private func countChunk() { chunks += 1 }
}

private actor SuspendedHTTP: IXHTTPClient {
    private var continuation: CheckedContinuation<IXHTTPResponse, Never>?
    private var released = false
    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        if released { return .json(500, [:]) }
        return await withCheckedContinuation { continuation = $0 }
    }
    func release() {
        released = true
        continuation?.resume(returning: .json(500, [:]))
        continuation = nil
    }
}
