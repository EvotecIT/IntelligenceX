import Foundation

/// A synchronous revocation token for one credential write.
///
/// Cancellation handlers and a newer authorization generation can invalidate
/// the token while an asynchronous store ignores task cancellation. The
/// serialized credential-access operation then removes the obsolete write
/// before allowing any later credential operation to run.
final class IXCodexCredentialWriteAuthorization: @unchecked Sendable {
    let id = UUID()

    private let lock = NSLock()
    private var valid = true

    var isValid: Bool {
        lock.withLock { valid }
    }

    func invalidate() {
        lock.withLock { valid = false }
    }
}

private final class IXCodexCredentialWriteLedger: @unchecked Sendable {
    private let lock = NSLock()
    private var committedAuthorizationID: UUID?

    func record(_ authorizationID: UUID) {
        lock.withLock { committedAuthorizationID = authorizationID }
    }

    func containsAny(_ authorizationIDs: Set<UUID>) -> Bool {
        lock.withLock {
            guard let committedAuthorizationID else { return false }
            return authorizationIDs.contains(committedAuthorizationID)
        }
    }

    func clear() {
        lock.withLock { committedAuthorizationID = nil }
    }
}

/// Serializes access to an asynchronous credential store.
///
/// Actor isolation alone is insufficient here because an actor method can be
/// re-entered while the underlying Keychain operation is suspended. The task
/// chain preserves the order in which load/save/delete operations were
/// requested, so a completed refresh can never write credentials after a
/// later sign-out has already been queued.
actor IXCodexCredentialAccess {
    private let store: any IXCodexCredentialStoring
    private let writeLedger = IXCodexCredentialWriteLedger()
    private var tail: Task<Void, Never> = Task {}

    init(store: any IXCodexCredentialStoring) {
        self.store = store
    }

    func load() async throws -> IXCodexAuthBundle? {
        let previous = tail
        let store = store
        let operation = Task {
            await previous.value
            return try await store.load()
        }
        tail = Task {
            _ = try? await operation.value
        }
        return try await operation.value
    }

    @discardableResult
    func save(
        _ bundle: IXCodexAuthBundle,
        authorizedBy authorization: IXCodexCredentialWriteAuthorization
    ) async throws -> Bool {
        let previous = tail
        let store = store
        let writeLedger = writeLedger
        let operation = Task {
            await previous.value
            guard authorization.isValid else { return false }
            try await store.save(bundle)
            guard authorization.isValid else {
                try await store.delete()
                writeLedger.clear()
                return false
            }
            writeLedger.record(authorization.id)
            return true
        }
        tail = Task {
            _ = try? await operation.value
        }
        return try await operation.value
    }

    /// Removes a write that committed immediately before its authorization was
    /// superseded. The identity check keeps this cleanup from deleting a newer
    /// credential write that was queued first.
    func revoke(_ authorizationIDs: Set<UUID>) async throws {
        guard !authorizationIDs.isEmpty else { return }
        let previous = tail
        let store = store
        let writeLedger = writeLedger
        let operation = Task {
            await previous.value
            guard writeLedger.containsAny(authorizationIDs) else {
                return false
            }
            try await store.delete()
            writeLedger.clear()
            return true
        }
        tail = Task {
            _ = try? await operation.value
        }
        _ = try await operation.value
    }

    func delete() async throws {
        let previous = tail
        let store = store
        let writeLedger = writeLedger
        let operation = Task {
            await previous.value
            try await store.delete()
            writeLedger.clear()
        }
        tail = Task {
            _ = try? await operation.value
        }
        try await operation.value
    }
}
