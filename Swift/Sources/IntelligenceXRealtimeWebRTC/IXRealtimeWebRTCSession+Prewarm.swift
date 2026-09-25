import Foundation

extension IXRealtimeWebRTCSession {
    /// Creates the process-wide WebRTC peer connection factory ahead of the
    /// first `connect()`.
    ///
    /// The first factory construction initializes SSL, codecs, and the audio
    /// device module, and can block its thread for hundreds of milliseconds on
    /// an iPhone. Call this when a voice entry point becomes likely (for
    /// example when the app becomes active or a Talk button appears) so the
    /// cost is paid before the person starts speaking.
    ///
    /// The work always runs on a detached background task, never on the
    /// caller's executor, so it is safe to await from the main actor. The call
    /// is idempotent and thread-safe: concurrent calls, and a `connect()` that
    /// starts while preparation is still running, join the same creation.
    /// Prewarming does not touch the audio session, request microphone
    /// permission, or open a network connection.
    ///
    /// - Parameter priority: The priority of the background creation when this
    ///   call starts it. A later `connect()` that joins the creation escalates
    ///   it to the connecting task's priority.
    public nonisolated static func prewarm(
        priority: TaskPriority = .utility
    ) async {
        _ = await IXRealtimeWebRTCPeerFactoryProvider.shared.factory(
            priority: priority
        )
    }
}
