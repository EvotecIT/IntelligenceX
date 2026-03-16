using System;
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

        var request = new ImapConnectionRequest(
            options.Server,
            options.Port,
            EmailToolBase.ParseSecureSocketOptions(options.SecureSocketOptions),
            options.TimeoutMs,
            options.SkipCertificateRevocation,
            options.SkipCertificateValidation,
            options.RetryCount,
            options.RetryDelayMilliseconds,
            options.RetryDelayBackoff);

        return ImapConnector.ConnectAuthenticatedAsync(
            request,
            options.UserName,
            options.Password,
            ProtocolAuthMode.Basic,
            cancellationToken);
    }
}
