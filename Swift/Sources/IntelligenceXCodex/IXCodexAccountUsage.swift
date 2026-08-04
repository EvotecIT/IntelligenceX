import Foundation

public struct IXCodexUsageWindow: Sendable, Equatable {
    public let usedPercent: Double
    public let duration: TimeInterval?
    public let resetsAt: Date?

    public init(
        usedPercent: Double,
        duration: TimeInterval? = nil,
        resetsAt: Date? = nil
    ) {
        self.usedPercent = min(max(usedPercent, 0), 100)
        self.duration = duration
        self.resetsAt = resetsAt
    }

    public var remainingPercent: Double {
        100 - usedPercent
    }
}

public struct IXCodexRateLimit: Identifiable, Sendable, Equatable {
    public let id: String
    public let name: String?
    public let isAllowed: Bool?
    public let isLimitReached: Bool?
    public let primaryWindow: IXCodexUsageWindow?
    public let secondaryWindow: IXCodexUsageWindow?

    public init(
        id: String,
        name: String? = nil,
        isAllowed: Bool? = nil,
        isLimitReached: Bool? = nil,
        primaryWindow: IXCodexUsageWindow? = nil,
        secondaryWindow: IXCodexUsageWindow? = nil
    ) {
        self.id = id
        self.name = name
        self.isAllowed = isAllowed
        self.isLimitReached = isLimitReached
        self.primaryWindow = primaryWindow
        self.secondaryWindow = secondaryWindow
    }
}

public struct IXCodexCredits: Sendable, Equatable {
    public let hasCredits: Bool?
    public let isUnlimited: Bool?
    public let balance: String?

    public init(
        hasCredits: Bool? = nil,
        isUnlimited: Bool? = nil,
        balance: String? = nil
    ) {
        self.hasCredits = hasCredits
        self.isUnlimited = isUnlimited
        self.balance = balance
    }
}

public struct IXCodexAccountUsage: Sendable, Equatable {
    public let plan: String?
    public let rateLimits: [IXCodexRateLimit]
    public let credits: IXCodexCredits?
    public let availableResetCredits: Int?

    public init(
        plan: String? = nil,
        rateLimits: [IXCodexRateLimit] = [],
        credits: IXCodexCredits? = nil,
        availableResetCredits: Int? = nil
    ) {
        self.plan = plan
        self.rateLimits = rateLimits
        self.credits = credits
        self.availableResetCredits = availableResetCredits
    }

    public var primaryRateLimit: IXCodexRateLimit? {
        rateLimits.first(where: { $0.id == "codex" }) ?? rateLimits.first
    }

    static func decode(_ data: Data) throws -> IXCodexAccountUsage {
        let value = try IXJSONValue.decode(data)
        var limits: [IXCodexRateLimit] = [
            rateLimit(
                id: "codex",
                name: nil,
                value: value["rate_limit"]
            ),
        ]
        limits.append(contentsOf: (value["additional_rate_limits"]?.arrayValue ?? [])
            .enumerated()
            .compactMap { index, item in
                guard let object = item.objectValue else { return nil }
                let id = object["metered_feature"]?.stringValue
                    ?? object["limit_name"]?.stringValue
                    ?? "additional-\(index)"
                return rateLimit(
                    id: id,
                    name: object["limit_name"]?.stringValue,
                    value: object["rate_limit"]
                )
            })

        let creditsValue = value["credits"]
        let credits: IXCodexCredits?
        if creditsValue?.objectValue != nil {
            credits = IXCodexCredits(
                hasCredits: creditsValue?["has_credits"]?.boolValue,
                isUnlimited: creditsValue?["unlimited"]?.boolValue,
                balance: creditsValue?["balance"]?.stringValue
            )
        } else {
            credits = nil
        }

        return IXCodexAccountUsage(
            plan: value["plan_type"]?.stringValue,
            rateLimits: limits,
            credits: credits,
            availableResetCredits: value["rate_limit_reset_credits"]?[
                "available_count"
            ]?.numberValue.map(Int.init)
        )
    }

    private static func rateLimit(
        id: String,
        name: String?,
        value: IXJSONValue?
    ) -> IXCodexRateLimit {
        IXCodexRateLimit(
            id: id,
            name: name,
            isAllowed: value?["allowed"]?.boolValue,
            isLimitReached: value?["limit_reached"]?.boolValue,
            primaryWindow: usageWindow(value?["primary_window"]),
            secondaryWindow: usageWindow(value?["secondary_window"])
        )
    }

    private static func usageWindow(
        _ value: IXJSONValue?
    ) -> IXCodexUsageWindow? {
        guard let usedPercent = value?["used_percent"]?.numberValue else {
            return nil
        }
        return IXCodexUsageWindow(
            usedPercent: usedPercent,
            duration: value?["limit_window_seconds"]?.numberValue,
            resetsAt: value?["reset_at"]?.numberValue.map {
                Date(timeIntervalSince1970: $0)
            }
        )
    }
}
