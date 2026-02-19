using System;
using System.Net;
using System.Threading.Tasks;
using MailKit.Security;
using Mailozaurr;

namespace IntelligenceX.Tools.Email;

internal sealed class SmtpConnectAuthResult {
    internal bool IsSuccess { get; init; }

    internal SecureSocketOptions SecureSocketOptions { get; init; } = SecureSocketOptions.Auto;

    internal string ErrorCode { get; init; } = string.Empty;

    internal string Error { get; init; } = string.Empty;

    internal bool IsTransient { get; init; }
}

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

    internal static async Task<SmtpConnectAuthResult> ConnectAndAuthenticateAsync(Smtp smtp, SmtpAccountOptions options) {
        if (smtp is null) {
            throw new ArgumentNullException(nameof(smtp));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var secure = EmailToolBase.ParseSecureSocketOptions(options.SecureSocketOptions);
        var connectResult = await smtp.ConnectAsync(options.Server, options.Port, secure, options.UseSsl).ConfigureAwait(false);
        if (!connectResult.Status) {
            return new SmtpConnectAuthResult {
                IsSuccess = false,
                SecureSocketOptions = secure,
                ErrorCode = "connect_failed",
                Error = connectResult.Error ?? "Connect failed.",
                IsTransient = true
            };
        }

        var authResult = smtp.Authenticate(new NetworkCredential(options.UserName, options.Password));
        if (!authResult.Status) {
            return new SmtpConnectAuthResult {
                IsSuccess = false,
                SecureSocketOptions = secure,
                ErrorCode = "auth_failed",
                Error = authResult.Error ?? "Authentication failed.",
                IsTransient = false
            };
        }

        return new SmtpConnectAuthResult {
            IsSuccess = true,
            SecureSocketOptions = secure
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
