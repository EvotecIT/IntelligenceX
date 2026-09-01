import Foundation
import IntelligenceXCodex
@testable import IntelligenceXRealtime
import XCTest

final class IXRealtimeClientTests: XCTestCase {
    func testClientSecretUsesChatGPTOAuthAndAccountHeaders() async throws {
        let recorder = RealtimeRequestRecorder()
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
                return .json(200, [
                    "value": "ek_test",
                    "expires_at": 2_000_000_000,
                    "session": ["model": "gpt-realtime-2.1"],
                ])
            },
            requestTimeoutInterval: 7
        )

        let secret = try await client.createClientSecret(
            options: .init(instructions: "Help with the home."),
            tools: [
                IXCodexToolDefinition(
                    name: "get_home_state",
                    description: "Read normalized home state.",
                    parameters: .object([
                        "type": .string("object"),
                        "properties": .object([:]),
                        "required": .array([]),
                        "additionalProperties": .bool(false),
                    ]),
                    strict: true
                ),
            ]
        )

        XCTAssertEqual(secret.value, "ek_test")
        let recordedRequest = await recorder.request
        let request = try XCTUnwrap(recordedRequest)
        XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer oauth-access")
        XCTAssertEqual(request.value(forHTTPHeaderField: "ChatGPT-Account-ID"), "account-1")
        XCTAssertEqual(request.timeoutInterval, 7)
        let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
        XCTAssertEqual(body["session"]?["model"]?.stringValue, "gpt-realtime-2.1")
        XCTAssertNil(body["session"]?["reasoning"])
        let expectedModalities: [IXJSONValue] = [.string("audio")]
        XCTAssertEqual(body["session"]?["output_modalities"]?.arrayValue, expectedModalities)
        let turnDetection = body["session"]?["audio"]?["input"]?["turn_detection"]
        XCTAssertEqual(turnDetection?["type"]?.stringValue, "semantic_vad")
        XCTAssertEqual(turnDetection?["eagerness"]?.stringValue, "auto")
        XCTAssertEqual(turnDetection?["create_response"]?.boolValue, true)
        XCTAssertEqual(turnDetection?["interrupt_response"]?.boolValue, true)
        XCTAssertEqual(
            body["session"]?["audio"]?["input"]?["noise_reduction"]?["type"]?.stringValue,
            "near_field"
        )
        XCTAssertEqual(
            body["session"]?["audio"]?["input"]?["transcription"]?["model"]?.stringValue,
            "gpt-live-transcribe"
        )
        XCTAssertEqual(
            body["session"]?["tools"]?.arrayValue?.first?["name"]?.stringValue,
            "get_home_state"
        )
        XCTAssertNil(body["session"]?["tools"]?.arrayValue?.first?["strict"])
    }

    func testClientSecretEnforcesAbsoluteDeadlineForNoncooperativeHTTPClient() async throws {
        let suspendedHTTP = NoncooperativeRealtimeHTTPClient()
        let client = IXRealtimeClient(
            authSession: makeAuthorizedSession(),
            httpClient: suspendedHTTP,
            requestTimeoutInterval: 0.02
        )
        let clock = ContinuousClock()
        let startedAt = clock.now

        do {
            _ = try await client.createClientSecret(
                options: .init(instructions: "Help with the home."),
                tools: []
            )
            XCTFail("Expected the absolute request deadline to expire")
        } catch let error as URLError {
            XCTAssertEqual(error.code, .timedOut)
        } catch {
            XCTFail("Expected URLError.timedOut, received \(error)")
        }

        XCTAssertLessThan(startedAt.duration(to: clock.now), .seconds(1))
        await suspendedHTTP.release()
    }

    func testClientSecretNormalizesNonfiniteTimeoutToFiniteFallback() async throws {
        let recorder = RealtimeRequestRecorder()
        let client = IXRealtimeClient(
            authSession: makeAuthorizedSession(),
            httpClient: IXClosureHTTPClient { request in
                await recorder.record(request)
                return .json(200, [
                    "value": "ek_test",
                    "expires_at": 2_000_000_000,
                    "session": ["model": "gpt-realtime-2.1"],
                ])
            },
            requestTimeoutInterval: .infinity
        )

        _ = try await client.createClientSecret(
            options: .init(instructions: "Help with the home."),
            tools: []
        )

        let recordedRequest = await recorder.request
        let request = try XCTUnwrap(recordedRequest)
        XCTAssertTrue(request.timeoutInterval.isFinite)
        XCTAssertEqual(request.timeoutInterval, 12)
    }

    func testSessionUpdateCarriesContextualTranscriptionHintsAndTurnPolicy() throws {
        let event = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Reply in the user's language.",
                turnDetection: .semantic(eagerness: .high),
                createsResponsesAutomatically: true,
                interruptsResponseOnSpeech: true,
                inputTranscription: .init(
                    model: .gptLiveTranscribe,
                    prompt: "Transcribe in the spoken language without translation.",
                    languageHints: ["pl", "en"],
                    keywords: ["CasaRay", "Home Assistant"],
                    delay: .high
                )
            )
        )

        let session = try XCTUnwrap(event["session"])
        XCTAssertEqual(
            session["audio"]?["input"]?["transcription"]?["languages"]?.arrayValue,
            [.string("pl"), .string("en")]
        )
        XCTAssertEqual(
            session["audio"]?["input"]?["transcription"]?["keywords"]?.arrayValue,
            [.string("CasaRay"), .string("Home Assistant")]
        )
        XCTAssertEqual(
            session["audio"]?["input"]?["transcription"]?["delay"]?.stringValue,
            "high"
        )
        XCTAssertEqual(
            session["audio"]?["input"]?["turn_detection"]?["interrupt_response"]?.boolValue,
            true
        )
    }

    func testLegacyTranscriptionModelsUseSingularLanguageSchema() throws {
        let event = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Transcribe the user.",
                inputTranscription: .init(
                    model: .gpt4oMiniTranscribe,
                    prompt: "Smart-home controls.",
                    languageHints: ["pl", "en"],
                    keywords: ["CasaRay"],
                    delay: .minimal
                )
            )
        )

        let transcription = try XCTUnwrap(
            event["session"]?["audio"]?["input"]?["transcription"]
        )
        XCTAssertEqual(transcription["model"]?.stringValue, "gpt-4o-mini-transcribe")
        XCTAssertEqual(transcription["language"]?.stringValue, "pl")
        XCTAssertNil(transcription["languages"])
        XCTAssertNil(transcription["keywords"])
        XCTAssertNil(transcription["delay"])
    }

    func testModelSpecificTranscriptionCapabilitiesShapePayloads() throws {
        let committed = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Transcribe a committed turn.",
                inputTranscription: .init(
                    model: .gptTranscribe,
                    prompt: "Smart-home controls.",
                    languageHints: ["pl", "en"],
                    keywords: ["CasaRay"],
                    delay: .high
                )
            )
        )
        let committedTranscription = try XCTUnwrap(
            committed["session"]?["audio"]?["input"]?["transcription"]
        )
        XCTAssertEqual(
            committedTranscription["languages"]?.arrayValue,
            [.string("pl"), .string("en")]
        )
        XCTAssertEqual(
            committedTranscription["keywords"]?.arrayValue,
            [.string("CasaRay")]
        )
        XCTAssertNil(committedTranscription["delay"])

        let streamingWhisper = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Transcribe a streaming turn.",
                inputTranscription: .init(
                    model: .gptRealtimeWhisper,
                    prompt: "This must be omitted.",
                    languageHints: ["pl"],
                    delay: .medium
                )
            )
        )
        let whisperTranscription = try XCTUnwrap(
            streamingWhisper["session"]?["audio"]?["input"]?["transcription"]
        )
        XCTAssertNil(whisperTranscription["prompt"])
        XCTAssertEqual(whisperTranscription["language"]?.stringValue, "pl")
        XCTAssertEqual(whisperTranscription["delay"]?.stringValue, "medium")
    }

    func testTranscriptionModelCatalogResolvesEveryRealtimeSchema() {
        let contextual: [IXRealtimeTranscriptionModel] = [
            .gptLiveTranscribe,
            .gptTranscribe,
        ]
        let legacy: [IXRealtimeTranscriptionModel] = [
            .gpt4oTranscribe,
            .gpt4oMiniTranscribe,
            .gpt4oMiniTranscribe2025_12_15,
            .gptRealtimeWhisper,
            .whisper1,
        ]

        XCTAssertTrue(contextual.allSatisfy { $0.contextStyle == .contextual })
        XCTAssertTrue(legacy.allSatisfy { $0.contextStyle == .legacy })
        for model in contextual + legacy {
            XCTAssertEqual(IXRealtimeTranscriptionModel.resolving(model.id), model)
        }
        XCTAssertEqual(
            IXRealtimeTranscriptionModel.resolving("gpt-transcribe-snapshot").contextStyle,
            .contextual
        )
    }

    func testLegacyStringTranscriptionAPIKeepsUnknownSnapshotsSingular() throws {
        var options = IXRealtimeSessionOptions(
            instructions: "Transcribe the user.",
            transcriptionModel: "gpt-4o-transcribe-2026-07-01",
            transcriptionLanguage: "pl"
        )

        var event = IXRealtimeClientEvent.sessionUpdate(options: options)
        var transcription = try XCTUnwrap(
            event["session"]?["audio"]?["input"]?["transcription"]
        )
        XCTAssertEqual(transcription["language"]?.stringValue, "pl")
        XCTAssertNil(transcription["languages"])

        options.transcriptionModel = "custom-legacy-transcriber"
        event = IXRealtimeClientEvent.sessionUpdate(options: options)
        transcription = try XCTUnwrap(
            event["session"]?["audio"]?["input"]?["transcription"]
        )
        XCTAssertEqual(transcription["language"]?.stringValue, "pl")
        XCTAssertNil(transcription["languages"])
    }

    func testSessionUpdateSequenceClearsTranscriptionBeforeApplyingProfile() throws {
        let events = IXRealtimeClientEvent.sessionUpdatesReplacingInputTranscription(
            options: .init(
                instructions: "Transcribe a committed turn.",
                inputTranscription: .init(
                    model: .gptTranscribe,
                    languageHints: ["pl"]
                )
            )
        )

        XCTAssertEqual(events.count, 2)
        XCTAssertEqual(
            events[0]["session"]?["audio"]?["input"]?["transcription"],
            .null
        )
        let replacement = try XCTUnwrap(
            events[1]["session"]?["audio"]?["input"]?["transcription"]
        )
        XCTAssertEqual(replacement["model"]?.stringValue, "gpt-transcribe")
        XCTAssertEqual(replacement["languages"]?.arrayValue, [.string("pl")])
        XCTAssertNil(replacement["prompt"])
        XCTAssertNil(replacement["keywords"])
        XCTAssertNil(replacement["delay"])
    }

    func testClientSecretSurfacesStructuredServiceError() async throws {
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
            httpClient: IXClosureHTTPClient { _ in
                .json(400, [
                    "error": [
                        "message": "Unknown parameter: session.tools[0].strict",
                    ],
                ])
            }
        )

        do {
            _ = try await client.createClientSecret(
                options: .init(instructions: "Help."),
                tools: []
            )
            XCTFail("Expected a request failure")
        } catch let IXCodexError.requestFailed(status, message) {
            XCTAssertEqual(status, 400)
            XCTAssertEqual(
                message,
                "Unknown parameter: session.tools[0].strict"
            )
        }
    }

    func testSessionUpdateSupportsLatencyFirstReasoning() throws {
        let event = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Act on a simple smart-home command.",
                reasoningEffort: .minimal
            )
        )

        XCTAssertEqual(
            event["session"]?["reasoning"]?["effort"]?.stringValue,
            "minimal"
        )
    }

    func testInputTranscriptionConfigurationCanBeExplicitlyCleared() {
        let event =
            IXRealtimeClientEvent.clearInputTranscriptionConfiguration

        XCTAssertEqual(event["type"]?.stringValue, "session.update")
        XCTAssertEqual(
            event["session"]?["audio"]?["input"]?["transcription"],
            .null
        )
    }

    func testSessionUpdateSupportsNoiseResistantServerVAD() throws {
        let event = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Help in a noisy environment.",
                turnDetection: .server(
                    threshold: 0.78,
                    prefixPaddingMilliseconds: 300,
                    silenceDurationMilliseconds: 850
                ),
                noiseReduction: .farField
            )
        )

        let input = try XCTUnwrap(event["session"]?["audio"]?["input"])
        let turnDetection = try XCTUnwrap(input["turn_detection"])
        XCTAssertEqual(turnDetection["type"]?.stringValue, "server_vad")
        XCTAssertEqual(turnDetection["threshold"]?.numberValue, 0.78)
        XCTAssertEqual(turnDetection["prefix_padding_ms"]?.numberValue, 300)
        XCTAssertEqual(turnDetection["silence_duration_ms"]?.numberValue, 850)
        XCTAssertEqual(
            input["noise_reduction"]?["type"]?.stringValue,
            "far_field"
        )
        XCTAssertEqual(turnDetection["create_response"]?.boolValue, true)
        XCTAssertEqual(turnDetection["interrupt_response"]?.boolValue, true)
    }

    func testOutputAudioBufferEventsExposeExactPlaybackLifecycle() throws {
        let started = try makeRealtimeEvent([
            "type": "output_audio_buffer.started",
            "response_id": "response-1",
        ])
        let stopped = try makeRealtimeEvent([
            "type": "output_audio_buffer.stopped",
            "response_id": "response-1",
        ])
        let cleared = try makeRealtimeEvent([
            "type": "output_audio_buffer.cleared",
            "response_id": "response-1",
        ])

        XCTAssertEqual(
            started.outputAudioBufferTransition,
            .started(responseID: "response-1")
        )
        XCTAssertEqual(
            stopped.outputAudioBufferTransition,
            .stopped(responseID: "response-1")
        )
        XCTAssertEqual(
            cleared.outputAudioBufferTransition,
            .cleared(responseID: "response-1")
        )
    }

    func testOutputAudioDeltaExposesPlaybackIdentity() throws {
        let event = try makeRealtimeEvent([
            "type": "response.output_audio.delta",
            "response_id": "response-1",
            "item_id": "item-1",
            "content_index": 2,
            "delta": Data([1, 2]).base64EncodedString(),
        ])

        XCTAssertEqual(event.responseID, "response-1")
        XCTAssertEqual(event.itemID, "item-1")
        XCTAssertEqual(event.contentIndex, 2)
        XCTAssertEqual(event.outputAudioData, Data([1, 2]))
        XCTAssertEqual(
            event.serverEvent,
            .responseAudioDelta(
                responseID: "response-1",
                itemID: "item-1",
                contentIndex: 2,
                data: Data([1, 2])
            )
        )
    }

    func testServerEventsExposeTypedConversationLifecycle() throws {
        let speech = try makeRealtimeEvent([
            "type": "input_audio_buffer.speech_started",
            "item_id": "item-1",
        ])
        let transcription = try makeRealtimeEvent([
            "type": "conversation.item.input_audio_transcription.completed",
            "item_id": "item-1",
            "transcript": "Dzień dobry",
            "languages": [
                ["code": "pl", "confidence": 0.98],
                ["code": "en", "confidence": 0.02],
            ],
        ])
        let transcriptionDelta = try makeRealtimeEvent([
            "type": "conversation.item.input_audio_transcription.delta",
            "item_id": "item-1",
            "delta": "Dzień",
        ])
        let created = try makeRealtimeEvent([
            "type": "response.created",
            "response": ["id": "response-1"],
        ])
        let completed = try makeRealtimeEvent([
            "type": "response.done",
            "response": [
                "id": "response-1",
                "status": "completed",
            ],
        ])

        XCTAssertEqual(speech.serverEvent, .speechStarted(itemID: "item-1"))
        XCTAssertEqual(
            transcriptionDelta.serverEvent,
            .inputTranscriptionDelta(itemID: "item-1", delta: "Dzień")
        )
        XCTAssertEqual(
            transcription.serverEvent,
            .inputTranscriptionCompleted(
                itemID: "item-1",
                transcript: "Dzień dobry"
            )
        )
        XCTAssertEqual(transcription.transcriptionLanguages, ["pl", "en"])
        XCTAssertEqual(
            created.serverEvent,
            .responseCreated(responseID: "response-1")
        )
        guard case .responseCompleted(
            let responseID,
            let response
        ) = completed.serverEvent else {
            return XCTFail("Expected a typed response completion")
        }
        XCTAssertEqual(responseID, "response-1")
        XCTAssertEqual(response["status"]?.stringValue, "completed")
    }

    func testServerEventDecodesFunctionCallOnce() throws {
        let event = try makeRealtimeEvent([
            "type": "response.output_item.done",
            "response_id": "response-1",
            "item": [
                "type": "function_call",
                "call_id": "call-1",
                "name": "get_home_state",
                "arguments": "{\"scope\":\"summary\"}",
            ],
        ])

        XCTAssertEqual(
            event.serverEvent,
            .functionCallCompleted(
                responseID: "response-1",
                call: IXCodexToolCall(
                    id: "call-1",
                    name: "get_home_state",
                    arguments: .object([
                        "scope": .string("summary"),
                    ])
                )
            )
        )
    }

    func testMalformedFunctionArgumentsNeverBecomeAnEmptyToolCall() throws {
        let event = try makeRealtimeEvent([
            "type": "response.output_item.done",
            "response_id": "response-1",
            "item": [
                "type": "function_call",
                "call_id": "call-1",
                "name": "open_gate",
                "arguments": "{not-json",
            ],
        ])

        guard case .error(let message) = event.serverEvent else {
            return XCTFail("Malformed arguments must fail before execution")
        }
        XCTAssertTrue(message.contains("malformed arguments"))
        XCTAssertTrue(message.contains("open_gate"))
    }

    func testSystemMessageCanRestoreTrustedContinuationContext() throws {
        let event = IXRealtimeClientEvent.systemMessage(
            "Continue after a recovered local tool result."
        )

        XCTAssertEqual(event["item"]?["role"]?.stringValue, "system")
        XCTAssertEqual(
            event["item"]?["content"]?.arrayValue?.first?["text"]?.stringValue,
            "Continue after a recovered local tool result."
        )
    }

    func testToolDisabledResponseRequestUsesPerResponseOverride() {
        let event = IXRealtimeClientEvent.createResponseWithoutTools

        XCTAssertEqual(event["type"]?.stringValue, "response.create")
        XCTAssertEqual(
            event["response"]?["tool_choice"]?.stringValue,
            "none"
        )
    }

    func testResponseCancellationUsesRealtimeProtocolEvent() {
        let event = IXRealtimeClientEvent.cancelResponse

        XCTAssertEqual(event["type"]?.stringValue, "response.cancel")
    }

    func testWebRTCAudioBuffersExposeExplicitClearEvents() {
        XCTAssertEqual(
            IXRealtimeClientEvent.clearInputAudioBuffer["type"]?.stringValue,
            "input_audio_buffer.clear"
        )
        XCTAssertEqual(
            IXRealtimeClientEvent.clearOutputAudioBuffer["type"]?.stringValue,
            "output_audio_buffer.clear"
        )
    }

    func testWebSocketAudioEventsEncodeAppendCommitAndTruncation() throws {
        let pcm = Data([1, 2, 3, 4])
        let append = IXRealtimeClientEvent.appendInputAudio(pcm)
        let truncate = IXRealtimeClientEvent.truncateConversationAudio(
            itemID: "item-1",
            contentIndex: 0,
            audioEndMilliseconds: 725
        )

        XCTAssertEqual(append["type"]?.stringValue, "input_audio_buffer.append")
        XCTAssertEqual(
            Data(base64Encoded: try XCTUnwrap(append["audio"]?.stringValue)),
            pcm
        )
        XCTAssertEqual(
            IXRealtimeClientEvent.commitInputAudioBuffer["type"]?.stringValue,
            "input_audio_buffer.commit"
        )
        XCTAssertEqual(truncate["item_id"]?.stringValue, "item-1")
        XCTAssertEqual(truncate["audio_end_ms"]?.numberValue, 725)
    }

    private func makeRealtimeEvent(
        _ object: [String: Any]
    ) throws -> IXRealtimeEvent {
        try IXRealtimeEvent(
            data: JSONSerialization.data(withJSONObject: object)
        )
    }

    private func makeAuthorizedSession() -> IXCodexAuthSession {
        IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "oauth-access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account-1"
            ))
        )
    }
}

private actor RealtimeRequestRecorder {
    private(set) var request: URLRequest?

    func record(_ request: URLRequest) {
        self.request = request
    }
}

private actor NoncooperativeRealtimeHTTPClient: IXHTTPClient {
    private var continuation: CheckedContinuation<IXHTTPResponse, Never>?

    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        await withCheckedContinuation { continuation in
            self.continuation = continuation
        }
    }

    func release() {
        continuation?.resume(
            returning: .json(200, [
                "value": "late-secret",
                "expires_at": 2_000_000_000,
                "session": ["model": "gpt-realtime-2.1"],
            ])
        )
        continuation = nil
    }
}

private extension IXHTTPResponse {
    static func json(_ statusCode: Int, _ object: Any) -> IXHTTPResponse {
        IXHTTPResponse(statusCode: statusCode, body: try! JSONSerialization.data(withJSONObject: object))
    }
}
