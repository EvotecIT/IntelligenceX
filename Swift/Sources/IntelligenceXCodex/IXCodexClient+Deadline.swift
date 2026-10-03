import Foundation

extension IXCodexClient {
    /// Each hosted round has an elapsed-time budget, including authorization,
    /// retries and continuous streaming. Tool execution and user approvals are
    /// conversation-owned and run outside this network deadline.
    func response(
        input: [IXJSONValue], sessionID: String, instructions: String,
        tools: [IXCodexToolDefinition], model: String?,
        reasoningEffort: IXCodexReasoningEffort?,
        responseOptions: IXCodexResponseOptions = .init(),
        webSearch: IXCodexWebSearchOptions?,
        imageGeneration: IXCodexImageGenerationOptions?,
        onTextDelta: IXCodexTextDeltaHandler? = nil,
        retryUnauthorized: Bool = true
    ) async throws -> IXCodexTurn {
        try await IXElapsedDeadline.run(timeoutInterval: requestTimeoutInterval) {
            try await self.responseWithinDeadline(
                input: input, sessionID: sessionID, instructions: instructions,
                tools: tools, model: model, reasoningEffort: reasoningEffort,
                responseOptions: responseOptions, webSearch: webSearch,
                imageGeneration: imageGeneration, onTextDelta: onTextDelta,
                retryUnauthorized: retryUnauthorized
            )
        }
    }

    func compact(
        input: [IXJSONValue], sessionID: String, instructions: String,
        model: String?, retryUnauthorized: Bool = true,
        recoveredBundle: IXCodexAuthSession.RequestAuthorization? = nil
    ) async throws -> [IXJSONValue] {
        try await IXElapsedDeadline.run(timeoutInterval: requestTimeoutInterval) {
            try await self.compactWithinDeadline(
                input: input, sessionID: sessionID, instructions: instructions,
                model: model, retryUnauthorized: retryUnauthorized,
                recoveredBundle: recoveredBundle
            )
        }
    }
}
