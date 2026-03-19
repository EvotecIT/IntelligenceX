using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Email tool pack (self-describing + self-registering).
/// </summary>
public sealed class EmailToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly EmailToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="EmailToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public EmailToolPack(EmailToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "email",
        Name = "Email (Mailozaurr)",
        Aliases = new[] { "mailozaurr" },
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "IMAP/SMTP workflows (search/get/probe/send) via Mailozaurr.",
        SourceKind = "builtin",
        EngineId = "mailozaurr",
        Category = "email",
        CapabilityTags = new[] { "email", "imap", ToolPackCapabilityTags.RemoteAnalysis, "smtp" }
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterEmailPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryEmailExtensions.GetRegisteredToolCatalog(_options);
    }
}
