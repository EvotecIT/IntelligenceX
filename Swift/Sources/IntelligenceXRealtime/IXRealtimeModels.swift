import Foundation
import IntelligenceXCodex

public enum IXRealtimeSemanticVADEagerness: String, Sendable, Equatable {
    case low
    case medium
    case high
    case automatic = "auto"
}

public enum IXRealtimeNoiseReduction: String, Sendable, Equatable {
    case nearField = "near_field"
    case farField = "far_field"
}

public enum IXRealtimeReasoningEffort: String, Sendable, Equatable {
    case minimal
    case low
    case medium
    case high
    case xhigh
}

/// Controls how a Realtime session decides that the user started and finished
/// a turn. Semantic VAD favors natural pauses; server VAD exposes acoustic
/// thresholds that are useful in noisy environments.
public enum IXRealtimeTurnDetection: Sendable, Equatable {
    case semantic(eagerness: IXRealtimeSemanticVADEagerness)
    case server(
        threshold: Double,
        prefixPaddingMilliseconds: Int,
        silenceDurationMilliseconds: Int
    )
}

public struct IXRealtimeSessionOptions: Sendable, Equatable {
    public static let defaultModel = "gpt-realtime-2.1"
    public static let defaultTranscriptionModel = "gpt-4o-transcribe"

    public var model: String
    public var instructions: String
    public var voice: String?
    public var outputModality: String
    public var transcriptionModel: String?
    public var transcriptionLanguage: String?
    public var transcriptionPrompt: String?
    public var reasoningEffort: IXRealtimeReasoningEffort?
    public var turnDetection: IXRealtimeTurnDetection
    public var createsResponsesAutomatically: Bool
    public var interruptsResponseOnSpeech: Bool
    public var noiseReduction: IXRealtimeNoiseReduction?
    public var clientSecretLifetime: Duration

    public init(
        model: String = Self.defaultModel,
        instructions: String,
        voice: String? = "marin",
        outputModality: String = "audio",
        transcriptionModel: String? = Self.defaultTranscriptionModel,
        transcriptionLanguage: String? = nil,
        transcriptionPrompt: String? = nil,
        reasoningEffort: IXRealtimeReasoningEffort? = nil,
        turnDetection: IXRealtimeTurnDetection = .semantic(eagerness: .automatic),
        createsResponsesAutomatically: Bool = true,
        interruptsResponseOnSpeech: Bool = true,
        noiseReduction: IXRealtimeNoiseReduction? = .nearField,
        clientSecretLifetime: Duration = .seconds(120)
    ) {
        self.model = model
        self.instructions = instructions
        self.voice = voice
        self.outputModality = outputModality
        self.transcriptionModel = transcriptionModel
        self.transcriptionLanguage = transcriptionLanguage
        self.transcriptionPrompt = transcriptionPrompt
        self.reasoningEffort = reasoningEffort
        self.turnDetection = turnDetection
        self.createsResponsesAutomatically = createsResponsesAutomatically
        self.interruptsResponseOnSpeech = interruptsResponseOnSpeech
        self.noiseReduction = noiseReduction
        self.clientSecretLifetime = clientSecretLifetime
    }
}

public struct IXRealtimeClientSecret: Sendable, Equatable {
    public let value: String
    public let expiresAt: Date
    public let model: String

    public init(value: String, expiresAt: Date, model: String) {
        self.value = value
        self.expiresAt = expiresAt
        self.model = model
    }
}

/// Exact lifecycle transitions for audio rendered by a Realtime WebRTC or SIP
/// client. These events describe the output buffer, not model generation.
public enum IXRealtimeOutputAudioBufferTransition: Sendable, Equatable {
    case started(responseID: String)
    case stopped(responseID: String)
    case cleared(responseID: String)
}

/// Provider-protocol events decoded into stable, typed semantics that can be
/// shared by phone, watch, CarPlay, and future product hosts. Product-specific
/// conversation state remains the responsibility of each consumer.
public enum IXRealtimeServerEvent: Sendable, Equatable {
    public enum TextDeltaKind: Sendable, Equatable {
        case text
        case audioTranscript
    }

