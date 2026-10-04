import Foundation

/// Provider grants are separate from usage-credit balance and local manual inventory.
public struct IXCodexResetCredits: Sendable, Equatable {
    public let availableCount: Int?
    public let applicableAvailableCount: Int?
    public let grants: [IXCodexResetCredit]
    public let detailsAvailable: Bool
    public let history: [IXCodexResetCreditEvent]
    public let historyAvailable: Bool
    public let historyWindowStart: Date?
    public let historyAsOf: Date?
    public let historyNextCursor: String?

    static func decode(summary: IXJSONValue?, details: Data?, history: Data?) -> Self? {
        let detail = details.flatMap { try? IXJSONValue.decode($0) }
        let events = history.flatMap { try? IXJSONValue.decode($0) }
        guard summary?.objectValue != nil || detail?.objectValue != nil || events?.objectValue != nil else { return nil }
        return Self(
            availableCount: count(detail?["available_count"]) ?? count(summary?["available_count"]),
            applicableAvailableCount: count(summary?["applicable_available_count"]),
            grants: (detail?["credits"]?.arrayValue ?? []).compactMap(IXCodexResetCredit.init),
            detailsAvailable: detail?["credits"]?.arrayValue != nil,
            history: (events?["events"]?.arrayValue ?? []).compactMap(IXCodexResetCreditEvent.init),
            historyAvailable: events?["events"]?.arrayValue != nil,
            historyWindowStart: timestamp(events?["window_start"]),
            historyAsOf: timestamp(events?["as_of"]),
            historyNextCursor: events?["next_cursor"]?.stringValue
        )
    }

    private static func count(_ value: IXJSONValue?) -> Int? {
        guard let number = value?.numberValue, number.isFinite, number >= 0,
              number < Double(Int.max), number.rounded(.towardZero) == number else { return nil }
        return Int(number)
    }

    static func timestamp(_ value: IXJSONValue?) -> Date? {
        if let number = value?.numberValue, number.isFinite { return Date(timeIntervalSince1970: number) }
        guard let text = value?.stringValue else { return nil }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = formatter.date(from: text) { return date }
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: text)
    }
}

/// Reading a grant never redeems it; status and unknown timestamps remain provider evidence.
public struct IXCodexResetCredit: Sendable, Equatable {
    public let id: String?
    public let resetType: String?
    public let title: String?
    public let status: String?
    public let isSupportedByPlan: Bool?
    public let grantedAt: Date?
    public let expiresAt: Date?
    /// Original timestamp retains provider precision for export and exact inspection.
    public let expiresAtRaw: String?
    public let redeemStartedAt: Date?
    public let redeemedAt: Date?

    init?(_ value: IXJSONValue) {
        guard value.objectValue != nil else { return nil }
        id = value["id"]?.stringValue
        resetType = value["reset_type"]?.stringValue
        title = value["title"]?.stringValue
        status = value["status"]?.stringValue
        isSupportedByPlan = value["is_supported_by_plan"]?.boolValue
        grantedAt = IXCodexResetCredits.timestamp(value["granted_at"])
        expiresAt = IXCodexResetCredits.timestamp(value["expires_at"])
        expiresAtRaw = value["expires_at"]?.stringValue
        redeemStartedAt = IXCodexResetCredits.timestamp(value["redeem_started_at"])
        redeemedAt = IXCodexResetCredits.timestamp(value["redeemed_at"])
    }
}

/// An event in the provider's returned history page, not a lifetime history.
public struct IXCodexResetCreditEvent: Sendable, Equatable {
    public let id: String?
    public let kind: String?
    public let occurredAt: Date?

    init?(_ value: IXJSONValue) {
        guard value.objectValue != nil else { return nil }
        id = value["id"]?.stringValue
        kind = value["kind"]?.stringValue
        occurredAt = IXCodexResetCredits.timestamp(value["occurred_at"])
    }
}
