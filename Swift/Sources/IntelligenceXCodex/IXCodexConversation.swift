import Foundation

public actor IXCodexConversation {
    private let client: IXCodexClient
    private let sessionID: String
    private let maximumHistoryItemsBeforeCompaction: Int
    private var history: [IXJSONValue] = []
    private var generation: UInt64 = 0
    private var activeRunID: UUID?

    public init(
        client: IXCodexClient,
        sessionID: String = UUID().uuidString,
        maximumHistoryItemsBeforeCompaction: Int = 96
    ) {
        self.client = client
        self.sessionID = sessionID
        self.maximumHistoryItemsBeforeCompaction = max(
            8,
            maximumHistoryItemsBeforeCompaction
        )
    }

    public func reset() {
        generation &+= 1
        activeRunID = nil
        history.removeAll(keepingCapacity: true)
    }

    public func restoreTranscript(
        _ messages: [IXCodexTranscriptMessage]
    ) {
        generation &+= 1
        activeRunID = nil
        history = messages.compactMap { message in
            let text = message.text.trimmingCharacters(
                in: .whitespacesAndNewlines
            )
            var content: [IXJSONValue] = []
            if !text.isEmpty {
                content.append(.object([
                    "type": .string(
                        message.role == .user
                            ? "input_text"
                            : "output_text"
                    ),
                    "text": .string(text),
                ]))
            }
            if message.role == .user {
                content.append(contentsOf: message.images.map { image in
                    .object([
                        "type": .string("input_image"),
                        "image_url": .string(
                            "data:\(image.mimeType);base64,\(image.data.base64EncodedString())"
                        ),
                        "detail": .string(IXImageDetail.high.rawValue),
                    ])
                })
            }
            guard !content.isEmpty else { return nil }
            return .object([
                "type": .string("message"),
                "role": .string(message.role.rawValue),
                "content": .array(content),
            ])
        }
    }

    public func run(
        input: [IXCodexInput],
        instructions: String,
        tools: [IXCodexToolDefinition] = [],
        executor: (any IXCodexToolExecuting)? = nil,
        model: String? = nil,
        reasoningEffort: IXCodexReasoningEffort? = nil,
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
        var aggregateUsage: IXCodexUsage?

        for round in 0...maximumToolRounds {
            var compactedThisRound = false
            if pendingHistory.count > maximumHistoryItemsBeforeCompaction,
               let compacted = try? await client.compact(
                   input: pendingHistory,
                   sessionID: sessionID,
                   instructions: instructions,
                   model: model
               ) {
                pendingHistory = compacted
                compactedThisRound = true
            }
            guard generation == expectedGeneration,
                  activeRunID == runID else {
                throw CancellationError()
            }
            let turn: IXCodexTurn
            do {
                turn = try await client.response(
                    input: pendingHistory,
                    sessionID: sessionID,
                    instructions: instructions,
                    tools: tools,
                    model: model,
                    reasoningEffort: reasoningEffort,
                    imageGeneration: imageGeneration
                )
            } catch let error as IXCodexError where
                !compactedThisRound && Self.isContextLimitError(error) {
                guard generation == expectedGeneration,
                      activeRunID == runID else {
                    throw CancellationError()
                }
                let compacted = try await client.compact(
                    input: pendingHistory,
                    sessionID: sessionID,
                    instructions: instructions,
                    model: model
                )
                guard generation == expectedGeneration,
                      activeRunID == runID else {
                    throw CancellationError()
                }
                pendingHistory = compacted
                turn = try await client.response(
                    input: pendingHistory,
                    sessionID: sessionID,
                    instructions: instructions,
                    tools: tools,
                    model: model,
                    reasoningEffort: reasoningEffort,
                    imageGeneration: imageGeneration
                )
            }
            guard generation == expectedGeneration, activeRunID == runID else {
                throw CancellationError()
            }
            if let usage = turn.usage {
                aggregateUsage = aggregateUsage?.adding(usage) ?? usage
            }
            pendingHistory.append(contentsOf: turn.replayItems.map(normalizeReplayItem))
            if turn.toolCalls.isEmpty {
                history = pendingHistory
                return IXCodexRunResult(
                    turn: turn,
                    toolCalls: allCalls,
                    usage: aggregateUsage
                )
            }
            guard round < maximumToolRounds, let executor else {
                throw IXCodexError.toolLoopLimitExceeded
            }
            allCalls.append(contentsOf: turn.toolCalls)
            let results = await executor.execute(turn.toolCalls)
            var resultsByCallID: [String: IXCodexToolResult] = [:]
            for result in results {
                resultsByCallID[result.callID] = result
            }
            for call in turn.toolCalls {
                guard let result = resultsByCallID[call.id] else {
                    throw IXCodexError.invalidResponse(
                        "Tool executor returned no result for \(call.id)"
                    )
                }
                guard generation == expectedGeneration, activeRunID == runID else {
                    throw CancellationError()
                }
                let output = try String(data: result.output.encodedData(), encoding: .utf8) ?? "{}"
                pendingHistory.append(.object([
                    "type": .string(
                        call.kind == .custom
                            ? "custom_tool_call_output"
                            : "function_call_output"
                    ),
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

    private static func isContextLimitError(_ error: IXCodexError) -> Bool {
        guard case .requestFailed(let status, let message) = error,
              status == 400 || status == 413 else {
            return false
        }
        let normalized = message.lowercased()
        return normalized.contains("context_length_exceeded") ||
            normalized.contains("context window") ||
            normalized.contains("too many input tokens") ||
            normalized.contains("maximum context length")
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
        if type == "custom_tool_call" {
            object = [
                "type": .string("custom_tool_call"),
                "call_id": .string(id),
                "name": .string(name),
                "input": .string(arguments),
            ]
        } else {
            object = [
                "type": .string("function_call"),
                "call_id": .string(id),
                "name": .string(name),
                "arguments": .string(arguments),
            ]
        }
        return .object(object)
    }
}
