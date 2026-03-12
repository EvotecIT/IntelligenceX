using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

/// <summary>
/// Shared options for the <c>IntelligenceX.Tools.Email</c> tool pack.
/// </summary>
public sealed class EmailToolOptions : IToolPackRuntimeConfigurable {
    /// <summary>
    /// IMAP account configuration used by IMAP tools.
    /// </summary>
    public ImapAccountOptions? Imap { get; set; }

    /// <summary>
    /// SMTP account configuration used by SMTP tools.
    /// </summary>
    public SmtpAccountOptions? Smtp { get; set; }

    /// <summary>
    /// When true, <c>email_smtp_send</c> requires a recent successful <c>email_smtp_probe</c> result
    /// (referenced by <c>auth_probe_id</c>) before applying <c>send=true</c>.
    /// </summary>
    public bool RequireSuccessfulSmtpProbeForSend { get; set; }

    /// <summary>
    /// Maximum allowed age (in seconds) for a successful SMTP probe when strict send gating is enabled.
    /// </summary>
    public int SmtpProbeMaxAgeSeconds { get; set; } = 900;

    /// <summary>
    /// Probe-session store shared by SMTP probe/send tools.
    /// </summary>
    public IToolAuthenticationProbeStore AuthenticationProbeStore { get; set; } = new InMemoryToolAuthenticationProbeStore();

    /// <summary>
    /// Maximum bytes returned per message body (text or HTML). Tool calls may cap further.
    /// </summary>
    public long MaxBodyBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Maximum number of results returned by list/search operations. Tool calls may cap further.
    /// </summary>
    public int MaxListResults { get; set; } = 50;

    /// <inheritdoc />
    public void ApplyRuntimeContext(ToolPackRuntimeContext context) {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AuthenticationProbeStore is not null) {
            AuthenticationProbeStore = context.AuthenticationProbeStore;
        }

        RequireSuccessfulSmtpProbeForSend = context.RequireSuccessfulSmtpProbeForSend;
        if (context.SmtpProbeMaxAgeSeconds > 0) {
            SmtpProbeMaxAgeSeconds = context.SmtpProbeMaxAgeSeconds;
        }
    }

    /// <summary>
    /// Validates this options instance.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when option values are invalid.</exception>
    public void Validate() {
        if (MaxBodyBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxBodyBytes), "MaxBodyBytes must be positive.");
        }
        if (MaxListResults <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxListResults), "MaxListResults must be positive.");
        }
        if (SmtpProbeMaxAgeSeconds <= 0) {
            throw new ArgumentOutOfRangeException(nameof(SmtpProbeMaxAgeSeconds), "SmtpProbeMaxAgeSeconds must be positive.");
        }
        if (AuthenticationProbeStore is null) {
            throw new InvalidOperationException("AuthenticationProbeStore is required.");
        }
    }
}

/// <summary>
/// IMAP account settings used by email tools.
/// </summary>
public sealed class ImapAccountOptions {
    /// <summary>
    /// IMAP server hostname.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// IMAP server port.
    /// </summary>
    public int Port { get; set; } = 993;

    /// <summary>
    /// IMAP user name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// IMAP password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional default folder name (for example: <c>INBOX</c>).
    /// </summary>
    public string? DefaultFolder { get; set; }

    /// <summary>
    /// MailKit secure socket options name (for example: <c>Auto</c>, <c>SslOnConnect</c>).
    /// </summary>
    public string SecureSocketOptions { get; set; } = "Auto";

    /// <summary>
    /// IMAP operation timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// When true, skips certificate revocation checks.
    /// </summary>
    public bool SkipCertificateRevocation { get; set; }

    /// <summary>
    /// When true, skips certificate validation.
    /// </summary>
    public bool SkipCertificateValidation { get; set; }

    /// <summary>
    /// Number of connection retries (0 means no retries).
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between retries in milliseconds.
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Backoff multiplier applied between retries (must be positive).
    /// </summary>
    public double RetryDelayBackoff { get; set; } = 2.0;

    /// <summary>
    /// Validates this options instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when required settings are missing or invalid.</exception>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(Server)) throw new InvalidOperationException("IMAP Server is required.");
        if (Port <= 0) throw new InvalidOperationException("IMAP Port must be positive.");
        if (string.IsNullOrWhiteSpace(UserName)) throw new InvalidOperationException("IMAP UserName is required.");
        if (string.IsNullOrWhiteSpace(Password)) throw new InvalidOperationException("IMAP Password is required.");
        if (TimeoutMs <= 0) throw new InvalidOperationException("IMAP TimeoutMs must be positive.");
        if (RetryCount < 0) throw new InvalidOperationException("IMAP RetryCount must be >= 0.");
        if (RetryDelayMilliseconds < 0) throw new InvalidOperationException("IMAP RetryDelayMilliseconds must be >= 0.");
        if (RetryDelayBackoff <= 0) throw new InvalidOperationException("IMAP RetryDelayBackoff must be positive.");
    }
}

/// <summary>
/// SMTP account settings used by email tools.
/// </summary>
public sealed class SmtpAccountOptions {
    /// <summary>
    /// SMTP server hostname.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// SMTP user name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// MailKit secure socket options name (for example: <c>Auto</c>, <c>StartTls</c>).
    /// </summary>
    public string SecureSocketOptions { get; set; } = "Auto";

    /// <summary>
    /// When true, requests SSL for connections where supported by the underlying library.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// SMTP operation timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Number of retries for SMTP operations (0 means no retries).
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Validates this options instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when required settings are missing or invalid.</exception>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(Server)) throw new InvalidOperationException("SMTP Server is required.");
        if (Port <= 0) throw new InvalidOperationException("SMTP Port must be positive.");
        if (string.IsNullOrWhiteSpace(UserName)) throw new InvalidOperationException("SMTP UserName is required.");
        if (string.IsNullOrWhiteSpace(Password)) throw new InvalidOperationException("SMTP Password is required.");
        if (TimeoutMs <= 0) throw new InvalidOperationException("SMTP TimeoutMs must be positive.");
        if (RetryCount < 0) throw new InvalidOperationException("SMTP RetryCount must be >= 0.");
    }
}
