import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime
@preconcurrency import LiveKitWebRTC
#if os(iOS)
import AVFAudio
#endif

public enum IXRealtimeConnectionState: Sendable, Equatable {
    case idle
    case connecting
    case connected
    case disconnected
    case failed(String)
}

/// Coalesces the several delegate and audio-lifecycle callbacks that can all
/// describe the same transport state. Realtime clients configure a session
/// when it first becomes connected, so forwarding duplicate `.connected`
/// callbacks can create a session-update feedback loop.
struct IXRealtimeConnectionStateGate {
    private(set) var state: IXRealtimeConnectionState = .idle

    mutating func accepts(_ next: IXRealtimeConnectionState) -> Bool {
        guard next != state else { return false }
        state = next
        return true
    }
}

@MainActor
public final class IXRealtimeWebRTCSession: NSObject {
    public typealias EventHandler = @MainActor @Sendable (IXRealtimeEvent) async -> Void
    public typealias StateHandler = @MainActor @Sendable (IXRealtimeConnectionState) -> Void

    private static let peerFactory: LKRTCPeerConnectionFactory = {
        _ = LKRTCInitializeSSL()
        let factory = LKRTCPeerConnectionFactory(
            audioDeviceModuleType: .audioEngine,
            bypassVoiceProcessing: false,
            encoderFactory: LKRTCDefaultVideoEncoderFactory(),
            decoderFactory: LKRTCDefaultVideoDecoderFactory(),
            audioProcessingModule: nil
        )
        _ = factory.audioDeviceModule.setEngineAvailability(
            LKRTCAudioEngineAvailability(isInputAvailable: true, isOutputAvailable: true)
        )
        return factory
    }()

    private let secret: IXRealtimeClientSecret
    private let exchange: any IXRealtimeSDPExchanging
    private let onEvent: EventHandler
    private let onState: StateHandler
    private nonisolated let delegateEventBridge = IXRealtimeDelegateEventBridge()
    private var peerConnection: LKRTCPeerConnection?
    private var dataChannel: LKRTCDataChannel?
    private var localAudioTrack: LKRTCAudioTrack?
    private var remoteAudioTracks: [String: LKRTCAudioTrack] = [:]
    private var outputPlaybackEnabled = true
    private var ownsAudioSession = false
    private var audioSessionOwnerID: UUID?
    private var lifecycleGeneration: UInt64 = 0
    private var pendingEventData: [(generation: UInt64, data: Data)] = []
    private var isDeliveringEvents = false
    private var audioSessionObserverTokens: [NSObjectProtocol] = []
    private var microphoneWasEnabledBeforeInterruption = false
    private var isRecoveringAudioSession = false
    private var connectionStateGate = IXRealtimeConnectionStateGate()

    /// Indicates whether the Realtime event channel can currently accept
    /// session updates, tool outputs, and response requests.
    public var isReady: Bool {
        peerConnection?.connectionState == .connected &&
            dataChannel?.readyState == .open
    }

    /// Controls whether the local microphone track is sent to Realtime without
    /// disconnecting the peer or disturbing output playback.
    public var isMicrophoneEnabled: Bool {
        localAudioTrack?.isEnabled == true
    }

    /// Indicates whether received Realtime audio is allowed to render locally.
    public var isOutputPlaybackEnabled: Bool {
        outputPlaybackEnabled
    }

    public init(
        secret: IXRealtimeClientSecret,
        exchange: any IXRealtimeSDPExchanging = IXOpenAIRealtimeSDPExchange(),
        onEvent: @escaping EventHandler,
        onState: @escaping StateHandler = { _ in }
    ) {
        self.secret = secret
        self.exchange = exchange
        self.onEvent = onEvent
        self.onState = onState
        super.init()
    }

