using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestUsageTelemetryPathResolverHonorsEnvironmentOverrides() {
        var previousEnabled = Environment.GetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable);
        var previousPath = Environment.GetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable);
        var tempDir = Path.Combine(Path.GetTempPath(), $"ix-usage-db-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var explicitPath = Path.Combine(tempDir, "explicit", "usage.db");
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable, "0");
            var resolvedExplicitPath = UsageTelemetryPathResolver.ResolveDatabasePath(explicitPath, enabledByDefault: false);
            AssertEqual(Path.GetFullPath(explicitPath), resolvedExplicitPath, "usage telemetry explicit path");

            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable, "1");
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable, tempDir + Path.DirectorySeparatorChar);
            var resolvedEnvPath = UsageTelemetryPathResolver.ResolveDatabasePath(null, enabledByDefault: false);
            AssertEqual(Path.Combine(tempDir, "usage.db"), resolvedEnvPath, "usage telemetry env directory path");

            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable, null);
            var defaultPath = UsageTelemetryPathResolver.ResolveDatabasePath(null, enabledByDefault: false);
            AssertEqual(UsageTelemetryPathResolver.BuildDefaultDatabasePath(), defaultPath, "usage telemetry default path");
        } finally {
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable, previousEnabled);
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable, previousPath);
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestUsageTelemetryPathResolverDisablesWhenFlagOff() {
        var previousEnabled = Environment.GetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable);
        var previousPath = Environment.GetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable);
        try {
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable, "off");
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable,
                Path.Combine(Path.GetTempPath(), $"ix-usage-db-{Guid.NewGuid():N}.db"));
            var resolved = UsageTelemetryPathResolver.ResolveDatabasePath(null, enabledByDefault: true);
            AssertEqual(null, resolved, "usage telemetry disabled path");
        } finally {
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.EnableEnvironmentVariable, previousEnabled);
            Environment.SetEnvironmentVariable(UsageTelemetryPathResolver.DatabasePathEnvironmentVariable, previousPath);
        }
    }

    private static void TestInternalIxUsageTelemetrySessionPersistsTurnsToSqlite() {
        var dbDirectory = Path.Combine(Path.GetTempPath(), $"ix-usage-runtime-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(dbDirectory, "usage.db");
        Directory.CreateDirectory(dbDirectory);
        try {
            var turn = TurnInfo.FromJson(new JsonObject()
                .Add("id", "turn_runtime_1")
                .Add("response_id", "resp_runtime_1")
                .Add("status", "completed")
                .Add("usage", new JsonObject()
                    .Add("input_tokens", 210L)
                    .Add("cached_input_tokens", 20L)
                    .Add("output_tokens", 45L)
                    .Add("reasoning_tokens", 5L)
                    .Add("total_tokens", 255L))
                .Add("output", new JsonArray()));
            using var client = CreateToolRunnerClient(turn);
            var options = new IntelligenceXClientOptions {
                EnableUsageTelemetry = true,
                UsageTelemetryDatabasePath = dbPath,
                UsageTelemetryMachineId = "runtime-devbox",
                UsageTelemetryAccountLabel = "work",
                UsageTelemetryProviderAccountId = "acct-runtime",
                UsageTelemetrySourcePath = "ix://internal/runtime-devbox"
            };

            using (var session = InternalIxUsageTelemetrySession.TryCreate(client, options)) {
                AssertNotNull(session, "runtime telemetry session");
                _ = client.ChatAsync(
                    ChatInput.FromText("persist telemetry"),
                    new ChatOptions {
                        TelemetryFeature = "reviewer",
                        TelemetrySurface = "cli"
                    }).GetAwaiter().GetResult();
            }

            using (var rootStore = new SqliteSourceRootStore(dbPath))
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                var roots = rootStore.GetAll();
                var events = eventStore.GetAll();

                AssertEqual(1, roots.Count, "runtime telemetry root count");
                AssertEqual("ix://internal/runtime-devbox", roots[0].Path, "runtime telemetry root path");
                AssertEqual(1, events.Count, "runtime telemetry event count");
                AssertEqual("acct-runtime", events[0].ProviderAccountId, "runtime telemetry provider account");
                AssertEqual("work", events[0].AccountLabel, "runtime telemetry account label");
                AssertEqual("runtime-devbox", events[0].MachineId, "runtime telemetry machine id");
                AssertEqual("reviewer", events[0].Surface, "runtime telemetry surface");
                AssertEqual(255L, events[0].TotalTokens, "runtime telemetry total tokens");
            }
        } finally {
            try {
                Directory.Delete(dbDirectory, recursive: true);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestInternalIxUsageTelemetrySessionClassifiesNativeTransportAsChatGpt() {
        var dbDirectory = Path.Combine(Path.GetTempPath(), $"ix-usage-runtime-native-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(dbDirectory, "usage.db");
        Directory.CreateDirectory(dbDirectory);
        try {
            var turn = TurnInfo.FromJson(new JsonObject()
                .Add("id", "turn_runtime_native_1")
                .Add("response_id", "resp_runtime_native_1")
                .Add("status", "completed")
                .Add("usage", new JsonObject()
                    .Add("input_tokens", 100L)
                    .Add("output_tokens", 25L)
                    .Add("total_tokens", 125L))
                .Add("output", new JsonArray()));
            using var client = CreateToolRunnerClient(turn);
            var options = new IntelligenceXClientOptions {
                EnableUsageTelemetry = true,
                UsageTelemetryDatabasePath = dbPath,
                UsageTelemetryMachineId = "runtime-native"
            };

            using (var session = InternalIxUsageTelemetrySession.TryCreate(client, options)) {
                AssertNotNull(session, "native runtime telemetry session");
                _ = client.ChatAsync(ChatInput.FromText("persist native telemetry")).GetAwaiter().GetResult();
            }

            using (var rootStore = new SqliteSourceRootStore(dbPath))
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                var roots = rootStore.GetAll();
                var events = eventStore.GetAll();

                AssertEqual("chatgpt", roots[0].ProviderId, "native runtime root provider");
                AssertEqual("chatgpt://internal/runtime-native", roots[0].Path, "native runtime root path");
                AssertEqual("chatgpt", events[0].ProviderId, "native runtime event provider");
            }
        } finally {
            try {
                Directory.Delete(dbDirectory, recursive: true);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestInternalIxUsageTelemetrySessionClassifiesAppServerTransportAsCodex() {
        var dbDirectory = Path.Combine(Path.GetTempPath(), $"ix-usage-runtime-appserver-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(dbDirectory, "usage.db");
        Directory.CreateDirectory(dbDirectory);
        try {
            var turn = TurnInfo.FromJson(new JsonObject()
                .Add("id", "turn_runtime_appserver_1")
                .Add("response_id", "resp_runtime_appserver_1")
                .Add("status", "completed")
                .Add("usage", new JsonObject()
                    .Add("input_tokens", 80L)
                    .Add("output_tokens", 20L)
                    .Add("total_tokens", 100L))
                .Add("output", new JsonArray()));
            using var client = CreateToolRunnerClient(turn, OpenAITransportKind.AppServer);
            var options = new IntelligenceXClientOptions {
                EnableUsageTelemetry = true,
                UsageTelemetryDatabasePath = dbPath,
                UsageTelemetryMachineId = "runtime-appserver",
                TransportKind = OpenAITransportKind.AppServer
            };

            using (var session = InternalIxUsageTelemetrySession.TryCreate(client, options)) {
                AssertNotNull(session, "appserver runtime telemetry session");
                _ = client.ChatAsync(ChatInput.FromText("persist appserver telemetry")).GetAwaiter().GetResult();
            }

            using (var rootStore = new SqliteSourceRootStore(dbPath))
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                var roots = rootStore.GetAll();
                var events = eventStore.GetAll();

                AssertEqual("codex", roots[0].ProviderId, "appserver runtime root provider");
                AssertEqual("codex://internal/runtime-appserver", roots[0].Path, "appserver runtime root path");
                AssertEqual("codex", events[0].ProviderId, "appserver runtime event provider");
            }
        } finally {
            try {
                Directory.Delete(dbDirectory, recursive: true);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestInternalIxUsageTelemetrySessionClassifiesCompatibleHttpLmStudioTransport() {
        var dbDirectory = Path.Combine(Path.GetTempPath(), $"ix-usage-runtime-lmstudio-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(dbDirectory, "usage.db");
        Directory.CreateDirectory(dbDirectory);
        try {
            var turn = TurnInfo.FromJson(new JsonObject()
                .Add("id", "turn_runtime_lmstudio_1")
                .Add("response_id", "resp_runtime_lmstudio_1")
                .Add("status", "completed")
                .Add("usage", new JsonObject()
                    .Add("input_tokens", 90L)
                    .Add("output_tokens", 30L)
                    .Add("total_tokens", 120L))
                .Add("output", new JsonArray()));
            using var client = CreateToolRunnerClient(turn, OpenAITransportKind.CompatibleHttp);
            var options = new IntelligenceXClientOptions {
                EnableUsageTelemetry = true,
                UsageTelemetryDatabasePath = dbPath,
                UsageTelemetryMachineId = "runtime-lmstudio",
                TransportKind = OpenAITransportKind.CompatibleHttp
            };
            options.CompatibleHttpOptions.BaseUrl = "http://localhost:1234/v1";
            options.CompatibleHttpOptions.AllowInsecureHttp = true;

            using (var session = InternalIxUsageTelemetrySession.TryCreate(client, options)) {
                AssertNotNull(session, "lmstudio runtime telemetry session");
                _ = client.ChatAsync(ChatInput.FromText("persist lmstudio telemetry")).GetAwaiter().GetResult();
            }

            using (var rootStore = new SqliteSourceRootStore(dbPath))
            using (var eventStore = new SqliteUsageEventStore(dbPath)) {
                var roots = rootStore.GetAll();
                var events = eventStore.GetAll();

                AssertEqual("lmstudio", roots[0].ProviderId, "lmstudio runtime root provider");
                AssertEqual("lmstudio://internal/runtime-lmstudio", roots[0].Path, "lmstudio runtime root path");
                AssertEqual("lmstudio", events[0].ProviderId, "lmstudio runtime event provider");
            }
        } finally {
            try {
                Directory.Delete(dbDirectory, recursive: true);
            } catch {
                // best-effort cleanup
            }
        }
    }
}