    case outputAudioBuffer(IXRealtimeOutputAudioBufferTransition)
    case speechStarted(itemID: String?)
    case speechStopped(itemID: String?)
    case inputAudioCommitted(itemID: String)
    case inputTranscriptionCompleted(itemID: String, transcript: String?)
    case inputTranscriptionFailed(itemID: String, message: String?)
    case responseCreated(responseID: String)
    case responseTextDelta(
        responseID: String,
        delta: String,
        kind: TextDeltaKind
    )
    case responseAudioDelta(
        responseID: String,
        itemID: String?,
        contentIndex: Int?,
        data: Data?
    )
    case responseAudioTranscriptCompleted(
        responseID: String,
        transcript: String
    )
    case responseAudioCompleted(responseID: String)
    case responseCompleted(
        responseID: String,
        response: [String: IXJSONValue]
    )
    case functionCallCompleted(
        responseID: String,
        call: IXCodexToolCall
    )
    case error(message: String)
    case other(type: String)
}

public struct IXRealtimeEvent: Sendable, Equatable {
    public let type: String
    public let raw: IXJSONValue
    public let textDelta: String?
    public let transcriptDelta: String?
    public let errorMessage: String?

    public init(data: Data) throws {
        let raw = try JSONDecoder().decode(IXJSONValue.self, from: data)
        guard let type = raw["type"]?.stringValue else {
            throw IXCodexError.invalidResponse("Realtime event type is missing")
        }
        self.type = type
        self.raw = raw
        self.textDelta = raw["delta"]?.stringValue
        self.transcriptDelta = type.contains("transcript") ? raw["delta"]?.stringValue : nil
        self.errorMessage = raw["error"]?["message"]?.stringValue ?? raw["message"]?.stringValue
    }

    /// PCM16 output carried by WebSocket Realtime sessions. WebRTC sessions
    /// render their remote audio track directly and leave this value nil.
    public var outputAudioData: Data? {
        guard type == "response.output_audio.delta",
              let encoded = raw["delta"]?.stringValue else {
            return nil
        }
        return Data(base64Encoded: encoded)
    }

    public var responseID: String? {
        raw["response_id"]?.stringValue ?? raw["response"]?["id"]?.stringValue
    }

    public var itemID: String? {
        raw["item_id"]?.stringValue ?? raw["item"]?["id"]?.stringValue
    }

    /// The content-part index associated with an output audio delta.
    public var contentIndex: Int? {
        raw["content_index"]?.numberValue.map(Int.init)
    }

    public var outputAudioBufferTransition:
        IXRealtimeOutputAudioBufferTransition? {
        guard let responseID = raw["response_id"]?.stringValue else {
            return nil
        }
        switch type {
        case "output_audio_buffer.started":
            return .started(responseID: responseID)
        case "output_audio_buffer.stopped":
            return .stopped(responseID: responseID)
        case "output_audio_buffer.cleared":
            return .cleared(responseID: responseID)
        default:
            return nil
        }
    }

