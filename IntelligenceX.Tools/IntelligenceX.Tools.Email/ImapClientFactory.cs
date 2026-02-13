using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Imap;
using Mailozaurr;

namespace IntelligenceX.Tools.Email;

internal static class ImapClientFactory {
    public static Task<ImapClient> ConnectAsync(ImapAccountOptions options, CancellationToken cancellationToken) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }
        options.Validate();

        var secure = EmailToolBase.ParseSecureSocketOptions(options.SecureSocketOptions);
        return ImapConnector.ConnectAsync(
            options.Server,
            options.Port,
            secure,
            options.TimeoutMs,
            options.SkipCertificateRevocation,
            options.SkipCertificateValidation,
            authenticateAsync: async (client, ct) => {
                await client.AuthenticateAsync(new NetworkCredential(options.UserName, options.Password), ct).ConfigureAwait(false);
            },
            options.RetryCount,
            options.RetryDelayMilliseconds,
            options.RetryDelayBackoff,
            cancellationToken);
    }
}

