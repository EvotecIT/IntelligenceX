import Foundation

/// Runs async work behind an absolute elapsed-time deadline without requiring
/// the underlying operation to cooperate with cancellation.
package enum IXElapsedDeadline {
    package static let fallbackTimeout: TimeInterval = 12
    package static let minimumTimeout: TimeInterval = 0.01
    // Int32.max seconds already exceeds any useful network operation lifetime
    // and stays comfortably inside Duration's portable floating-point range.
    package static let maximumTimeout: TimeInterval = TimeInterval(Int32.max)

    package static func normalized(
        _ value: TimeInterval
    ) -> TimeInterval {
        guard value.isFinite else { return fallbackTimeout }
        return min(maximumTimeout, max(minimumTimeout, value))
    }

    package static func run<Value: Sendable>(
        timeoutInterval: TimeInterval,
        operation: @escaping @Sendable () async throws -> Value
    ) async throws -> Value {
        let timeout = normalized(timeoutInterval)
        let race = IXElapsedDeadlineRace<Value>()
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                race.install(continuation)
                let operationTask = Task {
                    do {
                        try Task.checkCancellation()
                        guard race.permitsOperationStart() else { return }
                        try Task.checkCancellation()
                        race.resolve(.success(try await operation()))
                    } catch {
                        race.resolve(.failure(error))
                    }
                }
                let deadlineTask = Task {
                    do {
                        try await Task.sleep(for: .seconds(timeout))
                    } catch {
                        return
                    }
                    race.resolve(.failure(URLError(.timedOut)))
                }
                race.install(
                    operation: operationTask,
                    deadline: deadlineTask
                )
            }
        } onCancel: {
            race.resolve(.failure(CancellationError()))
        }
    }
}

private final class IXElapsedDeadlineRace<Value: Sendable>:
    @unchecked Sendable
{
    typealias DeadlineResult = Result<Value, Error>

    private let lock = NSLock()
    private var continuation: CheckedContinuation<Value, Error>?
    private var operation: Task<Void, Never>?
    private var deadline: Task<Void, Never>?
    private var settledResult: DeadlineResult?

    func install(_ continuation: CheckedContinuation<Value, Error>) {
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

    func permitsOperationStart() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        return settledResult == nil
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
