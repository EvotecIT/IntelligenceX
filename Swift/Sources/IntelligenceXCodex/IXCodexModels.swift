import Foundation

public struct IXCodexReasoningEffort: RawRepresentable, Codable, Sendable, Hashable, Identifiable {
    public let rawValue: String

    public init?(rawValue: String) {
        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !value.isEmpty else { return nil }
        self.rawValue = value
    }

    public var id: String { rawValue }

    public var displayName: String {
        switch rawValue {
        case "none": "None"
        case "minimal": "Minimal"
        case "low": "Low"
        case "medium": "Medium"
        case "high": "High"
        case "xhigh": "Extra High"
        case "max": "Maximum"
        case "ultra": "Ultra"
        default: rawValue.replacingOccurrences(of: "_", with: " ").capitalized
        }
    }

    public static let none = Self(rawValue: "none")!
    public static let minimal = Self(rawValue: "minimal")!
    public static let low = Self(rawValue: "low")!
    public static let medium = Self(rawValue: "medium")!
    public static let high = Self(rawValue: "high")!
    public static let xhigh = Self(rawValue: "xhigh")!
    public static let max = Self(rawValue: "max")!
    public static let ultra = Self(rawValue: "ultra")!
}

public struct IXCodexReasoningOption: Sendable, Equatable, Identifiable {
    public let effort: IXCodexReasoningEffort
    public let description: String?

    public var id: String { effort.id }

    public init(effort: IXCodexReasoningEffort, description: String? = nil) {
        self.effort = effort
        self.description = description
    }
}

public enum IXImageDetail: String, Codable, Sendable {
    case low
    case high
    case auto
}

public enum IXCodexInput: Sendable, Equatable {
    case text(String)
    case image(data: Data, mimeType: String, detail: IXImageDetail)
}

public enum IXCodexTranscriptRole: String, Codable, Sendable, Equatable {
    case user
    case assistant
}

public struct IXCodexTranscriptMessage: Codable, Sendable, Equatable {
    public let role: IXCodexTranscriptRole
    public let text: String
    public let images: [IXCodexImage]

    public init(
        role: IXCodexTranscriptRole,
        text: String,
        images: [IXCodexImage] = []
    ) {
        self.role = role
        self.text = text
        self.images = images
    }

    private enum CodingKeys: String, CodingKey {
        case role
        case text
        case images
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        role = try container.decode(IXCodexTranscriptRole.self, forKey: .role)
        text = try container.decode(String.self, forKey: .text)
        images = try container.decodeIfPresent(
            [IXCodexImage].self,
            forKey: .images
        ) ?? []
    }
}

public struct IXCodexToolDefinition: Sendable, Equatable {
    public let name: String
    public let description: String
    public let parameters: IXJSONValue
    public let requiresConfirmation: Bool
    public let strict: Bool

    public init(
        name: String,
        description: String,
        parameters: IXJSONValue,
        requiresConfirmation: Bool = false,
        strict: Bool = false
    ) {
        self.name = name
        self.description = description
        self.parameters = parameters
        self.requiresConfirmation = requiresConfirmation
        self.strict = strict
    }
}

public struct IXCodexImageGenerationOptions: Sendable, Equatable {
    public var quality: String?
    public var size: String?
    public var outputFormat: String?
    public var background: String?

    public init(
        quality: String? = nil,
        size: String? = nil,
        outputFormat: String? = nil,
        background: String? = nil
    ) {
        self.quality = quality
        self.size = size
        self.outputFormat = outputFormat
        self.background = background
    }
}

public enum IXCodexToolCallKind: String, Sendable, Equatable {
    case function
    case custom
}

public struct IXCodexToolCall: Sendable, Equatable, Identifiable {
    public let id: String
    public let name: String
    public let arguments: IXJSONValue
    public let kind: IXCodexToolCallKind

    public init(
        id: String,
        name: String,
        arguments: IXJSONValue,
        kind: IXCodexToolCallKind = .function
    ) {
        self.id = id
        self.name = name
        self.arguments = arguments
        self.kind = kind
    }
}

public struct IXCodexToolResult: Sendable, Equatable {
    public let callID: String
    public let output: IXJSONValue

    public init(callID: String, output: IXJSONValue) {
        self.callID = callID
        self.output = output
    }

    public static func success(callID: String, message: String, data: IXJSONValue? = nil) -> Self {
        var object: [String: IXJSONValue] = [
            "ok": .bool(true),
            "message": .string(message),
        ]
        if let data { object["data"] = data }
        return Self(callID: callID, output: .object(object))
    }

    public static func failure(callID: String, message: String) -> Self {
        Self(callID: callID, output: .object([
            "ok": .bool(false),
            "error": .string(message),
        ]))
    }
}

public struct IXCodexImage: Codable, Sendable, Equatable, Identifiable {
    public let id: String
    public let data: Data
    public let mimeType: String
    public let revisedPrompt: String?

