using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// Base class for DnsClientX tools.
/// </summary>
public abstract class DnsClientXToolBase : ToolBase {
    /// <summary>
    /// Shared options for DnsClientX tools.
    /// </summary>
    protected readonly DnsClientXToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClientXToolBase"/> class.
    /// </summary>
    protected DnsClientXToolBase(DnsClientXToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }
}

