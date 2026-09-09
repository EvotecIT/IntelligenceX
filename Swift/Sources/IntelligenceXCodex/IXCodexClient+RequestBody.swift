import Foundation

extension IXCodexClient {
    func buildRequestBody(
        input: [IXJSONValue],
        sessionID: String,
        instructions: String,
        tools: [IXCodexToolDefinition],
        model: String,
        reasoningEffort: IXCodexReasoningEffort,
        responseOptions: IXCodexResponseOptions,
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
            "reasoning": .object([
                "effort": .string(reasoningEffort.rawValue),
            ]),
            "prompt_cache_key": .string(sessionID),
        ]
        if let verbosity = responseOptions.textVerbosity {
            object["text"] = .object(["verbosity": .string(verbosity.rawValue)])
        }
        if let summary = responseOptions.reasoningSummary {
            object["reasoning"] = .object([
                "effort": .string(reasoningEffort.rawValue),
                "summary": .string(summary.rawValue),
            ])
        }
        if let tier = responseOptions.serviceTier {
            object["service_tier"] = .string(tier.rawValue)
        }
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
            let function: [String: IXJSONValue] = [
                "name": .string(tool.name),
                "description": .string(tool.description),
                schemaKey: tool.parameters,
                // Responses may normalize omitted strictness into strict mode,
                // changing optional fields into mandatory arguments.
                "strict": .bool(tool.strict),
            ]
            return .object([
                "type": .string("function"),
                "function": .object(function),
            ])
        case .functionFlatParameters, .functionFlatInputSchema:
            let function: [String: IXJSONValue] = [
                "type": .string("function"),
                "name": .string(tool.name),
                "description": .string(tool.description),
                schemaKey: tool.parameters,
                // Responses may normalize omitted strictness into strict mode,
                // changing optional fields into mandatory arguments.
                "strict": .bool(tool.strict),
            ]
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

}
