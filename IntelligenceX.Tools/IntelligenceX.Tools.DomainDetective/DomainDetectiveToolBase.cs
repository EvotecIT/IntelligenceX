using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Base class for DomainDetective tools.
/// </summary>
public abstract class DomainDetectiveToolBase : ToolBase {
    /// <summary>
    /// Shared options for DomainDetective tools.
    /// </summary>
    protected readonly DomainDetectiveToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectiveToolBase"/> class.
    /// </summary>
    protected DomainDetectiveToolBase(DomainDetectiveToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }
}

