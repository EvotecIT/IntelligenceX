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
            }
        )

        let secret = try await client.createClientSecret(
            options: .init(instructions: "Help with the home."),
            tools: [
                IXCodexToolDefinition(
                    name: "get_home_state",
                    description: "Read normalized home state.",
                    parameters: .object([:]),
                    strict: true
                ),
            ]
        )

        XCTAssertEqual(secret.value, "ek_test")
        let recordedRequest = await recorder.request
        let request = try XCTUnwrap(recordedRequest)
        XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer oauth-access")
        XCTAssertEqual(request.value(forHTTPHeaderField: "ChatGPT-Account-ID"), "account-1")
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
            "gpt-4o-transcribe"
        )
        XCTAssertEqual(
            body["session"]?["tools"]?.arrayValue?.first?["name"]?.stringValue,
            "get_home_state"
        )
        XCTAssertNil(body["session"]?["tools"]?.arrayValue?.first?["strict"])
    }

    func testSessionUpdatePreservesLanguageAndConversationalTurnPolicy() throws {
        let event = IXRealtimeClientEvent.sessionUpdate(
            options: .init(
                instructions: "Reply in the user's language.",
                transcriptionLanguage: "pl",
                transcriptionPrompt: "Transcribe in the spoken language without translation.",
                turnDetection: .semantic(eagerness: .high),
                createsResponsesAutomatically: true,
                interruptsResponseOnSpeech: true
            )
        )

        let session = try XCTUnwrap(event["session"])
        XCTAssertEqual(
            session["audio"]?["input"]?["transcription"]?["language"]?.stringValue,
            "pl"
        )
        XCTAssertEqual(
            session["audio"]?["input"]?["turn_detection"]?["interrupt_response"]?.boolValue,
            true
        )
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
            transcription.serverEvent,
            .inputTranscriptionCompleted(
                itemID: "item-1",
                transcript: "Dzień dobry"
            )
        )
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
}

private actor RealtimeRequestRecorder {
    private(set) var request: URLRequest?

    func record(_ request: URLRequest) {
        self.request = request
    }
}

private extension IXHTTPResponse {
    static func json(_ statusCode: Int, _ object: Any) -> IXHTTPResponse {
        IXHTTPResponse(statusCode: statusCode, body: try! JSONSerialization.data(withJSONObject: object))
    }
}