    public func connect() async throws {
        guard secret.expiresAt > Date() else {
            throw IXCodexError.authenticationRequired
        }
        disconnect()
        let expectedGeneration = lifecycleGeneration
        let audioSessionOwnerID = UUID()
        outputPlaybackEnabled = true
        do {
            try await IXRealtimeAppleAudioSession.shared.activate(
                ownerID: audioSessionOwnerID
            )
            guard lifecycleGeneration == expectedGeneration else {
                await IXRealtimeAppleAudioSession.shared.deactivate(
                    ownerID: audioSessionOwnerID
                )
                throw CancellationError()
            }
            ownsAudioSession = true
            self.audioSessionOwnerID = audioSessionOwnerID
            installAudioSessionObservers(generation: expectedGeneration)
            try await connectPeer(generation: expectedGeneration)
        } catch {
            if lifecycleGeneration == expectedGeneration {
                disconnect()
            } else {
                await IXRealtimeAppleAudioSession.shared.deactivate(
                    ownerID: audioSessionOwnerID
                )
            }
            throw error
        }
    }

    private func connectPeer(generation: UInt64) async throws {
        guard lifecycleGeneration == generation else {
            throw CancellationError()
        }
        reportState(.connecting)
        let configuration = LKRTCConfiguration()
        configuration.sdpSemantics = .unifiedPlan
        let constraints = LKRTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)
        guard let peer = Self.peerFactory.peerConnection(
            with: configuration,
            constraints: constraints,
            delegate: self
        ) else {
            throw IXCodexError.invalidResponse("WebRTC peer connection could not be created")
        }
        peerConnection = peer

        let audioSource = Self.peerFactory.audioSource(with: constraints)
        let track = Self.peerFactory.audioTrack(with: audioSource, trackId: "ix-microphone")
        let processingResult = track.setAudioProcessingOptions(
            .communication()
        )
        guard processingResult.isSuccess else {
            throw IXCodexError.invalidResponse(
                "WebRTC communication audio processing could not be enabled: " +
                    processingResult.message
            )
        }
        localAudioTrack = track
        guard peer.add(track, streamIds: ["ix-realtime"]) != nil else {
            throw IXCodexError.invalidResponse("WebRTC microphone track could not be added")
        }

        let channel = peer.dataChannel(forLabel: "oai-events", configuration: LKRTCDataChannelConfiguration())
        channel?.delegate = self
        dataChannel = channel

