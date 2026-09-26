import Foundation
import IntelligenceXCodex

/// Describes the Realtime client secret a voice start needs.
///
/// Two requests are interchangeable only when every field is equal, so a
/// secret prefetched for one account, model, or voice is never handed to a
/// start that asked for another.
public struct IXRealtimeClientSecretRequest: Sendable, Equatable {
    /// The session configuration minted into the secret.
    public var options: IXRealtimeSessionOptions
    /// The tools minted into the secret.
    public var tools: [IXCodexToolDefinition]
    /// An opaque authority scope, such as the signed-in account identifier.
    /// A secret is only reused for a request with the same scope. The
    /// prefetcher cannot observe sign-out on its own; also call
    /// `IXRealtimeClientSecretPrefetcher.invalidate()` when authority changes.
    public var scope: String?

    public init(
        options: IXRealtimeSessionOptions,
        tools: [IXCodexToolDefinition] = [],
        scope: String? = nil
    ) {
        self.options = options
        self.tools = tools
        self.scope = scope
    }

    /// A content-free request for clients that send their full session
    /// configuration with `session.update` once the connection opens.
    ///
    /// The minted secret keeps the model, voice, audio, turn-detection,
    /// transcription, reasoning, and lifetime settings of `options`, but
    /// carries no instructions and no tools and never creates responses on
    /// its own. Such a secret holds no conversation data, so it can be minted
    /// well before the person starts talking. Send
    /// `IXRealtimeClientEvent.sessionUpdate(options:tools:)` as soon as the
    /// session is ready; it restores `createsResponsesAutomatically` from the
    /// complete options before any turn is answered.
    public static func minimal(
        options: IXRealtimeSessionOptions,
        scope: String? = nil
    ) -> IXRealtimeClientSecretRequest {
        var minimalOptions = options
        minimalOptions.instructions = ""
        minimalOptions.createsResponsesAutomatically = false
        return IXRealtimeClientSecretRequest(
            options: minimalOptions,
            tools: [],
            scope: scope
        )
    }
}

/// A client secret handed out by `IXRealtimeClientSecretPrefetcher`.
public struct IXRealtimePrefetchedClientSecret: Sendable, Equatable {
    /// How the secret was obtained, for start-up diagnostics.
    public enum Source: String, Sendable, Equatable {
        /// A prefetched secret that had already been minted.
        case cached
        /// A prefetch that was still minting and was joined.
        case joinedPrefetch
        /// Minted on demand because nothing reusable was prepared.
        case minted
    }

    public let secret: IXRealtimeClientSecret
    public let source: Source

    public init(secret: IXRealtimeClientSecret, source: Source) {
        self.secret = secret
        self.source = source
    }
}

