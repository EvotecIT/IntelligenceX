import Foundation

/// A precise local failure for a strict function-tool schema.
public struct IXCodexToolSchemaIssue: Error, Sendable, Equatable {
    public let toolName: String
    public let path: String
    public let message: String

    public init(toolName: String, path: String, message: String) {
        self.toolName = toolName
        self.path = path
        self.message = message
    }
}

/// Validates the subset of JSON Schema required by OpenAI strict tools.
public enum IXCodexToolSchemaValidator {
    public static func validate(
        _ tools: [IXCodexToolDefinition]
    ) throws {
        for tool in tools where tool.strict {
            guard case .object(let root) = tool.parameters,
                  schemaTypes(in: root) == ["object"] else {
                throw IXCodexToolSchemaIssue(
                    toolName: tool.name,
                    path: "$",
                    message: "Strict function parameters must have type object."
                )
            }
            try validateSchema(
                tool.parameters,
                toolName: tool.name,
                path: "$"
            )
        }
    }

    private static func validateSchema(
        _ schema: IXJSONValue,
        toolName: String,
        path: String
    ) throws {
        guard case .object(let object) = schema else {
            throw IXCodexToolSchemaIssue(
                toolName: toolName,
                path: path,
                message: "Schema nodes must be JSON objects."
            )
        }

        if object["properties"] != nil,
           object["properties"]?.objectValue == nil {
            throw IXCodexToolSchemaIssue(
                toolName: toolName,
                path: path,
                message: "The properties keyword must be a JSON object."
            )
        }
        let properties = object["properties"]?.objectValue
        let isObjectSchema = schemaTypes(in: object).contains("object")
        if isObjectSchema {
            guard let properties else {
                throw IXCodexToolSchemaIssue(
                    toolName: toolName,
                    path: path,
                    message: "Strict object schemas must provide a properties object."
                )
            }
            guard object["additionalProperties"]?.boolValue == false else {
                throw IXCodexToolSchemaIssue(
                    toolName: toolName,
                    path: path,
                    message: "Strict object schemas must set additionalProperties to false."
                )
            }
            let propertyNames = Set(properties.keys)
            guard let requiredValues = object["required"]?.arrayValue,
                  requiredValues.allSatisfy({ $0.stringValue != nil })
            else {
                throw IXCodexToolSchemaIssue(
                    toolName: toolName,
                    path: path,
                    message: "Strict object schemas must provide a required array."
                )
            }
            let requiredNames = Set(requiredValues.compactMap(\.stringValue))
            guard requiredNames == propertyNames else {
                let missing = propertyNames.subtracting(requiredNames).sorted()
                let unknown = requiredNames.subtracting(propertyNames).sorted()
                var details: [String] = []
                if !missing.isEmpty {
                    details.append("missing required fields: \(missing.joined(separator: ", "))")
                }
                if !unknown.isEmpty {
                    details.append("unknown required fields: \(unknown.joined(separator: ", "))")
                }
                throw IXCodexToolSchemaIssue(
                    toolName: toolName,
                    path: path,
                    message: details.joined(separator: "; ")
                )
            }
        }

        for (name, propertySchema) in properties ?? [:] {
            try validateSchema(
                propertySchema,
                toolName: toolName,
                path: "\(path).properties.\(name)"
            )
        }
        if let itemSchema = object["items"], itemSchema != .bool(true), itemSchema != .bool(false) {
            try validateSchema(itemSchema, toolName: toolName, path: "\(path).items")
        }
        for keyword in ["anyOf", "oneOf", "allOf"] {
            for (index, branch) in (object[keyword]?.arrayValue ?? []).enumerated() {
                try validateSchema(
                    branch,
                    toolName: toolName,
                    path: "\(path).\(keyword)[\(index)]"
                )
            }
        }
        for keyword in ["$defs", "definitions"] {
            for (name, definition) in object[keyword]?.objectValue ?? [:] {
                try validateSchema(
                    definition,
                    toolName: toolName,
                    path: "\(path).\(keyword).\(name)"
                )
            }
        }
    }

    private static func schemaTypes(
        in object: [String: IXJSONValue]
    ) -> Set<String> {
        if let type = object["type"]?.stringValue {
            return [type]
        }
        return Set(object["type"]?.arrayValue?.compactMap(\.stringValue) ?? [])
    }
}

extension IXCodexToolSchemaIssue: LocalizedError {
    public var errorDescription: String? {
        "Strict tool '\(toolName)' has an invalid schema at \(path): \(message)"
    }
}
