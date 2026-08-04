import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public actor IXCodexAuthSession {
    private let configuration: IXCodexConfiguration
    private let credentialAccess: IXCodexCredentialAccess
    private let httpClient: any IXHTTPClient
    private let now: @Sendable () -> Date
    private let randomBytes: @Sendable (Int) -> [UInt8]
    private var authGeneration: UInt64 = 0
    private var refreshTask: Task<IXCodexAuthBundle, Error>?
    private var credentialWriteAuthorizations:
        [UUID: IXCodexCredentialWriteAuthorization] = [:]
    private var credentialMutationDepth = 0

    public init(
        configuration: IXCodexConfiguration = IXCodexConfiguration(),
        credentialStore: any IXCodexCredentialStoring,
        httpClient: any IXHTTPClient = IXURLSessionHTTPClient(),
        now: @escaping @Sendable () -> Date = Date.init,
        randomBytes: @escaping @Sendable (Int) -> [UInt8] = { count in
            var generator = SystemRandomNumberGenerator()
            return (0..<count).map { _ in UInt8.random(in: .min ... .max, using: &generator) }
        }
    ) {
        self.configuration = configuration
        credentialAccess = IXCodexCredentialAccess(store: credentialStore)
        self.httpClient = httpClient
        self.now = now
        self.randomBytes = randomBytes
    }

    public func account() async throws -> IXCodexAccount? {
        try validateCredentialRead()
        guard let bundle = try await credentialAccess.load() else { return nil }
        return IXJWTClaims.account(bundle: bundle)
    }

    public func currentBundle() async throws -> IXCodexAuthBundle? {
        try validateCredentialRead()
        return try await credentialAccess.load()
    }

    public func authorizationSnapshot() async throws
        -> IXCodexAuthorizationSnapshot {
        try validateCredentialRead()
        guard let bundle = try await credentialAccess.load() else {
            return IXCodexAuthorizationSnapshot(
                account: nil,
                accessTokenExpiresAt: nil,
                accessTokenNeedsRefresh: false
            )
        }
        return IXCodexAuthorizationSnapshot(
            account: IXJWTClaims.account(bundle: bundle),
            accessTokenExpiresAt: bundle.expiresAt,
            accessTokenNeedsRefresh: bundle.needsRefresh(at: now())
        )
    }

    public var browserCallbackPorts: [UInt16] {
        configuration.browserCallbackPorts
    }

    public func signOut() async throws {
        authGeneration &+= 1
        refreshTask?.cancel()
        refreshTask = nil
        invalidateCredentialWrites()
        credentialMutationDepth += 1
        defer { credentialMutationDepth -= 1 }
        try await credentialAccess.delete()
    }

    /// Starts the normal Codex ChatGPT browser flow for a temporary localhost callback.
    public func beginBrowserAuthorization(
        redirectURL: URL
    ) async throws -> IXCodexBrowserAuthorization {
        guard redirectURL.scheme == "http",
              redirectURL.host == "localhost",
              redirectURL.path == "/auth/callback",
              let port = redirectURL.port,
              configuration.browserCallbackPorts.contains(UInt16(port)) else {
            throw IXCodexError.invalidBrowserCallback("the redirect must use an allowed localhost callback")
        }
        authGeneration &+= 1
        refreshTask?.cancel()
        refreshTask = nil
        try await revokeCredentialWrites()
        let expectedGeneration = authGeneration
        let verifier = IXCodexPKCE.verifier(randomBytes: randomBytes(32))
        let state = IXCodexPKCE.state(randomBytes: randomBytes(32))
        var components = URLComponents(
            url: configuration.authorizationURL,
            resolvingAgainstBaseURL: false
        )
        components?.queryItems = [
            URLQueryItem(name: "response_type", value: "code"),
            URLQueryItem(name: "client_id", value: configuration.clientID),
            URLQueryItem(name: "redirect_uri", value: redirectURL.absoluteString),
            URLQueryItem(name: "scope", value: configuration.scope),
            URLQueryItem(name: "code_challenge", value: IXCodexPKCE.challenge(for: verifier)),
            URLQueryItem(name: "code_challenge_method", value: "S256"),
            URLQueryItem(name: "id_token_add_organizations", value: "true"),
            URLQueryItem(name: "codex_cli_simplified_flow", value: "true"),
            URLQueryItem(name: "state", value: state),
            URLQueryItem(name: "originator", value: configuration.originator),
        ]
        guard let authorizationURL = components?.url else {
            throw IXCodexError.invalidResponse("browser authorization URL could not be created")
        }
        return IXCodexBrowserAuthorization(
            authorizationURL: authorizationURL,
            redirectURL: redirectURL,
            expiresAt: now().addingTimeInterval(10 * 60),
            state: state,
            codeVerifier: verifier,
            expectedAuthGeneration: expectedGeneration
        )
    }

    /// Validates a localhost OAuth callback and stores the resulting rotatable token bundle.
    public func completeBrowserAuthorization(
        _ authorization: IXCodexBrowserAuthorization,
        callbackURL: URL
    ) async throws -> IXCodexAuthBundle {
        guard now() < authorization.expiresAt else {
            throw IXCodexError.browserAuthorizationExpired
        }
        guard callbackURL.scheme == authorization.redirectURL.scheme,
              callbackURL.host == authorization.redirectURL.host,
              callbackURL.port == authorization.redirectURL.port,
              callbackURL.path == authorization.redirectURL.path,
              let components = URLComponents(url: callbackURL, resolvingAgainstBaseURL: false) else {
            throw IXCodexError.invalidBrowserCallback("the callback URL did not match the request")
        }
        let queryItems = components.queryItems ?? []
        let states = queryItems.filter { $0.name == "state" }.compactMap(\.value)
        guard states.count == 1, states[0] == authorization.state else {
            throw IXCodexError.browserAuthorizationStateMismatch
        }
        if queryItems.contains(where: { $0.name == "error" }) {
            throw IXCodexError.browserAuthorizationDenied
        }
        let codes = queryItems.filter { $0.name == "code" }.compactMap(\.value)
        guard codes.count == 1, let code = codes.first, !code.isEmpty else {
            throw IXCodexError.invalidBrowserCallback("the authorization code was missing")
        }
        return try await requestToken(
            fields: [
                "grant_type": "authorization_code",
                "client_id": configuration.clientID,
                "code": code,
                "redirect_uri": authorization.redirectURL.absoluteString,
                "code_verifier": authorization.codeVerifier,
            ],
            previous: nil,
            expectedGeneration: authorization.expectedAuthGeneration
        )
    }

    public func beginDeviceAuthorization() async throws -> IXCodexDeviceCode {
        authGeneration &+= 1
        refreshTask?.cancel()
        refreshTask = nil
        try await revokeCredentialWrites()
        let expectedGeneration = authGeneration
        var request = URLRequest(url: configuration.deviceAuthorizationURL)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue(configuration.userAgent, forHTTPHeaderField: "User-Agent")
        request.httpBody = try JSONEncoder().encode(["client_id": configuration.clientID])

        let response = try await httpClient.send(request)
        guard (200..<300).contains(response.statusCode) else {
            throw requestError(response)
        }
        let payload = try IXJSONValue.decode(response.body)
        guard let object = payload.objectValue,
              let deviceID = object["device_auth_id"]?.stringValue,
              let userCode = object["user_code"]?.stringValue ?? object["usercode"]?.stringValue else {
            throw IXCodexError.invalidResponse("device authorization fields are missing")
        }
        let intervalSeconds = object["interval"]?.stringValue.flatMap(Double.init)
            ?? object["interval"]?.numberValue
            ?? 5
        return IXCodexDeviceCode(
            deviceAuthorizationID: deviceID,
            userCode: userCode,
            verificationURL: configuration.deviceVerificationURL,
            interval: .seconds(intervalSeconds),
            expiresAt: now().addingTimeInterval(15 * 60),
            expectedAuthGeneration: expectedGeneration
        )
    }

    public func completeDeviceAuthorization(_ code: IXCodexDeviceCode) async throws -> IXCodexAuthBundle {
        let expectedGeneration = code.expectedAuthGeneration
        while now() < code.expiresAt {
            try Task.checkCancellation()
            guard authGeneration == expectedGeneration else {
                throw CancellationError()
            }
            let result = try await pollDeviceAuthorization(code)
            switch result {
            case .pending:
                try await Task.sleep(for: code.interval)
            case .approved(let exchange):
                return try await exchangeAuthorizationCode(exchange, expectedGeneration: expectedGeneration)
            }
        }
        throw IXCodexError.deviceAuthorizationExpired
    }

    public func validBundle(forceRefresh: Bool = false) async throws -> IXCodexAuthBundle {
        let expectedGeneration = authGeneration
        try validateCredentialRead()
        guard let existing = try await credentialAccess.load() else {
            throw IXCodexError.authenticationRequired
        }
        guard authGeneration == expectedGeneration, !Task.isCancelled else {
            throw CancellationError()
        }
        guard forceRefresh || existing.needsRefresh(at: now()) else {
            return existing
        }
        if let refreshTask {
            return try await refreshTask.value
        }
        let task = Task {
            try await self.refresh(
                existing,
                retryAfterReload: true,
                expectedGeneration: expectedGeneration
            )
        }
        refreshTask = task
        do {
            let bundle = try await task.value
            if authGeneration == expectedGeneration { refreshTask = nil }
            return bundle
        } catch {
            if authGeneration == expectedGeneration { refreshTask = nil }
            throw error
        }
    }

    private enum DevicePollResult {
        case pending
        case approved(DeviceExchange)
    }

    private struct DeviceExchange: Decodable {
        let authorizationCode: String
        let codeChallenge: String
        let codeVerifier: String

        enum CodingKeys: String, CodingKey {
            case authorizationCode = "authorization_code"
            case codeChallenge = "code_challenge"
            case codeVerifier = "code_verifier"
        }
    }

    private func pollDeviceAuthorization(_ code: IXCodexDeviceCode) async throws -> DevicePollResult {
        let url = configuration.deviceAuthorizationURL
            .deletingLastPathComponent()
            .appendingPathComponent("token")
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue(configuration.userAgent, forHTTPHeaderField: "User-Agent")
        request.httpBody = try JSONEncoder().encode([
            "device_auth_id": code.deviceAuthorizationID,
            "user_code": code.userCode,
        ])
        let response = try await httpClient.send(request)
        if response.statusCode == 403 || response.statusCode == 404 {
            let object = (try? IXJSONValue.decode(response.body))?.objectValue
            let errorValue = object?["error"]?.stringValue ?? ""
            let description = object?["error_description"]?.stringValue ??
                object?["message"]?.stringValue ?? ""
            let normalized = "\(errorValue) \(description)".lowercased()
            if normalized.contains("access_denied") ||
                normalized.contains("authorization_denied") ||
                normalized.contains("declined") ||
                normalized.contains("denied") {
                throw IXCodexError.deviceAuthorizationDenied
            }
            if normalized.contains("expired") ||
                normalized.contains("invalid_device") {
                throw IXCodexError.deviceAuthorizationExpired
            }
            return .pending
        }
        guard (200..<300).contains(response.statusCode) else {
            throw requestError(response)
        }
        return .approved(try JSONDecoder().decode(DeviceExchange.self, from: response.body))
    }

    private func exchangeAuthorizationCode(
        _ exchange: DeviceExchange,
        expectedGeneration: UInt64
    ) async throws -> IXCodexAuthBundle {
        let fields = [
            "grant_type": "authorization_code",
            "client_id": configuration.clientID,
            "code": exchange.authorizationCode,
            "redirect_uri": configuration.deviceCallbackURL.absoluteString,
            "code_verifier": exchange.codeVerifier,
        ]
        return try await requestToken(
            fields: fields,
            previous: nil,
            expectedGeneration: expectedGeneration
        )
    }

    private func refresh(
        _ existing: IXCodexAuthBundle,
        retryAfterReload: Bool,
        expectedGeneration: UInt64
    ) async throws -> IXCodexAuthBundle {
        do {
            return try await requestToken(fields: [
                "grant_type": "refresh_token",
                "client_id": configuration.clientID,
                "refresh_token": existing.refreshToken,
            ], previous: existing, expectedGeneration: expectedGeneration)
        } catch let IXCodexError.requestFailed(_, message)
            where retryAfterReload && message.localizedCaseInsensitiveContains("refresh_token_reused") {
            guard let reloaded = try await credentialAccess.load(), reloaded.refreshToken != existing.refreshToken else {
                throw IXCodexError.requestFailed(status: 401, message: message)
            }
            guard authGeneration == expectedGeneration, !Task.isCancelled else {
                throw CancellationError()
            }
            return try await refresh(
                reloaded,
                retryAfterReload: false,
                expectedGeneration: expectedGeneration
            )
        }
    }

    private func requestToken(
        fields: [String: String],
        previous: IXCodexAuthBundle?,
        expectedGeneration: UInt64
    ) async throws -> IXCodexAuthBundle {
        var request = URLRequest(url: configuration.tokenURL)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.setValue(configuration.userAgent, forHTTPHeaderField: "User-Agent")
        request.httpBody = formEncoded(fields).data(using: .utf8)
        let response = try await httpClient.send(request)
        guard (200..<300).contains(response.statusCode) else {
            throw requestError(response)
        }
        let value = try IXJSONValue.decode(response.body)
        guard let object = value.objectValue,
              let accessToken = object["access_token"]?.stringValue else {
            throw IXCodexError.invalidResponse("OAuth access token is missing")
        }
        guard let refreshToken = object["refresh_token"]?.stringValue ?? previous?.refreshToken else {
            throw IXCodexError.invalidResponse("OAuth refresh token is missing")
        }
        let expiresIn = object["expires_in"]?.numberValue
            ?? object["expires_in"]?.stringValue.flatMap(Double.init)
        let idToken = object["id_token"]?.stringValue ?? previous?.idToken
        let accountID = IXJWTClaims.accountID(accessToken: accessToken, idToken: idToken)
            ?? previous?.accountID
        let bundle = IXCodexAuthBundle(
            accessToken: accessToken,
            refreshToken: refreshToken,
            expiresAt: expiresIn.map { now().addingTimeInterval($0) },
            accountID: accountID,
            idToken: idToken,
            tokenType: object["token_type"]?.stringValue ?? previous?.tokenType,
            scope: object["scope"]?.stringValue ?? previous?.scope
        )
        guard authGeneration == expectedGeneration, !Task.isCancelled else {
            throw CancellationError()
        }
        let authorization = IXCodexCredentialWriteAuthorization()
        credentialWriteAuthorizations[authorization.id] = authorization
        credentialMutationDepth += 1
        defer {
            credentialWriteAuthorizations.removeValue(forKey: authorization.id)
            credentialMutationDepth -= 1
        }
        let committed = try await withTaskCancellationHandler {
            try await credentialAccess.save(
                bundle,
                authorizedBy: authorization
            )
        } onCancel: {
            authorization.invalidate()
        }
        guard committed,
              authGeneration == expectedGeneration,
              !Task.isCancelled else {
            authorization.invalidate()
            try await credentialAccess.revoke([authorization.id])
            throw CancellationError()
        }
        return bundle
    }

    private func invalidateCredentialWrites() {
        for authorization in credentialWriteAuthorizations.values {
            authorization.invalidate()
        }
        credentialWriteAuthorizations.removeAll()
    }

    private func revokeCredentialWrites() async throws {
        let authorizations = credentialWriteAuthorizations
        invalidateCredentialWrites()
        credentialMutationDepth += 1
        defer { credentialMutationDepth -= 1 }
        try await credentialAccess.revoke(Set(authorizations.keys))
    }

    private func validateCredentialRead() throws {
        guard credentialMutationDepth == 0 else {
            throw CancellationError()
        }
    }

    private func requestError(_ response: IXHTTPResponse) -> IXCodexError {
        let fallback = String(data: response.body, encoding: .utf8) ?? "Unknown error"
        let parsed = (try? IXJSONValue.decode(response.body))?.objectValue
        let message = parsed?["error_description"]?.stringValue
            ?? parsed?["message"]?.stringValue
            ?? parsed?["error"]?.stringValue
            ?? fallback
        return .requestFailed(status: response.statusCode, message: message)
    }

    private func formEncoded(_ fields: [String: String]) -> String {
        fields.sorted { $0.key < $1.key }.map { key, value in
            "\(formEscape(key))=\(formEscape(value))"
        }.joined(separator: "&")
    }

    private func formEscape(_ value: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return value.addingPercentEncoding(withAllowedCharacters: allowed) ?? value
    }
}
