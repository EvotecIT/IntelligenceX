using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private static IReadOnlyList<IToolPack> BuildPacks(
        ReplOptions options,
        ToolRuntimePolicyContext runtimePolicy,
        Action<string>? onBootstrapWarning = null) {
        return ToolPackBootstrap.CreateDefaultReadOnlyPacks(BuildBootstrapOptions(options, runtimePolicy, onBootstrapWarning));
    }

    private static IReadOnlyList<string> GetPluginSearchPaths(ReplOptions options, ToolRuntimePolicyContext runtimePolicy) {
        return ToolPackBootstrap.GetPluginSearchPaths(BuildBootstrapOptions(options, runtimePolicy));
    }

    private static ToolPackBootstrapOptions BuildBootstrapOptions(
        ReplOptions options,
        ToolRuntimePolicyContext runtimePolicy,
        Action<string>? onBootstrapWarning = null) {
        return new ToolPackBootstrapOptions {
            AllowedRoots = options.AllowedRoots.ToArray(),
            AdDomainController = options.AdDomainController,
            AdDefaultSearchBaseDn = options.AdDefaultSearchBaseDn,
            AdMaxResults = options.AdMaxResults,
            EnablePowerShellPack = options.EnablePowerShellPack,
            PowerShellAllowWrite = options.PowerShellAllowWrite,
            EnableTestimoXPack = options.EnableTestimoXPack,
            EnableOfficeImoPack = options.EnableOfficeImoPack,
            EnableDefaultPluginPaths = options.EnableDefaultPluginPaths,
            PluginPaths = options.PluginPaths.ToArray(),
            AuthenticationProbeStore = runtimePolicy.AuthenticationProbeStore,
            RequireSuccessfulSmtpProbeForSend = runtimePolicy.RequireSuccessfulSmtpProbeForSend,
            SmtpProbeMaxAgeSeconds = runtimePolicy.SmtpProbeMaxAgeSeconds,
            RunAsProfilePath = runtimePolicy.Options.RunAsProfilePath,
            AuthenticationProfilePath = runtimePolicy.Options.AuthenticationProfilePath,
            OnBootstrapWarning = onBootstrapWarning
        };
    }

    private static ToolRuntimePolicyOptions BuildRuntimePolicyOptions(ReplOptions options) {
        return new ToolRuntimePolicyOptions {
            WriteGovernanceMode = options.WriteGovernanceMode,
            RequireWriteGovernanceRuntime = options.RequireWriteGovernanceRuntime,
            RequireWriteAuditSinkForWriteOperations = options.RequireWriteAuditSinkForWriteOperations,
            WriteAuditSinkMode = options.WriteAuditSinkMode,
            WriteAuditSinkPath = options.WriteAuditSinkPath,
            AuthenticationPreset = options.AuthenticationRuntimePreset,
            RequireAuthenticationRuntime = options.RequireAuthenticationRuntime,
            RunAsProfilePath = options.RunAsProfilePath,
            AuthenticationProfilePath = options.AuthenticationProfilePath
        };
    }

    private static void WritePolicyBanner(
        ReplOptions options,
        IReadOnlyList<IToolPack> packs,
        ToolRuntimePolicyContext runtimePolicyContext,
        ToolRuntimePolicyDiagnostics runtimePolicyDiagnostics,
        IReadOnlyList<string>? bootstrapWarnings = null) {
        var descriptors = ToolPackBootstrap.GetDescriptors(packs);
        var dangerousEnabled = descriptors.Any(static p => p.IsDangerous || p.Tier == ToolCapabilityTier.DangerousWrite);
        var pluginPaths = GetPluginSearchPaths(options, runtimePolicyContext);

        Console.WriteLine("Policy:");
        Console.WriteLine($"  Mode: {(dangerousEnabled ? "mixed (dangerous pack enabled)" : "read-only (no writes implied)")}");
        Console.WriteLine(
            $"  Write governance: mode={ToolRuntimePolicyBootstrap.FormatWriteGovernanceMode(runtimePolicyDiagnostics.WriteGovernanceMode)}, " +
            $"runtime_required={(runtimePolicyDiagnostics.RequireWriteGovernanceRuntime ? "on" : "off")}, " +
            $"runtime_configured={(runtimePolicyDiagnostics.WriteGovernanceRuntimeConfigured ? "yes" : "no")}");
        Console.WriteLine(
            $"  Write audit sink: mode={ToolRuntimePolicyBootstrap.FormatWriteAuditSinkMode(runtimePolicyDiagnostics.WriteAuditSinkMode)}, " +
            $"required={(runtimePolicyDiagnostics.RequireWriteAuditSinkForWriteOperations ? "on" : "off")}, " +
            $"configured={(runtimePolicyDiagnostics.WriteAuditSinkConfigured ? "yes" : "no")}, " +
            $"path={(string.IsNullOrWhiteSpace(runtimePolicyDiagnostics.WriteAuditSinkPath) ? "(none)" : runtimePolicyDiagnostics.WriteAuditSinkPath)}");
        Console.WriteLine(
            $"  Auth runtime: preset={ToolRuntimePolicyBootstrap.FormatAuthenticationRuntimePreset(runtimePolicyDiagnostics.AuthenticationPreset)}, " +
            $"required={(runtimePolicyDiagnostics.RequireAuthenticationRuntime ? "on" : "off")}, " +
            $"configured={(runtimePolicyDiagnostics.AuthenticationRuntimeConfigured ? "yes" : "no")}, " +
            $"smtp_probe_required={(runtimePolicyDiagnostics.RequireSuccessfulSmtpProbeForSend ? "on" : "off")}, " +
            $"smtp_probe_max_age_seconds={runtimePolicyDiagnostics.SmtpProbeMaxAgeSeconds}, " +
            $"run_as_profile_path={(string.IsNullOrWhiteSpace(runtimePolicyDiagnostics.RunAsProfilePath) ? "(none)" : runtimePolicyDiagnostics.RunAsProfilePath)}, " +
            $"auth_profile_path={(string.IsNullOrWhiteSpace(runtimePolicyDiagnostics.AuthenticationProfilePath) ? "(none)" : runtimePolicyDiagnostics.AuthenticationProfilePath)}");
        var maxTable = options.MaxTableRows <= 0 ? "(none)" : options.MaxTableRows.ToString();
        var maxSample = options.MaxSample <= 0 ? "(none)" : options.MaxSample.ToString();
        Console.WriteLine($"  Response shaping: max_table_rows={maxTable}, max_sample={maxSample}, redact={(options.Redact ? "on" : "off")}");
        Console.WriteLine($"  Allowed roots: {(options.AllowedRoots.Count == 0 ? "(none)" : string.Join("; ", options.AllowedRoots))}");
        Console.WriteLine($"  Plugin search paths: {(pluginPaths.Count == 0 ? "(none)" : string.Join("; ", pluginPaths))}");
        Console.WriteLine("  Packs:");

        foreach (var p in descriptors) {
            Console.WriteLine($"    - {p.Id} ({p.Tier})");
        }

        if (bootstrapWarnings is { Count: > 0 }) {
            Console.WriteLine("  Pack warnings:");
            foreach (var warning in bootstrapWarnings) {
                if (string.IsNullOrWhiteSpace(warning)) {
                    continue;
                }
                Console.WriteLine($"    - {warning.Trim()}");
            }
        }

        Console.WriteLine($"  Dangerous tools: {(dangerousEnabled ? "enabled (explicit opt-in)" : "disabled")}");
    }

    private static string? ApplyRuntimeShaping(string? instructions, ReplOptions options) {
        return ToolResponseShaping.AppendSessionResponseShapingInstructions(
            instructions,
            options.MaxTableRows,
            options.MaxSample,
            options.Redact);
    }

    private static string RedactText(string text) {
        return ToolResponseShaping.RedactEmailLikeTokens(text);
    }

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken ct, int seconds) {
        if (seconds <= 0) {
            return null;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    // Tool errors are returned as JSON strings to the model. Use the shared contract helper so
    // tool packs and hosts converge on the same envelope over time.
}
