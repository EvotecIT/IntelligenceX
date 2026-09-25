@preconcurrency import LiveKitWebRTC

/// Creates LiveKit's process-wide peer factory away from the main actor. The
/// first construction initializes SSL, codecs, and the audio device module and
/// can synchronously block for hundreds of milliseconds on an iPhone.
actor IXRealtimeWebRTCPeerFactoryProvider {
    static let shared = IXRealtimeWebRTCPeerFactoryProvider()

    private let factoryValue = IXRealtimeSingleFlightValue {
        IXRealtimeWebRTCPeerFactoryBox()
    }

    /// Returns the shared factory, creating it once. Concurrent callers join
    /// the same creation, so a `prewarm()` that is still running is reused by
    /// `connect()` instead of starting a second initialization.
    func factory(
        priority: TaskPriority = .userInitiated
    ) async -> IXRealtimeWebRTCPeerFactoryBox {
        await factoryValue.value(priority: priority)
    }

    /// Whether the factory has finished initializing.
    var isFactoryReady: Bool {
        get async { await factoryValue.isAvailable }
    }
}

/// A lazily created value whose construction runs once, detached from the
/// caller's executor. Concurrent requests join the in-flight construction, and
/// awaiting it escalates the creation task to the waiter's priority.
actor IXRealtimeSingleFlightValue<Value: Sendable> {
    private let make: @Sendable () -> Value
    private var storedValue: Value?
    private var creationTask: Task<Value, Never>?

    init(make: @escaping @Sendable () -> Value) {
        self.make = make
    }

    var isAvailable: Bool {
        storedValue != nil
    }

    func value(priority: TaskPriority) async -> Value {
        if let storedValue {
            return storedValue
        }
        let task: Task<Value, Never>
        if let creationTask {
            task = creationTask
        } else {
            let make = make
            task = Task.detached(priority: priority) {
                make()
            }
            creationTask = task
        }
        let value = await task.value
        storedValue = value
        creationTask = nil
        return value
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
