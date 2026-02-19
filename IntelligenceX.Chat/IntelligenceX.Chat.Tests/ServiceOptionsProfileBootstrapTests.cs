using System;
using System.IO;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Covers profile bootstrap semantics for service startup argument parsing.
/// </summary>
public sealed class ServiceOptionsProfileBootstrapTests {
    [Fact]
    public void Parse_DefaultsToNoResponseShapingLimits() {
        var options = ServiceOptions.Parse(Array.Empty<string>(), out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(0, options.MaxTableRows);
        Assert.Equal(0, options.MaxSample);
        Assert.True(options.EnableOfficeImoPack);
    }

    [Fact]
    public void Parse_AppliesExplicitResponseShapingOverrides() {
        var options = ServiceOptions.Parse(new[] {
            "--max-table-rows", "25",
            "--max-sample", "12"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(25, options.MaxTableRows);
        Assert.Equal(12, options.MaxSample);
    }

    [Fact]
    public void Parse_AllowsMissingProfile_WhenSaveProfileMatches() {
        var dbPath = Path.Combine(Path.GetTempPath(), "ix-chat-service-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            var options = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default",
                "--save-profile", "default"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error));
            Assert.Equal("default", options.ProfileName);
            Assert.Equal("default", options.SaveProfileName);
        } finally {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void Parse_RejectsMissingProfile_WhenNoMatchingSaveProfile() {
        var dbPath = Path.Combine(Path.GetTempPath(), "ix-chat-service-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            _ = ServiceOptions.Parse(new[] {
                "--pipe", "test.pipe",
                "--state-db", dbPath,
                "--profile", "default"
            }, out var error);

            Assert.Equal("Profile not found: default", error);
        } finally {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void Parse_Allows_Disabling_And_Enabling_OfficeImoPack() {
        var disabled = ServiceOptions.Parse(new[] { "--disable-officeimo-pack" }, out var disabledError);
        Assert.True(string.IsNullOrWhiteSpace(disabledError));
        Assert.False(disabled.EnableOfficeImoPack);

        var enabled = ServiceOptions.Parse(new[] { "--disable-officeimo-pack", "--enable-officeimo-pack" }, out var enabledError);
        Assert.True(string.IsNullOrWhiteSpace(enabledError));
        Assert.True(enabled.EnableOfficeImoPack);
    }

    [Fact]
    public void Parse_Allows_Disabling_And_Enabling_PowerShellPack() {
        var enabled = ServiceOptions.Parse(new[] { "--enable-powershell-pack" }, out var enabledError);
        Assert.True(string.IsNullOrWhiteSpace(enabledError));
        Assert.True(enabled.EnablePowerShellPack);

        var disabled = ServiceOptions.Parse(new[] { "--enable-powershell-pack", "--disable-powershell-pack" }, out var disabledError);
        Assert.True(string.IsNullOrWhiteSpace(disabledError));
        Assert.False(disabled.EnablePowerShellPack);
    }

    [Theory]
    [InlineData("copilot-cli")]
    [InlineData("copilot")]
    [InlineData("github-copilot")]
    [InlineData("githubcopilot")]
    public void Parse_AcceptsCopilotTransportAliases(string value) {
        var options = ServiceOptions.Parse(new[] { "--openai-transport", value }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(OpenAITransportKind.CopilotCli, options.OpenAITransport);
    }

    [Fact]
    public void Parse_AppliesWriteAndAuthRuntimePolicyOptions() {
        var options = ServiceOptions.Parse(new[] {
            "--write-governance-mode", "yolo",
            "--no-require-write-governance-runtime",
            "--require-write-audit-sink",
            "--write-audit-sink-mode", "file",
            "--write-audit-sink-path", "C:/temp/ix-audit.jsonl",
            "--auth-runtime-preset", "strict",
            "--require-auth-runtime",
            "--run-as-profile-path", "C:/temp/runas-profiles.json",
            "--auth-profile-path", "C:/temp/auth-profiles.json"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(ToolWriteGovernanceMode.Yolo, options.WriteGovernanceMode);
        Assert.False(options.RequireWriteGovernanceRuntime);
        Assert.True(options.RequireWriteAuditSinkForWriteOperations);
        Assert.Equal(ToolWriteAuditSinkMode.FileAppendOnly, options.WriteAuditSinkMode);
        Assert.Equal("C:/temp/ix-audit.jsonl", options.WriteAuditSinkPath);
        Assert.Equal(ToolAuthenticationRuntimePreset.Strict, options.AuthenticationRuntimePreset);
        Assert.True(options.RequireAuthenticationRuntime);
        Assert.Equal("C:/temp/runas-profiles.json", options.RunAsProfilePath);
        Assert.Equal("C:/temp/auth-profiles.json", options.AuthenticationProfilePath);
    }

    [Fact]
    public void Parse_RejectsInvalidWriteGovernanceMode() {
        _ = ServiceOptions.Parse(new[] {
            "--write-governance-mode", "invalid_mode"
        }, out var error);

        Assert.Equal("--write-governance-mode must be one of: enforced, yolo.", error);
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }
}
