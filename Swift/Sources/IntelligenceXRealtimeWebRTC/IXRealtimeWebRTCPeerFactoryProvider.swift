@preconcurrency import LiveKitWebRTC

/// Creates LiveKit's process-wide peer factory away from the main actor. The
/// first construction initializes SSL, codecs, and the audio device module and
/// can synchronously block for hundreds of milliseconds on an iPhone.
actor IXRealtimeWebRTCPeerFactoryProvider {
    static let shared = IXRealtimeWebRTCPeerFactoryProvider()

    private var storedFactory: IXRealtimeWebRTCPeerFactoryBox?
    private var creationTask: Task<IXRealtimeWebRTCPeerFactoryBox, Never>?

    func factory() async -> IXRealtimeWebRTCPeerFactoryBox {
        if let storedFactory {
            return storedFactory
        }
        let task: Task<IXRealtimeWebRTCPeerFactoryBox, Never>
        if let creationTask {
            task = creationTask
        } else {
            task = Task.detached(priority: .userInitiated) {
                IXRealtimeWebRTCPeerFactoryBox()
            }
            creationTask = task
        }
        let factory = await task.value
        storedFactory = factory
        creationTask = nil
        return factory
    }
}

/// LiveKit's Objective-C factory is internally thread-safe but does not carry
/// Swift Sendable annotations. The box crosses only the one background
/// initialization boundary; sessions continue to own peer state on MainActor.
final class IXRealtimeWebRTCPeerFactoryBox: @unchecked Sendable {
    let value: LKRTCPeerConnectionFactory

    init() {
        _ = LKRTCInitializeSSL()
        let factory = LKRTCPeerConnectionFactory(
            audioDeviceModuleType: .audioEngine,
            bypassVoiceProcessing: false,
            encoderFactory: LKRTCDefaultVideoEncoderFactory(),
            decoderFactory: LKRTCDefaultVideoDecoderFactory(),
            audioProcessingModule: nil
        )
        _ = factory.audioDeviceModule.setEngineAvailability(
            LKRTCAudioEngineAvailability(
                isInputAvailable: true,
                isOutputAvailable: true
            )
        )
        value = factory
    }
}
