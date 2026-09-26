import Foundation

/// Shared bookkeeping for everyone holding the process-wide voice audio
/// session: active Realtime sessions and caller-owned preparations. The most
/// recently activated owner decides the applied profile, and the session is
/// deactivated only when the last owner leaves.
struct IXRealtimeAudioSessionOwners {
    enum Release: Equatable {
        /// The owner was unknown, or another owner's configuration is still
        /// the one applied; nothing has to change.
        case unchanged
        /// Reapply the profile of the owner that is now most recent.
        case reconfigure(IXRealtimeAudioSessionProfile)
        /// No owner remains; deactivate the audio session.
        case deactivate
    }

    private var profilesByOwnerID: [UUID: IXRealtimeAudioSessionProfile] = [:]
    private var activationOrder: [UUID] = []

    var count: Int {
        profilesByOwnerID.count
    }

    var currentOwnerID: UUID? {
        activationOrder.last
    }

    func profile(for ownerID: UUID) -> IXRealtimeAudioSessionProfile? {
        profilesByOwnerID[ownerID]
    }

    mutating func activate(
        ownerID: UUID,
        profile: IXRealtimeAudioSessionProfile
    ) {
        profilesByOwnerID[ownerID] = profile
        activationOrder.removeAll(where: { $0 == ownerID })
        activationOrder.append(ownerID)
    }

    mutating func release(ownerID: UUID) -> Release {
        guard let releasedProfile = profilesByOwnerID.removeValue(
            forKey: ownerID
        ) else {
            return .unchanged
        }
        let wasCurrent = activationOrder.last == ownerID
        activationOrder.removeAll(where: { $0 == ownerID })
        guard let remainingOwnerID = activationOrder.last,
              let remainingProfile = profilesByOwnerID[remainingOwnerID]
        else {
            return .deactivate
        }
        // The applied configuration belongs to the most recent owner. Leaving
        // an older owner, or handing over to an identical profile, does not
        // need another category change and its route-change notifications.
        guard wasCurrent, remainingProfile != releasedProfile else {
            return .unchanged
        }
        return .reconfigure(remainingProfile)
    }
}

#if os(iOS)
@preconcurrency import AVFAudio
import IntelligenceXCodex

actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    private var owners = IXRealtimeAudioSessionOwners()

    func activate(
        ownerID: UUID,
        profile: IXRealtimeAudioSessionProfile
    ) async throws {
        let session = AVAudioSession.sharedInstance()
        let isAllowed: Bool
        switch session.recordPermission {
        case .granted:
            isAllowed = true
        case .denied:
            isAllowed = false
        case .undetermined:
            isAllowed = await Self.requestRecordPermission()
        @unknown default:
            isAllowed = false
        }

        guard isAllowed else {
            throw IXCodexError.invalidResponse(
                "Microphone access is off. Allow microphone access for this app in Settings."
            )
        }

        try Self.configureVoiceSession(profile: profile)
        owners.activate(ownerID: ownerID, profile: profile)
    }

    /// AVAudioSession deliberately invokes its permission callback on a TCC
    /// queue under Mac Catalyst. Keeping the callback outside the @MainActor
    /// executor prevents Swift 6 from asserting that TCC is the main queue.
    nonisolated private static func requestRecordPermission() async -> Bool {
        await withCheckedContinuation { continuation in
            AVAudioSession.sharedInstance().requestRecordPermission { granted in
                continuation.resume(returning: granted)
            }
        }
    }

    func deactivate(ownerID: UUID) async {
        switch owners.release(ownerID: ownerID) {
        case .unchanged:
            return
        case .reconfigure(let profile):
            try? Self.configureVoiceSession(profile: profile)
        case .deactivate:
            try? AVAudioSession.sharedInstance().setActive(
                false,
                options: .notifyOthersOnDeactivation
            )
        }
    }

    var activeOwnerCount: Int {
        owners.count
    }

    /// Reapplies the shared voice-chat configuration after an interruption,
    /// route change, or media-services reset without changing ownership.
    func recover(ownerID: UUID) async throws {
        guard owners.currentOwnerID == ownerID,
              let profile = owners.profile(for: ownerID) else { return }
        try Self.configureVoiceSession(profile: profile)
    }

    /// Keep category changes synchronous on this actor's serial executor. An
    /// awaited detached configuration would make the actor reentrant and let
    /// a superseded recovery finish after a newer owner became active.
    nonisolated private static func configureVoiceSession(
        profile: IXRealtimeAudioSessionProfile
    ) throws {
        let session = AVAudioSession.sharedInstance()
        switch profile {
        case .voiceConversation:
            try session.setCategory(
                .playAndRecord,
                mode: .voiceChat,
                options: [.defaultToSpeaker, .allowBluetoothHFP]
            )
        case .carPlayConversation:
            try session.setCategory(
                .playAndRecord,
                mode: .default,
                options: []
            )
        }
        try session.setActive(true)
    }
}
#else
actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    private var owners = IXRealtimeAudioSessionOwners()

    func activate(
        ownerID: UUID,
        profile: IXRealtimeAudioSessionProfile
    ) async throws {
        owners.activate(ownerID: ownerID, profile: profile)
    }
    func deactivate(ownerID: UUID) async {
        _ = owners.release(ownerID: ownerID)
    }
    func recover(ownerID: UUID) async throws {}
    var activeOwnerCount: Int { owners.count }
}
#endif
