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
        model: String = "gpt-realtime-2.1",
        instructions: String,
        voice: String? = "marin",
        outputModality: String = "audio",
        transcriptionModel: String? = "gpt-4o-mini-transcribe",
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
}

public enum IXRealtimeClientEvent {
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
