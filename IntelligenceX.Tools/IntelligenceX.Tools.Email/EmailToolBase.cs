using System;
using MailKit.Security;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Base class for email tool implementations.
/// </summary>
public abstract class EmailToolBase : ToolBase {
    /// <summary>
    /// Shared options for email tools.
    /// </summary>
    protected readonly EmailToolOptions Options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailToolBase"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when option values are invalid.</exception>
    protected EmailToolBase(EmailToolOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    internal static SecureSocketOptions ParseSecureSocketOptions(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return SecureSocketOptions.Auto;
        }
        return Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.Auto;
    }
}
