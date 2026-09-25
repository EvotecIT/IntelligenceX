import Foundation
import IntelligenceXCodex
@testable import IntelligenceXRealtime
import XCTest

final class IXRealtimeClientSecretPrefetcherTests: XCTestCase {
    private let referenceDate = Date(timeIntervalSince1970: 1_000_000)

    func testPrefetchedSecretIsHandedOutOnceAndThenMintedOnDemand() async throws {
        let minter = SecretMinter(expiresAt: referenceDate.addingTimeInterval(300))
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        try await waitUntilPrepared(prefetcher, request)

        let first = try await prefetcher.takeSecret(for: request)
        let second = try await prefetcher.takeSecret(for: request)

        XCTAssertEqual(first.source, .cached)
        XCTAssertEqual(first.secret.value, "secret-1")
        XCTAssertEqual(second.source, .minted)
        XCTAssertEqual(second.secret.value, "secret-2")
        let mintCount = await minter.count
        XCTAssertEqual(mintCount, 2)
    }

    func testRepeatedPrefetchesJoinOneMint() async throws {
        let minter = SecretMinter(expiresAt: referenceDate.addingTimeInterval(300))
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        await prefetcher.prefetch(request)
        try await waitUntilPrepared(prefetcher, request)
        await prefetcher.prefetch(request)

        let mintCount = await minter.count
        XCTAssertEqual(mintCount, 1)
    }

    func testTakeJoinsRunningPrefetchInsteadOfMintingAgain() async throws {
        let minter = SecretMinter(
            expiresAt: referenceDate.addingTimeInterval(300),
            holdsFirstMint: true
        )
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        await minter.waitUntilFirstMintStarted()
        let take = Task { try await prefetcher.takeSecret(for: request) }
        try await waitUntilJoined(prefetcher)
        await minter.releaseFirstMint()

        let result = try await take.value
        XCTAssertEqual(result.source, .joinedPrefetch)
        XCTAssertEqual(result.secret.value, "secret-1")
        let mintCount = await minter.count
        XCTAssertEqual(mintCount, 1)
    }

    func testConcurrentStartsNeverShareOneSecret() async throws {
        let minter = SecretMinter(
            expiresAt: referenceDate.addingTimeInterval(300),
            holdsFirstMint: true
        )
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        await minter.waitUntilFirstMintStarted()
        let first = Task { try await prefetcher.takeSecret(for: request) }
        try await waitUntilJoined(prefetcher)
        let second = try await prefetcher.takeSecret(for: request)
        await minter.releaseFirstMint()
        let joined = try await first.value

        XCTAssertEqual(joined.source, .joinedPrefetch)
        XCTAssertEqual(second.source, .minted)
        XCTAssertNotEqual(joined.secret.value, second.secret.value)
    }

    func testSecretCloseToExpiryIsNotReused() async throws {
        let minter = SecretMinter(expiresAt: referenceDate.addingTimeInterval(20))
        let prefetcher = makePrefetcher(minter, margin: .seconds(30))
        let request = Self.request()

        await prefetcher.prefetch(request)
        try await waitUntilMintCount(minter, 1)
        try await waitUntilIdle(prefetcher)
        let isPrepared = await prefetcher.hasPreparedSecret(for: request)
        XCTAssertFalse(isPrepared)

        let result = try await prefetcher.takeSecret(for: request)
        XCTAssertEqual(result.source, .minted)
        XCTAssertEqual(result.secret.value, "secret-2")
    }

    func testDifferentScopeOrOptionsNeverReceivePreparedSecret() async throws {
        let minter = SecretMinter(expiresAt: referenceDate.addingTimeInterval(300))
        let prefetcher = makePrefetcher(minter)
        let firstAccount = Self.request(scope: "account-1")

        await prefetcher.prefetch(firstAccount)
        try await waitUntilPrepared(prefetcher, firstAccount)

        let otherAccount = try await prefetcher.takeSecret(
            for: Self.request(scope: "account-2")
        )
        XCTAssertEqual(otherAccount.source, .minted)
        let stillPrepared = await prefetcher.hasPreparedSecret(for: firstAccount)
        XCTAssertFalse(stillPrepared)

        var otherVoice = Self.request(scope: "account-1")
        otherVoice.options.voice = "cedar"
        await prefetcher.prefetch(firstAccount)
        try await waitUntilPrepared(prefetcher, firstAccount)
        let voiceResult = try await prefetcher.takeSecret(for: otherVoice)
        XCTAssertEqual(voiceResult.source, .minted)
        let scopes = await minter.requests.map(\.scope)
        XCTAssertEqual(scopes, ["account-1", "account-2", "account-1", "account-1"])
        let voices = await minter.requests.map(\.options.voice)
        XCTAssertEqual(voices.last, "cedar")
    }

