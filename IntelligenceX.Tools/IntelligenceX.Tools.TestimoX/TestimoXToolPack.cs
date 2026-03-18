using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// IX.TestimoX tool pack (self-describing + self-registering).
/// </summary>
public sealed class TestimoXToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly TestimoXToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="TestimoXToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public TestimoXToolPack(TestimoXToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "testimox",
        Name = "TestimoX",
        Aliases = new[] { "testimoxpack" },
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "TestimoX rule, profile, baseline, and stored-run diagnostics.",
        SourceKind = "closed_source",
        EngineId = "testimox",
        Category = "testimox",
        CapabilityTags = new[] { "configuration", "evidence", "posture", ToolPackCapabilityTags.RemoteAnalysis },
        CapabilityParity = TestimoXToolPackParity.Slices
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterTestimoXPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryTestimoXExtensions.GetRegisteredToolCatalog(_options);
    }
}
