import Foundation

public actor IXCodexConversation {
    private let client: IXCodexClient
    private let sessionID: String
    private let maximumHistoryItemsBeforeCompaction: Int
    private var history: [IXJSONValue] = []
    private var generation: UInt64 = 0
    private var activeRunID: UUID?
    private var activeRunTask: (
        id: UUID,
        task: Task<IXCodexRunResult, Error>
    )?

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
        activeRunTask?.task.cancel()
        activeRunTask = nil
        activeRunID = nil
        history.removeAll(keepingCapacity: true)
    }

    public func restoreTranscript(
        _ messages: [IXCodexTranscriptMessage]
    ) {
        generation &+= 1
        activeRunTask?.task.cancel()
        activeRunTask = nil
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
        webSearch: IXCodexWebSearchOptions? = nil,
        imageGeneration: IXCodexImageGenerationOptions? = nil,
        maximumToolRounds: Int = 6,
        approveTools: IXCodexToolApprovalHandler? = nil,
        continuationTools: IXCodexContinuationToolProvider? = nil,
        onTextDelta: IXCodexTextDeltaHandler? = nil
    ) async throws -> IXCodexRunResult {
        guard activeRunTask == nil else {
            throw IXCodexError.conversationBusy
        }
        let taskID = UUID()
        let task = Task { [self] in
            try await runImpl(
                input: input,
                instructions: instructions,
                tools: tools,
                executor: executor,
                model: model,
                reasoningEffort: reasoningEffort,
                webSearch: webSearch,
                imageGeneration: imageGeneration,
                maximumToolRounds: maximumToolRounds,
                approveTools: approveTools,
                continuationTools: continuationTools,
                onTextDelta: onTextDelta
            )
        }
        activeRunTask = (taskID, task)
        defer {
            if activeRunTask?.id == taskID {
                activeRunTask = nil
            }
        }
        return try await withTaskCancellationHandler {
            try await task.value
        } onCancel: {
            task.cancel()
        }
    }

    private func runImpl(
        input: [IXCodexInput],
        instructions: String,
        tools: [IXCodexToolDefinition],
        executor: (any IXCodexToolExecuting)?,
        model: String?,
        reasoningEffort: IXCodexReasoningEffort?,
        webSearch: IXCodexWebSearchOptions?,
        imageGeneration: IXCodexImageGenerationOptions?,
        maximumToolRounds: Int,
        approveTools: IXCodexToolApprovalHandler?,
        continuationTools: IXCodexContinuationToolProvider?,
        onTextDelta: IXCodexTextDeltaHandler?
    ) async throws -> IXCodexRunResult {
        guard activeRunID == nil else { throw IXCodexError.conversationBusy }
        guard maximumToolRounds >= 0 else {
            throw IXCodexError.invalidResponse(
                "maximumToolRounds must be zero or greater"
            )
        }
        try Task.checkCancellation()
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
        var availableTools = tools

        for round in 0...maximumToolRounds {
            var compactedThisRound = false
            if pendingHistory.count > maximumHistoryItemsBeforeCompaction {
                do {
                    pendingHistory = try await client.compact(
                        input: pendingHistory,
                        sessionID: sessionID,
                        instructions: instructions,
                        model: model
                    )
                    compactedThisRound = true
                } catch is CancellationError {
                    throw CancellationError()
                } catch {
                    // Proactive compaction is optional. A context-limit error
                    // still uses the authoritative recovery path below.
                }
            }
            try Task.checkCancellation()
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
                    tools: availableTools,
                    model: model,
                    reasoningEffort: reasoningEffort,
                    webSearch: webSearch,
                    imageGeneration: imageGeneration,
                    onTextDelta: onTextDelta
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
                    tools: availableTools,
                    model: model,
                    reasoningEffort: reasoningEffort,
                    webSearch: webSearch,
                    imageGeneration: imageGeneration,
                    onTextDelta: onTextDelta
                )
            }
            try Task.checkCancellation()
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
            try Task.checkCancellation()
            let confirmationDefinitions = availableTools.filter { definition in
                definition.requiresConfirmation && turn.toolCalls.contains {
                    $0.name == definition.name
                }
            }
            if !confirmationDefinitions.isEmpty {
                guard let approveTools else {
                    throw IXCodexError.toolConfirmationRequired(
                        confirmationDefinitions.map(\.name)
                    )
                }
                let names = Set(confirmationDefinitions.map(\.name))
                let calls = turn.toolCalls.filter {
                    names.contains($0.name)
                }
                guard try await approveTools(calls, confirmationDefinitions)
                else {
                    throw IXCodexError.toolConfirmationDenied
                }
                try Task.checkCancellation()
                guard generation == expectedGeneration,
                      activeRunID == runID else {
                    throw CancellationError()
                }
            }
            allCalls.append(contentsOf: turn.toolCalls)
            try Task.checkCancellation()
            let results = await executor.execute(turn.toolCalls)
            var resultsByCallID: [String: IXCodexToolResult] = [:]
            for result in results {
                resultsByCallID[result.callID] = result
            }
            for call in turn.toolCalls {
                guard let result = resultsByCallID[call.id] else {
                    if Task.isCancelled { throw CancellationError() }
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
            if let continuationTools {
                let selectedTools = try await continuationTools(
                    turn.toolCalls,
                    results,
                    availableTools
                )
                try Self.validateContinuationTools(
                    selectedTools,
                    areSubsetOf: availableTools
                )
                availableTools = selectedTools
            }
            try Task.checkCancellation()
        }
        throw IXCodexError.toolLoopLimitExceeded
    }

    private static func validateContinuationTools(
        _ selectedTools: [IXCodexToolDefinition],
        areSubsetOf availableTools: [IXCodexToolDefinition]
    ) throws {
        var names = Set<String>()
        for definition in selectedTools {
            guard names.insert(definition.name).inserted else {
                throw IXCodexError.invalidResponse(
                    "Continuation tools contain duplicate definition \(definition.name)"
                )
            }
            guard availableTools.contains(definition) else {
                throw IXCodexError.invalidResponse(
                    "Continuation tool \(definition.name) was not offered in the preceding round"
                )
            }
        }
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