    func testInvalidateDropsPreparedSecret() async throws {
        let minter = SecretMinter(expiresAt: referenceDate.addingTimeInterval(300))
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        try await waitUntilPrepared(prefetcher, request)
        await prefetcher.invalidate()

        let result = try await prefetcher.takeSecret(for: request)
        XCTAssertEqual(result.source, .minted)
        XCTAssertEqual(result.secret.value, "secret-2")
    }

    func testInvalidateDuringJoinMintsReplacementSecret() async throws {
        let minter = SecretMinter(
            expiresAt: referenceDate.addingTimeInterval(300),
            holdsFirstMint: true,
            firstMintIgnoresCancellation: true
        )
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        await minter.waitUntilFirstMintStarted()
        let take = Task { try await prefetcher.takeSecret(for: request) }
        try await waitUntilJoined(prefetcher)
        await prefetcher.invalidate()
        await minter.releaseFirstMint()

        let result = try await take.value
        XCTAssertEqual(result.source, .minted)
        XCTAssertEqual(result.secret.value, "secret-2")
    }

    func testInvalidateDuringOnDemandMintDiscardsTheSecret() async throws {
        let minter = SecretMinter(
            expiresAt: referenceDate.addingTimeInterval(300),
            holdsFirstMint: true
        )
        let prefetcher = makePrefetcher(minter)
        let take = Task { try await prefetcher.takeSecret(for: Self.request()) }
        await minter.waitUntilFirstMintStarted()
        await prefetcher.invalidate()
        await minter.releaseFirstMint()

        do {
            _ = try await take.value
            XCTFail("A secret minted across invalidation must not be returned")
        } catch is CancellationError {
        }
    }

    func testFailedPrefetchIsRetriedOnDemand() async throws {
        let minter = SecretMinter(
            expiresAt: referenceDate.addingTimeInterval(300),
            failuresBeforeSuccess: 1
        )
        let prefetcher = makePrefetcher(minter)
        let request = Self.request()

        await prefetcher.prefetch(request)
        let result = try await prefetcher.takeSecret(for: request)

        XCTAssertEqual(result.source, .minted)
        let mintCount = await minter.count
        XCTAssertEqual(mintCount, 2)
    }

    func testOnDemandMintErrorsReachTheCaller() async throws {
        let minter = SecretMinter(
            expiresAt: referenceDate.addingTimeInterval(300),
            failuresBeforeSuccess: 5
        )
        let prefetcher = makePrefetcher(minter)

        do {
            _ = try await prefetcher.takeSecret(for: Self.request())
            XCTFail("A failed on-demand mint must throw")
        } catch let error as IXCodexError {
            XCTAssertEqual(error, .authenticationRequired)
        }
    }

    func testMinimalRequestOmitsInstructionsToolsAndAutomaticResponses() {
        let options = IXRealtimeSessionOptions(
            model: "gpt-realtime-2.1",
            instructions: "Private home context.",
            voice: "marin",
            reasoningEffort: .low,
            clientSecretLifetime: .seconds(300)
        )

        let request = IXRealtimeClientSecretRequest.minimal(
            options: options,
            scope: "account-1"
        )

        XCTAssertEqual(request.options.instructions, "")
        XCTAssertFalse(request.options.createsResponsesAutomatically)
        XCTAssertTrue(request.tools.isEmpty)
        XCTAssertEqual(request.scope, "account-1")
        XCTAssertEqual(request.options.model, "gpt-realtime-2.1")
        XCTAssertEqual(request.options.voice, "marin")
        XCTAssertEqual(request.options.reasoningEffort, .low)
        XCTAssertEqual(request.options.clientSecretLifetime, .seconds(300))
        XCTAssertEqual(request.options.inputTranscription, options.inputTranscription)
    }

