import Foundation
import IntelligenceXCodex

public struct IXRealtimeSessionOptions: Sendable, Equatable {
    public var model: String
    public var instructions: String
    public var voice: String?
    public var outputModality: String
    public var clientSecretLifetime: Duration

    public init(
        model: String = "gpt-realtime-2.1",
        instructions: String,
        voice: String? = "marin",
        outputModality: String = "audio",
        clientSecretLifetime: Duration = .seconds(120)
    ) {
        self.model = model
        self.instructions = instructions
        self.voice = voice
        self.outputModality = outputModality
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
        instructions: String,
        voice: String? = "marin",
        tools: [IXCodexToolDefinition] = []
    ) -> IXJSONValue {
        var session: [String: IXJSONValue] = [
            "type": .string("realtime"),
            "instructions": .string(instructions),
            "output_modalities": .array([.string("audio")]),
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
        if let voice {
            session["audio"] = .object([
                "input": .object([
                    "transcription": .object(["model": .string("gpt-4o-mini-transcribe")]),
                    "turn_detection": .object(["type": .string("semantic_vad")]),
                ]),
                "output": .object(["voice": .string(voice)]),
            ])
        }
        return .object([
            "type": .string("session.update"),
            "session": .object(session),
        ])
    }

    public static func textMessage(_ text: String) -> IXJSONValue {
        .object([
            "type": .string("conversation.item.create"),
            "item": .object([
                "type": .string("message"),
                "role": .string("user"),
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
}
