using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Config;
using ADPlayground.Monitoring.Diagnostics;
using ADPlayground.Monitoring.History;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Reachability;
using ADPlayground.Monitoring.Reporting;
using IntelligenceX.Json;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class TestimoXCatalogToolsTests {
    [Fact]
    public async Task TestimoXReportJobHistoryTool_ShouldReturnMonitoringReportJobs() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXReportJobHistoryTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("statuses", new JsonArray().Add("success"))
            .Add("page_size", 10);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("returned_count").GetInt32());
        var firstJob = root.GetProperty("jobs")
            .EnumerateArray()
            .First();
        Assert.Equal("dashboard-auto", firstJob.GetProperty("job_key").GetString());
        Assert.Equal("Success", firstJob.GetProperty("status").GetString());
        Assert.Equal(42, firstJob.GetProperty("history_entries").GetInt32());
    }

    [Fact]
    public async Task TestimoXHistoryQueryTool_ShouldReturnAvailabilityRollups() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXHistoryQueryTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("bucket_kind", "Hour")
            .Add("start_utc", "2026-03-02T10:00:00Z")
            .Add("end_utc", "2026-03-02T12:00:00Z")
            .Add("root_probe_names", new JsonArray().Add("ldap-health"))
            .Add("page_size", 10);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("returned_count").GetInt32() >= 1);
        var firstRow = root.GetProperty("rows")
            .EnumerateArray()
            .First();
        Assert.Equal("ldap-health", firstRow.GetProperty("probe_name").GetString());
        Assert.Equal(1, firstRow.GetProperty("up_count").GetInt32());
        Assert.Equal(1, firstRow.GetProperty("down_count").GetInt32());
    }

    [Fact]
    public async Task TestimoXMaintenanceWindowHistoryTool_ShouldReturnResolvedMaintenanceWindows() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXMaintenanceWindowHistoryTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("start_utc", "2026-03-02T10:00:00Z")
            .Add("end_utc", "2026-03-02T12:00:00Z")
            .Add("name_contains", "Patch")
            .Add("page_size", 10);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("returned_count").GetInt32());
        var firstRow = root.GetProperty("rows")
            .EnumerateArray()
            .First();
        Assert.Equal("Patch Tuesday", firstRow.GetProperty("name").GetString());
        Assert.Equal("Directory", firstRow.GetProperty("probe_type").GetString());
        Assert.True(firstRow.GetProperty("suppress_reporting").GetBoolean());
        Assert.True(firstRow.GetProperty("pause_probes").GetBoolean());
    }

    [Fact]
    public async Task TestimoXProbeIndexStatusTool_ShouldReturnLatestProbeIndexRows() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXProbeIndexStatusTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("probe_name_contains", "ldap")
            .Add("statuses", new JsonArray().Add("Down"))
            .Add("page_size", 10);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("returned_count").GetInt32());
        var firstRow = root.GetProperty("rows")
            .EnumerateArray()
            .First();
        Assert.Equal("ldap-health", firstRow.GetProperty("probe_name").GetString());
        Assert.Equal("Down", firstRow.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TestimoXMonitoringDiagnosticsGetTool_ShouldReturnCompactSnapshotAndSlowProbes() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXMonitoringDiagnosticsGetTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("include_slow_probes", true)
            .Add("max_slow_probes", 2);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var snapshot = root.GetProperty("snapshot");
        Assert.Equal(12, snapshot.GetProperty("notification_sent").GetInt64());
        Assert.Equal("Ok", snapshot.GetProperty("sqlite_last_check_status").GetString());
        Assert.True(snapshot.GetProperty("slow_probes_included").GetBoolean());
        Assert.Equal(2, root.GetProperty("slow_probes").GetArrayLength());
    }

    [Fact]
    public async Task TestimoXReportDataSnapshotGetTool_ShouldReturnPreviewByDefault() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXReportDataSnapshotGetTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("report_key", "dashboard-auto");

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var snapshot = root.GetProperty("snapshot");
        Assert.Equal("dashboard-auto", snapshot.GetProperty("report_key").GetString());
        Assert.False(snapshot.GetProperty("payload_included").GetBoolean());
        Assert.Contains("\"status\":\"healthy\"", snapshot.GetProperty("payload_preview").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestimoXReportSnapshotGetTool_ShouldReturnHtmlWhenRequested() {
        using var fixture = CreateMonitoringHistoryFixture();
        var options = new TestimoXToolOptions();
        options.AllowedHistoryRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXReportSnapshotGetTool(options);
        var arguments = new JsonObject()
            .Add("history_directory", fixture.HistoryDirectory)
            .Add("report_key", "dashboard-auto")
            .Add("include_html", true)
            .Add("max_chars", 128);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var snapshot = root.GetProperty("snapshot");
        Assert.Equal("dashboard-auto", snapshot.GetProperty("report_key").GetString());
        Assert.True(snapshot.GetProperty("html_included").GetBoolean());
        Assert.Contains("<html>", snapshot.GetProperty("html").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(snapshot.GetProperty("html_chars_returned").GetInt32() <= 128);
    }

    [Fact]
    public async Task TestimoXRunsListTool_ShouldReturnStoredRunCatalog() {
        using var fixture = CreateStoreFixture();
        var options = new TestimoXToolOptions();
        options.AllowedStoreRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXRunsListTool(options);
        var arguments = new JsonObject()
            .Add("store_directory", fixture.StoreDirectory)
            .Add("page_size", 5);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("returned_count").GetInt32());
        var firstRun = root.GetProperty("runs")
            .EnumerateArray()
            .First();
        Assert.Equal("run-001", firstRun.GetProperty("run_id").GetString());
        Assert.Equal(2, firstRun.GetProperty("stored_result_count").GetInt32());
    }

    [Fact]
    public async Task TestimoXRunSummaryTool_ShouldReturnStoredRunScores() {
        using var fixture = CreateStoreFixture();
        var options = new TestimoXToolOptions();
        options.AllowedStoreRoots.Add(fixture.RootDirectory);
        var tool = new TestimoXRunSummaryTool(options);
        var arguments = new JsonObject()
            .Add("store_directory", fixture.StoreDirectory)
            .Add("run_id", "run-001")
            .Add("page_size", 10);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("run-001", root.GetProperty("run_id").GetString());
        Assert.Equal(10, root.GetProperty("total_penalty").GetInt32());
        Assert.Equal(2, root.GetProperty("distinct_rule_count").GetInt32());
        var ruleNames = root.GetProperty("rows")
            .EnumerateArray()
            .Select(static row => row.GetProperty("rule_name").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Contains("DomainGpoLdapHardening", ruleNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DomainBackupCoverage", ruleNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestimoXBaselinesListTool_ShouldSupportVendorScopedLatestCatalog() {
        var tool = new TestimoXBaselinesListTool(new TestimoXToolOptions());
        var arguments = new JsonObject()
            .Add("vendor_ids", new JsonArray().Add("MSB"))
            .Add("latest_only", true)
            .Add("page_size", 5);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("returned_count").GetInt32() >= 1);
        var rows = root.GetProperty("baselines")
            .EnumerateArray()
            .ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal("MSB", row.GetProperty("vendor_id").GetString()));
    }

    [Fact]
    public async Task TestimoXBaselineCompareTool_ShouldReturnComparisonRowsForKnownProduct() {
        var tool = new TestimoXBaselineCompareTool(new TestimoXToolOptions());
        var arguments = new JsonObject()
            .Add("product_id", "Windows-Server-2025")
            .Add("vendor_ids", new JsonArray().Add("MSB").Add("CIS").Add("STIG"))
            .Add("version_wildcard", "*")
            .Add("page_size", 5);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Windows-Server-2025", root.GetProperty("product_id").GetString());
        Assert.True(root.GetProperty("matched_baseline_count").GetInt32() >= 1);
        Assert.True(root.GetProperty("returned_count").GetInt32() >= 1);
        var firstRow = root.GetProperty("rows")
            .EnumerateArray()
            .First();
        Assert.False(string.IsNullOrWhiteSpace(firstRow.GetProperty("anchor").GetString()));
    }

    [Fact]
    public async Task TestimoXSourceQueryTool_ShouldReturnProvenanceForKnownRule() {
        var tool = new TestimoXSourceQueryTool(new TestimoXToolOptions());
        var arguments = new JsonObject()
            .Add("rule_names", new JsonArray().Add("DomainGpoLdapHardening"))
            .Add("page_size", 5);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("returned_count").GetInt32() >= 1);
        var firstRow = root.GetProperty("rules")
            .EnumerateArray()
            .First();
        Assert.Equal("DomainGpoLdapHardening", firstRow.GetProperty("rule_name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(firstRow.GetProperty("source_type").GetString()));
    }

    [Fact]
    public async Task TestimoXProfilesListTool_ShouldReturnCuratedProfiles() {
        var tool = new TestimoXProfilesListTool(new TestimoXToolOptions());

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var profiles = root.GetProperty("profiles")
            .EnumerateArray()
            .Select(static node => node.GetProperty("profile").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Contains("AdSecurityAssessment", profiles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestimoXRuleInventoryTool_ShouldSupportProfileScopedPagedInventory() {
        var tool = new TestimoXRuleInventoryTool(new TestimoXToolOptions());
        var arguments = new JsonObject()
            .Add("profile", "AdSecurityAssessment")
            .Add("page_size", 1);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("AdSecurityAssessment", root.GetProperty("profile").GetString());
        Assert.True(root.GetProperty("returned_count").GetInt32() <= 1);
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, root.GetProperty("rules_view").ValueKind);
    }

    [Fact]
    public async Task TestimoXBaselineCrosswalkTool_ShouldReturnCrosswalkForKnownRule() {
        var tool = new TestimoXBaselineCrosswalkTool(new TestimoXToolOptions());
        var arguments = new JsonObject()
            .Add("rule_names", new JsonArray().Add("DomainGpoLdapHardening"))
            .Add("page_size", 1);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("returned_count").GetInt32() >= 1);
        var firstRow = root.GetProperty("rules")
            .EnumerateArray()
            .First();
        Assert.Equal("DomainGpoLdapHardening", firstRow.GetProperty("rule_name").GetString());
        Assert.True(firstRow.GetProperty("match_count").GetInt32() > 0 || firstRow.GetProperty("doc_count").GetInt32() > 0);
    }

    private static StoreFixture CreateStoreFixture() {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "ix-testimox-store-" + Guid.NewGuid().ToString("N"));
        var storeDirectory = Path.Combine(rootDirectory, "store");
        var runDirectory = Path.Combine(storeDirectory, "runs", "run-001");
        var resultDirectoryOne = Path.Combine(storeDirectory, "scopes", "domain", "contoso.local", "rules", "DomainGpoLdapHardening");
        var resultDirectoryTwo = Path.Combine(storeDirectory, "scopes", "dc", "contoso.local__dc01.contoso.local", "rules", "DomainBackupCoverage");
        Directory.CreateDirectory(runDirectory);
        Directory.CreateDirectory(resultDirectoryOne);
        Directory.CreateDirectory(resultDirectoryTwo);

        File.WriteAllText(
            Path.Combine(runDirectory, "run.json"),
            """
            {"RunId":"run-001","Started":"2026-03-01T10:00:00Z","Ended":"2026-03-01T10:05:00Z","Policy":"ReadWrite","TtlDays":7,"AcceptStale":false,"Match":"Strict","Raw":"Compact","PlannedTasks":2,"CompletedTasks":2,"EligibleForestFamilies":1,"EligibleDomainFamilies":1,"EligibleDcFamilies":1,"ToolVersion":"1.0.0"}
            """);
        File.WriteAllText(
            Path.Combine(runDirectory, "index.jsonl"),
            """
            {"path":"scopes/domain/contoso.local/rules/DomainGpoLdapHardening/result-001.json"}
            {"path":"scopes/dc/contoso.local__dc01.contoso.local/rules/DomainBackupCoverage/result-002.json"}
            """);
        File.WriteAllText(
            Path.Combine(resultDirectoryOne, "result-001.json"),
            """
            {"ScopeGroup":"domain","ScopeId":"contoso.local","Domain":"contoso.local","DomainController":"","RuleName":"DomainGpoLdapHardening","OverallStatus":"Passed","CompletedUtc":"2026-03-01T10:01:00Z","TestsSecurityCount":4,"TestsHealthCount":2,"PenaltySecurity":2,"PenaltyHealth":1,"PenaltyTotal":3}
            """);
        File.WriteAllText(
            Path.Combine(resultDirectoryTwo, "result-002.json"),
            """
            {"ScopeGroup":"dc","ScopeId":"contoso.local__dc01.contoso.local","Domain":"contoso.local","DomainController":"dc01.contoso.local","RuleName":"DomainBackupCoverage","OverallStatus":"Warning","CompletedUtc":"2026-03-01T10:02:00Z","TestsSecurityCount":3,"TestsHealthCount":3,"PenaltySecurity":4,"PenaltyHealth":3,"PenaltyTotal":7}
            """);

        return new StoreFixture(rootDirectory, storeDirectory);
    }

    private static MonitoringHistoryFixture CreateMonitoringHistoryFixture() {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "ix-testimox-history-" + Guid.NewGuid().ToString("N"));
        var historyDirectory = Path.Combine(rootDirectory, "history");
        Directory.CreateDirectory(historyDirectory);

        using (var store = new MonitoringReportJobStore(
                   new HistoryDatabaseConfig {
                       Provider = HistoryDatabaseProvider.Sqlite
                   },
                   new SqliteHistoryOptions(),
                   historyDirectory)) {
            var job = store.StartAsync(
                    jobKey: "dashboard-auto",
                    reportPath: Path.Combine(historyDirectory, "dashboard.html"),
                    trigger: "manual",
                    runId: 7,
                    startedUtc: new DateTimeOffset(2026, 03, 02, 11, 00, 00, TimeSpan.Zero),
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            store.CompleteAsync(
                    job.JobId,
                    MonitoringReportJobStatus.Success,
                    outcome: "Generated",
                    errorText: null,
                    metrics: new MonitoringReportJobMetrics {
                        HistoryEntries = 42,
                        HistoryRootCount = 5,
                        HistoryProbeCount = 7,
                        HistorySampleCount = 99,
                        HistoryLoadSeconds = 1.5,
                        HistoryCacheMode = "warm",
                        ReportBuildSeconds = 2.25,
                        ReportRenderSeconds = 1.75,
                        ReportWriteSeconds = 0.5,
                        ReportBytes = 2048,
                        ReportHash = "abc123",
                        SourceUpdatedUtc = new DateTimeOffset(2026, 03, 02, 10, 59, 00, TimeSpan.Zero)
                    },
                    diagnosticsJson: """{"reportWorkingSetStartBytes":1000,"reportWorkingSetEndBytes":2000}""",
                    completedUtc: new DateTimeOffset(2026, 03, 02, 11, 05, 00, TimeSpan.Zero),
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var failed = store.StartAsync(
                    jobKey: "dashboard-nightly",
                    reportPath: Path.Combine(historyDirectory, "nightly.html"),
                    trigger: "watchdog",
                    runId: 8,
                    startedUtc: new DateTimeOffset(2026, 03, 03, 02, 00, 00, TimeSpan.Zero),
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            store.CompleteAsync(
                    failed.JobId,
                    MonitoringReportJobStatus.Failed,
                    outcome: "RendererError",
                    errorText: "Chromium timeout",
                    metrics: null,
                    diagnosticsJson: null,
                    completedUtc: new DateTimeOffset(2026, 03, 03, 02, 02, 00, TimeSpan.Zero),
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        using (var historyStore = new DbaClientXHistoryStore(
                   new HistoryDatabaseConfig {
                       Provider = HistoryDatabaseProvider.Sqlite
                   },
                   historyDirectory,
                   sqliteOptions: new SqliteHistoryOptions())) {
            historyStore.WriteAsync(
                    new ProbeResult {
                        Name = "ldap-health",
                        RootProbe = "ldap-health",
                        Type = ProbeType.Directory,
                        Status = ProbeStatus.Up,
                        CompletedUtc = new DateTimeOffset(2026, 03, 02, 11, 00, 00, TimeSpan.Zero),
                        Agent = "agent-a",
                        Zone = "core",
                        Target = "dc01.contoso.local",
                        Protocol = "LDAP",
                        Metadata = new global::System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                            ["DirectoryKind"] = "ldap",
                            ["DirectoryScope"] = "domain_controller"
                        }
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            historyStore.WriteAsync(
                    new ProbeResult {
                        Name = "ldap-health",
                        RootProbe = "ldap-health",
                        Type = ProbeType.Directory,
                        Status = ProbeStatus.Down,
                        CompletedUtc = new DateTimeOffset(2026, 03, 02, 11, 30, 00, TimeSpan.Zero),
                        Agent = "agent-a",
                        Zone = "core",
                        Target = "dc01.contoso.local",
                        Protocol = "LDAP",
                        Metadata = new global::System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                            ["DirectoryKind"] = "ldap",
                            ["DirectoryScope"] = "domain_controller"
                        }
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        using (var rollupStore = new MonitoringAvailabilityRollupStore(
                   new HistoryDatabaseConfig {
                       Provider = HistoryDatabaseProvider.Sqlite
                   },
                   new SqliteHistoryOptions(),
                   historyDirectory)) {
            rollupStore.RefreshRangeAsync(
                    MonitoringAvailabilityRollupBucketKind.Hour,
                    new DateTimeOffset(2026, 03, 02, 10, 00, 00, TimeSpan.Zero),
                    new DateTimeOffset(2026, 03, 02, 12, 00, 00, TimeSpan.Zero),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        using (var maintenanceStore = new MonitoringMaintenanceWindowHistoryStore(
                   new HistoryDatabaseConfig {
                       Provider = HistoryDatabaseProvider.Sqlite
                   },
                   new SqliteHistoryOptions(),
                   historyDirectory)) {
            maintenanceStore.RecordWindowsForRangeAsync(
                    new[] {
                        new NotificationMaintenanceWindow {
                            Name = "Patch Tuesday",
                            Reason = "Monthly servicing",
                            Enabled = true,
                            ProbeType = ProbeType.Directory,
                            ProbeNamePattern = "ldap-*",
                            AgentPattern = "agent-*",
                            TargetPattern = "*.contoso.local",
                            TargetPatterns = { "dc01.contoso.local" },
                            ProtocolPattern = "LDAP",
                            StartUtc = new DateTimeOffset(2026, 03, 02, 10, 45, 00, TimeSpan.Zero),
                            EndUtc = new DateTimeOffset(2026, 03, 02, 12, 15, 00, TimeSpan.Zero),
                            SuppressNotifications = false,
                            SuppressSummaries = true,
                            SuppressReporting = true,
                            PauseProbes = true
                        }
                    },
                    new DateTimeOffset(2026, 03, 02, 10, 00, 00, TimeSpan.Zero),
                    new DateTimeOffset(2026, 03, 02, 12, 00, 00, TimeSpan.Zero),
                    new DateTimeOffset(2026, 03, 02, 11, 30, 00, TimeSpan.Zero),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        using (var dataSnapshotStore = new MonitoringReportDataSnapshotStore(
                   new HistoryDatabaseConfig {
                       Provider = HistoryDatabaseProvider.Sqlite
                   },
                   new SqliteHistoryOptions(),
                   historyDirectory)) {
            dataSnapshotStore.WriteAsync(
                    new MonitoringReportDataSnapshot {
                        ReportKey = "dashboard-auto",
                        GeneratedUtc = new DateTimeOffset(2026, 03, 02, 11, 05, 00, TimeSpan.Zero),
                        SourceUpdatedUtc = new DateTimeOffset(2026, 03, 02, 10, 59, 00, TimeSpan.Zero),
                        PayloadJson = """{"status":"healthy","summary":{"up":1,"down":1},"items":[1,2,3,4,5,6,7,8,9,10]}""",
                        PayloadBytes = 0,
                        PayloadHash = null,
                        MetadataJson = """{"source":"unit-test","kind":"data"}"""
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        using (var snapshotStore = new MonitoringReportSnapshotStore(
                   new HistoryDatabaseConfig {
                       Provider = HistoryDatabaseProvider.Sqlite
                   },
                   new SqliteHistoryOptions(),
                   historyDirectory)) {
            snapshotStore.WriteAsync(
                    new MonitoringReportSnapshot {
                        ReportKey = "dashboard-auto",
                        GeneratedUtc = new DateTimeOffset(2026, 03, 02, 11, 05, 00, TimeSpan.Zero),
                        SourceUpdatedUtc = new DateTimeOffset(2026, 03, 02, 10, 59, 00, TimeSpan.Zero),
                        Html = "<html><body><h1>Dashboard</h1><p>Healthy</p><p>Extra content for truncation checks.</p></body></html>",
                        HtmlBytes = 0,
                        HtmlHash = null,
                        MetadataJson = """{"source":"unit-test","kind":"html"}"""
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        var diagnosticsSnapshot = new MonitoringDiagnosticsSnapshot {
            GeneratedUtc = new DateTimeOffset(2026, 03, 03, 09, 00, 00, TimeSpan.Zero),
            SinceUtc = new DateTimeOffset(2026, 03, 01, 00, 00, 00, TimeSpan.Zero),
            NotificationSent = 12,
            NotificationFailed = 1,
            NotificationDeduped = 4,
            NotificationCooldownSuppressions = 2,
            NotificationRateLimitHits = 3,
            NotificationQueueDepth = 2,
            NotificationQueueCapacity = 10,
            NotificationQueueFallbacks = 1,
            NotificationQueueDrops = 0,
            NotificationLastFailedChannel = "smtp",
            NotificationLastFailedError = "timeout",
            ProactiveTriggers = 7,
            ProactiveFollowUpsScheduled = 5,
            HistoryQueueDepth = 3,
            HistoryQueueMaxDepth = 25,
            HistoryWriteFailures = 1,
            HistorySpoolFileCount = 2,
            HistorySpoolItemCount = 9,
            HistoryMaintenanceRuns = 6,
            HistoryMaintenanceFailures = 1,
            HistoryMaintenanceLastCompletedUtc = new DateTimeOffset(2026, 03, 03, 08, 30, 00, TimeSpan.Zero),
            HistoryMaintenanceLastError = "vacuum skipped",
            ProbeHardTimeoutInFlight = 1,
            AlertLogQueueDepth = 0,
            SmtpFailureStreak = 1,
            SmtpCooldownUntilUtc = new DateTimeOffset(2026, 03, 03, 09, 10, 00, TimeSpan.Zero),
            SlowProbes = new[] {
                new MonitoringSlowProbeSnapshot {
                    Name = "ldap-health",
                    Type = ProbeType.Directory,
                    Status = ProbeStatus.Down,
                    Target = "dc01.contoso.local",
                    CompletedUtc = new DateTimeOffset(2026, 03, 03, 08, 59, 00, TimeSpan.Zero),
                    DurationSeconds = 18.5,
                    IntervalSeconds = 5,
                    TimeoutSeconds = 15,
                    IntervalOverrun = true
                },
                new MonitoringSlowProbeSnapshot {
                    Name = "ntp-health",
                    Type = ProbeType.Ntp,
                    Status = ProbeStatus.Degraded,
                    Target = "dc02.contoso.local",
                    CompletedUtc = new DateTimeOffset(2026, 03, 03, 08, 58, 00, TimeSpan.Zero),
                    DurationSeconds = 11.25,
                    IntervalSeconds = 10,
                    TimeoutSeconds = 12,
                    IntervalOverrun = true
                }
            },
            SqliteHealth = new SqliteHealthSnapshot {
                LastCheckUtc = new DateTimeOffset(2026, 03, 03, 08, 45, 00, TimeSpan.Zero),
                LastCheckStatus = SqliteHealthStatus.Ok,
                LastCheckMessage = "ok",
                LastBackupUtc = new DateTimeOffset(2026, 03, 03, 08, 00, 00, TimeSpan.Zero),
                LastBackupPath = Path.Combine(historyDirectory, "backup", "monitoring.sqlite.bak"),
                LastRestoreUtc = new DateTimeOffset(2026, 03, 02, 08, 00, 00, TimeSpan.Zero),
                LastRestoreSource = Path.Combine(historyDirectory, "restore", "monitoring.sqlite.bak"),
                LastRestoreMessage = "dry-run"
            },
            Reachability = new ReachabilityServiceMetricsSnapshot {
                GeneratedUtc = new DateTimeOffset(2026, 03, 03, 09, 00, 00, TimeSpan.Zero),
                Agent = "agent-a",
                TargetsConfigured = 4,
                HostsTracked = 2,
                ZonesTracked = 1,
                SchedulerQueueDepth = 1,
                PersistQueueDepth = 2,
                PersistQueueDropped = 0,
                PingsStarted = 100,
                PingsSucceeded = 95,
                PingsFailed = 5,
                StoreFailureStreak = 0
            }
        };
        File.WriteAllText(
            Path.Combine(historyDirectory, MonitoringDiagnosticsSnapshot.DefaultFileName),
            JsonSerializer.Serialize(diagnosticsSnapshot));

        return new MonitoringHistoryFixture(rootDirectory, historyDirectory);
    }

    private sealed class StoreFixture : IDisposable {
        public StoreFixture(string rootDirectory, string storeDirectory) {
            RootDirectory = rootDirectory;
            StoreDirectory = storeDirectory;
        }

        public string RootDirectory { get; }

        public string StoreDirectory { get; }

        public void Dispose() {
            try {
                if (Directory.Exists(RootDirectory)) {
                    Directory.Delete(RootDirectory, recursive: true);
                }
            } catch {
                // Best-effort cleanup for temp fixtures.
            }
        }
    }

    private sealed class MonitoringHistoryFixture : IDisposable {
        public MonitoringHistoryFixture(string rootDirectory, string historyDirectory) {
            RootDirectory = rootDirectory;
            HistoryDirectory = historyDirectory;
        }

        public string RootDirectory { get; }

        public string HistoryDirectory { get; }

        public void Dispose() {
            try {
                if (Directory.Exists(RootDirectory)) {
                    Directory.Delete(RootDirectory, recursive: true);
                }
            } catch {
                // Best-effort cleanup for temp fixtures.
            }
        }
    }
}