    func testClientBackedPrefetcherMintsMinimalSessionPayload() async throws {
        let recorder = PrefetchRequestRecorder()
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "oauth-access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account-1"
            ))
        )
        let client = IXRealtimeClient(
            authSession: auth,
            httpClient: IXClosureHTTPClient { request in
                await recorder.record(request)
                let object: [String: Any] = [
                    "value": "ek_prefetched",
                    "expires_at": Date().addingTimeInterval(300).timeIntervalSince1970,
                    "session": ["model": "gpt-realtime-2.1"],
                ]
                return IXHTTPResponse(
                    statusCode: 200,
                    body: try JSONSerialization.data(withJSONObject: object)
                )
            }
        )
        let prefetcher = IXRealtimeClientSecretPrefetcher(client: client)
        let request = IXRealtimeClientSecretRequest.minimal(
            options: .init(instructions: "Private home context.", clientSecretLifetime: .seconds(300)),
            scope: "account-1"
        )

        await prefetcher.prefetch(request)
        let result = try await prefetcher.takeSecret(for: request)

        XCTAssertEqual(result.secret.value, "ek_prefetched")
        let requests = await recorder.requests
        XCTAssertEqual(requests.count, 1)
        let body = try IXJSONValue.decode(try XCTUnwrap(requests.first?.httpBody))
        XCTAssertEqual(body["expires_after"]?["seconds"], .number(300))
        XCTAssertEqual(body["session"]?["instructions"]?.stringValue, "")
        XCTAssertEqual(body["session"]?["tools"]?.arrayValue?.count, 0)
        XCTAssertEqual(
            body["session"]?["audio"]?["input"]?["turn_detection"]?["create_response"]?.boolValue,
            false
        )
    }

    // MARK: - Helpers

    private static func request(scope: String? = "account-1") -> IXRealtimeClientSecretRequest {
        .minimal(options: .init(instructions: ""), scope: scope)
    }

    private func makePrefetcher(
        _ minter: SecretMinter,
        margin: Duration = .seconds(30)
    ) -> IXRealtimeClientSecretPrefetcher {
        let now = referenceDate
        return IXRealtimeClientSecretPrefetcher(
            minimumRemainingLifetime: margin,
            now: { now },
            mint: { request in try await minter.mint(request) }
        )
    }

    private func waitUntilPrepared(
        _ prefetcher: IXRealtimeClientSecretPrefetcher,
        _ request: IXRealtimeClientSecretRequest
    ) async throws {
        for _ in 0..<400 {
            if await prefetcher.hasPreparedSecret(for: request) { return }
            try await Task.sleep(for: .milliseconds(5))
        }
        XCTFail("The prefetch did not finish")
    }

    private func waitUntilJoined(
        _ prefetcher: IXRealtimeClientSecretPrefetcher
    ) async throws {
        for _ in 0..<400 {
            if await prefetcher.joinedMintCount > 0 { return }
            try await Task.sleep(for: .milliseconds(5))
        }
        XCTFail("The start did not join the prefetch")
    }

    private func waitUntilIdle(
        _ prefetcher: IXRealtimeClientSecretPrefetcher
    ) async throws {
        for _ in 0..<400 {
            if await !prefetcher.isMinting { return }
            try await Task.sleep(for: .milliseconds(5))
        }
        XCTFail("The prefetch did not settle")
    }

    private func waitUntilMintCount(_ minter: SecretMinter, _ count: Int) async throws {
        for _ in 0..<400 {
            if await minter.count >= count { return }
            try await Task.sleep(for: .milliseconds(5))
        }
        XCTFail("The mint did not start")
    }
}

private actor SecretMinter {
    private let expiresAt: Date
    private let holdsFirstMint: Bool
    private let firstMintIgnoresCancellation: Bool
    private var failuresRemaining: Int
    private var firstMintRelease: CheckedContinuation<Void, Never>?
    private var firstMintReleased = false
    private var firstMintStartWaiters: [CheckedContinuation<Void, Never>] = []
    private(set) var requests: [IXRealtimeClientSecretRequest] = []

    init(
        expiresAt: Date,
        holdsFirstMint: Bool = false,
        firstMintIgnoresCancellation: Bool = false,
        failuresBeforeSuccess: Int = 0
    ) {
        self.expiresAt = expiresAt
        self.holdsFirstMint = holdsFirstMint
        self.firstMintIgnoresCancellation = firstMintIgnoresCancellation
        failuresRemaining = failuresBeforeSuccess
    }

    var count: Int { requests.count }

    func mint(_ request: IXRealtimeClientSecretRequest) async throws -> IXRealtimeClientSecret {
        requests.append(request)
        let number = requests.count
        if number == 1 {
            for waiter in firstMintStartWaiters { waiter.resume() }
            firstMintStartWaiters.removeAll()
            if holdsFirstMint && !firstMintReleased {
                await withCheckedContinuation { firstMintRelease = $0 }
            }
            if !firstMintIgnoresCancellation {
                try Task.checkCancellation()
            }
        }
        if failuresRemaining > 0 {
            failuresRemaining -= 1
            throw IXCodexError.authenticationRequired
        }
        return IXRealtimeClientSecret(
            value: "secret-\(number)",
            expiresAt: expiresAt,
            model: request.options.model
        )
    }

    func waitUntilFirstMintStarted() async {
        guard requests.isEmpty else { return }
        await withCheckedContinuation { firstMintStartWaiters.append($0) }
    }

    func releaseFirstMint() {
        firstMintReleased = true
        firstMintRelease?.resume()
        firstMintRelease = nil
    }
}

private actor PrefetchRequestRecorder {
    private(set) var requests: [URLRequest] = []

    func record(_ request: URLRequest) {
        requests.append(request)
    }
}
