import Foundation

/// Serializes access to an asynchronous credential store.
///
/// Actor isolation alone is insufficient here because an actor method can be
/// re-entered while the underlying Keychain operation is suspended. The task
/// chain preserves the order in which load/save/delete operations were
/// requested, so a completed refresh can never write credentials after a
/// later sign-out has already been queued.
actor IXCodexCredentialAccess {
    private let store: any IXCodexCredentialStoring
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

    func save(_ bundle: IXCodexAuthBundle) async throws {
        let previous = tail
        let store = store
        let operation = Task {
            await previous.value
            try await store.save(bundle)
        }
        tail = Task {
            _ = try? await operation.value
        }
        try await operation.value
    }

    func delete() async throws {
        let previous = tail
        let store = store
        let operation = Task {
            await previous.value
            try await store.delete()
        }
        tail = Task {
            _ = try? await operation.value
        }
        try await operation.value
    }
}
