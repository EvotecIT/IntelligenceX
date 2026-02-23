using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class SystemToolBaseHelperTests {
    [Fact]
    public void ResolveBoundedOptionLimit_ShouldClampToMinAndCapToOptionsMax() {
        var tool = new HarnessTool(maxResults: 50);

        Assert.Equal(50, tool.ResolveLimit("max_entries", arguments: null));
        Assert.Equal(1, tool.ResolveLimit("max_entries", new JsonObject().Add("max_entries", 0)));
        Assert.Equal(1, tool.ResolveLimit("max_entries", new JsonObject().Add("max_entries", -5)));
        Assert.Equal(50, tool.ResolveLimit("max_entries", new JsonObject().Add("max_entries", 500)));
        Assert.Equal(12, tool.ResolveLimit("max_entries", new JsonObject().Add("max_entries", 12)));
    }

    [Fact]
    public void ResolveMaxResults_ShouldUseSharedOptionBoundedBehavior() {
        var tool = new HarnessTool(maxResults: 40);

        Assert.Equal(40, tool.ResolveMax(arguments: null));
        Assert.Equal(1, tool.ResolveMax(new JsonObject().Add("max_results", 0)));
        Assert.Equal(40, tool.ResolveMax(new JsonObject().Add("max_results", 999)));
        Assert.Equal(22, tool.ResolveMax(new JsonObject().Add("max_results", 22)));
    }

    [Fact]
    public void AddMaxResultsMeta_ShouldPopulateMetaField() {
        var meta = new JsonObject();
        HarnessTool.AddMax(meta, 77);

        Assert.Equal(77, meta.GetInt64("max_results"));
    }

    [Fact]
    public void ResolveTargetComputerName_ShouldFallbackToLocalMachineOnlyWhenMissing() {
        Assert.Equal(Environment.MachineName, HarnessTool.ResolveTarget(computerName: null));
        Assert.Equal(Environment.MachineName, HarnessTool.ResolveTarget("   "));
        Assert.Equal(".", HarnessTool.ResolveTarget("."));
        Assert.Equal("server01", HarnessTool.ResolveTarget("server01"));
    }

    [Fact]
    public void IsLocalTarget_ShouldRecognizeDotAndMachineName() {
        Assert.True(HarnessTool.IsLocal(computerName: null, target: Environment.MachineName));
        Assert.True(HarnessTool.IsLocal(computerName: ".", target: "."));
        Assert.True(HarnessTool.IsLocal(computerName: "localhost", target: Environment.MachineName));
        Assert.False(HarnessTool.IsLocal(computerName: "server01", target: "server01"));
    }

    [Fact]
    public void AddComputerAndPendingMeta_ShouldPopulateSharedFields() {
        var meta = new JsonObject();

        HarnessTool.AddComputer(meta, "server01");
        HarnessTool.AddPending(meta, includePendingLocal: true, pendingIncluded: false);

        Assert.Equal("server01", meta.GetString("computer_name"));
        Assert.True(meta.GetBoolean("include_pending_local"));
        Assert.False(meta.GetBoolean("pending_included"));
    }

    [Fact]
    public void BuildFactsMeta_ShouldIncludeComputerAndCustomFields() {
        var meta = HarnessTool.BuildMeta(
            count: 5,
            truncated: false,
            target: "server01",
            mutate: json => json.Add("include_policy", true));

        Assert.Equal(5, meta.GetInt64("count"));
        Assert.False(meta.GetBoolean("truncated"));
        Assert.Equal("server01", meta.GetString("computer_name"));
        Assert.True(meta.GetBoolean("include_policy"));
    }

    [Fact]
    public void AddReadOnlyPostureChainingMeta_ShouldEmitNextActionsAndDiscoveryStatus() {
        var meta = new JsonObject();

        HarnessTool.AddReadOnlyChaining(
            meta: meta,
            currentTool: "system_updates_installed",
            targetComputer: "server01",
            isRemoteScope: true,
            scanned: 25,
            truncated: false);

        var nextActions = meta.GetArray("next_actions");
        Assert.NotNull(nextActions);
        Assert.True(nextActions!.Count >= 2);

        var hasPatchCompliance = false;
        var hasSecurityOptions = false;
        foreach (var value in nextActions) {
            var action = value.AsObject();
            var tool = action?.GetString("tool");
            if (string.Equals(tool, "system_patch_compliance", StringComparison.OrdinalIgnoreCase)) {
                hasPatchCompliance = true;
            } else if (string.Equals(tool, "system_security_options", StringComparison.OrdinalIgnoreCase)) {
                hasSecurityOptions = true;
            }
        }

        Assert.True(hasPatchCompliance);
        Assert.True(hasSecurityOptions);

        var discovery = meta.GetObject("discovery_status");
        Assert.NotNull(discovery);
        Assert.Equal("remote", discovery!.GetString("scope"));
        Assert.Equal("server01", discovery.GetString("computer_name"));
        Assert.Equal("system_updates_installed", discovery.GetString("current_tool"));
    }

    [Fact]
    public void ValidateWindowsSupport_ShouldMatchCurrentHostPlatform() {
        var response = HarnessTool.ValidateWindows("system_test_tool");
        if (OperatingSystem.IsWindows()) {
            Assert.Null(response);
            return;
        }

        Assert.NotNull(response);
        using var doc = JsonDocument.Parse(response!);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("not_supported", root.GetProperty("error_code").GetString());
        Assert.Equal("system_test_tool is available only on Windows hosts.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ResolveTimeoutMs_ShouldClampUsingSharedBounds() {
        Assert.Equal(10_000, HarnessTool.ResolveTimeout(arguments: null));
        Assert.Equal(200, HarnessTool.ResolveTimeout(new JsonObject().Add("timeout_ms", 1)));
        Assert.Equal(120_000, HarnessTool.ResolveTimeout(new JsonObject().Add("timeout_ms", 999_999)));
        Assert.Equal(8_000, HarnessTool.ResolveTimeout(arguments: null, defaultValue: 8_000));
    }

    private sealed class HarnessTool : SystemToolBase {
        private static readonly ToolDefinition DefinitionValue = new(
            "system_test_harness",
            "System helper harness.",
            ToolSchema.Object().NoAdditionalProperties());

        public HarnessTool(int maxResults) : base(new SystemToolOptions { MaxResults = maxResults }) { }

        public override ToolDefinition Definition => DefinitionValue;

        public int ResolveLimit(string argumentName, JsonObject? arguments) {
            return ResolveBoundedOptionLimit(arguments, argumentName);
        }

        public int ResolveMax(JsonObject? arguments) {
            return ResolveMaxResults(arguments);
        }

        public static void AddMax(JsonObject meta, int maxResults) {
            AddMaxResultsMeta(meta, maxResults);
        }

        public static string ResolveTarget(string? computerName) {
            return ResolveTargetComputerName(computerName);
        }

        public static bool IsLocal(string? computerName, string target) {
            return IsLocalTarget(computerName, target);
        }

        public static void AddComputer(JsonObject meta, string target) {
            AddComputerNameMeta(meta, target);
        }

        public static void AddPending(JsonObject meta, bool includePendingLocal, bool pendingIncluded) {
            AddPendingLocalMeta(meta, includePendingLocal, pendingIncluded);
        }

        public static JsonObject BuildMeta(int count, bool truncated, string target, Action<JsonObject>? mutate = null) {
            return BuildFactsMeta(count, truncated, target, mutate);
        }

        public static void AddReadOnlyChaining(
            JsonObject meta,
            string currentTool,
            string targetComputer,
            bool isRemoteScope,
            int scanned,
            bool truncated) {
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: currentTool,
                targetComputer: targetComputer,
                isRemoteScope: isRemoteScope,
                scanned: scanned,
                truncated: truncated);
        }

        public static string? ValidateWindows(string toolName) {
            return ValidateWindowsSupport(toolName);
        }

        public static int ResolveTimeout(JsonObject? arguments, int defaultValue = 10_000) {
            return ResolveTimeoutMs(arguments, defaultValue: defaultValue);
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }
    }
}