        let offerConstraints = LKRTCMediaConstraints(
            mandatoryConstraints: [
                "OfferToReceiveAudio": "true",
                "OfferToReceiveVideo": "false",
            ],
            optionalConstraints: nil
        )
        let offer = try await createOffer(peer: peer, constraints: offerConstraints)
        guard lifecycleGeneration == generation else {
            throw CancellationError()
        }
        try await setLocalDescription(offer, peer: peer)
        guard lifecycleGeneration == generation else {
            throw CancellationError()
        }
        let answerSDP = try await exchange.exchange(offer: offer.sdp, secret: secret)
        guard lifecycleGeneration == generation else {
            throw CancellationError()
        }
        let answer = LKRTCSessionDescription(type: .answer, sdp: answerSDP)
        try await setRemoteDescription(answer, peer: peer)
        guard lifecycleGeneration == generation else {
            throw CancellationError()
        }
    }

    @discardableResult
    public func send(_ event: IXJSONValue) throws -> Bool {
        guard let dataChannel, isReady else {
            let channelState = dataChannel.map {
                String(describing: $0.readyState)
            } ?? "missing"
            let peerState = peerConnection.map {
                String(describing: $0.connectionState)
            } ?? "missing"
            throw IXCodexError.invalidResponse(
                "Realtime data channel is not open (channel: \(channelState), peer: \(peerState))"
            )
        }
        let data = try event.encodedData()
        return dataChannel.sendData(LKRTCDataBuffer(data: data, isBinary: false))
    }

    public func disconnect() {
        lifecycleGeneration &+= 1
        removeAudioSessionObservers()
        pendingEventData.removeAll(keepingCapacity: true)
        let detachedDataChannel = dataChannel
        detachedDataChannel?.delegate = nil
        dataChannel = nil
        let detachedLocalAudioTrack = localAudioTrack
        localAudioTrack = nil
        let detachedRemoteAudioTracks = Array(remoteAudioTracks.values)
        remoteAudioTracks.removeAll()
        let detachedPeerConnection = peerConnection
        detachedPeerConnection?.delegate = nil
        peerConnection = nil
        let shouldDeactivateAudioSession = ownsAudioSession
        let detachedAudioSessionOwnerID = audioSessionOwnerID
        ownsAudioSession = false
        audioSessionOwnerID = nil
        reportState(.idle)

        let teardown = IXRealtimeWebRTCTeardown(
            dataChannel: detachedDataChannel,
            localAudioTrack: detachedLocalAudioTrack,
            remoteAudioTracks: detachedRemoteAudioTracks,
            peerConnection: detachedPeerConnection
        )
        Task.detached(priority: .utility) {
            teardown.perform()
            if shouldDeactivateAudioSession,
               let detachedAudioSessionOwnerID {
                await IXRealtimeAppleAudioSession.shared.deactivate(
                    ownerID: detachedAudioSessionOwnerID
                )
            }
        }
    }

    private func enqueueEventData(_ data: Data) {
        pendingEventData.append((lifecycleGeneration, data))
        guard !isDeliveringEvents else { return }
        isDeliveringEvents = true
        Task { @MainActor [weak self] in
            await self?.drainEventData()
        }
    }

    private func drainEventData() async {
        defer { isDeliveringEvents = false }
        while !pendingEventData.isEmpty {
            let pending = pendingEventData.removeFirst()
            guard pending.generation == lifecycleGeneration else { continue }
            guard let event = try? IXRealtimeEvent(data: pending.data) else {
                continue
            }
            await onEvent(event)
            guard pending.generation == lifecycleGeneration else { return }
        }
    }

    public func setMicrophoneEnabled(_ isEnabled: Bool) {
        localAudioTrack?.isEnabled = isEnabled
    }

    /// Immediately mutes or restores received Realtime audio without waiting
    /// for a server-side output-buffer acknowledgement.
    public func setOutputPlaybackEnabled(_ isEnabled: Bool) {
        outputPlaybackEnabled = isEnabled
        for track in remoteAudioTracks.values {
            track.isEnabled = isEnabled
        }
    }

    private func installAudioSessionObservers(generation: UInt64) {
#if os(iOS)
        removeAudioSessionObservers()
        let center = NotificationCenter.default
        audioSessionObserverTokens = [
            center.addObserver(
                forName: AVAudioSession.interruptionNotification,
                object: AVAudioSession.sharedInstance(),
                queue: .main
            ) { [weak self] notification in
                let rawType = notification.userInfo?[
                    AVAudioSessionInterruptionTypeKey
                ] as? UInt
                let rawOptions = notification.userInfo?[
                    AVAudioSessionInterruptionOptionKey
                ] as? UInt
                Task { @MainActor [weak self] in
                    await self?.handleAudioInterruption(
                        rawType: rawType,
                        rawOptions: rawOptions,
                        generation: generation
                    )
                }
            },
            center.addObserver(
                forName: AVAudioSession.routeChangeNotification,
                object: AVAudioSession.sharedInstance(),
                queue: .main
            ) { [weak self] notification in
                let rawReason = notification.userInfo?[
                    AVAudioSessionRouteChangeReasonKey
                ] as? UInt
                Task { @MainActor [weak self] in
                    await self?.handleAudioRouteChange(
                        rawReason: rawReason,
                        generation: generation
                    )
                }
            },
            center.addObserver(
                forName: AVAudioSession.mediaServicesWereResetNotification,
                object: AVAudioSession.sharedInstance(),
                queue: .main
            ) { [weak self] _ in
                Task { @MainActor [weak self] in
                    await self?.recoverAudioSession(generation: generation)
                }
            },
        ]
#endif
    }

    private func removeAudioSessionObservers() {
        let center = NotificationCenter.default
        for token in audioSessionObserverTokens {
            center.removeObserver(token)
        }
        audioSessionObserverTokens = []
        microphoneWasEnabledBeforeInterruption = false
        isRecoveringAudioSession = false
    }

