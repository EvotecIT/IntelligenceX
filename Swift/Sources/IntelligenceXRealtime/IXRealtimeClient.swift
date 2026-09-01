import Foundation
import IntelligenceXCodex
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public actor IXRealtimeClient {
    private let endpoint: URL
    private let authSession: IXCodexAuthSession
    private let httpClient: any IXHTTPClient
    private let userAgent: String
    private let requestTimeoutInterval: TimeInterval

    public init(
        authSession: IXCodexAuthSession,
        endpoint: URL = URL(string: "https://api.openai.com/v1/realtime/client_secrets")!,
        httpClient: any IXHTTPClient = IXURLSessionHTTPClient(),
        userAgent: String = "intelligencex-swift/0.1",
        requestTimeoutInterval: TimeInterval = 12
    ) {
        self.authSession = authSession
        self.endpoint = endpoint
        self.httpClient = httpClient
        self.userAgent = userAgent
        self.requestTimeoutInterval = requestTimeoutInterval
    }

    public func createClientSecret(
        options: IXRealtimeSessionOptions,
        tools: [IXCodexToolDefinition] = [],
        retryUnauthorized: Bool = true
    ) async throws -> IXRealtimeClientSecret {
        try IXCodexToolSchemaValidator.validate(tools)
        let bundle = try await authSession.validBundle()
        let seconds = max(10, min(7_200, Int(options.clientSecretLifetime.components.seconds)))
        let body = IXJSONValue.object([
            "expires_after": .object([
                "anchor": .string("created_at"),
                "seconds": .number(Double(seconds)),
            ]),
            "session": .object(options.sessionConfiguration(tools: tools)),
        ])
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(bundle.accessToken)", forHTTPHeaderField: "Authorization")
        if let accountID = bundle.accountID {
            request.setValue(accountID, forHTTPHeaderField: "ChatGPT-Account-ID")
        }
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        request.httpBody = try JSONEncoder().encode(body)
        let response = try await httpClient.send(
            request,
            timeoutInterval: requestTimeoutInterval
        )
        if response.statusCode == 401 && retryUnauthorized {
            _ = try await authSession.validBundle(forceRefresh: true)
            return try await createClientSecret(
                options: options,
                tools: tools,
                retryUnauthorized: false
            )
        }
        guard (200..<300).contains(response.statusCode) else {
            let value = try? IXJSONValue.decode(response.body)
            let message = value?["error"]?["message"]?.stringValue
                ?? value?["detail"]?.stringValue
                ?? value?["message"]?.stringValue
                ?? String(data: response.body, encoding: .utf8)
                ?? "Unknown error"
            throw IXCodexError.requestFailed(status: response.statusCode, message: message)
        }
        let value = try JSONDecoder().decode(IXJSONValue.self, from: response.body)
        guard let secret = value["value"]?.stringValue,
              let expiresAt = value["expires_at"]?.numberValue else {
            throw IXCodexError.invalidResponse("Realtime client secret fields are missing")
        }
        let model = value["session"]?["model"]?.stringValue ?? options.model
        return IXRealtimeClientSecret(
            value: secret,
            expiresAt: Date(timeIntervalSince1970: expiresAt),
            model: model
        )
    }
}

private extension IXJSONValue {
    var numberValue: Double? {
        guard case .number(let value) = self else { return nil }
        return value
    }
}
