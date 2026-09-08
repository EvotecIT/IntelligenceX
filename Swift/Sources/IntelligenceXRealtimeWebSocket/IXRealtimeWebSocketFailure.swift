import Foundation

/// Content-free transport evidence. Error descriptions, URLs, headers, close
/// reasons and NSError userInfo are deliberately excluded.
public struct IXRealtimeWebSocketFailure: Equatable, Sendable {
    public enum Operation: String, Sendable { case connect, send, receive }
    public enum Domain: String, Sendable { case url, posix, cocoa, cancellation, other }

    public let operation: Operation
    public let domain: Domain
    public let code: Int
    public let taskWasCancelled: Bool
    public let closeCode: Int?

    public init(
        error: any Error, operation: Operation,
        taskWasCancelled: Bool, closeCode: Int? = nil
    ) {
        let value = error as NSError
        self.operation = operation
        self.domain = if error is CancellationError { .cancellation }
            else { switch value.domain {
            case NSURLErrorDomain: .url
            case NSPOSIXErrorDomain: .posix
            case NSCocoaErrorDomain: .cocoa
            default: .other
            } }
        self.code = value.code
        self.taskWasCancelled = taskWasCancelled
        self.closeCode = closeCode
    }
}
