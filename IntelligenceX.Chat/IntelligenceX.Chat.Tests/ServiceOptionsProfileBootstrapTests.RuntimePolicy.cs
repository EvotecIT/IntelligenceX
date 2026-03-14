using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Covers runtime write-governance and authentication policy parsing/persistence.
/// </summary>
public sealed partial class ServiceOptionsProfileBootstrapTests {
    [Fact]
    public void Parse_AppliesWriteAndAuthRuntimePolicyOptions() {
        var options = ServiceOptions.Parse(new[] {
            "--write-governance-mode", "yolo",
            "--no-require-write-governance-runtime",
            "--require-write-audit-sink",
            "--write-audit-sink-mode", "file",
            "--write-audit-sink-path", "C:/temp/ix-audit.jsonl",
            "--auth-runtime-preset", "strict",
            "--require-explicit-routing-metadata",
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
        Assert.True(options.RequireExplicitRoutingMetadata);
        Assert.True(options.RequireAuthenticationRuntime);
        Assert.Equal("C:/temp/runas-profiles.json", options.RunAsProfilePath);
        Assert.Equal("C:/temp/auth-profiles.json", options.AuthenticationProfilePath);
    }

    [Theory]
    [InlineData("bearer", OpenAICompatibleHttpAuthMode.Bearer)]
    [InlineData("token", OpenAICompatibleHttpAuthMode.Bearer)]
    [InlineData("api-key", OpenAICompatibleHttpAuthMode.Bearer)]
    [InlineData("basic", OpenAICompatibleHttpAuthMode.Basic)]
    [InlineData("none", OpenAICompatibleHttpAuthMode.None)]
    [InlineData("off", OpenAICompatibleHttpAuthMode.None)]
    public void Parse_AcceptsCompatibleHttpAuthModeAliases(string rawValue, OpenAICompatibleHttpAuthMode expected) {
        var options = ServiceOptions.Parse(new[] {
            "--openai-auth-mode", rawValue
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal(expected, options.OpenAIAuthMode);
    }

    [Fact]
    public void Parse_ClearBasicAuth_WinsOverProvidedBasicCredentials() {
        var options = ServiceOptions.Parse(new[] {
            "--openai-auth-mode", "basic",
            "--openai-basic-username", "user1",
            "--openai-basic-password", "secret1",
            "--openai-clear-basic-auth"
        }, out var error);

        Assert.NotNull(options);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Null(options.OpenAIBasicUsername);
        Assert.Null(options.OpenAIBasicPassword);
    }

    [Fact]
    public void Parse_ReadsBasicPasswordFromEnvironment_WhenCliValueMissing() {
        WithTemporaryEnvironmentVariable(ChatServiceEnvironmentVariables.OpenAIBasicPassword, "env-secret", () => {
            var options = ServiceOptions.Parse(new[] {
                "--openai-auth-mode", "basic",
                "--openai-basic-username", "user1"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error));
            Assert.Equal("user1", options.OpenAIBasicUsername);
            Assert.Equal("env-secret", options.OpenAIBasicPassword);
        });
    }

    [Fact]
    public void Parse_DoesNotReadBasicPasswordFromEnvironment_WhenBasicAuthClearFlagPresent() {
        WithTemporaryEnvironmentVariable(ChatServiceEnvironmentVariables.OpenAIBasicPassword, "env-secret", () => {
            var options = ServiceOptions.Parse(new[] {
                "--openai-auth-mode", "basic",
                "--openai-basic-username", "user1",
                "--openai-clear-basic-auth"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error));
            Assert.Null(options.OpenAIBasicUsername);
            Assert.Null(options.OpenAIBasicPassword);
        });
    }

    [Fact]
    public void Parse_CliBasicPasswordWinsOverEnvironmentValue() {
        WithTemporaryEnvironmentVariable(ChatServiceEnvironmentVariables.OpenAIBasicPassword, "env-secret", () => {
            var options = ServiceOptions.Parse(new[] {
                "--openai-auth-mode", "basic",
                "--openai-basic-username", "user1",
                "--openai-basic-password", "cli-secret"
            }, out var error);

            Assert.NotNull(options);
            Assert.True(string.IsNullOrWhiteSpace(error));
            Assert.Equal("cli-secret", options.OpenAIBasicPassword);
        });
    }

    [Fact]
    public void Parse_RejectsInvalidWriteGovernanceMode() {
        _ = ServiceOptions.Parse(new[] {
            "--write-governance-mode", "invalid_mode"
        }, out var error);

        Assert.Equal("--write-governance-mode must be one of: enforced, yolo.", error);
    }

    [Fact]
    public void Parse_ProfileRoundTrip_PersistsRuntimePolicyWithCanonicalValues() {
        var dbPath = TempPathTestHelper.CreateTempFilePath("ix-chat-service", ".db");
        try {
            var save = ServiceOptions.Parse(new[] {
                "--state-db", dbPath,
                "--profile", "runtime",
                "--save-profile", "runtime",
                "--disable-pack-id", "custom_plugin_pack",
                "--enable-pack-id", "powershell",
                "--no-default-built-in-tool-assemblies",
                "--built-in-tool-assembly", "IntelligenceX.Tools.System",
                "--built-in-tool-assembly", "IntelligenceX.Tools.EventLog",
                "--allow-mutating-parallel-tools",
                "--write-governance-mode", "yolo",
                "--no-require-write-governance-runtime",
                "--require-write-audit-sink",
                "--write-audit-sink-mode", "jsonl",
                "--write-audit-sink-path", "C:/temp/ix-audit.jsonl",
                "--auth-runtime-preset", "strict",
                "--require-explicit-routing-metadata",
                "--require-auth-runtime",
                "--run-as-profile-path", "C:/temp/runas-profiles.json",
                "--auth-profile-path", "C:/temp/auth-profiles.json"
            }, out var saveError);

            Assert.NotNull(save);
            Assert.True(string.IsNullOrWhiteSpace(saveError));

            var loaded = ServiceOptions.Parse(new[] {
                "--state-db", dbPath,
                "--profile", "runtime"
            }, out var loadError);

            Assert.NotNull(loaded);
            Assert.True(string.IsNullOrWhiteSpace(loadError));
            Assert.True(loaded.AllowMutatingParallelToolCalls);
            Assert.Equal(ToolWriteGovernanceMode.Yolo, loaded.WriteGovernanceMode);
            Assert.False(loaded.RequireWriteGovernanceRuntime);
            Assert.True(loaded.RequireWriteAuditSinkForWriteOperations);
            Assert.Equal(ToolWriteAuditSinkMode.FileAppendOnly, loaded.WriteAuditSinkMode);
            Assert.Equal("C:/temp/ix-audit.jsonl", loaded.WriteAuditSinkPath);
            Assert.Equal(ToolAuthenticationRuntimePreset.Strict, loaded.AuthenticationRuntimePreset);
            Assert.True(loaded.RequireExplicitRoutingMetadata);
            Assert.True(loaded.RequireAuthenticationRuntime);
            Assert.Equal("C:/temp/runas-profiles.json", loaded.RunAsProfilePath);
            Assert.Equal("C:/temp/auth-profiles.json", loaded.AuthenticationProfilePath);
            Assert.DoesNotContain("powershell", loaded.DisabledPackIds);
            Assert.Contains("custom_plugin_pack", loaded.DisabledPackIds);
            Assert.Contains("powershell", loaded.EnabledPackIds);
            Assert.False(loaded.UseDefaultBuiltInToolAssemblyNames);
            Assert.Contains("IntelligenceX.Tools.System", loaded.BuiltInToolAssemblyNames);
            Assert.Contains("IntelligenceX.Tools.EventLog", loaded.BuiltInToolAssemblyNames);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }
}
