using Mailozaurr;

namespace IntelligenceX.Tools.Email;

internal static class EmailSessionRequests {
    internal static ImapSessionRequest BuildImapSessionRequest(ImapAccountOptions options) {
        return new ImapSessionRequest {
            Connection = new ImapConnectionRequest(
                options.Server,
                options.Port,
                EmailToolBase.ParseSecureSocketOptions(options.SecureSocketOptions),
                timeout: options.TimeoutMs,
                skipCertificateRevocation: options.SkipCertificateRevocation,
                skipCertificateValidation: options.SkipCertificateValidation,
                retryCount: options.RetryCount,
                retryDelayMilliseconds: options.RetryDelayMilliseconds,
                retryDelayBackoff: options.RetryDelayBackoff),
            UserName = options.UserName,
            Secret = options.Password
        };
    }

    internal static SmtpSessionRequest BuildSmtpSessionRequest(SmtpAccountOptions options, bool dryRun) {
        return new SmtpSessionRequest {
            Server = options.Server,
            Port = options.Port,
            SecureSocketOptions = EmailToolBase.ParseSecureSocketOptions(options.SecureSocketOptions),
            UseSsl = options.UseSsl,
            TimeoutMs = options.TimeoutMs,
            RetryCount = options.RetryCount,
            DryRun = dryRun,
            UserName = options.UserName,
            Password = options.Password
        };
    }

    internal static void ApplySmtpRuntimeOptions(Smtp smtp, SmtpAccountOptions options, bool dryRun) {
        ArgumentNullException.ThrowIfNull(smtp);
        ArgumentNullException.ThrowIfNull(options);

        smtp.Timeout = options.TimeoutMs;
        smtp.RetryCount = options.RetryCount;
        smtp.DryRun = dryRun;
    }
}
