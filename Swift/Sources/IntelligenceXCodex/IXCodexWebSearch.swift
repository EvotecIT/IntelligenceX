import Foundation

/// Controls how much hosted web-search context is supplied to the model.
public enum IXCodexWebSearchContextSize: String, Codable, Sendable {
    case low
    case medium
    case high
}

/// Options for OpenAI's hosted Responses API web-search tool.
///
/// Location is intentionally not part of this contract. Consumers should put
/// an explicitly approved place in the query instead of silently forwarding a
/// device or home location.
public struct IXCodexWebSearchOptions: Sendable, Equatable {
    public var contextSize: IXCodexWebSearchContextSize
    public var allowsLiveInternetAccess: Bool
    public var requiresSearch: Bool

    public init(
        contextSize: IXCodexWebSearchContextSize = .low,
        allowsLiveInternetAccess: Bool = true,
        requiresSearch: Bool = false
    ) {
        self.contextSize = contextSize
        self.allowsLiveInternetAccess = allowsLiveInternetAccess
        self.requiresSearch = requiresSearch
    }
}

/// A clickable source annotation returned with a web-grounded answer.
public struct IXCodexCitation: Sendable, Equatable, Hashable, Identifiable {
    public let title: String
    public let url: URL
    public let startIndex: Int?
    public let endIndex: Int?

    public var id: String {
        "\(url.absoluteString)#\(startIndex.map(String.init) ?? "-"):\(endIndex.map(String.init) ?? "-")"
    }

    public init(
        title: String,
        url: URL,
        startIndex: Int? = nil,
        endIndex: Int? = nil
    ) {
        self.title = title
        self.url = url
        self.startIndex = startIndex
        self.endIndex = endIndex
    }
}

/// Observable metadata for one hosted web-search action.
public struct IXCodexWebSearchActivity: Sendable, Equatable, Identifiable {
    public let id: String
    public let status: String?
    public let action: String?
    public let queries: [String]
    public let sourceURLs: [URL]

    public init(
        id: String,
        status: String? = nil,
        action: String? = nil,
        queries: [String] = [],
        sourceURLs: [URL] = []
    ) {
        self.id = id
        self.status = status
        self.action = action
        self.queries = queries
        self.sourceURLs = sourceURLs
    }
}
