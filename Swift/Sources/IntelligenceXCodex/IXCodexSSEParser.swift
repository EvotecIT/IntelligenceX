import Foundation

struct IXCodexSSEParser {
    struct Result {
        var text = ""
        var completedResponse: IXJSONValue?
        var streamedItems: [IXJSONValue] = []
        var errorMessage: String?
        var eventTypes: [String] = []
        var eventKeys: [String: [String]] = [:]
    }

    func parse(_ data: Data) throws -> Result {
        guard let source = String(data: data, encoding: .utf8) else {
            throw IXCodexError.invalidResponse("SSE stream is not UTF-8")
        }
        var result = Result()
        var pendingItems: [String: [String: IXJSONValue]] = [:]
        var argumentBuffers: [String: String] = [:]
        var finalizedItemIDs = Set<String>()
        let normalized = source.replacingOccurrences(of: "\r\n", with: "\n")
        for block in normalized.components(separatedBy: "\n\n") {
            let payload = block.split(separator: "\n")
                .compactMap { line -> String? in
                    guard line.hasPrefix("data:") else { return nil }
                    return String(line.dropFirst(5)).trimmingCharacters(in: .whitespaces)
                }
                .joined(separator: "\n")
            guard !payload.isEmpty, payload != "[DONE]",
                  let eventData = payload.data(using: .utf8),
                  let event = try? IXJSONValue.decode(eventData),
                  let object = event.objectValue,
                  let type = object["type"]?.stringValue else {
                continue
            }
            result.eventTypes.append(type)
            result.eventKeys[type] = Array(
                Set(result.eventKeys[type, default: []]).union(object.keys)
            ).sorted()
            switch type {
            case "response.output_text.delta", "response.refusal.delta":
                if let delta = object["delta"]?.stringValue { result.text += delta }
            case "response.output_item.added":
                if let itemID = object["item"]?["id"]?.stringValue
                    ?? object["item_id"]?.stringValue
                    ?? object["item"]?["call_id"]?.stringValue {
                    var item = object["item"]?.objectValue ?? [:]
                    item["id"] = item["id"] ?? .string(itemID)
                    if let callID = object["call_id"] { item["call_id"] = callID }
                    if let name = object["name"] { item["name"] = name }
                    pendingItems[itemID] = item
                }
            case "response.function_call_arguments.delta", "response.custom_tool_call_input.delta":
                if let itemID = object["item_id"]?.stringValue ?? object["call_id"]?.stringValue,
                   let delta = object["delta"]?.stringValue {
                    argumentBuffers[itemID, default: ""] += delta
                }
            case "response.function_call_arguments.done", "response.custom_tool_call_input.done":
                if let itemID = object["item_id"]?.stringValue ?? object["call_id"]?.stringValue {
                    var item = pendingItems[itemID] ?? [:]
                    item["id"] = item["id"] ?? .string(itemID)
                    item["type"] = item["type"] ?? .string(
                        type.hasPrefix("response.custom_")
                            ? "custom_tool_call"
                            : "function_call"
                    )
                    if let arguments = object["arguments"]?.stringValue ?? object["input"]?.stringValue {
                        item[type.hasPrefix("response.custom_") ? "input" : "arguments"] = .string(arguments)
                    } else if let arguments = argumentBuffers[itemID] {
                        item[type.hasPrefix("response.custom_") ? "input" : "arguments"] = .string(arguments)
                    }
                    if let name = object["name"]?.stringValue { item["name"] = .string(name) }
                    item["call_id"] = item["call_id"] ?? object["call_id"] ?? .string(itemID)
                    pendingItems[itemID] = item
                }
            case "response.output_item.done":
                if var item = object["item"]?.objectValue {
                    let itemID = item["id"]?.stringValue
                        ?? object["item_id"]?.stringValue
                        ?? item["call_id"]?.stringValue
                    if let itemID, let arguments = argumentBuffers[itemID], item["arguments"] == nil {
                        item["arguments"] = .string(arguments)
                    }
                    result.streamedItems.append(.object(item))
                    if let itemID { finalizedItemIDs.insert(itemID) }
                } else if let itemID = object["item_id"]?.stringValue ?? object["call_id"]?.stringValue,
                          var item = pendingItems[itemID] {
                    if let arguments = argumentBuffers[itemID], item["arguments"] == nil {
                        item["arguments"] = .string(arguments)
                    }
                    result.streamedItems.append(.object(item))
                    finalizedItemIDs.insert(itemID)
                }
            case "response.completed", "response.done":
                result.completedResponse = object["response"]
            case "response.failed":
                result.errorMessage = object["response"]?["error"]?["message"]?.stringValue
                    ?? "ChatGPT response failed."
            case "error":
                result.errorMessage = object["message"]?.stringValue
                    ?? object["error"]?["message"]?.stringValue
                    ?? "ChatGPT response failed."
            default:
                break
            }
        }
        for (itemID, var item) in pendingItems where !finalizedItemIDs.contains(itemID) {
            if let arguments = argumentBuffers[itemID], item["arguments"] == nil {
                item["arguments"] = .string(arguments)
            }
            result.streamedItems.append(.object(item))
        }
        if let errorMessage = result.errorMessage {
            throw IXCodexError.invalidResponse(errorMessage)
        }
        return result
    }
}
