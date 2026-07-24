import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime

public protocol IXRealtimeSDPExchanging: Sendable {
    func exchange(offer: String, secret: IXRealtimeClientSecret) async throws -> String
}

public struct IXOpenAIRealtimeSDPExchange: IXRealtimeSDPExchanging {
    private let endpoint: URL
    private let session: URLSession

    public init(
        endpoint: URL = URL(string: "https://api.openai.com/v1/realtime/calls")!,
        session: URLSession = .shared
    ) {
        self.endpoint = endpoint
        self.session = session
    }

    public func exchange(offer: String, secret: IXRealtimeClientSecret) async throws -> String {
        let boundary = "ix-\(UUID().uuidString)"
        let body = IXRealtimeMultipartForm.sdpBody(offer, boundary: boundary)

        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("Bearer \(secret.value)", forHTTPHeaderField: "Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        let (data, response) = try await session.data(for: request)
        guard let response = response as? HTTPURLResponse else {
            throw IXCodexError.invalidResponse("Realtime SDP exchange did not return HTTP metadata")
        }
        guard (200..<300).contains(response.statusCode) else {
            let detail = String(data: data, encoding: .utf8) ?? "Unknown error"
            throw IXCodexError.requestFailed(status: response.statusCode, message: detail)
        }
        guard let answer = String(data: data, encoding: .utf8), !answer.isEmpty else {
            throw IXCodexError.invalidResponse("Realtime SDP answer is empty")
        }
        return answer
    }
}

enum IXRealtimeMultipartForm {
    static func sdpBody(_ offer: String, boundary: String) -> Data {
        var body = Data()
        body.appendUTF8("--\(boundary)\r\n")
        // OpenAI expects `sdp` as a form field. Adding a filename makes multipart
        // parsers classify it as a file upload and the endpoint reports it missing.
        body.appendUTF8("Content-Disposition: form-data; name=\"sdp\"\r\n")
        body.appendUTF8("Content-Type: application/sdp\r\n\r\n")
        body.appendUTF8(offer)
        body.appendUTF8("\r\n--\(boundary)--\r\n")
        return body
    }
}

private extension Data {
    mutating func appendUTF8(_ value: String) {
        append(Data(value.utf8))
    }
}
