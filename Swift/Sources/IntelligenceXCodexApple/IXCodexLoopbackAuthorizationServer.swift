import Foundation
import IntelligenceXCodex
import Network

final class IXCodexLoopbackAuthorizationServer: @unchecked Sendable {
    let port: UInt16

    private let listener: NWListener
    private let queue = DispatchQueue(label: "com.evotecit.intelligencex.codex-oauth-loopback")
    private let lock = NSLock()
    private var startupContinuation: CheckedContinuation<Void, Error>?
    private var callbackHandler: (@Sendable (Result<URL, Error>) -> Void)?
    private var pendingCallback: Result<URL, Error>?
    private var callbackCompleted = false

    private init(port: UInt16) throws {
        self.port = port
        let parameters = NWParameters.tcp
        parameters.allowLocalEndpointReuse = true
        parameters.requiredLocalEndpoint = .hostPort(
            host: NWEndpoint.Host("127.0.0.1"),
            port: try NWEndpoint.Port(rawValue: port).unwrap()
        )
        listener = try NWListener(using: parameters)
    }

    static func start(ports: [UInt16]) async throws -> IXCodexLoopbackAuthorizationServer {
        var lastError: Error?
        for port in ports {
            do {
                let server = try IXCodexLoopbackAuthorizationServer(port: port)
                try await server.start()
                return server
            } catch {
                lastError = error
            }
        }
        throw lastError ?? IXCodexError.invalidBrowserCallback(
            "no allowed localhost callback port was available"
        )
    }

    func receiveCallback(_ handler: @escaping @Sendable (Result<URL, Error>) -> Void) {
        lock.withLock {
            if let pendingCallback {
                self.pendingCallback = nil
                handler(pendingCallback)
            } else {
                callbackHandler = handler
            }
        }
    }

    func stop() {
        listener.cancel()
        completeCallback(.failure(CancellationError()))
    }

    private func start() async throws {
        listener.newConnectionHandler = { [weak self] connection in
            self?.handle(connection)
        }
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                lock.withLock {
                    startupContinuation = continuation
                }
                listener.stateUpdateHandler = { [weak self] state in
                    switch state {
                    case .ready:
                        self?.completeStartup(.success(()))
                    case .failed(let error):
                        self?.completeStartup(.failure(error))
                    case .cancelled:
                        self?.completeStartup(.failure(CancellationError()))
                    default:
                        break
                    }
                }
                listener.start(queue: queue)
            }
        } onCancel: {
            self.stop()
        }
    }

    private func handle(_ connection: NWConnection) {
        connection.start(queue: queue)
        connection.receive(
            minimumIncompleteLength: 1,
            maximumLength: 16 * 1024
        ) { [weak self] data, _, _, error in
            guard let self else {
                connection.cancel()
                return
            }
            if let error {
                connection.cancel()
                completeCallback(.failure(error))
                return
            }
            guard let data,
                  let request = String(data: data, encoding: .utf8),
                  let requestLine = request.components(separatedBy: "\r\n").first else {
                respond(connection, status: "400 Bad Request", body: "Invalid authorization callback.")
                return
            }
            let parts = requestLine.split(separator: " ", maxSplits: 2).map(String.init)
            guard parts.count >= 2,
                  parts[0] == "GET",
                  let callbackURL = URL(string: "http://localhost:\(port)\(parts[1])"),
                  callbackURL.path == "/auth/callback" else {
                respond(connection, status: "404 Not Found", body: "Not found.")
                return
            }
            respond(
                connection,
                status: "200 OK",
                body: """
                <!doctype html><html><head><meta name="viewport" content="width=device-width"></head>
                <body style="font-family:-apple-system;padding:2rem">
                <h1>ChatGPT sign-in complete</h1><p>You can close this window and return to the app.</p>
                </body></html>
                """
            ) { [weak self] in
                self?.completeCallback(.success(callbackURL))
            }
        }
    }

    private func respond(
        _ connection: NWConnection,
        status: String,
        body: String,
        completion: (@Sendable () -> Void)? = nil
    ) {
        let bodyData = Data(body.utf8)
        let header = """
        HTTP/1.1 \(status)\r
        Content-Type: text/html; charset=utf-8\r
        Content-Length: \(bodyData.count)\r
        Connection: close\r
        Cache-Control: no-store\r
        \r
        """
        var response = Data(header.utf8)
        response.append(bodyData)
        connection.send(
            content: response,
            contentContext: .finalMessage,
            isComplete: true,
            completion: .contentProcessed { _ in
                connection.cancel()
                completion?()
            }
        )
    }

    private func completeStartup(_ result: Result<Void, Error>) {
        let continuation = lock.withLock {
            let value = startupContinuation
            startupContinuation = nil
            return value
        }
        continuation?.resume(with: result)
    }

    private func completeCallback(_ result: Result<URL, Error>) {
        let handler: (@Sendable (Result<URL, Error>) -> Void)? = lock.withLock {
            guard !callbackCompleted else { return nil }
            callbackCompleted = true
            if let callbackHandler {
                self.callbackHandler = nil
                return callbackHandler
            }
            pendingCallback = result
            return nil
        }
        handler?(result)
    }
}

private extension Optional {
    func unwrap(
        file: StaticString = #fileID,
        line: UInt = #line
    ) throws -> Wrapped {
        guard let self else {
            throw IXCodexError.invalidBrowserCallback(
                "an allowed callback port could not be represented"
            )
        }
        return self
    }
}
