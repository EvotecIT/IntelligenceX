using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Active Directory tool pack (self-describing + self-registering).
/// </summary>
public sealed class ActiveDirectoryToolPack : IToolPack {
    private readonly ActiveDirectoryToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="ActiveDirectoryToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public ActiveDirectoryToolPack(ActiveDirectoryToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "ad",
        Name = "Active Directory",
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "Active Directory read-only analysis (via ADPlayground engine).",
        SourceKind = "closed_source"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterActiveDirectoryPack(_options);
    }
}
