using System;
using System.IO;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolRuntimePolicyBootstrapTests {
    [Fact]
    public void CreateContext_StrictPreset_RequiresSmtpProbeValidation() {
        var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict
        });

        Assert.NotNull(context.AuthenticationProbeStore);
        Assert.True(context.RequireSuccessfulSmtpProbeForSend);
        Assert.Equal(600, context.SmtpProbeMaxAgeSeconds);
    }

    [Fact]
    public void CreateContext_FileAuditSink_CreatesAppendOnlySink() {
        var filePath = Path.Combine(Path.GetTempPath(), "ix-write-audit-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try {
            var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
                WriteAuditSinkMode = ToolWriteAuditSinkMode.FileAppendOnly,
                WriteAuditSinkPath = filePath
            });

            Assert.NotNull(context.WriteAuditSink);
            Assert.IsType<AppendOnlyJsonlToolWriteAuditSink>(context.WriteAuditSink);
        } finally {
            TryDelete(filePath);
        }
    }

    [Fact]
    public void ApplyToRegistry_YoloMode_DisablesStrictRuntimeByDefault() {
        var registry = new ToolRegistry();
        var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            WriteGovernanceMode = ToolWriteGovernanceMode.Yolo,
            RequireWriteGovernanceRuntime = true
        });

        var diagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, context);

        Assert.Equal(ToolWriteGovernanceMode.Yolo, registry.WriteGovernanceMode);
        Assert.True(registry.RequireWriteGovernanceRuntime);
        Assert.Null(registry.WriteGovernanceRuntime);
        Assert.False(diagnostics.WriteGovernanceRuntimeConfigured);
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best-effort cleanup.
        }
    }
}
