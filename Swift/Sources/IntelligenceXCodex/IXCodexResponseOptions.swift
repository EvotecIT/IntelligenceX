import Foundation

/// Length preference for generated text; explicit user instructions still apply.
public enum IXCodexTextVerbosity: String, Codable, Sendable {
    case low, medium, high
}

/// Requested processing tier. Availability and usage costs depend on the account.
public enum IXCodexServiceTier: String, Codable, Sendable {
    case auto
    case standard = "default"
    case priority
    case fast
    case flex
}

/// Optional provider-generated reasoning summary, distinct from reasoning effort.
public enum IXCodexReasoningSummary: String, Codable, Sendable {
    case auto, concise, detailed
}

/// Per-run Responses settings, retained across tool rounds and request retries.
/// Defaults preserve medium verbosity, automatic summaries, and the account's
/// processing tier. A nil verbosity or summary omits that unsupported parameter.
public struct IXCodexResponseOptions: Sendable, Equatable {
    public var textVerbosity: IXCodexTextVerbosity?
    public var reasoningSummary: IXCodexReasoningSummary?
    public var serviceTier: IXCodexServiceTier?
    /// The `prompt_cache_key` sent with each request of the run. When nil, the
    /// conversation's session identifier is used, so turns of one
    /// conversation share a cache.
    ///
    /// Set a stable key to share the cached prompt prefix across separate
    /// conversations that repeat the same instructions and tools, such as a
    /// one-shot classifier created per request. The key is sent to the
    /// provider as-is: derive it from the stable prompt (for example a digest
    /// of the model and instructions) and never embed raw user or account
    /// identifiers. An empty key is treated as nil. Compare
    /// `IXCodexUsage.cachedInputTokens` to confirm reuse.
    public var promptCacheKey: String?

    public init(textVerbosity: IXCodexTextVerbosity? = .medium,
                reasoningSummary: IXCodexReasoningSummary? = .auto,
                serviceTier: IXCodexServiceTier? = nil,
                promptCacheKey: String? = nil) {
        self.textVerbosity = textVerbosity
        self.reasoningSummary = reasoningSummary
        self.serviceTier = serviceTier
        self.promptCacheKey = promptCacheKey
    }
}

/// A processing tier advertised by the signed-in account's model catalog.
public struct IXCodexServiceTierOption: Sendable, Equatable, Identifiable {
    public let id: String
    public let name: String
    public let description: String?

    public init(id: String, name: String? = nil, description: String? = nil) {
        self.id = id
        self.name = name ?? id
        self.description = description
    }
}

extension IXCodexModel {
    /// Omits optional parameters the catalog explicitly marks unsupported.
    /// Unknown capabilities preserve the requested settings and processing tier.
    public func supportedResponseOptions(_ requested: IXCodexResponseOptions) -> IXCodexResponseOptions {
        var result = requested
        if supportsTextVerbosity == false { result.textVerbosity = nil }
        if supportsReasoningSummaryParameter == false { result.reasoningSummary = nil }
        return result
    }
}
