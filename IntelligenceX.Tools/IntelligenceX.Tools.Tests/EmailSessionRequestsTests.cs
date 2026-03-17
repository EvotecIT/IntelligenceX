using IntelligenceX.Tools.Email;
using Mailozaurr;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EmailSessionRequestsTests {
    [Fact]
    public void ApplySmtpRuntimeOptions_ShouldPreserveTimeoutRetryAndDryRun() {
        var smtp = new Smtp();
        var options = new SmtpAccountOptions {
            Server = "smtp.example.test",
            Port = 587,
            UserName = "service-account",
            Password = "secret",
            TimeoutMs = 4321,
            RetryCount = 3
        };

        EmailSessionRequests.ApplySmtpRuntimeOptions(smtp, options, dryRun: true);

        Assert.Equal(4321, smtp.Timeout);
        Assert.Equal(3, smtp.RetryCount);
        Assert.True(smtp.DryRun);
    }
}
