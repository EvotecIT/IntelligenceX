import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public struct IXHTTPResponse: Sendable {
    public let statusCode: Int
    public let headers: [String: String]
    public let body: Data

    public init(statusCode: Int, headers: [String: String] = [:], body: Data = Data()) {
        self.statusCode = statusCode
        self.headers = headers
        self.body = body
    }
}

public protocol IXHTTPClient: Sendable {
    func send(_ request: URLRequest) async throws -> IXHTTPResponse
}

public extension IXHTTPClient {
    /// Sends one request with an absolute elapsed-time deadline. The deadline
    /// remains authoritative even when a custom transport ignores
    /// `URLRequest.timeoutInterval` or a response keeps producing small chunks.
    func send(
        _ request: URLRequest,
        timeoutInterval: TimeInterval
    ) async throws -> IXHTTPResponse {
        let timeout = IXElapsedDeadline.normalized(timeoutInterval)
        var boundedRequest = request
        boundedRequest.timeoutInterval = timeout
        let requestToSend = boundedRequest
        return try await IXElapsedDeadline.run(timeoutInterval: timeout) {
            try await send(requestToSend)
        }
    }
}

public struct IXHTTPStreamingResponse: Sendable {
    public let statusCode: Int
    public let headers: [String: String]
    public let body: AsyncThrowingStream<Data, Error>

    public init(
        statusCode: Int,
        headers: [String: String] = [:],
        body: AsyncThrowingStream<Data, Error>
    ) {
        self.statusCode = statusCode
        self.headers = headers
        self.body = body
    }
}

/// Optional incremental transport. IXCodexClient uses it when a consumer asks
/// for deltas and falls back to the buffered IXHTTPClient contract otherwise.
public protocol IXHTTPStreamingClient: IXHTTPClient {
    func stream(_ request: URLRequest) async throws -> IXHTTPStreamingResponse
}

public final class IXURLSessionHTTPClient:
    IXHTTPStreamingClient, @unchecked Sendable {
    private let session: URLSession

    public init(session: URLSession = .shared) {
        self.session = session
    }

    public func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        let (data, response) = try await session.data(for: request)
        guard let response = response as? HTTPURLResponse else {
            throw IXCodexError.invalidResponse("missing HTTP response")
        }
        let headers = response.allHeaderFields.reduce(into: [String: String]()) { result, entry in
            result[String(describing: entry.key).lowercased()] = String(describing: entry.value)
        }
        return IXHTTPResponse(statusCode: response.statusCode, headers: headers, body: data)
    }


    public func stream(
        _ request: URLRequest
    ) async throws -> IXHTTPStreamingResponse {
        let (bytes, response) = try await session.bytes(for: request)
        guard let response = response as? HTTPURLResponse else {
            throw IXCodexError.invalidResponse("missing HTTP response")
        }
        let headers = response.allHeaderFields.reduce(
            into: [String: String]()
        ) { result, entry in
            result[String(describing: entry.key).lowercased()] =
                String(describing: entry.value)
        }
        let body = AsyncThrowingStream<Data, Error> { continuation in
            let task = Task {
                do {
                    var chunk = Data()
                    chunk.reserveCapacity(4_096)
                    for try await byte in bytes {
                        try Task.checkCancellation()
                        chunk.append(byte)
                        if chunk.count >= 4_096 || byte == 0x0A {
                            continuation.yield(chunk)
                            chunk.removeAll(keepingCapacity: true)
                        }
                    }
                    if !chunk.isEmpty { continuation.yield(chunk) }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
        return IXHTTPStreamingResponse(
            statusCode: response.statusCode,
            headers: headers,
            body: body
        )
    }
}

public struct IXClosureHTTPClient: IXHTTPClient {
    private let handler: @Sendable (URLRequest) async throws -> IXHTTPResponse

    public init(handler: @escaping @Sendable (URLRequest) async throws -> IXHTTPResponse) {
        self.handler = handler
    }

    public func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        try await handler(request)
    }
}
