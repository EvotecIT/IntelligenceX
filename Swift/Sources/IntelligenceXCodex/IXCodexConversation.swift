import Foundation

public actor IXCodexConversation {
    private let client: IXCodexClient
    private let sessionID: String
    private var history: [IXJSONValue] = []
    private var generation: UInt64 = 0
    private var activeRunID: UUID?

    public init(client: IXCodexClient, sessionID: String = UUID().uuidString) {
        self.client = client
        self.sessionID = sessionID
    }

    public func reset() {
        generation &+= 1
        activeRunID = nil
        history.removeAll(keepingCapacity: true)
    }

    public func run(
        input: [IXCodexInput],
        instructions: String,
        tools: [IXCodexToolDefinition] = [],
        executor: (any IXCodexToolExecuting)? = nil,
        model: String? = nil,
        imageGeneration: IXCodexImageGenerationOptions? = nil,
        maximumToolRounds: Int = 6
    ) async throws -> IXCodexRunResult {
        guard activeRunID == nil else { throw IXCodexError.conversationBusy }
        let runID = UUID()
        let expectedGeneration = generation
        activeRunID = runID
        defer {
            if activeRunID == runID { activeRunID = nil }
        }
        let userMessage = try makeUserMessage(input)
        var pendingHistory = history
        pendingHistory.append(userMessage)
        var allCalls: [IXCodexToolCall] = []

        for round in 0...maximumToolRounds {
            let turn = try await client.response(
                input: pendingHistory,
                sessionID: sessionID,
                instructions: instructions,
                tools: tools,
                model: model,
                imageGeneration: imageGeneration
            )
            guard generation == expectedGeneration, activeRunID == runID else {
                throw CancellationError()
            }
            pendingHistory.append(contentsOf: turn.replayItems.map(normalizeReplayItem))
            if turn.toolCalls.isEmpty {
                history = pendingHistory
                return IXCodexRunResult(turn: turn, toolCalls: allCalls)
            }
            guard round < maximumToolRounds, let executor else {
                throw IXCodexError.toolLoopLimitExceeded
            }
            allCalls.append(contentsOf: turn.toolCalls)
            for call in turn.toolCalls {
                let result = await executor.execute(call)
                guard generation == expectedGeneration, activeRunID == runID else {
                    throw CancellationError()
                }
                let output = try String(data: result.output.encodedData(), encoding: .utf8) ?? "{}"
                pendingHistory.append(.object([
                    "type": .string("custom_tool_call_output"),
                    "call_id": .string(result.callID),
                    "output": .string(output),
                ]))
            }
            // A tool may already have changed external state. Preserve the
            // canonical call/result checkpoint before asking the model to
            // continue so a transient follow-up failure cannot replay it.
            history = pendingHistory
        }
        throw IXCodexError.toolLoopLimitExceeded
    }

    private func makeUserMessage(_ input: [IXCodexInput]) throws -> IXJSONValue {
        let content = input.map { item -> IXJSONValue in
            switch item {
            case .text(let text):
                .object(["type": .string("input_text"), "text": .string(text)])
            case .image(let data, let mimeType, let detail):
                .object([
                    "type": .string("input_image"),
                    "image_url": .string("data:\(mimeType);base64,\(data.base64EncodedString())"),
                    "detail": .string(detail.rawValue),
                ])
            }
        }
        return .object([
            "type": .string("message"),
            "role": .string("user"),
            "content": .array(content),
        ])
    }

    private func normalizeReplayItem(_ item: IXJSONValue) -> IXJSONValue {
        guard var object = item.objectValue else { return item }
        let type = object["type"]?.stringValue
        guard ["custom_tool_call", "tool_call", "function_call"].contains(type) else {
            return item
        }
        let function = object["function"]?.objectValue
        guard let id = object["call_id"]?.stringValue
                ?? object["tool_call_id"]?.stringValue
                ?? object["id"]?.stringValue,
              let name = object["name"]?.stringValue ?? function?["name"]?.stringValue else {
            return item
        }
        let rawArguments = object["input"] ?? object["arguments"] ?? function?["arguments"] ?? .object([:])
        let arguments: String
        if let string = rawArguments.stringValue {
            arguments = string
        } else {
            arguments = (try? String(data: rawArguments.encodedData(), encoding: .utf8)) ?? "{}"
        }
        object = [
            "type": .string("custom_tool_call"),
            "call_id": .string(id),
            "name": .string(name),
            "input": .string(arguments),
        ]
        return .object(object)
    }
}