/// Mints a Realtime client secret ahead of a voice start and hands it out
/// once.
///
/// Minting a client secret is a network round trip, and it also refreshes an
/// expiring ChatGPT token and warms DNS and TLS to the API host. Calling
/// `prefetch(_:)` when a voice start becomes likely moves that work off the
/// start-up critical path:
///
/// - A prefetched secret is reused for at most one session, and only while at
///   least `minimumRemainingLifetime` remains before it expires, so it
///   outlives connection setup.
/// - `takeSecret(for:)` joins a prefetch that is still minting instead of
///   starting a second request, and mints on demand when nothing reusable
///   matches. A failed or expired prefetch is retried once on demand.
/// - `invalidate()` drops every prepared secret and cancels a running mint.
///   Call it on sign-out, account change, or loss of entitlement so no secret
///   outlives its authority. A start that joined an invalidated prefetch
///   mints a new secret instead of using the discarded one.
///
/// Prefer `IXRealtimeClientSecretRequest.minimal(options:scope:)` for
/// prefetching, so no instructions, tools, or home data are minted into a
/// secret that may never be used.
public actor IXRealtimeClientSecretPrefetcher {
    /// Mints one client secret for a request.
    public typealias Mint = @Sendable (
        IXRealtimeClientSecretRequest
    ) async throws -> IXRealtimeClientSecret

    private struct PreparedSecret {
        let id: UUID
        let request: IXRealtimeClientSecretRequest
        let secret: IXRealtimeClientSecret
    }

    private struct RunningMint {
        let id: UUID
        let request: IXRealtimeClientSecretRequest
        let generation: UInt64
        let task: Task<IXRealtimeClientSecret, Error>
    }

    /// The lifetime a prepared secret must still have to be handed out.
    public let minimumRemainingLifetime: Duration

    private let mint: Mint
    private let now: @Sendable () -> Date
    private var prepared: PreparedSecret?
    private var running: RunningMint?
    private var joined: [UUID: Task<IXRealtimeClientSecret, Error>] = [:]
    private var generation: UInt64 = 0

    /// Creates a prefetcher that mints with `client`.
    ///
    /// - Parameters:
    ///   - client: The Realtime client that mints secrets. Its authentication
    ///     session validates, and if needed refreshes, the ChatGPT token.
    ///   - minimumRemainingLifetime: How long a prepared secret must remain
    ///     valid to be handed out. Keep it longer than connection setup, and
    ///     shorter than the requested `clientSecretLifetime`.
    public init(
        client: IXRealtimeClient,
        minimumRemainingLifetime: Duration = .seconds(30)
    ) {
        self.init(
            minimumRemainingLifetime: minimumRemainingLifetime,
            mint: { request in
                try await client.createClientSecret(
                    options: request.options,
                    tools: request.tools
                )
            }
        )
    }

    /// Creates a prefetcher with a custom minting operation, for example a
    /// server that mints secrets on the app's behalf.
    public init(
        minimumRemainingLifetime: Duration = .seconds(30),
        now: @escaping @Sendable () -> Date = Date.init,
        mint: @escaping Mint
    ) {
        self.minimumRemainingLifetime = minimumRemainingLifetime
        self.now = now
        self.mint = mint
    }

    /// Starts minting a secret for `request` in the background unless a
    /// reusable secret for it is already prepared or being minted.
    ///
    /// Returns without waiting for the network. A prefetch for a different
    /// request replaces the previous one. A failed prefetch is silent;
    /// `takeSecret(for:)` mints again and reports the error.
    ///
    /// - Parameter priority: The priority of the background mint. Speculative
    ///   prefetches should stay at `.utility`; a start that joins the mint
    ///   escalates it.
    public func prefetch(
        _ request: IXRealtimeClientSecretRequest,
        priority: TaskPriority = .utility
    ) {
        if let prepared {
            if prepared.request == request, isReusable(prepared.secret) {
                return
            }
            self.prepared = nil
        }
        if let running {
            if running.request == request { return }
            running.task.cancel()
            self.running = nil
        }
        let id = UUID()
        let mint = mint
        let task = Task.detached(priority: priority) {
            try await mint(request)
        }
        running = RunningMint(
            id: id,
            request: request,
            generation: generation,
            task: task
        )
        Task { [weak self] in
            let result = await task.result
            await self?.finishPrefetch(id: id, result: result)
        }
    }

    /// Returns a secret for `request`, consuming a prepared one when it
    /// matches and remains reusable, joining a matching prefetch that is
    /// still minting, and otherwise minting on demand.
    ///
    /// A secret is handed out at most once; every Realtime connection needs
    /// its own. Waiting for a joined prefetch is bounded by the minting
    /// client's request deadline.
    ///
    /// - Throws: The on-demand mint's error, or `CancellationError` when the
    ///   calling task is cancelled or `invalidate()` runs while this call
    ///   mints on demand.
    public func takeSecret(
        for request: IXRealtimeClientSecretRequest
    ) async throws -> IXRealtimePrefetchedClientSecret {
        try Task.checkCancellation()
        if let prepared {
            self.prepared = nil
            if prepared.request == request, isReusable(prepared.secret) {
                return .init(secret: prepared.secret, source: .cached)
            }
        }
        if let running {
            if running.request == request {
                if let secret = try await join(running) {
                    return .init(secret: secret, source: .joinedPrefetch)
                }
            } else {
                running.task.cancel()
                self.running = nil
            }
        }
        let mintGeneration = generation
        let secret = try await mint(request)
        // A secret minted across an invalidation may belong to the previous
        // authority; the start that asked for it must begin again.
        guard mintGeneration == generation else {
            throw CancellationError()
        }
        return .init(secret: secret, source: .minted)
    }

    /// Drops every prepared secret and cancels a running prefetch. Starts
    /// that already joined the cancelled prefetch mint a new secret.
    public func invalidate() {
        generation &+= 1
        prepared = nil
        running?.task.cancel()
        running = nil
        for task in joined.values {
            task.cancel()
        }
    }

    /// Whether a prepared, still-reusable secret matches `request`. Useful
    /// for diagnostics; `takeSecret(for:)` does not require checking first.
    public func hasPreparedSecret(
        for request: IXRealtimeClientSecretRequest
    ) -> Bool {
        guard let prepared else { return false }
        return prepared.request == request && isReusable(prepared.secret)
    }

    /// Test and diagnostics hooks.
    var isMinting: Bool { running != nil }
    var joinedMintCount: Int { joined.count }

    /// Claims a matching running prefetch, waits for it, and consumes its
    /// result. Claiming removes it from `running`, so a concurrent start
    /// cannot receive the same secret and mints its own instead. Returns nil
    /// when the prefetch failed, was invalidated, or produced a secret that is
    /// no longer reusable, so the caller mints on demand.
    private func join(_ mint: RunningMint) async throws -> IXRealtimeClientSecret? {
        running = nil
        joined[mint.id] = mint.task
        // Waiting is bounded by the minting client's own request deadline.
        let result = await mint.task.result
        joined[mint.id] = nil
        try Task.checkCancellation()
        guard mint.generation == generation,
              case .success(let secret) = result,
              isReusable(secret) else {
            return nil
        }
        return secret
    }

    private func finishPrefetch(
        id: UUID,
        result: Result<IXRealtimeClientSecret, Error>
    ) {
        guard let running, running.id == id else { return }
        self.running = nil
        guard running.generation == generation,
              case .success(let secret) = result,
              isReusable(secret) else {
            return
        }
        prepared = PreparedSecret(
            id: id,
            request: running.request,
            secret: secret
        )
    }

    private func isReusable(_ secret: IXRealtimeClientSecret) -> Bool {
        secret.expiresAt.timeIntervalSince(now()) >=
            minimumRemainingLifetime.ixTimeInterval
    }
}

private extension Duration {
    var ixTimeInterval: TimeInterval {
        let components = components
        return TimeInterval(components.seconds) +
            TimeInterval(components.attoseconds) / 1e18
    }
}
