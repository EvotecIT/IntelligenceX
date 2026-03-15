using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Active Directory lifecycle/write tool pack (self-describing + self-registering).
/// </summary>
public sealed class ActiveDirectoryLifecycleToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly ActiveDirectoryToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="ActiveDirectoryLifecycleToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public ActiveDirectoryLifecycleToolPack(ActiveDirectoryToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "active_directory_lifecycle",
        Name = "AD Lifecycle",
        Tier = ToolCapabilityTier.DangerousWrite,
        IsDangerous = true,
        Description = "ADPlayground-backed governed Active Directory lifecycle tools for joiner/leaver and account-write workflows.",
        SourceKind = "closed_source",
        EngineId = "adplayground",
        Category = "active_directory",
        CapabilityTags = new[] {
            "directory",
            "identity_lifecycle",
            "joiner_leaver",
            "remote_analysis",
            "write_capable"
        },
        SearchTokens = new[] {
            "ad",
            "adplayground",
            "identity_lifecycle",
            "joiner",
            "leaver",
            "offboarding",
            "user_provisioning",
            "password_reset",
            "account_disable"
        }
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterActiveDirectoryLifecyclePack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryActiveDirectoryLifecycleExtensions.GetRegisteredToolCatalog(_options);
    }
}
