using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Security;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Base class for email tool implementations.
/// </summary>
public abstract class EmailToolBase : ToolBase {
    /// <summary>
    /// Pipeline context key used to cache validated SMTP options.
    /// </summary>
    protected const string SmtpOptionsContextKey = "email.smtp_options";

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

    /// <summary>
    /// Shared middleware that requires SMTP configuration and caches validated options in pipeline context.
    /// </summary>
    protected Task<string> EnsureSmtpConfiguredAsync<TRequest>(
        ToolPipelineContext<TRequest> context,
        CancellationToken cancellationToken,
        ToolPipelineNext<TRequest> next)
        where TRequest : notnull {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var smtpOptions = Options.Smtp;
        if (smtpOptions is null) {
            return Task.FromResult(ToolResultV2.Error("not_configured", "SMTP is not configured."));
        }

        smtpOptions.Validate();
        context.SetItem(SmtpOptionsContextKey, smtpOptions);
        return next(context, cancellationToken);
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
