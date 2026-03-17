using System;
using Mailozaurr;

namespace IntelligenceX.Tools.Email;

internal static class SmtpClientFactory {
    internal static Smtp Create(SmtpAccountOptions options, bool dryRun) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return new Smtp {
            DryRun = dryRun,
            Timeout = options.TimeoutMs,
            RetryCount = options.RetryCount
        };
    }

    internal static void DisposeQuietly(Smtp? smtp) {
        if (smtp is null) {
            return;
        }

        try {
            smtp.Disconnect();
        } catch {
            // best-effort
        }

        try {
            smtp.Dispose();
        } catch {
            // best-effort
        }
    }
}
