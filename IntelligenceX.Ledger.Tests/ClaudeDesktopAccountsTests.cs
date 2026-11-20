using IntelligenceX.Ledger.Reporting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ClaudeDesktopAccountsTests {
    private const string Old = "522f0df8-0000-4000-8000-000000000001";
    private const string Current = "788f8a4f-0000-4000-8000-000000000002";

    [Fact]
    public void SessionsFollowTheAccountTheDesktopAppFiledThemUnderAndTheRestTheCliAccount() {
        var root = Path.Combine(Path.GetTempPath(), "ix-claude-accounts-" + Guid.NewGuid().ToString("N"));
        try {
            Write(root, Old, "org-a", "local_1.json", """{"cliSessionId":"aaaa-old","title":"Old work","isArchived":true}""");
            Write(root, Current, "org-b", "local_2.json", """{"cliSessionId":"bbbb-new"}""");
            Write(root, Current, "org-b", "local_3.json", "not json");
            Write(root, "not-an-account", "org", "local_4.json", """{"cliSessionId":"cccc-ignored"}""");
            var cli = Path.Combine(root, "claude.json");
            File.WriteAllText(cli, $$$"""{"oauthAccount":{"accountUuid":"{{{Current}}}","emailAddress":"private@example.invalid"}}""");
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
            var stoppedAt = clock.GetUtcNow();
            File.SetLastWriteTimeUtc(cli, stoppedAt.AddHours(-1).UtcDateTime);
            var accounts = new ClaudeDesktopAccounts(root, cli, clock);

            Assert.Equal(Old, accounts.For("AAAA-OLD"));
            // The app's archive flag comes along; a session it never had has none.
            Assert.Equal((true, false, (bool?)null), (accounts.Archived("aaaa-old"), accounts.Archived("bbbb-new"), accounts.Archived("started-from-the-cli")));
            Assert.Equal(Current, accounts.For("bbbb-new"));
            Assert.Equal(Current, accounts.For("cccc-ignored"));
            Assert.Equal(Current, accounts.For("started-from-the-cli"));
            Assert.Equal(Current, accounts.For("started-from-the-cli", stoppedAtUtc: stoppedAt));

            // A session filed after the last read shows on the next one, not before.
            Write(root, Old, "org-a", "local_5.json", """{"cliSessionId":"dddd-later"}""");
            Assert.Equal(Current, accounts.For("dddd-later"));
            File.WriteAllText(cli, $$$"""{"oauthAccount":{"accountUuid":"{{{Old}}}"}}""");
            File.SetLastWriteTimeUtc(cli, stoppedAt.AddMinutes(1).UtcDateTime);
            clock.Advance(ClaudeDesktopAccounts.RereadAfter);
            Assert.Equal(Old, accounts.For("dddd-later"));
            Assert.Null(accounts.For("started-from-the-cli", stoppedAtUtc: stoppedAt));
            Assert.Equal(Old, accounts.For("started-from-the-cli", stoppedAtUtc: clock.GetUtcNow()));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArchiveStateSurvivesARereadThatCantReadTheFileAndOtherProfilesGetNoCliAccount() {
        var root = Path.Combine(Path.GetTempPath(), "ix-claude-accounts-" + Guid.NewGuid().ToString("N"));
        try {
            Write(root, Old, "org-a", "local_1.json", """{"cliSessionId":"aaaa-old","isArchived":true}""");
            Write(root, Old, "org-a", "local_2.json", "[]");
            var cli = Path.Combine(root, "claude.json");
            File.WriteAllText(cli, "[]");
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
            var accounts = new ClaudeDesktopAccounts(root, cli, clock);
            Assert.True(accounts.Archived("aaaa-old"));
            Assert.Null(accounts.For("started-from-the-cli"));

            // The app is mid-write: the file reads as broken JSON, and what was known about it stays.
            Write(root, Old, "org-a", "local_1.json", """{"cliSessionId":"aaaa-o""");
            File.WriteAllText(cli, $$$"""{"oauthAccount":{"accountUuid":"{{{Current}}}"}}""");
            clock.Advance(ClaudeDesktopAccounts.RereadAfter);
            Assert.Equal((true, Old), (accounts.Archived("aaaa-old"), accounts.For("aaaa-old")));
            Assert.Equal(Current, accounts.For("started-from-the-cli"));
            Assert.Null(accounts.For("started-from-the-cli", cliProfile: false));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NoDesktopAppAndNoCliSignInMeansNoAccount() {
        var missing = Path.Combine(Path.GetTempPath(), "ix-no-claude-" + Guid.NewGuid().ToString("N"));
        Assert.Null(new ClaudeDesktopAccounts(missing, Path.Combine(missing, "claude.json")).For("any"));
    }

    private static void Write(string root, string account, string organization, string name, string content) {
        var folder = Path.Combine(root, account, organization);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), content);
    }
}
