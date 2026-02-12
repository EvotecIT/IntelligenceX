using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Reviewer setup tool pack (guidance-first).
/// </summary>
public sealed class ReviewerSetupToolPack : IToolPack {
    private readonly ReviewerSetupToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="ReviewerSetupToolPack"/>.
    /// </summary>
    public ReviewerSetupToolPack(ReviewerSetupToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "reviewer_setup",
        Name = "Reviewer Setup",
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "Path contract and execution guidance for IntelligenceX reviewer onboarding."
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterReviewerSetupPack(_options);
    }
}
