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

public struct IXRealtimeSessionOptions: Sendable, Equatable {
    public var model: String
    public var instructions: String
    public var voice: String?
    public var outputModality: String
    public var transcriptionModel: String?
    public var transcriptionLanguage: String?
    public var transcriptionPrompt: String?
    public var semanticVADEagerness: IXRealtimeSemanticVADEagerness
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
        semanticVADEagerness: IXRealtimeSemanticVADEagerness = .high,
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
        self.semanticVADEagerness = semanticVADEagerness
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

        var input: [String: IXJSONValue] = [
            "turn_detection": .object([
                "type": .string("semantic_vad"),
                "eagerness": .string(semanticVADEagerness.rawValue),
                "create_response": .bool(createsResponsesAutomatically),
                "interrupt_response": .bool(interruptsResponseOnSpeech),
            ]),
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

        return [
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
    }
}