    /// A typed view of the Realtime server protocol. Consumers should prefer
    /// this over repeating string event names and raw JSON paths.
    public var serverEvent: IXRealtimeServerEvent {
        if let outputAudioBufferTransition {
            return .outputAudioBuffer(outputAudioBufferTransition)
        }
        switch type {
        case "input_audio_buffer.speech_started":
            return .speechStarted(itemID: itemID)
        case "input_audio_buffer.speech_stopped":
            return .speechStopped(itemID: itemID)
        case "input_audio_buffer.committed":
            guard let itemID else { break }
            return .inputAudioCommitted(itemID: itemID)
        case "conversation.item.input_audio_transcription.completed":
            guard let itemID else { break }
            return .inputTranscriptionCompleted(
                itemID: itemID,
                transcript: raw["transcript"]?.stringValue
            )
        case "conversation.item.input_audio_transcription.failed":
            guard let itemID else { break }
            return .inputTranscriptionFailed(
                itemID: itemID,
                message: errorMessage
            )
        case "response.created":
            guard let responseID else { break }
            return .responseCreated(responseID: responseID)
        case "response.output_text.delta":
            guard let responseID else { break }
            return .responseTextDelta(
                responseID: responseID,
                delta: textDelta ?? "",
                kind: .text
            )
        case "response.output_audio_transcript.delta":
            guard let responseID else { break }
            return .responseTextDelta(
                responseID: responseID,
                delta: textDelta ?? "",
                kind: .audioTranscript
            )
        case "response.output_audio.delta":
            guard let responseID else { break }
            return .responseAudioDelta(
                responseID: responseID,
                itemID: itemID,
                contentIndex: contentIndex,
                data: outputAudioData
            )
        case "response.output_audio_transcript.done":
            guard let responseID,
                  let transcript = raw["transcript"]?.stringValue else {
                break
            }
            return .responseAudioTranscriptCompleted(
                responseID: responseID,
                transcript: transcript
            )
        case "response.output_audio.done":
            guard let responseID else { break }
            return .responseAudioCompleted(responseID: responseID)
        case "response.done":
            guard let response = raw["response"]?.objectValue,
                  let responseID = response["id"]?.stringValue else {
                break
            }
            return .responseCompleted(
                responseID: responseID,
                response: response
            )
        case "response.output_item.done":
            guard let responseID,
                  let item = raw["item"]?.objectValue,
                  item["type"]?.stringValue == "function_call",
                  let callID = item["call_id"]?.stringValue,
                  let name = item["name"]?.stringValue else {
                break
            }
            let rawArguments = item["arguments"]?.stringValue ?? "{}"
            let arguments = rawArguments.data(using: .utf8)
                .flatMap { try? IXJSONValue.decode($0) } ?? .object([:])
            return .functionCallCompleted(
                responseID: responseID,
                call: IXCodexToolCall(
                    id: callID,
                    name: name,
                    arguments: arguments
                )
            )
        default:
            break
        }
        if let errorMessage {
            return .error(message: errorMessage)
        }
        return .other(type: type)
    }
}

public enum IXRealtimeClientEvent {
    public static func appendInputAudio(_ pcm16Data: Data) -> IXJSONValue {
        .object([
            "type": .string("input_audio_buffer.append"),
            "audio": .string(pcm16Data.base64EncodedString()),
        ])
    }

    public static let commitInputAudioBuffer: IXJSONValue = .object([
        "type": .string("input_audio_buffer.commit"),
    ])

    /// Truncates unplayed assistant audio after a local barge-in. Callers must
    /// pass the duration that was actually rendered, not the amount received.
    public static func truncateConversationAudio(
        itemID: String,
        contentIndex: Int,
        audioEndMilliseconds: Int
    ) -> IXJSONValue {
        .object([
            "type": .string("conversation.item.truncate"),
            "item_id": .string(itemID),
            "content_index": .number(Double(max(contentIndex, 0))),
            "audio_end_ms": .number(Double(max(audioEndMilliseconds, 0))),
        ])
    }
    public static func sessionUpdate(
        options: IXRealtimeSessionOptions,
        tools: [IXCodexToolDefinition] = []
    ) -> IXJSONValue {
        .object([
            "type": .string("session.update"),
            "session": .object(options.sessionConfiguration(tools: tools)),
        ])
    }

    /// Explicitly clears the server's nested input transcription
    /// configuration. Omitting `language` from a later update does not clear a
    /// previously configured language, so clients can send this first and then
    /// re-enable transcription without a language hint.
    public static let clearInputTranscriptionConfiguration: IXJSONValue =
        .object([
            "type": .string("session.update"),
            "session": .object([
                "type": .string("realtime"),
                "audio": .object([
                    "input": .object([
                        "transcription": .null,
                    ]),
                ]),
            ]),
        ])

    public static func sessionUpdate(
        instructions: String,
        voice: String? = "marin",
        tools: [IXCodexToolDefinition] = []
    ) -> IXJSONValue {
        sessionUpdate(
            options: IXRealtimeSessionOptions(
                instructions: instructions,
                voice: voice
            ),
            tools: tools
        )
    }

    public static func textMessage(_ text: String) -> IXJSONValue {
        message(text, role: "user")
    }

    public static func systemMessage(_ text: String) -> IXJSONValue {
        message(text, role: "system")
    }

    private static func message(_ text: String, role: String) -> IXJSONValue {
        .object([
            "type": .string("conversation.item.create"),
            "item": .object([
                "type": .string("message"),
                "role": .string(role),
                "content": .array([
                    .object(["type": .string("input_text"), "text": .string(text)]),
                ]),
            ]),
        ])
    }

