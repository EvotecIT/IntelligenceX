import Foundation
import IntelligenceXCodex

/// Selects how a transcription model accepts language and accuracy hints.
public enum IXRealtimeTranscriptionContextStyle: Sendable, Equatable, Hashable {
    /// Uses plural `languages` hints.
    case contextual
    /// Uses one singular `language` hint.
    case legacy
}

/// Identifies a Realtime input-transcription model and its request schema.
///
/// The predefined values cover every model currently documented for Realtime
/// input transcription. Custom identifiers remain possible for snapshots and
/// future models by choosing the matching context style explicitly.
public struct IXRealtimeTranscriptionModel: Sendable, Equatable, Hashable {
    public static let gptLiveTranscribe = Self(
        "gpt-live-transcribe",
        contextStyle: .contextual,
        supportsKeywords: true,
        supportsDelay: true
    )
    public static let gptTranscribe = Self(
        "gpt-transcribe",
        contextStyle: .contextual,
        supportsKeywords: true
    )
    public static let gpt4oTranscribe = Self(
        "gpt-4o-transcribe",
        contextStyle: .legacy
    )
    public static let gpt4oMiniTranscribe = Self(
        "gpt-4o-mini-transcribe",
        contextStyle: .legacy
    )
    public static let gpt4oMiniTranscribe2025_12_15 = Self(
        "gpt-4o-mini-transcribe-2025-12-15",
        contextStyle: .legacy
    )
    public static let gptRealtimeWhisper = Self(
        "gpt-realtime-whisper",
        contextStyle: .legacy,
        supportsPrompt: false,
        supportsDelay: true
    )
    public static let whisper1 = Self(
        "whisper-1",
        contextStyle: .legacy
    )

    public let id: String
    public let contextStyle: IXRealtimeTranscriptionContextStyle
    public let supportsPrompt: Bool
    public let supportsKeywords: Bool
    public let supportsDelay: Bool

    public init(
        _ id: String,
        contextStyle: IXRealtimeTranscriptionContextStyle,
        supportsPrompt: Bool = true,
        supportsKeywords: Bool = false,
        supportsDelay: Bool = false
    ) {
        self.id = id
        self.contextStyle = contextStyle
        self.supportsPrompt = supportsPrompt
        self.supportsKeywords = supportsKeywords
        self.supportsDelay = supportsDelay
    }

    /// Resolves the request schema for a known model identifier.
    /// Unknown identifiers use the current contextual schema.
    public static func resolving(_ id: String) -> Self {
        knownModel(id) ?? Self(id, contextStyle: .contextual)
    }

    /// Preserves the singular-language behavior of the original string API
    /// for unlisted snapshots while still recognizing current model IDs.
    static func resolvingLegacyIdentifier(_ id: String) -> Self {
        knownModel(id) ?? Self(id, contextStyle: .legacy)
    }

    private static func knownModel(_ id: String) -> Self? {
        switch id {
        case gptLiveTranscribe.id:
            return .gptLiveTranscribe
        case gptTranscribe.id:
            return .gptTranscribe
        case gpt4oTranscribe.id:
            return .gpt4oTranscribe
        case gpt4oMiniTranscribe.id:
            return .gpt4oMiniTranscribe
        case gpt4oMiniTranscribe2025_12_15.id:
            return .gpt4oMiniTranscribe2025_12_15
        case gptRealtimeWhisper.id:
            return .gptRealtimeWhisper
        case whisper1.id:
            return .whisper1
        default:
            return nil
        }
    }
}

/// Trades partial-transcript latency for additional audio context.
public enum IXRealtimeTranscriptionDelay: String, Sendable, Equatable, Hashable {
    case minimal
    case low
    case medium
    case high
    case xhigh
}

/// Configures transcription of microphone input in a Realtime session.
public struct IXRealtimeInputTranscriptionOptions: Sendable, Equatable {
    public var model: IXRealtimeTranscriptionModel
    public var prompt: String?
    public var languageHints: [String]
    public var keywords: [String]
    public var delay: IXRealtimeTranscriptionDelay?

    public init(
        model: IXRealtimeTranscriptionModel = .gptLiveTranscribe,
        prompt: String? = nil,
        languageHints: [String] = [],
        keywords: [String] = [],
        delay: IXRealtimeTranscriptionDelay? = nil
    ) {
        self.model = model
        self.prompt = prompt
        self.languageHints = languageHints
        self.keywords = keywords
        self.delay = delay
    }

    var sessionConfiguration: [String: IXJSONValue] {
        var configuration: [String: IXJSONValue] = [
            "model": .string(model.id),
        ]
        if let prompt, model.supportsPrompt {
            configuration["prompt"] = .string(prompt)
        }

        switch model.contextStyle {
        case .contextual:
            if !languageHints.isEmpty {
                configuration["languages"] = .array(
                    languageHints.map(IXJSONValue.string)
                )
            }
        case .legacy:
            if let language = languageHints.first {
                configuration["language"] = .string(language)
            }
        }
        if model.supportsKeywords, !keywords.isEmpty {
            configuration["keywords"] = .array(
                keywords.map(IXJSONValue.string)
            )
        }
        if model.supportsDelay, let delay {
            configuration["delay"] = .string(delay.rawValue)
        }
        return configuration
    }
}
