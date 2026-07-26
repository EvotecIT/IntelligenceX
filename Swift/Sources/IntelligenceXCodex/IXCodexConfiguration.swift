import Foundation

public struct IXCodexConfiguration: Sendable, Equatable {
    public var clientID: String
    public var scope: String
    public var authorizationURL: URL
    public var browserCallbackPorts: [UInt16]
    public var deviceAuthorizationURL: URL
    public var deviceVerificationURL: URL
    public var deviceCallbackURL: URL
    public var tokenURL: URL
    public var responsesURL: URL
    public var compactionURL: URL
    public var accountUsageURL: URL
    public var modelURLs: [URL]
    public var modelCatalogClientVersion: String
    public var defaultModel: String
    public var defaultReasoningEffort: IXCodexReasoningEffort
    public var fallbackModels: [String]
    public var originator: String
    public var userAgent: String

    public init(
        clientID: String = "app_EMoamEEZ73f0CkXaXp7hrann",
        scope: String = "openid profile email offline_access api.connectors.read api.connectors.invoke",
        authorizationURL: URL = URL(string: "https://auth.openai.com/oauth/authorize")!,
        browserCallbackPorts: [UInt16] = [1455, 1457],
        deviceAuthorizationURL: URL = URL(string: "https://auth.openai.com/api/accounts/deviceauth/usercode")!,
        deviceVerificationURL: URL = URL(string: "https://auth.openai.com/codex/device")!,
        deviceCallbackURL: URL = URL(string: "https://auth.openai.com/deviceauth/callback")!,
        tokenURL: URL = URL(string: "https://auth.openai.com/oauth/token")!,
        responsesURL: URL = URL(string: "https://chatgpt.com/backend-api/codex/responses")!,
        compactionURL: URL = URL(string: "https://chatgpt.com/backend-api/codex/responses/compact")!,
        accountUsageURL: URL = URL(string: "https://chatgpt.com/backend-api/wham/usage")!,
        modelURLs: [URL] = [
            URL(string: "https://chatgpt.com/backend-api/codex/models")!,
        ],
        modelCatalogClientVersion: String = "0.146.0-alpha.3.1",
        defaultModel: String = "gpt-5.6-sol",
        defaultReasoningEffort: IXCodexReasoningEffort = .low,
        fallbackModels: [String] = ["gpt-5.5", "gpt-5.4", "gpt-5.3-codex"],
        originator: String = "intelligencex",
        userAgent: String = "intelligencex-swift/0.1"
    ) {
        self.clientID = clientID
        self.scope = scope
        self.authorizationURL = authorizationURL
        self.browserCallbackPorts = browserCallbackPorts
        self.deviceAuthorizationURL = deviceAuthorizationURL
        self.deviceVerificationURL = deviceVerificationURL
        self.deviceCallbackURL = deviceCallbackURL
        self.tokenURL = tokenURL
        self.responsesURL = responsesURL
        self.compactionURL = compactionURL
        self.accountUsageURL = accountUsageURL
        self.modelURLs = modelURLs
        self.modelCatalogClientVersion = modelCatalogClientVersion
        self.defaultModel = defaultModel
        self.defaultReasoningEffort = defaultReasoningEffort
        self.fallbackModels = fallbackModels
        self.originator = originator
        self.userAgent = userAgent
    }
}

public enum IXCodexError: LocalizedError, Equatable, Sendable {
    case authenticationRequired
    case invalidResponse(String)
    case requestFailed(status: Int, message: String)
    case browserAuthorizationExpired
    case browserAuthorizationDenied
    case browserAuthorizationStateMismatch
    case invalidBrowserCallback(String)
    case deviceAuthorizationExpired
    case deviceAuthorizationDenied
    case toolLoopLimitExceeded
    case malformedToolCall(String)
    case conversationBusy

    public var requiresReauthorization: Bool {
        switch self {
        case .authenticationRequired:
            return true
        case .requestFailed(let status, let message):
            return status == 401 || (
                status == 400 &&
                (
                    message.localizedCaseInsensitiveContains("invalid_grant") ||
                    message.localizedCaseInsensitiveContains("refresh token") ||
                    message.localizedCaseInsensitiveContains("token expired")
                )
            )
        default:
            return false
        }
    }

    public var isUsageLimitReached: Bool {
        switch self {
        case .requestFailed(let status, let message):
            let normalized = message.lowercased()
            return status == 402 ||
                status == 429 ||
                normalized.contains("usage limit") ||
                normalized.contains("rate limit") ||
                normalized.contains("usage cap") ||
                normalized.contains("insufficient_quota") ||
                normalized.contains("quota exceeded") ||
                normalized.contains("quota exhausted") ||
                normalized.contains("credits exhausted") ||
                normalized.contains("credit balance")
        default:
            return false
        }
    }

    public var errorDescription: String? {
        switch self {
        case .authenticationRequired:
            "Sign in with ChatGPT to continue."
        case .invalidResponse(let detail):
            "ChatGPT returned an invalid response: \(detail)"
        case .requestFailed(let status, let message):
            "ChatGPT request failed (HTTP \(status)): \(message)"
        case .browserAuthorizationExpired:
            "The ChatGPT browser sign-in expired."
        case .browserAuthorizationDenied:
            "ChatGPT browser sign-in was declined."
        case .browserAuthorizationStateMismatch:
            "ChatGPT browser sign-in returned an invalid security state."
        case .invalidBrowserCallback(let detail):
            "ChatGPT browser sign-in returned an invalid callback: \(detail)"
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
