using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Storage;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class UsageLimitRulesTests {
    private static readonly DateTimeOffset Recorded = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("turn-end")]
    [InlineData("first-observation")]
    [InlineData("last-activity")]
    public void ARecordedLimitExpiresAtTheFallbackBoundaryEvenIfTheReporterGoesSilent(string clock) {
        var item = Limited(clock == "turn-end");
        if (clock != "last-activity") item.ConditionFirstObservedAtUtc["usage-limit"] =
            clock == "turn-end" ? Recorded.AddHours(2) : Recorded;
        if (clock == "first-observation") item = item with { Session = item.Session! with { LastActivityAtUtc = Recorded.AddHours(-2) } };
        var rules = new LedgerRules(new());
        var deadline = Recorded.AddHours(5);

        var before = rules.Evaluate(item, deadline.AddTicks(-1));
        var after = rules.Evaluate(item with { Current = before }, deadline, reporterSilent: true);

        Assert.Equal(("needs-you", "usage-limit", new UsageLimitView(Recorded, null, true)),
            (before.Value, before.ReasonCode, before.UsageLimit));
        Assert.Equal(("stuck", "usage-limit", new UsageLimitView(Recorded, null, false)),
            (after.Value, after.ReasonCode, after.UsageLimit));
        Assert.Equal(before.EpisodeId, after.EpisodeId);
        Assert.Null(after.Hold);
    }

    [Theory]
    [InlineData("codex", true, true, true)]
    [InlineData("claude", true, true, true)]
    [InlineData("claude", false, true, true)]
    [InlineData("claude", true, false, true)]
    [InlineData("claude", true, true, false)]
    public void OnlyANewerMatchingAccountWindowCanExtendTheFallback(string provider, bool newer, bool sameMachine, bool sameWindow) {
        var reset = Recorded.AddDays(sameWindow ? 2 : 8);
        var account = new AccountLimit(provider, "synthetic-account", Recorded.AddMinutes(newer ? 1 : -1)) {
            Machine = sameMachine ? "synthetic-machine" : "other-machine",
            AccountId = provider + ":synthetic",
            LimitReached = provider == "codex" ? true : null,
            UsedPercent = 100,
            WindowMinutes = 10080,
            ResetsAtUtc = reset,
            ShortUsedPercent = 25,
            ShortResetsAtUtc = Recorded.AddHours(2)
        };
        var rules = new LedgerRules(new(), _ => account);
        var session = Limited(ended: true);
        var item = session with { Provider = provider, Session = session.Session! with { AccountId = account.AccountId } };

        var afterFallback = rules.Evaluate(item, Recorded.AddHours(6));
        var afterReset = rules.Evaluate(item with { Current = afterFallback }, reset);

        var known = newer && sameMachine && sameWindow;
        Assert.Equal(known ? "needs-you" : "stuck", afterFallback.Value);
        Assert.Equal(new(Recorded, known ? reset : null, known), afterFallback.UsageLimit);
        Assert.Equal(("stuck", "usage-limit", new UsageLimitView(Recorded, known ? reset : null, false)),
            (afterReset.Value, afterReset.ReasonCode, afterReset.UsageLimit));
    }

    [Fact]
    public void AClearedStopStaysClearedAcrossRestartUntilTheNextTurn() {
        var item = Limited(ended: false);
        var now = Recorded.AddHours(1);
        var rules = new LedgerRules(new(), _ => new("claude", "synthetic-account", Recorded.AddMinutes(10)) {
            Machine = item.MachineId, AccountId = item.Session!.AccountId, UsedPercent = 80, ShortUsedPercent = 4
        });

        var clear = rules.Evaluate(item, now);
        var offline = new LedgerRules(new());
        var question = offline.Evaluate(item with { Current = clear, Session = item.Session! with {
            Question = true, InputId = "question", QuestionAtUtc = now.AddMinutes(1)
        } }, now.AddMinutes(1));
        Assert.Equal("question", question.ReasonCode);
        Assert.False(question.UsageLimit!.InForce);
        var stored = JsonSerializer.Serialize(item with { Current = question }, LedgerJson.Options);
        Assert.DoesNotContain("\"usageLimit\":{", stored);
        var restarted = offline.Evaluate(JsonSerializer.Deserialize<LedgerItem>(stored, LedgerJson.Options)!, now.AddMinutes(2));
        var resumed = rules.Evaluate(item with { Current = clear, Session = new() {
            TurnId = "next-turn", TurnOpen = true, TurnStartedAtUtc = now, LastActivityAtUtc = now
        } }, now);

        Assert.Equal(("stuck", "usage-limit", new UsageLimitView(Recorded, null, false)),
            (clear.Value, clear.ReasonCode, clear.UsageLimit));
        Assert.Equal(("stuck", "usage-limit", clear.UsageLimit), (restarted.Value, restarted.ReasonCode, restarted.UsageLimit));
        Assert.Equal(("working", "active"), (resumed.Value, resumed.ReasonCode));
        Assert.Null(resumed.UsageLimit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("claude:other")]
    public void TheHostsCurrentAccountCannotClearAnUnmatchedSessionLimit(string? accountId) {
        var item = Limited(ended: false);
        item = item with { Session = item.Session! with { AccountId = accountId } };
        var rules = new LedgerRules(new(), _ => new("claude", "synthetic-account", Recorded.AddMinutes(1)) {
            AccountId = "claude:synthetic", Machine = item.MachineId, UsedPercent = 20, ShortUsedPercent = 3
        });

        Assert.Equal("needs-you", rules.Evaluate(item, Recorded.AddHours(1)).Value);
    }

    [Fact]
    public void AReportedCodexBlockDoesNotUseAResetFromAnUnexhaustedQuota() {
        var item = Limited(ended: true);
        item = item with { Provider = "codex", Session = item.Session! with { AccountId = "codex:synthetic" } };
        var rules = new LedgerRules(new(), _ => new("codex", "synthetic-account", Recorded.AddMinutes(1)) {
            Machine = item.MachineId, AccountId = item.Session!.AccountId, LimitReached = true,
            UsedPercent = 20, WindowMinutes = 10080, ResetsAtUtc = Recorded.AddDays(2)
        });

        var result = rules.Evaluate(item, Recorded.AddHours(6));
        Assert.Equal(("stuck", new UsageLimitView(Recorded, null, false)), (result.Value, result.UsageLimit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShortWindowResetClearsTheStopUnlessTheWeeklyWindowIsAlsoExceeded(bool weeklyLimit) {
        var item = Limited(ended: false);
        var shortReset = Recorded.AddHours(2);
        var weeklyReset = Recorded.AddDays(2);
        var rules = new LedgerRules(new(), _ => new("claude", "synthetic-account", Recorded.AddMinutes(10)) {
            Machine = item.MachineId, AccountId = item.Session!.AccountId,
            UsedPercent = weeklyLimit ? 100 : 20, WindowMinutes = 10080, ResetsAtUtc = weeklyReset,
            ShortUsedPercent = 100, ShortResetsAtUtc = shortReset
        });

        var afterShortReset = rules.Evaluate(item, shortReset);

        Assert.Equal(new UsageLimitView(Recorded, weeklyLimit ? weeklyReset : shortReset, weeklyLimit), afterShortReset.UsageLimit);
        Assert.Equal(weeklyLimit ? "needs-you" : "stuck", afterShortReset.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AStoppedAccountsKnownResetSurvivesPartialUpdatesAccountSwitchAndServiceRestart(bool weekly, bool percentageOnly) {
        var directory = LedgerTestPaths.NewDirectory("limit-restart");
        Directory.CreateDirectory(directory);
        try {
            var db = Path.Combine(directory, "ledger.sqlite");
            var snapshot = Path.Combine(directory, "status-line.json");
            var reset = weekly ? Recorded.AddDays(2) : Recorded.AddHours(2);
            var limits = new WorkAccountLimits(directory);
            var session = Limited(ended: true).Session!;
            var report = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                MachineId = AccountLimits.LocalMachine, ReporterId = "synthetic-reporter", ItemId = "limited-session",
                Provider = "claude", Source = "watcher", ObservedAtUtc = Recorded, Session = session };
            using (var store = new LedgerStore(db, new(new(), limits.ForItem))) {
                await store.InitializeAsync();
                await store.IngestAsync([report], Recorded);
                var accountObserved = Recorded.AddMinutes(1);
                File.WriteAllText(snapshot, Status("synthetic", accountObserved, includeExhausted: true));
                limits.ObserveClaude(snapshot, accountObserved);

                var switched = Recorded.AddMinutes(5);
                File.WriteAllText(snapshot, Status("synthetic", switched.AddMinutes(-1), includeExhausted: false, percentageOnly));
                limits.ObserveClaude(snapshot, switched.AddMinutes(-1));
                await store.IngestAsync([report with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = switched,
                    Session = session with { AccountId = "claude:other" } }], switched);
                File.WriteAllText(snapshot, Status("other", switched, includeExhausted: false));
                limits.ObserveClaude(snapshot, switched);
                var retained = Assert.Single(await store.EvaluateAllAsync(switched));
                Assert.Equal(("claude:synthetic", "needs-you", new UsageLimitView(Recorded, reset, true)),
                    (retained.Session!.AccountId, retained.Current!.Value, retained.Current.UsageLimit));
            }

            File.Delete(snapshot);
            var restarted = new WorkAccountLimits(directory);
            using var reopened = new LedgerStore(db, new(new(), restarted.ForItem));
            await reopened.InitializeAsync();
            var expired = Assert.Single(await reopened.EvaluateAllAsync(reset));
            Assert.Equal(("stuck", "usage-limit", new UsageLimitView(Recorded, reset, false)),
                (expired.Current!.Value, expired.Current.ReasonCode, expired.Current.UsageLimit));

            string Status(string account, DateTimeOffset observed, bool includeExhausted, bool percentageOnly = false) {
                var windows = new Dictionary<string, object> {
                    [weekly ? "five_hour" : "seven_day"] = new { used_percentage = 25 }
                };
                if (includeExhausted)
                    windows[weekly ? "seven_day" : "five_hour"] = new { used_percentage = 100, resets_at = reset.ToUnixTimeSeconds() };
                else if (percentageOnly) windows[weekly ? "seven_day" : "five_hour"] = new { used_percentage = 100 };
                return JsonSerializer.Serialize(new { observedAtUtc = observed, accountId = account, rate_limits = windows });
            }
        } finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ANewAccountLimitDoesNotReopenAnEarlierClearedStop() {
        var directory = LedgerTestPaths.NewDirectory("limit-cleared");
        Directory.CreateDirectory(directory);
        try {
            var item = Limited(ended: true) with { MachineId = AccountLimits.LocalMachine };
            var snapshot = Path.Combine(directory, "status-line.json");
            var limits = new WorkAccountLimits(directory);
            var clearedAt = Recorded.AddMinutes(10);
            File.WriteAllText(snapshot, Status(clearedAt, 20, Recorded.AddHours(4)));
            limits.ObserveClaude(snapshot, clearedAt);

            var limitedAgain = Recorded.AddHours(1);
            var nextReset = Recorded.AddHours(4);
            File.WriteAllText(snapshot, Status(limitedAgain, 100, nextReset));
            limits.ObserveClaude(snapshot, limitedAgain);

            var restarted = new WorkAccountLimits(directory);
            var rules = new LedgerRules(new(), restarted.ForItem);
            var oldStop = rules.Evaluate(item, limitedAgain);
            Assert.Equal(("stuck", new UsageLimitView(Recorded, null, false)), (oldStop.Value, oldStop.UsageLimit));
            var newStopAt = limitedAgain.AddMinutes(-1);
            var newStop = rules.Evaluate(item with { Session = item.Session! with {
                TurnId = "another-turn", TurnEndedAtUtc = newStopAt
            } }, limitedAgain);
            Assert.Equal(("needs-you", new UsageLimitView(newStopAt, nextReset, true)), (newStop.Value, newStop.UsageLimit));

            static string Status(DateTimeOffset observed, int shortUsed, DateTimeOffset reset) => JsonSerializer.Serialize(new {
                observedAtUtc = observed,
                accountId = "synthetic",
                rate_limits = new {
                    five_hour = new { used_percentage = shortUsed, resets_at = reset.ToUnixTimeSeconds() },
                    seven_day = new { used_percentage = 25 }
                }
            });
        } finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ConfirmedResetSurvivesRepeatedEvaluationsAcrossTheFallbackDeadline() {
        var directory = LedgerTestPaths.NewDirectory("limit-deadline");
        try {
            var item = Limited(ended: true);
            var reset = Recorded.AddHours(2);
            var limit = new AccountLimit("claude", "synthetic-account", Recorded.AddMinutes(1)) {
                Machine = item.MachineId, AccountId = item.Session!.AccountId,
                ShortUsedPercent = 100, ShortResetsAtUtc = reset, UsedPercent = 25, WindowMinutes = 10080
            };
            using var store = new LedgerStore(Path.Combine(directory, "ledger.sqlite"), new(new(), _ => limit));
            await store.InitializeAsync();
            await store.IngestAsync([new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = item.MachineId,
                ReporterId = "synthetic-reporter", ItemId = item.Id, Provider = item.Provider, Source = "watcher",
                ObservedAtUtc = Recorded, Session = item.Session
            }], Recorded);

            var cleared = Assert.Single(await store.EvaluateAllAsync(reset));
            Assert.Equal(new UsageLimitView(Recorded, reset, false), cleared.Current!.UsageLimit);

            limit = limit with {
                ObservedAtUtc = Recorded.AddHours(3), ShortUsedPercent = 50, ShortResetsAtUtc = null,
                UsedPercent = 100, ResetsAtUtc = Recorded.AddDays(2)
            };
            foreach (var now in new[] { Recorded.AddHours(3), Recorded.AddHours(5), Recorded.AddHours(5).AddMinutes(1) }) {
                var pass = Assert.Single(await store.EvaluateAllAsync(now));
                Assert.Equal(("stuck", new UsageLimitView(Recorded, reset, false)), (pass.Current!.Value, pass.Current.UsageLimit));
            }
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static LedgerItem Limited(bool ended) => new() {
        Id = "synthetic-session", Kind = "session", Provider = "claude", MachineId = "synthetic-machine",
        FirstObservedAtUtc = Recorded.AddDays(-1), Session = new() {
            AccountId = "claude:synthetic", TurnId = "limited-turn", TurnOpen = !ended, Outcome = ended ? "failed" : "incomplete",
            TurnEndedAtUtc = ended ? Recorded : null, LastActivityAtUtc = Recorded, UsageLimit = true
        }
    };
}
