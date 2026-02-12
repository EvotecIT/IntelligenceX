using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Base class for TestimoX tools with shared option validation.
/// </summary>
public abstract class TestimoXToolBase : ToolBase {
    /// <summary>
    /// Shared options for TestimoX tools.
    /// </summary>
    protected readonly TestimoXToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXToolBase"/> class.
    /// </summary>
    protected TestimoXToolBase(TestimoXToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }
}
