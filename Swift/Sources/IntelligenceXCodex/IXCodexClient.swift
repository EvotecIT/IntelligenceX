import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public actor IXCodexClient {
    static let maximumBufferedStreamingResponseBytes = 32 * 1_024 * 1_024

    private enum ToolWireFormat: CaseIterable {
        case functionNestedParameters
        case functionNestedInputSchema
        case functionFlatParameters
        case functionFlatInputSchema
        case customParameters
        case customInputSchema
    }

    private let configuration: IXCodexConfiguration
    private let authSession: IXCodexAuthSession
    private let httpClient: any IXHTTPClient
    private var preferredToolWireFormat: ToolWireFormat?

    public init(
        configuration: IXCodexConfiguration = IXCodexConfiguration(),
        authSession: IXCodexAuthSession,
        httpClient: any IXHTTPClient = IXURLSessionHTTPClient()
    ) {
        self.configuration = configuration
        self.authSession = authSession
        self.httpClient = httpClient
    }

    public func models() async throws -> [IXCodexModel] {
        let bundle = try await authSession.validBundle()
        guard let accountID = bundle.accountID else {
            throw IXCodexError.invalidResponse("ChatGPT account ID is missing from the OAuth token")
        }
        var lastError: Error?
        for url in configuration.modelURLs {
            do {
                var request = URLRequest(url: modelCatalogURL(from: url))
                request.httpMethod = "GET"
                applyHeaders(to: &request, bundle: bundle, accountID: accountID, sessionID: UUID().uuidString)
                let response = try await httpClient.send(request)
                guard (200..<300).contains(response.statusCode) else {
                    throw responseError(response)
                }
                let value = try IXJSONValue.decode(response.body)
                let items = value.arrayValue
                    ?? value["models"]?.arrayValue
                    ?? value["data"]?.arrayValue
                    ?? []
                let models = items.compactMap { item -> IXCodexModel? in
                    guard let object = item.objectValue else { return nil }
                    if let visibility = object["visibility"]?.stringValue,
                       visibility != "list" {
                        return nil
                    }
                    guard
                          // Codex catalogs can expose an internal family ID and
                          // a request-ready slug. The Responses route accepts
                          // the slug, while the internal ID may be rejected for
                          // ChatGPT-backed accounts.
                          let id = object["slug"]?.stringValue ?? object["id"]?.stringValue else { return nil }
                    let reasoningItems = object["supported_reasoning_levels"]?.arrayValue
                        ?? object["supported_reasoning_efforts"]?.arrayValue
                        ?? object["supportedReasoningEfforts"]?.arrayValue
                        ?? []
                    let reasoning = reasoningItems.compactMap { value -> IXCodexReasoningOption? in
                        let effortValue = value["effort"]?.stringValue
                            ?? value["reasoning_effort"]?.stringValue
                            ?? value["reasoningEffort"]?.stringValue
                        guard let effortValue,
                              let effort = IXCodexReasoningEffort(rawValue: effortValue) else { return nil }
                        return IXCodexReasoningOption(
                            effort: effort,
                            description: value["description"]?.stringValue
                        )
                    }
                    let defaultEffortValue = object["default_reasoning_level"]?.stringValue
                        ?? object["default_reasoning_effort"]?.stringValue
                        ?? object["defaultReasoningEffort"]?.stringValue
                    return IXCodexModel(
                        id: id,
                        displayName: object["display_name"]?.stringValue
                            ?? object["displayName"]?.stringValue
                            ?? object["name"]?.stringValue,
                        description: object["description"]?.stringValue,
                        supportedReasoningEfforts: reasoning,
                        defaultReasoningEffort: defaultEffortValue.flatMap(IXCodexReasoningEffort.init(rawValue:))
                    )
                }
                if !models.isEmpty { return models }
            } catch {
                lastError = error
            }
        }
        if let lastError { throw lastError }
        return [IXCodexModel(id: configuration.defaultModel)]
    }

    public func accountUsage(
        retryUnauthorized: Bool = true
    ) async throws -> IXCodexAccountUsage {
        let bundle = try await authSession.validBundle()
        guard let accountID = bundle.accountID else {
            throw IXCodexError.invalidResponse(
                "ChatGPT account ID is missing from the OAuth token"
            )
        }
        var request = URLRequest(url: configuration.accountUsageURL)
        request.httpMethod = "GET"
        request.setValue(
            "Bearer \(bundle.accessToken)",
            forHTTPHeaderField: "Authorization"
        )
        request.setValue(
            accountID,
            forHTTPHeaderField: "ChatGPT-Account-ID"
        )
        request.setValue(
            configuration.userAgent,
            forHTTPHeaderField: "User-Agent"
        )
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        let response = try await httpClient.send(request)
        if response.statusCode == 401 && retryUnauthorized {
            _ = try await authSession.validBundle(forceRefresh: true)
            return try await accountUsage(retryUnauthorized: false)
        }
        guard (200..<300).contains(response.statusCode) else {
            throw responseError(response)
        }
        return try IXCodexAccountUsage.decode(response.body)
    }

    private func modelCatalogURL(from url: URL) -> URL {
        guard url.path.hasSuffix("/backend-api/codex/models"),
              var components = URLComponents(url: url, resolvingAgainstBaseURL: false) else {
            return url
        }
        var queryItems = components.queryItems ?? []
        if !queryItems.contains(where: { $0.name == "client_version" }) {
            queryItems.append(URLQueryItem(
                name: "client_version",
                value: configuration.modelCatalogClientVersion
            ))
        }
        components.queryItems = queryItems
        return components.url ?? url
    }

    func response(
        input: [IXJSONValue],
        sessionID: String,
        instructions: String,
        tools: [IXCodexToolDefinition],
        model: String?,
        reasoningEffort: IXCodexReasoningEffort?,
        webSearch: IXCodexWebSearchOptions?,
        imageGeneration: IXCodexImageGenerationOptions?,
        onTextDelta: IXCodexTextDeltaHandler? = nil,
        retryUnauthorized: Bool = true
    ) async throws -> IXCodexTurn {
        try IXCodexToolSchemaValidator.validate(tools)
        let requestedModel = model ?? configuration.defaultModel
        do {
            return try await sendResponse(
                input: input,
                sessionID: sessionID,
                instructions: instructions,
                tools: tools,
                model: requestedModel,
                reasoningEffort: reasoningEffort ?? configuration.defaultReasoningEffort,
                webSearch: webSearch,
                imageGeneration: imageGeneration,
                onTextDelta: onTextDelta,
                retryUnauthorized: retryUnauthorized
            )
        } catch let error as IXCodexError where
            model == nil && isUnsupportedModel(error) {
            for fallback in await fallbackModels(excluding: requestedModel) {
                do {
                    return try await sendResponse(
                        input: input,
                        sessionID: sessionID,
                        instructions: instructions,
                        tools: tools,
                        model: fallback,
                        reasoningEffort: reasoningEffort ?? configuration.defaultReasoningEffort,
                        webSearch: webSearch,
                        imageGeneration: imageGeneration,
                        onTextDelta: onTextDelta,
                        retryUnauthorized: retryUnauthorized
                    )
                } catch let retryError as IXCodexError where isUnsupportedModel(retryError) {
                    continue
                }
            }
            throw error
        }
    }

    func compact(
        input: [IXJSONValue],
        sessionID: String,
        instructions: String,
        model: String?,
        retryUnauthorized: Bool = true
    ) async throws -> [IXJSONValue] {
        let bundle = try await authSession.validBundle()
        guard let accountID = bundle.accountID else {
            throw IXCodexError.invalidResponse(
                "ChatGPT account ID is missing from the OAuth token"
            )
        }
        var request = URLRequest(url: configuration.compactionURL)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        applyHeaders(
            to: &request,
            bundle: bundle,
            accountID: accountID,
            sessionID: sessionID
        )
        request.httpBody = try IXJSONValue.object([
            "model": .string(model ?? configuration.defaultModel),
            "store": .bool(false),
            "stream": .bool(true),
            "instructions": .string(instructions),
            "input": .array(input),
        ]).encodedData()
        let response = try await httpClient.send(request)
        if response.statusCode == 401 && retryUnauthorized {
            _ = try await authSession.validBundle(forceRefresh: true)
            return try await compact(
                input: input,
                sessionID: sessionID,
                instructions: instructions,
                model: model,
                retryUnauthorized: false
            )
        }
        guard (200..<300).contains(response.statusCode) else {
            throw responseError(response)
        }
        return try parseCompaction(response.body)
    }

    private func sendResponse(
        input: [IXJSONValue],
        sessionID: String,
        instructions: String,
        tools: [IXCodexToolDefinition],
        model: String,
        reasoningEffort: IXCodexReasoningEffort,
        webSearch: IXCodexWebSearchOptions?,
        imageGeneration: IXCodexImageGenerationOptions?,
        onTextDelta: IXCodexTextDeltaHandler?,
        retryUnauthorized: Bool
    ) async throws -> IXCodexTurn {
        let bundle = try await authSession.validBundle()
        guard let accountID = bundle.accountID else {
            throw IXCodexError.invalidResponse("ChatGPT account ID is missing from the OAuth token")
        }
        let formats: [ToolWireFormat]
        if tools.isEmpty {
            formats = [.functionNestedParameters]
        } else if let preferredToolWireFormat {
            formats = [preferredToolWireFormat]
                + ToolWireFormat.allCases.filter { $0 != preferredToolWireFormat }
        } else {
            formats = ToolWireFormat.allCases
        }
        var lastError: IXCodexError?
        for format in formats {
            let body = buildRequestBody(
                input: input,
                sessionID: sessionID,
                instructions: instructions,
                tools: tools,
                model: model,
                reasoningEffort: reasoningEffort,
                webSearch: webSearch,
                imageGeneration: imageGeneration,
                toolWireFormat: format
            )
            var request = URLRequest(url: configuration.responsesURL)
            request.httpMethod = "POST"
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            applyHeaders(to: &request, bundle: bundle, accountID: accountID, sessionID: sessionID)
            request.httpBody = try body.encodedData()
            let response: IXHTTPResponse
            if let onTextDelta,
               let streamingClient = httpClient as?
                    any IXHTTPStreamingClient {
                response = try await bufferedStreamingResponse(
                    request,
                    client: streamingClient,
                    onTextDelta: onTextDelta
                )
            } else {
                response = try await httpClient.send(request)
            }
            if response.statusCode == 401 && retryUnauthorized {
                _ = try await authSession.validBundle(forceRefresh: true)
                return try await sendResponse(
                    input: input,
                    sessionID: sessionID,
                    instructions: instructions,
                    tools: tools,
                    model: model,
                    reasoningEffort: reasoningEffort,
                    webSearch: webSearch,
                    imageGeneration: imageGeneration,
                    onTextDelta: onTextDelta,
                    retryUnauthorized: false
                )
            }
            guard (200..<300).contains(response.statusCode) else {
                let error = responseError(response)
                lastError = error
                if isRetryableToolSchemaError(error), format != formats.last {
                    continue
                }
                throw error
            }
            if !tools.isEmpty { preferredToolWireFormat = format }
            return try parseTurn(response.body, imageGeneration: imageGeneration)
        }
        throw lastError ?? IXCodexError.invalidResponse("Tool schema fallback exhausted")
    }

    private func bufferedStreamingResponse(
        _ request: URLRequest,
        client: any IXHTTPStreamingClient,
        onTextDelta: IXCodexTextDeltaHandler
    ) async throws -> IXHTTPResponse {
        let response = try await client.stream(request)
        var body = Data()
        var lineBuffer = Data()
        for try await chunk in response.body {
            try Task.checkCancellation()
            guard body.count <= Self.maximumBufferedStreamingResponseBytes - chunk.count else {
                throw IXCodexError.invalidResponse(
                    "The streaming response exceeded the local safety limit"
                )
            }
            body.append(chunk)
            lineBuffer.append(chunk)
            while let newline = lineBuffer.firstIndex(of: 0x0A) {
                let lineData = Data(lineBuffer[..<newline])
                lineBuffer.removeSubrange(...newline)
                await emitTextDelta(
                    fromSSELine: lineData,
                    handler: onTextDelta
                )
            }
        }
        if !lineBuffer.isEmpty {
            await emitTextDelta(
                fromSSELine: lineBuffer,
                handler: onTextDelta
            )
        }
        return IXHTTPResponse(
            statusCode: response.statusCode,
            headers: response.headers,
            body: body
        )
    }

    private func emitTextDelta(
        fromSSELine lineData: Data,
        handler: IXCodexTextDeltaHandler
    ) async {
        guard var line = String(data: lineData, encoding: .utf8) else {
            return
        }
        line = line.trimmingCharacters(in: .whitespacesAndNewlines)
        guard line.hasPrefix("data:") else { return }
        let payload = line.dropFirst(5).trimmingCharacters(
            in: .whitespaces
        )
        guard payload != "[DONE]",
              let data = payload.data(using: .utf8),
              let event = try? IXJSONValue.decode(data),
              let eventType = event["type"]?.stringValue,
              eventType == "response.output_text.delta" ||
                  eventType == "response.refusal.delta",
              let delta = event["delta"]?.stringValue,
              !delta.isEmpty else {
            return
        }
        await handler(delta)
    }

    private func buildRequestBody(
        input: [IXJSONValue],
        sessionID: String,
        instructions: String,
        tools: [IXCodexToolDefinition],
        model: String,
        reasoningEffort: IXCodexReasoningEffort,
        webSearch: IXCodexWebSearchOptions?,
        imageGeneration: IXCodexImageGenerationOptions?,
        toolWireFormat: ToolWireFormat
    ) -> IXJSONValue {
        var object: [String: IXJSONValue] = [
            "model": .string(model),
            "store": .bool(false),
            "stream": .bool(true),
            "instructions": .string(instructions),
            "input": .array(input),
            "text": .object(["verbosity": .string("medium")]),
            "reasoning": .object([
                "effort": .string(reasoningEffort.rawValue),
                "summary": .string("auto"),
            ]),
            "prompt_cache_key": .string(sessionID),
        ]
        var include: [IXJSONValue] = [.string("reasoning.encrypted_content")]
        var serializedTools = tools.map { serializeTool($0, format: toolWireFormat) }
        if let webSearch {
            serializedTools.insert(serializeWebSearch(webSearch), at: 0)
            include.append(.string("web_search_call.action.sources"))
        }
        if let imageGeneration {
            serializedTools.insert(serializeImageGeneration(imageGeneration), at: 0)
        }
        object["include"] = .array(include)
        if !serializedTools.isEmpty {
            object["tools"] = .array(serializedTools)
        }
        let hasSelectableTools = !tools.isEmpty || webSearch != nil
        if hasSelectableTools {
            object["tool_choice"] = webSearch?.requiresSearch == true
                ? .object(["type": .string("web_search")])
                : .string("auto")
            object["parallel_tool_calls"] = .bool(true)
        }
        return .object(object)
    }

    private func serializeWebSearch(
        _ options: IXCodexWebSearchOptions
    ) -> IXJSONValue {
        .object([
            "type": .string("web_search"),
            "search_context_size": .string(options.contextSize.rawValue),
            "external_web_access": .bool(options.allowsLiveInternetAccess),
        ])
    }

    private func serializeImageGeneration(_ options: IXCodexImageGenerationOptions) -> IXJSONValue {
        var object: [String: IXJSONValue] = ["type": .string("image_generation")]
        if let quality = options.quality { object["quality"] = .string(quality) }
        if let size = options.size { object["size"] = .string(size) }
        if let outputFormat = options.outputFormat { object["output_format"] = .string(outputFormat) }
        if let background = options.background { object["background"] = .string(background) }
        return .object(object)
    }

    private func serializeTool(_ tool: IXCodexToolDefinition, format: ToolWireFormat) -> IXJSONValue {
        let schemaKey = switch format {
        case .functionNestedInputSchema, .functionFlatInputSchema, .customInputSchema: "input_schema"
        default: "parameters"
        }
        switch format {
        case .functionNestedParameters, .functionNestedInputSchema:
            var function: [String: IXJSONValue] = [
                "name": .string(tool.name),
                "description": .string(tool.description),
                schemaKey: tool.parameters,
            ]
            if tool.strict {
                function["strict"] = .bool(true)
            }
            return .object([
                "type": .string("function"),
                "function": .object(function),
            ])
        case .functionFlatParameters, .functionFlatInputSchema:
            var function: [String: IXJSONValue] = [
                "type": .string("function"),
                "name": .string(tool.name),
                "description": .string(tool.description),
                schemaKey: tool.parameters,
            ]
            if tool.strict {
                function["strict"] = .bool(true)
            }
            return .object(function)
        case .customParameters, .customInputSchema:
            return .object([
                "type": .string("custom"),
                "name": .string(tool.name),
                "description": .string(tool.description),
                schemaKey: tool.parameters,
            ])
        }
    }

    private func fallbackModels(excluding currentModel: String) async -> [String] {
        var result: [String] = []
        var seen = Set([currentModel.lowercased()])
        func append(_ value: String) {
            let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty, seen.insert(trimmed.lowercased()).inserted else { return }
            result.append(trimmed)
        }
        configuration.fallbackModels.forEach(append)
        if let discovered = try? await models() {
            discovered.map(\.id).forEach(append)
        }
        return result
    }

    private func isUnsupportedModel(_ error: IXCodexError) -> Bool {
        guard case .requestFailed(_, let message) = error else { return false }
        let text = message.lowercased()
        return text.contains("model is not supported") && text.contains("chatgpt account")
    }

    private func isRetryableToolSchemaError(_ error: IXCodexError) -> Bool {
        guard case .requestFailed(let status, let message) = error, status == 400 || status == 422 else { return false }
        let text = message.lowercased()
        guard text.contains("tools") else { return false }
        return text.contains("unknown parameter")
            || text.contains("unknown field")
            || text.contains("unrecognized request argument")
            || (text.contains("missing required parameter") && text.contains(".name"))
    }

    private func applyHeaders(
        to request: inout URLRequest,
        bundle: IXCodexAuthBundle,
        accountID: String,
        sessionID: String
    ) {
        request.setValue("Bearer \(bundle.accessToken)", forHTTPHeaderField: "Authorization")
        request.setValue(accountID, forHTTPHeaderField: "chatgpt-account-id")
        request.setValue("responses=experimental", forHTTPHeaderField: "OpenAI-Beta")
        request.setValue(configuration.originator, forHTTPHeaderField: "originator")
        request.setValue(sessionID, forHTTPHeaderField: "session_id")
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        request.setValue(configuration.userAgent, forHTTPHeaderField: "User-Agent")
    }

    private func parseTurn(
        _ data: Data,
        imageGeneration: IXCodexImageGenerationOptions?
    ) throws -> IXCodexTurn {
        let parsed = try IXCodexSSEParser().parse(data)
        let response = parsed.completedResponse?.objectValue
        let completedItems = response?["output"]?.arrayValue ?? []
        let responseItems = completedItems.isEmpty ? parsed.streamedItems : completedItems
        var text = parsed.text
        var calls: [IXCodexToolCall] = []
        var images: [IXCodexImage] = []
        var citations: [IXCodexCitation] = []
        var webSearchActivities: [IXCodexWebSearchActivity] = []

        for item in responseItems {
            guard let object = item.objectValue else { continue }
            let type = object["type"]?.stringValue ?? ""
            if type == "message" {
                for part in object["content"]?.arrayValue ?? [] {
                    let partType = part["type"]?.stringValue
                    if partType == "output_text" {
                        citations.append(contentsOf: parseCitations(part))
                        if let value = part["text"]?.stringValue, parsed.text.isEmpty {
                            text += value
                        }
                    } else if partType == "refusal", let value = part["refusal"]?.stringValue, parsed.text.isEmpty {
                        text += value
                    }
                }
            } else if ["custom_tool_call", "tool_call", "function_call"].contains(type) {
                if let call = try parseToolCall(item) { calls.append(call) }
            } else if type == "image_generation_call",
                      let base64 = object["result"]?.stringValue,
                      let imageData = Data(base64Encoded: base64) {
                images.append(IXCodexImage(
                    id: object["id"]?.stringValue ?? object["call_id"]?.stringValue ?? UUID().uuidString,
                    data: imageData,
                    mimeType: imageMimeType(
                        object["output_format"]?.stringValue ?? imageGeneration?.outputFormat
                    ),
                    revisedPrompt: object["revised_prompt"]?.stringValue
                ))
            } else if type == "web_search_call" {
                webSearchActivities.append(parseWebSearchActivity(object))
            }
        }
        let normalizedText = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedText.isEmpty || !calls.isEmpty || !images.isEmpty else {
            let eventTypes = Array(Set(parsed.eventTypes)).sorted().joined(separator: ", ")
            let itemTypes = responseItems.compactMap { $0["type"]?.stringValue }
                .uniqued()
                .sorted()
                .joined(separator: ", ")
            let eventShapes = parsed.eventKeys
                .filter { key, _ in
                    key.contains("output_item") || key.contains("function_call_arguments")
                }
                .sorted { $0.key < $1.key }
                .map { "\($0.key)[\($0.value.joined(separator: ","))]" }
                .joined(separator: "; ")
            let diagnostics = [
                eventShapes.isEmpty ? nil : "shapes: \(eventShapes)",
                itemTypes.isEmpty ? nil : "items: \(itemTypes)",
                eventTypes.isEmpty ? nil : "events: \(eventTypes)",
            ].compactMap { $0 }.joined(separator: "; ")
            throw IXCodexError.invalidResponse(
                diagnostics.isEmpty
                    ? "ChatGPT returned no usable assistant output."
                    : "ChatGPT returned no usable assistant output (\(diagnostics))."
            )
        }
        return IXCodexTurn(
            responseID: response?["id"]?.stringValue,
            status: response?["status"]?.stringValue ?? "completed",
            text: normalizedText,
            toolCalls: calls.uniquedByID(),
            images: images,
            citations: citations.uniqued(),
            webSearchActivities: webSearchActivities,
            usage: response.flatMap(parseUsage),
            replayItems: responseItems
        )
    }

    private func parseCitations(_ contentPart: IXJSONValue) -> [IXCodexCitation] {
        (contentPart["annotations"]?.arrayValue ?? []).compactMap { annotation in
            guard annotation["type"]?.stringValue == "url_citation",
                  let rawURL = annotation["url"]?.stringValue,
                  let url = URL(string: rawURL),
                  let scheme = url.scheme?.lowercased(),
                  scheme == "https" || scheme == "http" else {
                return nil
            }
            return IXCodexCitation(
                title: annotation["title"]?.stringValue ?? url.host ?? rawURL,
                url: url,
                startIndex: annotation["start_index"]?.numberValue.map(Int.init),
                endIndex: annotation["end_index"]?.numberValue.map(Int.init)
            )
        }
    }

    private func parseWebSearchActivity(
        _ object: [String: IXJSONValue]
    ) -> IXCodexWebSearchActivity {
        let action = object["action"]?.objectValue
        let query = action?["query"]?.stringValue
        let queries = action?["queries"]?.arrayValue?.compactMap(\.stringValue)
            ?? query.map { [$0] }
            ?? []
        let sourceURLs: [URL] = (action?["sources"]?.arrayValue ?? []).compactMap {
            guard let rawURL = $0["url"]?.stringValue,
                  let url = URL(string: rawURL),
                  let scheme = url.scheme?.lowercased(),
                  scheme == "https" || scheme == "http" else {
                return nil
            }
            return url
        }
        return IXCodexWebSearchActivity(
            id: object["id"]?.stringValue ?? UUID().uuidString,
            status: object["status"]?.stringValue,
            action: action?["type"]?.stringValue,
            queries: queries,
            sourceURLs: sourceURLs
        )
    }

    private func parseCompaction(_ data: Data) throws -> [IXJSONValue] {
        let response: [String: IXJSONValue]?
        if let value = try? IXJSONValue.decode(data) {
            response = value.objectValue
        } else {
            let parsed = try IXCodexSSEParser().parse(data)
            response = parsed.completedResponse?.objectValue
        }
        let output = response?["output"]?.arrayValue ?? []
        let compacted = output.filter {
            $0["type"]?.stringValue == "compaction"
        }
        guard compacted.count == 1 else {
            throw IXCodexError.invalidResponse(
                "ChatGPT returned no usable conversation compaction."
            )
        }
        return output
    }

    private func parseUsage(
        _ response: [String: IXJSONValue]
    ) -> IXCodexUsage? {
        guard let usage = response["usage"]?.objectValue else { return nil }
        let input = Int(usage["input_tokens"]?.numberValue ?? 0)
        let output = Int(usage["output_tokens"]?.numberValue ?? 0)
        let reasoning = Int(
            usage["output_tokens_details"]?.objectValue?["reasoning_tokens"]?
                .numberValue ?? 0
        )
        let total = Int(
            usage["total_tokens"]?.numberValue ?? Double(input + output)
        )
        return IXCodexUsage(
            inputTokens: input,
            outputTokens: output,
            reasoningTokens: reasoning,
            totalTokens: total
        )
    }

    private func imageMimeType(_ outputFormat: String?) -> String {
        switch outputFormat?.lowercased() {
        case "jpeg", "jpg": "image/jpeg"
        case "webp": "image/webp"
        default: "image/png"
        }
    }

    private func parseToolCall(_ item: IXJSONValue) throws -> IXCodexToolCall? {
        guard let object = item.objectValue else { return nil }
        let function = object["function"]?.objectValue
        guard let id = object["call_id"]?.stringValue
                ?? object["tool_call_id"]?.stringValue
                ?? object["id"]?.stringValue,
              let name = object["name"]?.stringValue ?? function?["name"]?.stringValue else {
            return nil
        }
        let raw = object["input"] ?? object["arguments"] ?? function?["arguments"] ?? .object([:])
        let arguments: IXJSONValue
        if let value = raw.stringValue, let data = value.data(using: .utf8) {
            arguments = (try? IXJSONValue.decode(data)) ?? .object(["raw": .string(value)])
        } else {
            arguments = raw
        }
        return IXCodexToolCall(
            id: id,
            name: name,
            arguments: arguments,
            kind: object["type"]?.stringValue == "custom_tool_call" ? .custom : .function
        )
    }

    private func responseError(_ response: IXHTTPResponse) -> IXCodexError {
        let fallback = String(data: response.body, encoding: .utf8) ?? "Unknown error"
        let value = try? IXJSONValue.decode(response.body)
        let message = value?["error"]?["message"]?.stringValue
            ?? value?["detail"]?.stringValue
            ?? value?["message"]?.stringValue
            ?? fallback
        return .requestFailed(status: response.statusCode, message: message)
    }
}

private extension Array where Element: Hashable {
    func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}

private extension Array where Element == IXCodexToolCall {
    func uniquedByID() -> [IXCodexToolCall] {
        var ids = Set<String>()
        return filter { ids.insert($0.id).inserted }
    }
}