#if os(iOS)
    private func handleAudioInterruption(
        rawType: UInt?,
        rawOptions: UInt?,
        generation: UInt64
    ) async {
        guard generation == lifecycleGeneration,
              let rawType,
              let type = AVAudioSession.InterruptionType(rawValue: rawType) else { return }
        switch type {
        case .began:
            microphoneWasEnabledBeforeInterruption = isMicrophoneEnabled
            setMicrophoneEnabled(false)
            reportState(.disconnected)
        case .ended:
            let options = AVAudioSession.InterruptionOptions(
                rawValue: rawOptions ?? 0
            )
            guard options.contains(.shouldResume) else {
                reportState(.failed("Audio interruption did not allow the voice session to resume"))
                return
            }
            await recoverAudioSession(generation: generation)
            if generation == lifecycleGeneration, microphoneWasEnabledBeforeInterruption {
                setMicrophoneEnabled(true)
            }
        @unknown default:
            reportState(.failed("Unknown audio interruption state"))
        }
    }

    private func handleAudioRouteChange(
        rawReason: UInt?,
        generation: UInt64
    ) async {
        guard generation == lifecycleGeneration,
              let rawReason,
              let reason = AVAudioSession.RouteChangeReason(
                rawValue: rawReason
              ) else { return }

        switch reason {
        case .newDeviceAvailable,
             .oldDeviceUnavailable,
             .noSuitableRouteForCategory,
             .wakeFromSleep:
            await recoverAudioSession(generation: generation)
        case .unknown,
             .categoryChange,
             .override,
             .routeConfigurationChange:
            // Reapplying `.playAndRecord` produces a category-change
            // notification itself. Recovering from that notification creates
            // an unbounded AVAudioSession loop and blocks the UI thread.
            break
        @unknown default:
            break
        }
    }
#endif

    private func recoverAudioSession(generation: UInt64) async {
        guard generation == lifecycleGeneration,
              let audioSessionOwnerID,
              ownsAudioSession,
              !isRecoveringAudioSession else { return }
        isRecoveringAudioSession = true
        defer { isRecoveringAudioSession = false }
        do {
            try await IXRealtimeAppleAudioSession.shared.recover(ownerID: audioSessionOwnerID)
            guard generation == lifecycleGeneration else { return }
            if isReady {
                reportState(.connected)
            }
        } catch {
            guard generation == lifecycleGeneration else { return }
            reportState(.failed(error.localizedDescription))
        }
    }

    private func reportState(_ state: IXRealtimeConnectionState) {
        guard connectionStateGate.accepts(state) else { return }
        onState(state)
    }

    private func createOffer(
        peer: LKRTCPeerConnection,
        constraints: LKRTCMediaConstraints
    ) async throws -> LKRTCSessionDescription {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<LKRTCSessionDescription, Error>) in
            peer.offer(for: constraints) { description, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if let description {
                    continuation.resume(returning: description)
                } else {
                    continuation.resume(throwing: IXCodexError.invalidResponse("WebRTC offer is missing"))
                }
            }
        }
    }

    private func setLocalDescription(
        _ description: LKRTCSessionDescription,
        peer: LKRTCPeerConnection
    ) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            peer.setLocalDescription(description) { error in
                if let error { continuation.resume(throwing: error) }
                else { continuation.resume() }
            }
        }
    }

    private func setRemoteDescription(
        _ description: LKRTCSessionDescription,
        peer: LKRTCPeerConnection
    ) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            peer.setRemoteDescription(description) { error in
                if let error { continuation.resume(throwing: error) }
                else { continuation.resume() }
            }
        }
    }
}

/// LiveKit/WebRTC close and AVAudioSession deactivation can synchronously wait
/// on media and XPC queues. Detaching the already-disowned objects keeps those
/// waits off the UI actor while retaining them until teardown is complete.
private final class IXRealtimeWebRTCTeardown: @unchecked Sendable {
    private let dataChannel: LKRTCDataChannel?
    private let localAudioTrack: LKRTCAudioTrack?
    private let remoteAudioTracks: [LKRTCAudioTrack]
    private let peerConnection: LKRTCPeerConnection?

    init(
        dataChannel: LKRTCDataChannel?,
        localAudioTrack: LKRTCAudioTrack?,
        remoteAudioTracks: [LKRTCAudioTrack],
        peerConnection: LKRTCPeerConnection?
    ) {
        self.dataChannel = dataChannel
        self.localAudioTrack = localAudioTrack
        self.remoteAudioTracks = remoteAudioTracks
        self.peerConnection = peerConnection
    }

    func perform() {
        dataChannel?.close()
        localAudioTrack?.isEnabled = false
        for track in remoteAudioTracks {
            track.isEnabled = false
        }
        peerConnection?.close()
    }
}