    public init(id: String = UUID().uuidString, data: Data, mimeType: String, revisedPrompt: String? = nil) {
        self.id = id
        self.data = data
        self.mimeType = mimeType
        self.revisedPrompt = revisedPrompt
    }
}

public struct IXCodexUsage: Sendable, Equatable {
    public let inputTokens: Int
    public let outputTokens: Int
    public let reasoningTokens: Int
    public let totalTokens: Int

    public init(
        inputTokens: Int,
        outputTokens: Int,
        reasoningTokens: Int,
        totalTokens: Int
    ) {
        self.inputTokens = inputTokens
        self.outputTokens = outputTokens
        self.reasoningTokens = reasoningTokens
        self.totalTokens = totalTokens
    }

    func adding(_ other: IXCodexUsage) -> IXCodexUsage {
        IXCodexUsage(
            inputTokens: inputTokens + other.inputTokens,
            outputTokens: outputTokens + other.outputTokens,
            reasoningTokens: reasoningTokens + other.reasoningTokens,
            totalTokens: totalTokens + other.totalTokens
        )
    }
}

public struct IXCodexTurn: Sendable, Equatable {
    public let responseID: String?
    public let status: String
    public let text: String
    public let toolCalls: [IXCodexToolCall]
    public let images: [IXCodexImage]
    public let citations: [IXCodexCitation]
    public let webSearchActivities: [IXCodexWebSearchActivity]
    public let usage: IXCodexUsage?
    let replayItems: [IXJSONValue]

    public init(
        responseID: String? = nil,
        status: String = "completed",
        text: String,
        toolCalls: [IXCodexToolCall] = [],
        images: [IXCodexImage] = [],
        citations: [IXCodexCitation] = [],
        webSearchActivities: [IXCodexWebSearchActivity] = [],
        usage: IXCodexUsage? = nil
    ) {
        self.responseID = responseID
        self.status = status
        self.text = text
        self.toolCalls = toolCalls
        self.images = images
        self.citations = citations
        self.webSearchActivities = webSearchActivities
        self.usage = usage
        self.replayItems = []
    }

    init(
        responseID: String?,
        status: String,
        text: String,
        toolCalls: [IXCodexToolCall],
        images: [IXCodexImage],
        citations: [IXCodexCitation],
        webSearchActivities: [IXCodexWebSearchActivity],
        usage: IXCodexUsage?,
        replayItems: [IXJSONValue]
    ) {
        self.responseID = responseID
        self.status = status
        self.text = text
        self.toolCalls = toolCalls
        self.images = images
        self.citations = citations
        self.webSearchActivities = webSearchActivities
        self.usage = usage
        self.replayItems = replayItems
    }
}

public struct IXCodexRunResult: Sendable, Equatable {
    public let turn: IXCodexTurn
    public let toolCalls: [IXCodexToolCall]
    public let usage: IXCodexUsage?

    public init(
        turn: IXCodexTurn,
        toolCalls: [IXCodexToolCall],
        usage: IXCodexUsage? = nil
    ) {
        self.turn = turn
        self.toolCalls = toolCalls
        self.usage = usage
    }
}

/// Executes model-requested tools.
///
/// Implementations that can change external state must treat `call.id` as an
/// idempotency key and replay the original result when that ID is received
/// again. A conversation can be reset while an awaited side effect completes,
/// so conversation history alone is not an exactly-once boundary.
public protocol IXCodexToolExecuting: Sendable {
    func execute(_ call: IXCodexToolCall) async -> IXCodexToolResult
    func execute(_ calls: [IXCodexToolCall]) async -> [IXCodexToolResult]
}

public extension IXCodexToolExecuting {
    func execute(_ calls: [IXCodexToolCall]) async -> [IXCodexToolResult] {
        var results: [IXCodexToolResult] = []
        results.reserveCapacity(calls.count)
        for call in calls {
            results.append(await execute(call))
        }
        return results
    }
}

public struct IXClosureCodexToolExecutor: IXCodexToolExecuting {
    private let handler: @Sendable (IXCodexToolCall) async -> IXCodexToolResult

    public init(handler: @escaping @Sendable (IXCodexToolCall) async -> IXCodexToolResult) {
        self.handler = handler
    }

    public func execute(_ call: IXCodexToolCall) async -> IXCodexToolResult {
        await handler(call)
    }
}

public struct IXCodexModel: Sendable, Equatable, Identifiable {
    public let id: String
    public let displayName: String
    public let description: String?
    public let supportedReasoningEfforts: [IXCodexReasoningOption]
    public let defaultReasoningEffort: IXCodexReasoningEffort?

    public init(
        id: String,
        displayName: String? = nil,
        description: String? = nil,
        supportedReasoningEfforts: [IXCodexReasoningOption] = [],
        defaultReasoningEffort: IXCodexReasoningEffort? = nil
    ) {
        self.id = id
        self.displayName = displayName ?? id
        self.description = description
        self.supportedReasoningEfforts = supportedReasoningEfforts
        self.defaultReasoningEffort = defaultReasoningEffort
    }
}
