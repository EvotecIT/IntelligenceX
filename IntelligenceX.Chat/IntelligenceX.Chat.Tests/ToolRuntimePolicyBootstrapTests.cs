using System;
using System.IO;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolRuntimePolicyBootstrapTests {
    [Fact]
    public void CreateOptions_MapsRuntimePolicySettingsContract() {
        var options = ToolRuntimePolicyBootstrap.CreateOptions(new TestRuntimePolicySettings {
            WriteGovernanceMode = ToolWriteGovernanceMode.Yolo,
            RequireWriteGovernanceRuntime = false,
            RequireWriteAuditSinkForWriteOperations = true,
            WriteAuditSinkMode = ToolWriteAuditSinkMode.SqliteAppendOnly,
            WriteAuditSinkPath = " C:/temp/write-audit.db ",
            AuthenticationRuntimePreset = ToolAuthenticationRuntimePreset.Strict,
            RequireExplicitRoutingMetadata = true,
            RequireAuthenticationRuntime = true,
            RunAsProfilePath = " C:/temp/runas.json ",
            AuthenticationProfilePath = " C:/temp/auth.json "
        });

        Assert.Equal(ToolWriteGovernanceMode.Yolo, options.WriteGovernanceMode);
        Assert.False(options.RequireWriteGovernanceRuntime);
        Assert.True(options.RequireWriteAuditSinkForWriteOperations);
        Assert.Equal(ToolWriteAuditSinkMode.SqliteAppendOnly, options.WriteAuditSinkMode);
        Assert.Equal(" C:/temp/write-audit.db ", options.WriteAuditSinkPath);
        Assert.Equal(ToolAuthenticationRuntimePreset.Strict, options.AuthenticationPreset);
        Assert.True(options.RequireExplicitRoutingMetadata);
        Assert.True(options.RequireAuthenticationRuntime);
        Assert.Equal(" C:/temp/runas.json ", options.RunAsProfilePath);
        Assert.Equal(" C:/temp/auth.json ", options.AuthenticationProfilePath);
    }

    [Fact]
    public void CreateContext_StrictPreset_RequiresSmtpProbeValidation() {
        var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict
        });

        Assert.NotNull(context.AuthenticationProbeStore);
        Assert.True(context.Options.RequireAuthenticationRuntime);
        Assert.True(context.RequireSuccessfulSmtpProbeForSend);
        Assert.Equal(600, context.SmtpProbeMaxAgeSeconds);
    }

    [Fact]
    public void CreateContext_FileAuditSink_CreatesAppendOnlySink() {
        var filePath = TempPathTestHelper.CreateTempFilePath("ix-write-audit", ".jsonl");
        try {
            var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
                WriteAuditSinkMode = ToolWriteAuditSinkMode.FileAppendOnly,
                WriteAuditSinkPath = filePath
            });

            Assert.NotNull(context.WriteAuditSink);
            Assert.IsType<AppendOnlyJsonlToolWriteAuditSink>(context.WriteAuditSink);
        } finally {
            TempPathTestHelper.TryDeleteFile(filePath);
        }
    }

    [Fact]
    public void ApplyToRegistry_YoloMode_PreservesExplicitRuntimeRequirement() {
        var registry = new ToolRegistry();
        var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            WriteGovernanceMode = ToolWriteGovernanceMode.Yolo,
            RequireWriteGovernanceRuntime = true,
            RequireExplicitRoutingMetadata = true
        });

        var diagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, context);

        Assert.Equal(ToolWriteGovernanceMode.Yolo, registry.WriteGovernanceMode);
        Assert.True(registry.RequireWriteGovernanceRuntime);
        Assert.True(registry.RequireExplicitRoutingMetadata);
        Assert.NotNull(registry.WriteGovernanceRuntime);
        Assert.True(diagnostics.RequireWriteGovernanceRuntime);
        Assert.True(diagnostics.RequireExplicitRoutingMetadata);
        Assert.True(diagnostics.WriteGovernanceRuntimeConfigured);
    }

    [Fact]
    public void ApplyToRegistry_YoloMode_StillAllowsExplicitRuntimeBypassWhenDisabled() {
        var registry = new ToolRegistry();
        var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            WriteGovernanceMode = ToolWriteGovernanceMode.Yolo,
            RequireWriteGovernanceRuntime = false
        });

        var diagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, context);

        Assert.Equal(ToolWriteGovernanceMode.Yolo, registry.WriteGovernanceMode);
        Assert.False(registry.RequireWriteGovernanceRuntime);
        Assert.Null(registry.WriteGovernanceRuntime);
        Assert.False(diagnostics.RequireWriteGovernanceRuntime);
        Assert.False(diagnostics.WriteGovernanceRuntimeConfigured);
    }

    [Fact]
    public void ResolveOptions_StrictPreset_ElevatesRequireAuthenticationRuntime() {
        var resolved = ToolRuntimePolicyBootstrap.ResolveOptions(new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
            RequireAuthenticationRuntime = false
        });

        Assert.True(resolved.Options.RequireAuthenticationRuntime);
        Assert.True(resolved.RequireSuccessfulSmtpProbeForSend);
        Assert.Equal(600, resolved.SmtpProbeMaxAgeSeconds);
    }

    [Fact]
    public void ResolveOptions_YoloMode_PreservesExplicitWriteRuntimeRequirement() {
        var resolved = ToolRuntimePolicyBootstrap.ResolveOptions(new ToolRuntimePolicyOptions {
            WriteGovernanceMode = ToolWriteGovernanceMode.Yolo,
            RequireWriteGovernanceRuntime = true
        });

        Assert.Equal(ToolWriteGovernanceMode.Yolo, resolved.Options.WriteGovernanceMode);
        Assert.True(resolved.Options.RequireWriteGovernanceRuntime);
    }

    [Fact]
    public void BuildDiagnostics_IncludesRunAsAndAuthProfilePaths() {
        var context = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            RunAsProfilePath = " .\\profiles\\run-as.json ",
            AuthenticationProfilePath = " .\\profiles\\auth.json "
        });

        var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(context);

        Assert.Equal(context.Options.RunAsProfilePath, diagnostics.RunAsProfilePath);
        Assert.Equal(context.Options.AuthenticationProfilePath, diagnostics.AuthenticationProfilePath);
    }

    [Fact]
    public void WriteRuntimePolicyCliHelp_WritesCanonicalPolicyFlags() {
        var lines = new System.Collections.Generic.List<string>();

        ToolRuntimePolicyBootstrap.WriteRuntimePolicyCliHelp(lines.Add);

        Assert.Contains(lines, static line => line.StartsWith("  --write-governance-mode", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("  --write-audit-sink-mode", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("  --auth-runtime-preset", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("  --require-explicit-routing-metadata", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("  --allow-inferred-routing-metadata", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("  --run-as-profile-path", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.StartsWith("  --auth-profile-path", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseErrorConstants_ExposeCanonicalMessages() {
        Assert.Equal("--write-governance-mode must be one of: enforced, yolo.", ToolRuntimePolicyBootstrap.WriteGovernanceModeParseError);
        Assert.Equal("--write-audit-sink-mode must be one of: none, file, sqlite.", ToolRuntimePolicyBootstrap.WriteAuditSinkModeParseError);
        Assert.Equal("--auth-runtime-preset must be one of: default, strict, lab.", ToolRuntimePolicyBootstrap.AuthenticationRuntimePresetParseError);
    }

    [Fact]
    public void TryApplyRuntimePolicyCliArgument_AppliesWriteGovernanceMode() {
        var writeMode = ToolWriteGovernanceMode.Enforced;
        var requireWriteRuntime = true;
        var requireWriteAuditSink = false;
        var writeAuditSinkMode = ToolWriteAuditSinkMode.None;
        string? writeAuditSinkPath = null;
        var authPreset = ToolAuthenticationRuntimePreset.Default;
        var requireExplicitRoutingMetadata = false;
        var requireAuthRuntime = false;
        string? runAsProfilePath = null;
        string? authProfilePath = null;

        var ok = ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
            argument: "--write-governance-mode",
            consumeValue: static () => (true, "yolo", null),
            setWriteGovernanceMode: mode => writeMode = mode,
            setRequireWriteGovernanceRuntime: required => requireWriteRuntime = required,
            setRequireWriteAuditSinkForWriteOperations: required => requireWriteAuditSink = required,
            setWriteAuditSinkMode: mode => writeAuditSinkMode = mode,
            setWriteAuditSinkPath: path => writeAuditSinkPath = path,
            setAuthenticationRuntimePreset: preset => authPreset = preset,
            setRequireExplicitRoutingMetadata: required => requireExplicitRoutingMetadata = required,
            setRequireAuthenticationRuntime: required => requireAuthRuntime = required,
            setRunAsProfilePath: path => runAsProfilePath = path,
            setAuthenticationProfilePath: path => authProfilePath = path,
            handled: out var handled,
            error: out var error);

        Assert.True(ok);
        Assert.True(handled);
        Assert.Null(error);
        Assert.Equal(ToolWriteGovernanceMode.Yolo, writeMode);
        Assert.True(requireWriteRuntime);
        Assert.False(requireWriteAuditSink);
        Assert.Equal(ToolWriteAuditSinkMode.None, writeAuditSinkMode);
        Assert.Null(writeAuditSinkPath);
        Assert.Equal(ToolAuthenticationRuntimePreset.Default, authPreset);
        Assert.False(requireExplicitRoutingMetadata);
        Assert.False(requireAuthRuntime);
        Assert.Null(runAsProfilePath);
        Assert.Null(authProfilePath);
    }

    [Fact]
    public void TryApplyRuntimePolicyCliArgument_TogglesExplicitRoutingMetadataRequirement() {
        var requireExplicitRoutingMetadata = false;

        var requireOk = ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
            argument: "--require-explicit-routing-metadata",
            consumeValue: static () => throw new InvalidOperationException("consumeValue should not be called."),
            setWriteGovernanceMode: static _ => { },
            setRequireWriteGovernanceRuntime: static _ => { },
            setRequireWriteAuditSinkForWriteOperations: static _ => { },
            setWriteAuditSinkMode: static _ => { },
            setWriteAuditSinkPath: static _ => { },
            setAuthenticationRuntimePreset: static _ => { },
            setRequireExplicitRoutingMetadata: required => requireExplicitRoutingMetadata = required,
            setRequireAuthenticationRuntime: static _ => { },
            setRunAsProfilePath: static _ => { },
            setAuthenticationProfilePath: static _ => { },
            handled: out var requireHandled,
            error: out var requireError);

        Assert.True(requireOk);
        Assert.True(requireHandled);
        Assert.Null(requireError);
        Assert.True(requireExplicitRoutingMetadata);

        var allowOk = ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
            argument: "--allow-inferred-routing-metadata",
            consumeValue: static () => throw new InvalidOperationException("consumeValue should not be called."),
            setWriteGovernanceMode: static _ => { },
            setRequireWriteGovernanceRuntime: static _ => { },
            setRequireWriteAuditSinkForWriteOperations: static _ => { },
            setWriteAuditSinkMode: static _ => { },
            setWriteAuditSinkPath: static _ => { },
            setAuthenticationRuntimePreset: static _ => { },
            setRequireExplicitRoutingMetadata: required => requireExplicitRoutingMetadata = required,
            setRequireAuthenticationRuntime: static _ => { },
            setRunAsProfilePath: static _ => { },
            setAuthenticationProfilePath: static _ => { },
            handled: out var allowHandled,
            error: out var allowError);

        Assert.True(allowOk);
        Assert.True(allowHandled);
        Assert.Null(allowError);
        Assert.False(requireExplicitRoutingMetadata);
    }

    [Fact]
    public void TryApplyRuntimePolicyCliArgument_InvalidAuthPreset_ReturnsCanonicalError() {
        var ok = ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
            argument: "--auth-runtime-preset",
            consumeValue: static () => (true, "invalid", null),
            setWriteGovernanceMode: static _ => { },
            setRequireWriteGovernanceRuntime: static _ => { },
            setRequireWriteAuditSinkForWriteOperations: static _ => { },
            setWriteAuditSinkMode: static _ => { },
            setWriteAuditSinkPath: static _ => { },
            setAuthenticationRuntimePreset: static _ => { },
            setRequireExplicitRoutingMetadata: static _ => { },
            setRequireAuthenticationRuntime: static _ => { },
            setRunAsProfilePath: static _ => { },
            setAuthenticationProfilePath: static _ => { },
            handled: out var handled,
            error: out var error);

        Assert.False(ok);
        Assert.True(handled);
        Assert.Equal(ToolRuntimePolicyBootstrap.AuthenticationRuntimePresetParseError, error);
    }

    [Fact]
    public void TryApplyRuntimePolicyCliArgument_UnknownArgument_IsNotHandled() {
        var ok = ToolRuntimePolicyBootstrap.TryApplyRuntimePolicyCliArgument(
            argument: "--not-runtime-policy",
            consumeValue: static () => throw new InvalidOperationException("consumeValue should not be called."),
            setWriteGovernanceMode: static _ => { },
            setRequireWriteGovernanceRuntime: static _ => { },
            setRequireWriteAuditSinkForWriteOperations: static _ => { },
            setWriteAuditSinkMode: static _ => { },
            setWriteAuditSinkPath: static _ => { },
            setAuthenticationRuntimePreset: static _ => { },
            setRequireExplicitRoutingMetadata: static _ => { },
            setRequireAuthenticationRuntime: static _ => { },
            setRunAsProfilePath: static _ => { },
            setAuthenticationProfilePath: static _ => { },
            handled: out var handled,
            error: out var error);

        Assert.True(ok);
        Assert.False(handled);
        Assert.Null(error);
    }

    [Fact]
    public void ApplyProfileRuntimePolicy_ValidTokens_UpdateAllRuntimePolicyFields() {
        var writeMode = ToolWriteGovernanceMode.Enforced;
        var requireWriteRuntime = false;
        var requireWriteAuditSink = false;
        var writeAuditSinkMode = ToolWriteAuditSinkMode.None;
        string? writeAuditSinkPath = null;
        var authPreset = ToolAuthenticationRuntimePreset.Default;
        var requireExplicitRoutingMetadata = false;
        var requireAuthRuntime = false;
        string? runAsProfilePath = null;
        string? authProfilePath = null;

        ToolRuntimePolicyBootstrap.ApplyProfileRuntimePolicy(
            writeGovernanceMode: "yolo",
            requireWriteGovernanceRuntime: true,
            requireWriteAuditSinkForWriteOperations: true,
            writeAuditSinkMode: "sqlite",
            writeAuditSinkPath: "C:/temp/write-audit.db",
            authenticationRuntimePreset: "strict",
            requireExplicitRoutingMetadata: true,
            requireAuthenticationRuntime: true,
            runAsProfilePath: "C:/profiles/runas.json",
            authenticationProfilePath: "C:/profiles/auth.json",
            setWriteGovernanceMode: mode => writeMode = mode,
            setRequireWriteGovernanceRuntime: required => requireWriteRuntime = required,
            setRequireWriteAuditSinkForWriteOperations: required => requireWriteAuditSink = required,
            setWriteAuditSinkMode: mode => writeAuditSinkMode = mode,
            setWriteAuditSinkPath: path => writeAuditSinkPath = path,
            setAuthenticationRuntimePreset: preset => authPreset = preset,
            setRequireExplicitRoutingMetadata: required => requireExplicitRoutingMetadata = required,
            setRequireAuthenticationRuntime: required => requireAuthRuntime = required,
            setRunAsProfilePath: path => runAsProfilePath = path,
            setAuthenticationProfilePath: path => authProfilePath = path);

        Assert.Equal(ToolWriteGovernanceMode.Yolo, writeMode);
        Assert.True(requireWriteRuntime);
        Assert.True(requireWriteAuditSink);
        Assert.Equal(ToolWriteAuditSinkMode.SqliteAppendOnly, writeAuditSinkMode);
        Assert.Equal("C:/temp/write-audit.db", writeAuditSinkPath);
        Assert.Equal(ToolAuthenticationRuntimePreset.Strict, authPreset);
        Assert.True(requireExplicitRoutingMetadata);
        Assert.True(requireAuthRuntime);
        Assert.Equal("C:/profiles/runas.json", runAsProfilePath);
        Assert.Equal("C:/profiles/auth.json", authProfilePath);
    }

    [Fact]
    public void ApplyProfileRuntimePolicy_InvalidTokens_KeepEnumModesButApplyFlagsAndPaths() {
        var writeMode = ToolWriteGovernanceMode.Yolo;
        var requireWriteRuntime = false;
        var requireWriteAuditSink = true;
        var writeAuditSinkMode = ToolWriteAuditSinkMode.FileAppendOnly;
        string? writeAuditSinkPath = "before";
        var authPreset = ToolAuthenticationRuntimePreset.Strict;
        var requireExplicitRoutingMetadata = true;
        var requireAuthRuntime = true;
        string? runAsProfilePath = "before-runas";
        string? authProfilePath = "before-auth";

        ToolRuntimePolicyBootstrap.ApplyProfileRuntimePolicy(
            writeGovernanceMode: "invalid",
            requireWriteGovernanceRuntime: true,
            requireWriteAuditSinkForWriteOperations: false,
            writeAuditSinkMode: "invalid",
            writeAuditSinkPath: "after",
            authenticationRuntimePreset: "invalid",
            requireExplicitRoutingMetadata: false,
            requireAuthenticationRuntime: false,
            runAsProfilePath: "after-runas",
            authenticationProfilePath: "after-auth",
            setWriteGovernanceMode: mode => writeMode = mode,
            setRequireWriteGovernanceRuntime: required => requireWriteRuntime = required,
            setRequireWriteAuditSinkForWriteOperations: required => requireWriteAuditSink = required,
            setWriteAuditSinkMode: mode => writeAuditSinkMode = mode,
            setWriteAuditSinkPath: path => writeAuditSinkPath = path,
            setAuthenticationRuntimePreset: preset => authPreset = preset,
            setRequireExplicitRoutingMetadata: required => requireExplicitRoutingMetadata = required,
            setRequireAuthenticationRuntime: required => requireAuthRuntime = required,
            setRunAsProfilePath: path => runAsProfilePath = path,
            setAuthenticationProfilePath: path => authProfilePath = path);

        Assert.Equal(ToolWriteGovernanceMode.Yolo, writeMode);
        Assert.True(requireWriteRuntime);
        Assert.False(requireWriteAuditSink);
        Assert.Equal(ToolWriteAuditSinkMode.FileAppendOnly, writeAuditSinkMode);
        Assert.Equal("after", writeAuditSinkPath);
        Assert.Equal(ToolAuthenticationRuntimePreset.Strict, authPreset);
        Assert.False(requireExplicitRoutingMetadata);
        Assert.False(requireAuthRuntime);
        Assert.Equal("after-runas", runAsProfilePath);
        Assert.Equal("after-auth", authProfilePath);
    }

    private sealed class TestRuntimePolicySettings : IToolRuntimePolicySettings {
        public ToolWriteGovernanceMode WriteGovernanceMode { get; init; } = ToolWriteGovernanceMode.Enforced;
        public bool RequireWriteGovernanceRuntime { get; init; } = true;
        public bool RequireWriteAuditSinkForWriteOperations { get; init; }
        public ToolWriteAuditSinkMode WriteAuditSinkMode { get; init; }
        public string? WriteAuditSinkPath { get; init; }
        public ToolAuthenticationRuntimePreset AuthenticationRuntimePreset { get; init; } = ToolAuthenticationRuntimePreset.Default;
        public bool RequireExplicitRoutingMetadata { get; init; }
        public bool RequireAuthenticationRuntime { get; init; }
        public string? RunAsProfilePath { get; init; }
        public string? AuthenticationProfilePath { get; init; }
    }
}
