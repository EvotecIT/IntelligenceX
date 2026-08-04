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

    /// Commits bookkeeping under the same lock used by invalidation. This
    /// closes the final race between checking validity and publishing a write
    /// as the credential store's current value.
    func commitIfValid(_ body: () -> Void) -> Bool {
        lock.withLock {
            guard valid else { return false }
            body()
            return true
        }
    }
}

private final class IXCodexCredentialWriteLedger: @unchecked Sendable {
    struct Commit: Sendable {
        let authorizationID: UUID
        let previousBundle: IXCodexAuthBundle?
        var isRevoked = false
    }

    struct Rollback: Sendable {
        let previousBundle: IXCodexAuthBundle?
        let commitCount: Int
    }

    private let lock = NSLock()
    private var commits: [Commit] = []

    func record(_ commit: Commit) {
        lock.withLock { commits.append(commit) }
    }

    func prepareRevocation(
        _ authorizationIDs: Set<UUID>
    ) -> Rollback? {
        lock.withLock {
            var found = false
            for index in commits.indices where authorizationIDs.contains(
                commits[index].authorizationID
            ) {
                commits[index].isRevoked = true
                found = true
            }
            guard found else {
                return nil
            }
            var rollbackCount = 0
            var previousBundle: IXCodexAuthBundle?
            for commit in commits.reversed() {
                guard commit.isRevoked else { break }
                previousBundle = commit.previousBundle
                rollbackCount += 1
            }
            return Rollback(
                previousBundle: previousBundle,
                commitCount: rollbackCount
            )
        }
    }

    func complete(_ rollback: Rollback) {
        lock.withLock {
            guard rollback.commitCount > 0,
                  rollback.commitCount <= commits.count else {
                return
            }
            commits.removeLast(rollback.commitCount)
        }
    }

    func finalize(_ authorizationID: UUID) {
        lock.withLock {
            guard let index = commits.firstIndex(where: {
                $0.authorizationID == authorizationID
            }) else { return }
            if index == commits.index(before: commits.endIndex) {
                // The newest settled value supersedes all earlier writes.
                commits.removeAll()
            } else {
                commits.remove(at: index)
            }
        }
    }

    func clear() {
        lock.withLock { commits.removeAll() }
    }
}

private final class IXCodexCredentialReadGate: @unchecked Sendable {
    private let lock = NSLock()
    private var readable = true

    var isReadable: Bool {
        lock.withLock { readable }
    }

    func allowReads() {
        lock.withLock { readable = true }
    }

    func failClosed() {
        lock.withLock { readable = false }
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
    private let readGate = IXCodexCredentialReadGate()
    private var tail: Task<Void, Never> = Task {}

    init(store: any IXCodexCredentialStoring) {
        self.store = store
    }

    func load() async throws -> IXCodexAuthBundle? {
        let previous = tail
        let store = store
        let readGate = readGate
        let operation = Task {
            await previous.value
            guard readGate.isReadable else {
                throw IXCodexError.invalidResponse(
                    "Credential storage could not be restored. Sign in again."
                )
            }
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
        let readGate = readGate
        let operation = Task {
            await previous.value
            guard authorization.isValid else { return false }
            let hadReadablePrevious = readGate.isReadable
            let previousBundle: IXCodexAuthBundle?
            if hadReadablePrevious {
                do {
                    previousBundle = try await store.load()
                } catch {
                    readGate.failClosed()
                    throw error
                }
            } else {
                previousBundle = nil
            }
            guard authorization.isValid else { return false }
            do {
                try await store.save(bundle)
            } catch {
                readGate.failClosed()
                throw error
            }
            if !hadReadablePrevious {
                writeLedger.clear()
            }
            let committed = authorization.commitIfValid {
                writeLedger.record(.init(
                    authorizationID: authorization.id,
                    previousBundle: previousBundle
                ))
            }
            guard committed else {
                do {
                    if let previousBundle {
                        try await store.save(previousBundle)
                    } else {
                        try await store.delete()
                    }
                    readGate.allowReads()
                } catch {
                    readGate.failClosed()
                    throw error
                }
                return false
            }
            readGate.allowReads()
            return true
        }
        tail = Task {
            _ = try? await operation.value
        }
        return try await operation.value
    }

    /// Marks every matching write revoked and rolls back only when the newest
    /// committed layers are all revoked. A non-latest revocation stays durable
    /// so a later rollback cannot resurrect that canceled credential bundle.
    func revoke(_ authorizationIDs: Set<UUID>) async throws {
        guard !authorizationIDs.isEmpty else { return }
        let previous = tail
        let store = store
        let writeLedger = writeLedger
        let readGate = readGate
        let operation = Task {
            await previous.value
            guard let rollback = writeLedger.prepareRevocation(
                authorizationIDs
            ) else {
                return false
            }
            guard rollback.commitCount > 0 else { return false }
            do {
                if let previousBundle = rollback.previousBundle {
                    try await store.save(previousBundle)
                } else {
                    try await store.delete()
                }
            } catch {
                readGate.failClosed()
                throw error
            }
            writeLedger.complete(rollback)
            readGate.allowReads()
            return true
        }
        tail = Task {
            _ = try? await operation.value
        }
        _ = try await operation.value
    }

    /// Releases rollback history once a credential write has become the
    /// caller-visible settled value. Older overlapping writes cannot overwrite
    /// it because all store operations remain serialized by the same tail.
    func finalize(_ authorizationID: UUID) async {
        let previous = tail
        let writeLedger = writeLedger
        let operation = Task {
            await previous.value
            writeLedger.finalize(authorizationID)
        }
        tail = operation
        await operation.value
    }

    func delete() async throws {
        let previous = tail
        let store = store
        let writeLedger = writeLedger
        let readGate = readGate
        let operation = Task {
            await previous.value
            do {
                try await store.delete()
            } catch {
                readGate.failClosed()
                throw error
            }
            writeLedger.clear()
            readGate.allowReads()
        }
        tail = Task {
            _ = try? await operation.value
        }
        try await operation.value
    }
}
