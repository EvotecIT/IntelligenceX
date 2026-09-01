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
        let timeout = IXHTTPDeadline.normalized(timeoutInterval)
        var boundedRequest = request
        boundedRequest.timeoutInterval = timeout
        let requestToSend = boundedRequest
        let race = IXHTTPDeadlineRace()

        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                race.install(continuation)
                let operation = Task {
                    do {
                        race.resolve(.success(try await send(requestToSend)))
                    } catch {
                        race.resolve(.failure(error))
                    }
                }
                let deadline = Task {
                    do {
                        try await Task.sleep(for: .seconds(timeout))
                    } catch {
                        return
                    }
                    race.resolve(.failure(URLError(.timedOut)))
                }
                race.install(operation: operation, deadline: deadline)
            }
        } onCancel: {
            race.resolve(.failure(CancellationError()))
        }
    }
}

private enum IXHTTPDeadline {
    static let fallbackTimeout: TimeInterval = 12
    static let minimumTimeout: TimeInterval = 0.01
    // Keep floating-point conversion comfortably inside Duration's portable
    // representation. Int32.max seconds already exceeds any useful HTTP
    // request lifetime while avoiding platform-specific overflow boundaries.
    static let maximumTimeout: TimeInterval = TimeInterval(Int32.max)

    static func normalized(_ value: TimeInterval) -> TimeInterval {
        guard value.isFinite else { return fallbackTimeout }
        return min(maximumTimeout, max(minimumTimeout, value))
    }
}

/// Resolves the caller without making structured concurrency wait for a
/// custom HTTP client that does not cooperate with cancellation. The losing
/// task is still cancelled so URLSession and well-behaved clients stop work.
private final class IXHTTPDeadlineRace: @unchecked Sendable {
    typealias DeadlineResult = Result<IXHTTPResponse, Error>

    private let lock = NSLock()
    private var continuation:
        CheckedContinuation<IXHTTPResponse, Error>?
    private var operation: Task<Void, Never>?
    private var deadline: Task<Void, Never>?
    private var settledResult: DeadlineResult?

    func install(
        _ continuation: CheckedContinuation<IXHTTPResponse, Error>
    ) {
        lock.lock()
        if let settledResult {
            lock.unlock()
            continuation.resume(with: settledResult)
            return
        }
        self.continuation = continuation
        lock.unlock()
    }

    func install(
        operation: Task<Void, Never>,
        deadline: Task<Void, Never>
    ) {
        lock.lock()
        let alreadySettled = settledResult != nil
        if !alreadySettled {
            self.operation = operation
            self.deadline = deadline
        }
        lock.unlock()
        if alreadySettled {
            operation.cancel()
            deadline.cancel()
        }
    }

    func resolve(_ result: DeadlineResult) {
        lock.lock()
        guard settledResult == nil else {
            lock.unlock()
            return
        }
        settledResult = result
        let continuation = continuation
        self.continuation = nil
        let operation = operation
        self.operation = nil
        let deadline = deadline
        self.deadline = nil
        lock.unlock()

        operation?.cancel()
        deadline?.cancel()
        continuation?.resume(with: result)
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
