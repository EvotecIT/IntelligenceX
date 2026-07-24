import Foundation

public enum IXImageDetail: String, Codable, Sendable {
    case low
    case high
    case auto
}

public enum IXCodexInput: Sendable, Equatable {
    case text(String)
    case image(data: Data, mimeType: String, detail: IXImageDetail)
}

public struct IXCodexToolDefinition: Sendable, Equatable {
    public let name: String
    public let description: String
    public let parameters: IXJSONValue
    public let requiresConfirmation: Bool

    public init(
        name: String,
        description: String,
        parameters: IXJSONValue,
        requiresConfirmation: Bool = false
    ) {
        self.name = name
        self.description = description
        self.parameters = parameters
        self.requiresConfirmation = requiresConfirmation
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

public struct IXCodexToolCall: Sendable, Equatable, Identifiable {
    public let id: String
    public let name: String
    public let arguments: IXJSONValue

    public init(id: String, name: String, arguments: IXJSONValue) {
        self.id = id
        self.name = name
        self.arguments = arguments
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

public struct IXCodexImage: Sendable, Equatable, Identifiable {
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

public struct IXCodexTurn: Sendable, Equatable {
    public let responseID: String?
    public let status: String
    public let text: String
    public let toolCalls: [IXCodexToolCall]
    public let images: [IXCodexImage]
    let replayItems: [IXJSONValue]

    public init(
        responseID: String? = nil,
        status: String = "completed",
        text: String,
        toolCalls: [IXCodexToolCall] = [],
        images: [IXCodexImage] = []
    ) {
        self.responseID = responseID
        self.status = status
        self.text = text
        self.toolCalls = toolCalls
        self.images = images
        self.replayItems = []
    }

    init(
        responseID: String?,
        status: String,
        text: String,
        toolCalls: [IXCodexToolCall],
        images: [IXCodexImage],
        replayItems: [IXJSONValue]
    ) {
        self.responseID = responseID
        self.status = status
        self.text = text
        self.toolCalls = toolCalls
        self.images = images
        self.replayItems = replayItems
    }
}

public struct IXCodexRunResult: Sendable, Equatable {
    public let turn: IXCodexTurn
    public let toolCalls: [IXCodexToolCall]

    public init(turn: IXCodexTurn, toolCalls: [IXCodexToolCall]) {
        self.turn = turn
        self.toolCalls = toolCalls
    }
}

public protocol IXCodexToolExecuting: Sendable {
    func execute(_ call: IXCodexToolCall) async -> IXCodexToolResult
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

    public init(id: String, displayName: String? = nil) {
        self.id = id
        self.displayName = displayName ?? id
    }
}
