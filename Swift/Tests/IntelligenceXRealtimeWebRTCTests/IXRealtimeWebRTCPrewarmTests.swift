import Foundation
import XCTest
@testable import IntelligenceXRealtimeWebRTC

final class IXRealtimeWebRTCPrewarmTests: XCTestCase {
    func testSingleFlightValueCreatesOnceForConcurrentCallers() async {
        let counter = CreationCounter()
        let value = IXRealtimeSingleFlightValue {
            counter.increment()
            Thread.sleep(forTimeInterval: 0.05)
            return CreatedObject()
        }

        let objects = await withTaskGroup(of: CreatedObject.self) { group in
            for index in 0..<16 {
                group.addTask {
                    await value.value(priority: index.isMultiple(of: 2) ? .utility : .userInitiated)
                }
            }
            var objects: [CreatedObject] = []
            for await object in group {
                objects.append(object)
            }
            return objects
        }

        XCTAssertEqual(counter.value, 1)
        XCTAssertEqual(objects.count, 16)
        XCTAssertTrue(objects.allSatisfy { $0 === objects[0] })
        let isAvailable = await value.isAvailable
        XCTAssertTrue(isAvailable)
    }

    @MainActor
    func testSingleFlightValueRunsCreationOffTheMainThread() async {
        let value = IXRealtimeSingleFlightValue { Thread.isMainThread }
        let createdOnMainThread = await value.value(priority: .userInitiated)
        XCTAssertFalse(createdOnMainThread)
    }

    @MainActor
    func testPrewarmPreparesTheFactoryThatConnectReuses() async {
        await IXRealtimeWebRTCSession.prewarm()
        await IXRealtimeWebRTCSession.prewarm(priority: .userInitiated)

        let isReady = await IXRealtimeWebRTCPeerFactoryProvider.shared.isFactoryReady
        XCTAssertTrue(isReady)
        let first = await IXRealtimeWebRTCPeerFactoryProvider.shared.factory()
        let second = await IXRealtimeWebRTCPeerFactoryProvider.shared.factory()
        XCTAssertTrue(first === second)
    }
}

final class IXRealtimeAudioSessionOwnershipTests: XCTestCase {
    func testPreparedReservationKeepsSessionThroughHandOffToConnect() {
        var owners = IXRealtimeAudioSessionOwners()
        let reservation = UUID()
        let session = UUID()

        owners.activate(ownerID: reservation, profile: .voiceConversation)
        owners.activate(ownerID: session, profile: .voiceConversation)

        // Releasing the warm-up after connect leaves the conversation alone.
        XCTAssertEqual(owners.release(ownerID: reservation), .unchanged)
        XCTAssertEqual(owners.count, 1)
        XCTAssertEqual(owners.release(ownerID: session), .deactivate)
        XCTAssertEqual(owners.count, 0)
    }

    func testReleasingUnusedReservationDeactivates() {
        var owners = IXRealtimeAudioSessionOwners()
        let reservation = UUID()

        owners.activate(ownerID: reservation, profile: .voiceConversation)

        XCTAssertEqual(owners.release(ownerID: reservation), .deactivate)
        XCTAssertEqual(owners.release(ownerID: reservation), .unchanged)
    }

    func testReleasingMostRecentOwnerRestoresPreviousDifferentProfile() {
        var owners = IXRealtimeAudioSessionOwners()
        let phone = UUID()
        let car = UUID()

        owners.activate(ownerID: phone, profile: .voiceConversation)
        owners.activate(ownerID: car, profile: .carPlayConversation)

        XCTAssertEqual(owners.currentOwnerID, car)
        XCTAssertEqual(owners.release(ownerID: car), .reconfigure(.voiceConversation))
        XCTAssertEqual(owners.currentOwnerID, phone)
    }

    func testReactivatingOwnerMovesItToTheFront() {
        var owners = IXRealtimeAudioSessionOwners()
        let first = UUID()
        let second = UUID()

        owners.activate(ownerID: first, profile: .carPlayConversation)
        owners.activate(ownerID: second, profile: .voiceConversation)
        owners.activate(ownerID: first, profile: .carPlayConversation)

        XCTAssertEqual(owners.count, 2)
        XCTAssertEqual(owners.currentOwnerID, first)
        XCTAssertEqual(owners.release(ownerID: second), .unchanged)
    }

    func testPublicReservationTracksAndReleasesOwnership() async throws {
        let initialCount = await IXRealtimeAppleAudioSession.shared.activeOwnerCount

        let reservation = try await IXRealtimeAudioSessionReservation.prepare(
            profile: .carPlayConversation
        )
        XCTAssertEqual(reservation.profile, .carPlayConversation)
        let heldCount = await IXRealtimeAppleAudioSession.shared.activeOwnerCount
        XCTAssertEqual(heldCount, initialCount + 1)

        await reservation.release()
        await reservation.release()
        let finalCount = await IXRealtimeAppleAudioSession.shared.activeOwnerCount
        XCTAssertEqual(finalCount, initialCount)
    }
}

private final class CreatedObject: Sendable {}

private final class CreationCounter: @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    var value: Int {
        lock.withLock { count }
    }

    func increment() {
        lock.withLock { count += 1 }
    }
}
