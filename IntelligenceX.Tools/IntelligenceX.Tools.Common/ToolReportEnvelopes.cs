using System;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared typed envelope for EVTX-style reports.
/// </summary>
public abstract class EvtxReportEnvelope {
    /// <summary>
    /// Gets or sets EVTX file path.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets provider name used for query/report scope.
    /// </summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets maximum events scanned during query.
    /// </summary>
    public int MaxEventsScanned { get; init; }

    /// <summary>
    /// Gets or sets scanned events count.
    /// </summary>
    public int ScannedEvents { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether result set is truncated by caps.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Gets or sets minimum event time among returned/scanned items.
    /// </summary>
    public DateTime? TimeCreatedUtcMin { get; init; }

    /// <summary>
    /// Gets or sets maximum event time among returned/scanned items.
    /// </summary>
    public DateTime? TimeCreatedUtcMax { get; init; }

    /// <summary>
    /// Gets or sets optional query lower-bound time.
    /// </summary>
    public DateTime? StartTimeUtc { get; init; }

    /// <summary>
    /// Gets or sets optional query upper-bound time.
    /// </summary>
    public DateTime? EndTimeUtc { get; init; }
}

/// <summary>
/// Shared typed envelope for AD query context.
/// </summary>
public abstract class ActiveDirectoryContextEnvelope {
    /// <summary>
    /// Gets or sets domain controller used for query.
    /// </summary>
    public string DomainController { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets search base distinguished name used for query.
    /// </summary>
    public string SearchBaseDn { get; init; } = string.Empty;
}

/// <summary>
/// Shared typed envelope for AD query context + LDAP filter.
/// </summary>
public abstract class ActiveDirectoryQueryEnvelope : ActiveDirectoryContextEnvelope {
    /// <summary>
    /// Gets or sets LDAP filter used for query.
    /// </summary>
    public string LdapFilter { get; init; } = string.Empty;
}