extension IXRealtimeWebRTCSession: LKRTCDataChannelDelegate {
    nonisolated public func dataChannelDidChangeState(_ dataChannel: LKRTCDataChannel) {
        Task { @MainActor [weak self] in
            guard let self, dataChannel === self.dataChannel else { return }
            if dataChannel.readyState == .open { self.reportState(.connected) }
            if dataChannel.readyState == .closed { self.reportState(.disconnected) }
        }
    }

    nonisolated public func dataChannel(
        _ dataChannel: LKRTCDataChannel,
        didReceiveMessageWith buffer: LKRTCDataBuffer
    ) {
        let data = buffer.data
        delegateEventBridge.enqueue { @MainActor [weak self] in
            guard let self, dataChannel === self.dataChannel else { return }
            self.enqueueEventData(data)
        }
    }
}

/// Preserves callback order while crossing from WebRTC's delegate queue to
/// the main actor. Creating one unstructured task per callback can reorder two
/// adjacent Realtime events under load, which is especially harmful for tool
/// arguments and response-completion events.
private final class IXRealtimeDelegateEventBridge: @unchecked Sendable {
    typealias Delivery = @MainActor @Sendable () async -> Void

    private let lock = NSLock()
    private var deliveries: [Delivery] = []
    private var isDraining = false

    func enqueue(_ delivery: @escaping Delivery) {
        lock.lock()
        deliveries.append(delivery)
        let shouldStart = !isDraining
        if shouldStart {
            isDraining = true
        }
        lock.unlock()

        guard shouldStart else { return }
        Task { @MainActor [self] in
            await drain()
        }
    }

    @MainActor
    private func drain() async {
        while let delivery = next() {
            await delivery()
        }
    }

    private func next() -> Delivery? {
        lock.lock()
        defer { lock.unlock() }
        guard !deliveries.isEmpty else {
            isDraining = false
            return nil
        }
        return deliveries.removeFirst()
    }
}

extension IXRealtimeWebRTCSession: LKRTCPeerConnectionDelegate {
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didChange stateChanged: LKRTCSignalingState) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didAdd stream: LKRTCMediaStream) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didRemove stream: LKRTCMediaStream) {}
    nonisolated public func peerConnectionShouldNegotiate(_ peerConnection: LKRTCPeerConnection) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didChange newState: LKRTCIceConnectionState) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didChange newState: LKRTCIceGatheringState) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didGenerate candidate: LKRTCIceCandidate) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didRemove candidates: [LKRTCIceCandidate]) {}
    nonisolated public func peerConnection(_ peerConnection: LKRTCPeerConnection, didOpen dataChannel: LKRTCDataChannel) {}

    nonisolated public func peerConnection(
        _ peerConnection: LKRTCPeerConnection,
        didAdd receiver: LKRTCRtpReceiver,
        streams: [LKRTCMediaStream]
    ) {
        Task { @MainActor [weak self] in
            guard let self,
                  peerConnection === self.peerConnection,
                  let track = receiver.track as? LKRTCAudioTrack else {
                return
            }
            self.remoteAudioTracks[receiver.receiverId] = track
            track.isEnabled = self.outputPlaybackEnabled
        }
    }

    nonisolated public func peerConnection(
        _ peerConnection: LKRTCPeerConnection,
        didRemove receiver: LKRTCRtpReceiver
    ) {
        Task { @MainActor [weak self] in
            guard let self,
                  peerConnection === self.peerConnection else {
                return
            }
            self.remoteAudioTracks.removeValue(forKey: receiver.receiverId)
        }
    }

    nonisolated public func peerConnection(
        _ peerConnection: LKRTCPeerConnection,
        didChange newState: LKRTCPeerConnectionState
    ) {
        Task { @MainActor [weak self] in
            guard let self, peerConnection === self.peerConnection else { return }
            switch newState {
            // A connected peer does not guarantee that the event data channel is
            // open yet. `dataChannelDidChangeState` is the readiness signal used
            // before callers send session configuration or tool events.
            case .connected:
                if self.dataChannel?.readyState == .open {
                    self.reportState(.connected)
                }
            // Report transport loss even when ICE may recover later. Clients
            // need this signal to stop sending events and begin their own
            // bounded recovery policy.
            case .disconnected: self.reportState(.disconnected)
            case .closed: self.reportState(.disconnected)
            case .failed: self.reportState(.failed("WebRTC connection failed"))
            default: break
            }
        }
    }
}
