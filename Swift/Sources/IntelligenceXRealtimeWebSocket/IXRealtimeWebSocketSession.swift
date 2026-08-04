import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime

public enum IXRealtimeWebSocketConnectionState: Sendable, Equatable {
    case idle
    case connecting
    case connected
    case disconnected
    case failed(String)
}

@MainActor
public final class IXRealtimeWebSocketSession {
    public typealias EventHandler = @MainActor @Sendable (IXRealtimeEvent) async -> Void
    public typealias StateHandler = @MainActor @Sendable (
        IXRealtimeWebSocketConnectionState
    ) -> Void

    private let secret: IXRealtimeClientSecret
    private let endpoint: URL
    private let connector: any IXRealtimeWebSocketConnecting
    private let onEvent: EventHandler
    private let onState: StateHandler
    private var connection: (any IXRealtimeWebSocketConnection)?
    private var receiveTask: Task<Void, Never>?
    private var lifecycleGeneration: UInt64 = 0

    public private(set) var state: IXRealtimeWebSocketConnectionState = .idle

    public var isReady: Bool { state == .connected }

    public init(
        secret: IXRealtimeClientSecret,
        endpoint: URL = URL(string: "wss://api.openai.com/v1/realtime")!,
        connector: any IXRealtimeWebSocketConnecting =
            IXURLSessionRealtimeWebSocketConnector(),
        onEvent: @escaping EventHandler,
        onState: @escaping StateHandler = { _ in }
    ) {
        self.secret = secret
        self.endpoint = endpoint
        self.connector = connector
        self.onEvent = onEvent
        self.onState = onState
    }

    deinit {
        receiveTask?.cancel()
        let connection = connection
        Task {
            await connection?.close()
        }
    }

    public func connect() async throws {
        guard secret.expiresAt > Date() else {
            throw IXCodexError.authenticationRequired
        }
        await disconnect()
        lifecycleGeneration &+= 1
        let generation = lifecycleGeneration
        try await withTaskCancellationHandler {
            try await connect(generation: generation)
        } onCancel: {
            Task { @MainActor [weak self] in
                await self?.disconnect(generation: generation)
            }
        }
    }

    private func connect(generation: UInt64) async throws {
        transition(to: .connecting)

        var components = URLComponents(
            url: endpoint,
            resolvingAgainstBaseURL: false
        )
        var queryItems = components?.queryItems ?? []
        queryItems.removeAll { $0.name == "model" }
        queryItems.append(URLQueryItem(name: "model", value: secret.model))
        components?.queryItems = queryItems
        guard let url = components?.url else {
            transition(to: .failed("Realtime WebSocket endpoint is invalid"))
            throw IXCodexError.invalidResponse(
                "Realtime WebSocket endpoint is invalid"
            )
        }

        do {
            let connection = try await connector.connect(
                to: url,
                protocols: [
                    "realtime",
                    "openai-insecure-api-key.\(secret.value)",
                ]
            )
            try await validate(connection, generation: generation)
            self.connection = connection
            // A successful protocol event proves the socket is writable. The
            // API queues the clear until the opening handshake completes.
            do {
                try await connection.send(
                    IXRealtimeClientEvent.clearInputAudioBuffer.encodedData()
                )
                try await validate(connection, generation: generation)
            } catch {
                await connection.close()
                if generation == lifecycleGeneration {
                    self.connection = nil
                }
                throw error
            }
            transition(to: .connected)
            let onEvent = onEvent
            receiveTask = Task { [weak self, connection] in
                do {
                    while !Task.isCancelled {
                        let data = try await connection.receive()
                        guard self?.lifecycleGeneration == generation else {
                            return
                        }
                        let event = try IXRealtimeEvent(data: data)
                        await onEvent(event)
                    }
                } catch is CancellationError {
                    return
                } catch {
                    guard !Task.isCancelled else { return }
                    await connection.close()
                    guard let self,
                          generation == self.lifecycleGeneration else {
                        return
                    }
                    self.transition(to: .failed(error.localizedDescription))
                    self.connection = nil
                }
            }
            try await validate(connection, generation: generation)
        } catch is CancellationError {
            if generation == lifecycleGeneration {
                await disconnect()
            }
            throw CancellationError()
        } catch {
            if generation == lifecycleGeneration {
                transition(to: .failed(error.localizedDescription))
                self.connection = nil
            }
            throw error
        }
    }

    public func send(_ event: IXJSONValue) async throws {
        guard let connection, isReady else {
            throw IXRealtimeWebSocketError.notConnected
        }
        try await connection.send(event.encodedData())
    }

    public func disconnect() async {
        lifecycleGeneration &+= 1
        receiveTask?.cancel()
        receiveTask = nil
        if let connection {
            await connection.close()
        }
        connection = nil
        transition(to: .idle)
    }

    private func disconnect(generation: UInt64) async {
        guard generation == lifecycleGeneration else { return }
        await disconnect()
    }

    private func validate(
        _ connection: any IXRealtimeWebSocketConnection,
        generation: UInt64
    ) async throws {
        do {
            try Task.checkCancellation()
            guard generation == lifecycleGeneration else {
                throw CancellationError()
            }
        } catch {
            await connection.close()
            throw CancellationError()
        }
    }

    private func transition(to state: IXRealtimeWebSocketConnectionState) {
        self.state = state
        onState(state)
    }
}
