import Foundation

struct IXCodexSSEParser {
    struct Result {
        var text = ""
        var completedResponse: IXJSONValue?
        var streamedItems: [IXJSONValue] = []
        var errorMessage: String?
    }

    func parse(_ data: Data) throws -> Result {
        guard let source = String(data: data, encoding: .utf8) else {
            throw IXCodexError.invalidResponse("SSE stream is not UTF-8")
        }
        var result = Result()
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
            switch type {
            case "response.output_text.delta", "response.refusal.delta":
                if let delta = object["delta"]?.stringValue { result.text += delta }
            case "response.output_item.done":
                if let item = object["item"] { result.streamedItems.append(item) }
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
        if let errorMessage = result.errorMessage {
            throw IXCodexError.invalidResponse(errorMessage)
        }
        return result
    }
}
