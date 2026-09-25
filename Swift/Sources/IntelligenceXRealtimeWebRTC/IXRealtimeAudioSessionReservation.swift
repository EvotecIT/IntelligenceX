import Foundation

/// A caller-owned hold on the shared voice audio session.
///
/// `IXRealtimeWebRTCSession.connect()` always activates the audio session
/// itself, so apps do not need this type. Use it only to move that work off
/// the start-up critical path: prepare the session while other start-up work
/// runs (for example while a client secret is minted), then connect.
///
/// Ownership is explicit and reference counted:
/// - The caller that calls `prepare(profile:)` owns the returned reservation
///   and is the only one that releases it. IntelligenceX never releases a
///   caller's reservation.
/// - A connecting `IXRealtimeWebRTCSession` registers its own ownership, and
///   it releases only that ownership when it disconnects.
/// - The audio session is deactivated only when the last owner releases it.
///   Releasing a reservation after `connect()` succeeded therefore leaves the
///   conversation's audio untouched, while releasing one whose connect never
///   happened, or failed, deactivates the warmed session.
///
/// Typical use, overlapping audio preparation with secret minting:
///
/// ```swift
/// let audio = Task { try await IXRealtimeAudioSessionReservation.prepare() }
/// let secret: IXRealtimeClientSecret
/// do {
///     secret = try await realtimeClient.createClientSecret(options: options)
/// } catch {
///     try? await audio.value.release()
///     throw error
/// }
/// let reservation = try await audio.value
/// let session = IXRealtimeWebRTCSession(secret: secret, onEvent: handle)
/// do {
///     try await session.connect()
/// } catch {
///     await reservation.release()
///     throw error
/// }
/// // The connected session now holds the audio session on its own.
/// await reservation.release()
/// ```
///
/// On platforms without `AVAudioSession` (macOS), preparing only records the
/// ownership and never touches audio hardware.
public struct IXRealtimeAudioSessionReservation: Sendable, Hashable {
    /// The audio-session policy this reservation applied.
    public let profile: IXRealtimeAudioSessionProfile
    let ownerID: UUID

    init(profile: IXRealtimeAudioSessionProfile, ownerID: UUID) {
        self.profile = profile
        self.ownerID = ownerID
    }

    /// Resolves microphone permission and activates the shared audio session
    /// with the same configuration `connect()` applies for `profile`.
    ///
    /// Shows the system microphone prompt when permission was never decided
    /// and waits for the answer. Pass the same profile the session will use
    /// so its own activation becomes a cheap re-apply.
    ///
    /// - Throws: An error when microphone access is denied or the audio
    ///   session cannot be activated. Nothing is held when this throws.
    public static func prepare(
        profile: IXRealtimeAudioSessionProfile = .voiceConversation
    ) async throws -> IXRealtimeAudioSessionReservation {
        let reservation = IXRealtimeAudioSessionReservation(
            profile: profile,
            ownerID: UUID()
        )
        try await IXRealtimeAppleAudioSession.shared.activate(
            ownerID: reservation.ownerID,
            profile: profile
        )
        return reservation
    }

    /// Gives up this reservation. The audio session is deactivated only when
    /// no Realtime session or other reservation still holds it. Releasing
    /// more than once has no further effect.
    public func release() async {
        await IXRealtimeAppleAudioSession.shared.deactivate(ownerID: ownerID)
    }
}
