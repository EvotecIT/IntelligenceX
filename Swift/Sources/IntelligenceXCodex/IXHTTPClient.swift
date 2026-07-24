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

public final class IXURLSessionHTTPClient: IXHTTPClient, @unchecked Sendable {
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
