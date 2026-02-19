using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Email;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EmailSmtpProbeStrictModeTests {
    [Fact]
    public async Task SmtpProbeTool_WithoutSmtpConfig_ShouldReturnNotConfigured() {
        var tool = new EmailSmtpProbeTool(new EmailToolOptions());

        string json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("not_configured", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task SmtpSend_StrictModeWithoutProbeId_ShouldReturnProbeRequired() {
        var options = CreateStrictOptions();
        var tool = new EmailSmtpSendTool(options);

        string json = await tool.InvokeAsync(CreateSendArguments(send: true), CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("smtp_probe_required", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task SmtpSend_StrictModeWithExpiredProbe_ShouldReturnProbeExpired() {
        var probeStore = new InMemoryToolAuthenticationProbeStore();
        var options = CreateStrictOptions(probeStore: probeStore);
        var tool = new EmailSmtpSendTool(options);
        var smtpOptions = options.Smtp!;
        var probeId = "probe-expired";

        probeStore.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = probeId,
            ToolName = "email_smtp_probe",
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            TargetFingerprint = BuildFingerprint(smtpOptions),
            ProbedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-(options.SmtpProbeMaxAgeSeconds + 5)),
            IsSuccessful = true
        });

        string json = await tool.InvokeAsync(
            CreateSendArguments(send: true).Add(ToolAuthenticationArgumentNames.ProbeId, probeId),
            CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("smtp_probe_expired", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task SmtpSend_StrictModeWithMismatchedProbe_ShouldReturnProbeIncompatible() {
        var probeStore = new InMemoryToolAuthenticationProbeStore();
        var options = CreateStrictOptions(probeStore: probeStore);
        var tool = new EmailSmtpSendTool(options);
        var probeId = "probe-mismatch";

        probeStore.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = probeId,
            ToolName = "email_smtp_probe",
            AuthenticationContractId = ToolAuthenticationContract.DefaultContractId,
            TargetFingerprint = "different|fingerprint",
            ProbedAtUtc = DateTimeOffset.UtcNow,
            IsSuccessful = true
        });

        string json = await tool.InvokeAsync(
            CreateSendArguments(send: true).Add(ToolAuthenticationArgumentNames.ProbeId, probeId),
            CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("smtp_probe_incompatible", root.GetProperty("error_code").GetString());
    }

    private static EmailToolOptions CreateStrictOptions(IToolAuthenticationProbeStore? probeStore = null) {
        return new EmailToolOptions {
            RequireSuccessfulSmtpProbeForSend = true,
            SmtpProbeMaxAgeSeconds = 30,
            AuthenticationProbeStore = probeStore ?? new InMemoryToolAuthenticationProbeStore(),
            Smtp = new SmtpAccountOptions {
                Server = "smtp.example.test",
                Port = 587,
                UserName = "service-account",
                Password = "secret",
                SecureSocketOptions = "Auto",
                UseSsl = false,
                TimeoutMs = 1000,
                RetryCount = 0
            }
        };
    }

    private static JsonObject CreateSendArguments(bool send) {
        return new JsonObject()
            .Add("from", "sender@example.test")
            .Add("to", new JsonArray().Add("recipient@example.test"))
            .Add("subject", "hello")
            .Add("text_body", "body")
            .Add("send", send);
    }

    private static string BuildFingerprint(SmtpAccountOptions options) {
        static string Normalize(string? value) {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        return string.Join("|", new[] {
            Normalize(options.Server),
            options.Port.ToString(),
            Normalize(options.UserName),
            Normalize(options.SecureSocketOptions),
            options.UseSsl ? "use_ssl=1" : "use_ssl=0"
        });
    }
}
