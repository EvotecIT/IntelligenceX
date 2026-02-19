using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Email tool pack (self-describing + self-registering).
/// </summary>
public sealed class EmailToolPack : IToolPack {
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
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "IMAP/SMTP workflows (search/get/probe/send) via Mailozaurr.",
        SourceKind = "builtin"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterEmailPack(_options);
    }
}