    public static func toolResult(_ result: IXCodexToolResult) throws -> IXJSONValue {
        let data = try JSONEncoder().encode(result.output)
        let output = String(data: data, encoding: .utf8) ?? "{}"
        return .object([
            "type": .string("conversation.item.create"),
            "item": .object([
                "type": .string("function_call_output"),
                "call_id": .string(result.callID),
                "output": .string(output),
            ]),
        ])
    }

    public static let createResponse: IXJSONValue = .object([
        "type": .string("response.create"),
    ])

    /// Cancels the currently generating response. Clients can use this after
    /// classifying microphone input as a genuine barge-in rather than echo.
    public static let cancelResponse: IXJSONValue = .object([
        "type": .string("response.cancel"),
    ])

    /// Clears uncommitted microphone audio. WebRTC clients can use this when
    /// output playback begins so acoustic leakage cannot become a later turn.
    public static let clearInputAudioBuffer: IXJSONValue = .object([
        "type": .string("input_audio_buffer.clear"),
    ])

    /// Immediately cuts off queued WebRTC/SIP output audio. Send
    /// `cancelResponse` first when the response is still generating.
    public static let clearOutputAudioBuffer: IXJSONValue = .object([
        "type": .string("output_audio_buffer.clear"),
    ])

    /// Requests one response that cannot call tools. This lets a client speak
    /// an already-completed local result without allowing the recovery
    /// response to repeat a side effect.
    public static let createResponseWithoutTools: IXJSONValue = .object([
        "type": .string("response.create"),
        "response": .object([
            "tool_choice": .string("none"),
        ]),
    ])
}

extension IXRealtimeSessionOptions {
    func sessionConfiguration(
        tools: [IXCodexToolDefinition] = []
    ) -> [String: IXJSONValue] {
        var transcription: [String: IXJSONValue] = [:]
        if let transcriptionModel {
            transcription["model"] = .string(transcriptionModel)
        }
        if let transcriptionLanguage {
            transcription["language"] = .string(transcriptionLanguage)
        }
        if let transcriptionPrompt {
            transcription["prompt"] = .string(transcriptionPrompt)
        }

        var turnDetectionConfiguration: [String: IXJSONValue] = [
            "create_response": .bool(createsResponsesAutomatically),
            "interrupt_response": .bool(interruptsResponseOnSpeech),
        ]
        switch turnDetection {
        case .semantic(let eagerness):
            turnDetectionConfiguration["type"] = .string("semantic_vad")
            turnDetectionConfiguration["eagerness"] = .string(eagerness.rawValue)
        case .server(
            let threshold,
            let prefixPaddingMilliseconds,
            let silenceDurationMilliseconds
        ):
            turnDetectionConfiguration["type"] = .string("server_vad")
            turnDetectionConfiguration["threshold"] = .number(
                min(max(threshold, 0), 1)
            )
            turnDetectionConfiguration["prefix_padding_ms"] = .number(
                Double(max(prefixPaddingMilliseconds, 0))
            )
            turnDetectionConfiguration["silence_duration_ms"] = .number(
                Double(max(silenceDurationMilliseconds, 0))
            )
        }

        var input: [String: IXJSONValue] = [
            "turn_detection": .object(turnDetectionConfiguration),
        ]
        if !transcription.isEmpty {
            input["transcription"] = .object(transcription)
        }
        if let noiseReduction {
            input["noise_reduction"] = .object([
                "type": .string(noiseReduction.rawValue),
            ])
        }

        var audio: [String: IXJSONValue] = [
            "input": .object(input),
        ]
        if let voice {
            audio["output"] = .object(["voice": .string(voice)])
        }

        var configuration: [String: IXJSONValue] = [
            "type": .string("realtime"),
            "model": .string(model),
            "instructions": .string(instructions),
            "output_modalities": .array([.string(outputModality)]),
            "audio": .object(audio),
            "tool_choice": .string("auto"),
            "tools": .array(tools.map { tool in
                .object([
                    "type": .string("function"),
                    "name": .string(tool.name),
                    "description": .string(tool.description),
                    "parameters": tool.parameters,
                ])
            }),
        ]
        if let reasoningEffort {
            configuration["reasoning"] = .object([
                "effort": .string(reasoningEffort.rawValue),
            ])
        }
        return configuration
    }
}
