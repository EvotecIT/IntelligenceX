import Foundation

public struct IXCodexConfiguration: Sendable, Equatable {
    public var clientID: String
    public var scope: String
    public var deviceAuthorizationURL: URL
    public var deviceVerificationURL: URL
    public var deviceCallbackURL: URL
    public var tokenURL: URL
    public var responsesURL: URL
    public var modelURLs: [URL]
    public var defaultModel: String
    public var fallbackModels: [String]
    public var originator: String
    public var userAgent: String

    public init(
        clientID: String = "app_EMoamEEZ73f0CkXaXp7hrann",
        scope: String = "openid profile email offline_access",
        deviceAuthorizationURL: URL = URL(string: "https://auth.openai.com/api/accounts/deviceauth/usercode")!,
        deviceVerificationURL: URL = URL(string: "https://auth.openai.com/codex/device")!,
        deviceCallbackURL: URL = URL(string: "https://auth.openai.com/deviceauth/callback")!,
        tokenURL: URL = URL(string: "https://auth.openai.com/oauth/token")!,
        responsesURL: URL = URL(string: "https://chatgpt.com/backend-api/codex/responses")!,
        modelURLs: [URL] = [
            URL(string: "https://chatgpt.com/backend-api/codex/models")!,
            URL(string: "https://chatgpt.com/backend-api/models")!,
        ],
        defaultModel: String = "gpt-5.6-sol",
        fallbackModels: [String] = ["gpt-5.5", "gpt-5.4", "gpt-5.3-codex"],
        originator: String = "intelligencex",
        userAgent: String = "intelligencex-swift/0.1"
    ) {
        self.clientID = clientID
        self.scope = scope
        self.deviceAuthorizationURL = deviceAuthorizationURL
        self.deviceVerificationURL = deviceVerificationURL
        self.deviceCallbackURL = deviceCallbackURL
        self.tokenURL = tokenURL
        self.responsesURL = responsesURL
        self.modelURLs = modelURLs
        self.defaultModel = defaultModel
        self.fallbackModels = fallbackModels
        self.originator = originator
        self.userAgent = userAgent
    }
}

public enum IXCodexError: LocalizedError, Equatable, Sendable {
    case authenticationRequired
    case invalidResponse(String)
    case requestFailed(status: Int, message: String)
    case deviceAuthorizationExpired
    case deviceAuthorizationDenied
    case toolLoopLimitExceeded
    case malformedToolCall(String)
    case conversationBusy

    public var errorDescription: String? {
        switch self {
        case .authenticationRequired:
            "Sign in with ChatGPT to continue."
        case .invalidResponse(let detail):
            "ChatGPT returned an invalid response: \(detail)"
        case .requestFailed(let status, let message):
            "ChatGPT request failed (HTTP \(status)): \(message)"
        case .deviceAuthorizationExpired:
            "The ChatGPT sign-in code expired."
        case .deviceAuthorizationDenied:
            "ChatGPT sign-in was declined."
        case .toolLoopLimitExceeded:
            "The assistant stopped because it requested too many consecutive actions."
        case .malformedToolCall(let detail):
            "The assistant returned an invalid action request: \(detail)"
        case .conversationBusy:
            "This conversation is already processing another request."
        }
    }
}
