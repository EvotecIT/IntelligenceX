using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationImageTests : IDisposable {
    // The same clock as CoordinationAskTests, whose helpers complete asks here.
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly CoordinationPrincipal Sessions = new("pc-sessions", "client", "unused", ["sample"], "pc");
    private readonly string _directory = LedgerTestPaths.NewDirectory("coordination-images");

    internal static byte[] Png(int size, byte fill = 1) {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes.AsSpan(8).Fill(fill);
        return bytes;
    }

    [Fact]
    public void StoreKeepsRealImagesByContentAndMakesRoomOnlyFromExpiredOnes() {
        var store = new CoordinationImageStore(_directory, maximumBytes: 3000, maximumFiles: 10);
        var first = store.Put(Png(1000), Now.AddDays(-40));
        Assert.Equal((Convert.ToHexStringLower(SHA256.HashData(Png(1000))), "image/png", 1000L), (first.Id, first.MediaType, first.Bytes));
        // The same bytes are the same image.
        Assert.Equal(first.Id, store.Put(Png(1000), Now.AddDays(-40)).Id);
        Assert.Equal("image-type-unsupported", Assert.Throws<CoordinationConflictException>(() =>
            store.Put("not an image at all"u8.ToArray(), Now)).Message);
        Assert.Equal("image-too-large", Assert.Throws<CoordinationConflictException>(() =>
            store.Put(Png(CoordinationImage.MaximumBytes + 1), Now)).Message);

        var recent = store.Put(Png(1000, 2), Now);
        // A third image does not fit; only the 40-day-old one is past retention, so it alone goes.
        var third = store.Put(Png(1500, 3), Now);
        Assert.False(store.Exists(first.Id));
        Assert.True(store.Exists(recent.Id) && store.Exists(third.Id));
        // Nothing left is old enough to make room, so the store refuses rather than drop a current image.
        Assert.Equal("image-store-capacity", Assert.Throws<CoordinationConflictException>(() => store.Put(Png(1000, 4), Now)).Message);
        Assert.Null(store.Find("../" + new string('a', 61)));
    }

    [Fact]
    public void ReferencesAndTypesComeFromTheContentNotFromNames() {
        var a = new string('a', 64);
        var b = new string('b', 64);
        Assert.Equal([a, b], CoordinationImage.References("See ledger-image:" + a + " and ledger-image:" + b + ", again ledger-image:" + a +
            "; not ledger-image:" + new string('c', 63) + " or ledger-image:" + new string('A', 64)));
        Assert.Equal("image/jpeg", CoordinationImage.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.Equal("image/webp", CoordinationImage.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal("image/gif", CoordinationImage.Sniff("GIF89a"u8));
        Assert.Null(CoordinationImage.Sniff("<svg xmlns="u8));
    }

    [Fact]
    public void AVisualReviewGoesToClaudeWithItsImagesAndAFollowUpCarriesTheEarlierResult() {
        var state = new CoordinationState { Policy = new(1, [
            new("pc", 1, 1, 4, 4096, ["windows-shell"], AllowedCreateProviders: ["claude"]),
            new("mac", 2, 1, 4, 4096, ["macos-shell"], AllowedCreateProviders: ["codex"]) ]) };
        var engine = new CoordinationEngine(state, Now);
        engine.ObserveHost(new("pc", "boot", Now, ["windows-shell"], CreatableProviders: ["claude"]),
            new("pc-host", "host", "unused", ["sample"], "pc"));
        engine.ObserveHost(new("mac", "boot", Now, ["macos-shell"], CreatableProviders: ["codex"]),
            new("mac-host", "host", "unused", ["sample"], "mac"));
        // The earlier ask finishes first, so it has the machine to itself.
        CoordinationAskTests.Complete(engine, state, "first");
        var image = new string('a', 64);
        var review = engine.Ask(Ask("review", "pc") with { Output = "visual-review", Images = [image] }, Sessions);
        Assert.Equal(("claude", "visual-review"), (review.Request.RequiredProvider, review.Request.Helper!.Output));
        Assert.Equal([image], review.Request.Helper.Images!);
        Assert.Equal("ask-visual-review-requires-claude", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("codex-review", "pc") with { Output = "visual-review", Provider = "codex" }, Sessions)).Message);
        Assert.Equal("invalid-ask", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("bad-image", "pc") with { Images = ["not-an-id"] }, Sessions)).Message);

        var earlier = "{\"verdict\":\"changes\",\"images\":[\"ledger-image:" + image + " (header.png)\"]}";
        var round = engine.Ask(Ask("second-round", "pc") with { Continues = "first", Output = "visual-review" }, Sessions, null, earlier);
        Assert.Equal(("claude", null, "first", earlier), (round.Request.RequiredProvider, round.Request.Helper!.ForkSessionId,
            round.Request.Helper.ContinuesRequestId, round.Request.Helper.PreviousResult));
        // Without the earlier result there is nothing to carry forward.
        Assert.Equal("ask-continues-unavailable", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("third-round", "pc") with { Continues = "first", Output = "visual-review" }, Sessions)).Message);
    }

    [Fact]
    public void VisualReviewResultsPassOnlyOnApproveAndStayWithinTheLimitWithImages() {
        var answer = new JsonObject {
            ["outcome"] = "result", ["text"] = "Two spacing issues.",
            ["review"] = new JsonObject {
                ["verdict"] = "changes", ["summary"] = "Header spacing is off.", ["instanceReviewed"] = true,
                ["checked"] = new JsonArray("light theme", "dark theme"),
                ["findings"] = new JsonArray(Enumerable.Range(0, 60).Select(i => (JsonNode)new JsonObject {
                    ["severity"] = i == 59 ? "high" : "low", ["area"] = "Header " + i, ["issue"] = new string('x', 200),
                    ["suggestion"] = "Align to the grid.", ["image"] = "header.png" }).ToArray())
            }
        }.ToJsonString();
        var result = JsonNode.Parse(AgentService.VisualReviewResult(answer, "session-1")!)!.AsObject();
        Assert.Equal((false, "changes", true), ((bool)result["pass"]!, (string)result["verdict"]!, (bool)result["instanceReviewed"]!));
        // Lowest-severity findings make way first: the high one is kept.
        Assert.Equal("high", (string)result["findings"]![0]!["severity"]!);
        Assert.NotNull(result["truncated"]);

        var references = Enumerable.Range(0, 8).Select(i => "ledger-image:" + new string("0123456789abcdef"[i], 64) + " (frame-" + i + ".png)").ToArray();
        var withImages = AgentService.WithImages(result.ToJsonString(), references);
        Assert.True(Encoding.UTF8.GetByteCount(withImages) <= 8192);
        Assert.Equal(8, CoordinationImage.References(withImages).Length);
        var text = AgentService.WithImages(new string('y', 8192), references);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 8192);
        Assert.EndsWith(references[^1], text);
        Assert.Equal("{\"pass\":true}", AgentService.WithImages("{\"pass\":true}", []));
        Assert.Equal("blocked", (string)JsonNode.Parse(AgentService.VisualReviewWithoutVerdict("The app would not start.", "s"))!["verdict"]!);
    }

    [Fact]
    public void ThePromptListsTheImagesTheEarlierResultAndWhereToReturnPictures() {
        var state = new CoordinationState { Policy = new(1, [
            new("mac", 2, 1, 4, 4096, ["macos-shell"], AllowedCreateProviders: ["codex", "claude"]) ]) };
        var engine = new CoordinationEngine(state, Now);
        var image = new string('a', 64);
        var work = engine.Ask(Ask("review") with { Output = "visual-review", Images = [image] }, Sessions);
        var inbound = Path.Combine(_directory, AgentService.ImagesIn);
        Directory.CreateDirectory(inbound);
        File.WriteAllBytes(Path.Combine(inbound, "01-" + image + ".png"), Png(16));

        var prompt = AgentService.HelperPrompt(work, _directory);
        Assert.Contains(Path.Combine(inbound, "01-" + image + ".png"), prompt);
        Assert.Contains(Path.Combine(_directory, AgentService.ImagesOut), prompt);
        Assert.Contains("This is a visual review", prompt);
        Assert.DoesNotContain("Push anything worth keeping", prompt);
        Assert.DoesNotContain("which is the conversation above", prompt);
    }

    [Fact]
    public async Task ImagesLargerThanAnOrdinaryCommandTravelThroughTheServiceAndIntoAnAsk() {
        var sessionsToken = new string('s', 32);
        var hostToken = new string('h', 32);
        var screenshot = Png(1536 * 1024, 7);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 1, 1, 4, 4096, ["macos-shell"], AllowedCreateProviders: ["claude"])]),
            Principals = [
                new("sessions", "client", sessionsToken, ["sample"], "synthetic-machine"),
                new("host", "host", hostToken, ["sample"], "synthetic-machine")
            ]
        }, async (endpoint, _, token) => {
            using var sessions = LedgerReadClient.ForCoordination(new(endpoint, sessionsToken));
            using var host = LedgerReadClient.ForCoordination(new(endpoint, hostToken));
            // Bigger than the service's 1 MiB limit for other requests.
            var image = await sessions.UploadImageAsync(screenshot, token);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(screenshot)), image.Id);
            var file = await host.DownloadImageAsync(image.Id, _directory, token, "01-");
            Assert.Equal(screenshot, await File.ReadAllBytesAsync(file, token));
            Assert.Equal("01-" + image.Id + ".png", Path.GetFileName(file));

            var rejected = await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() =>
                sessions.AskAsync(Ask("unknown-image", "synthetic-machine") with { Images = [new string('f', 64)] }, token));
            Assert.Equal("ask-image-unknown", rejected.ErrorCode);
            var work = await sessions.AskAsync(Ask("with-image", "synthetic-machine") with { Output = "visual-review", Images = [image.Id] }, token);
            Assert.Equal(("claude", image.Id), (work.Request.RequiredProvider, Assert.Single(work.Request.Helper!.Images!)));
        });
    }

    [Theory]
    [InlineData("EvotecIT/Marquee#12", "EvotecIT/Marquee#12")]
    [InlineData("https://github.com/EvotecIT/Marquee/pull/12", "EvotecIT/Marquee#12")]
    [InlineData("https://github.com/EvotecIT/Marquee/pull/12/", "EvotecIT/Marquee#12")]
    [InlineData("https://example.test/EvotecIT/Marquee/pull/12", null)]
    [InlineData("EvotecIT/Marquee#0", null)]
    [InlineData("Marquee#12", null)]
    public void PullRequestsAreNamedByOwnerNameAndNumber(string value, string? expected) =>
        Assert.Equal(expected, CoordinationAsk.PullRequestReference(value));

    [Fact]
    public void OnlyAVisualReviewPostsToAPullRequestAndItsCommentReadsAsAReview() {
        var state = new CoordinationState { Policy = new(1, [
            new("pc", 1, 1, 4, 4096, ["windows-shell"], AllowedCreateProviders: ["claude", "codex"]) ]) };
        var engine = new CoordinationEngine(state, Now);
        Assert.Equal("ask-pull-request-requires-visual-review", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("plain", "pc") with { PullRequest = "EvotecIT/Marquee#12" }, Sessions)).Message);
        var work = engine.Ask(Ask("visual", "pc") with { Output = "visual-review", PullRequest = "https://github.com/EvotecIT/Marquee/pull/12" }, Sessions);
        Assert.Equal("EvotecIT/Marquee#12", work.Request.Helper!.PullRequest);

        var result = new JsonObject {
            ["pass"] = false, ["verdict"] = "changes", ["summary"] = "The header crowds the logo.", ["instanceReviewed"] = true,
            ["checked"] = new JsonArray("light theme", "dark theme"),
            ["findings"] = new JsonArray(new JsonObject { ["severity"] = "high", ["area"] = "Header", ["issue"] = "The logo touches the edge.",
                ["suggestion"] = "Add 16 px of padding.", ["image"] = "header.png" })
        }.ToJsonString();
        var comment = AgentService.PullRequestComment(result, ["header.png"]);
        Assert.StartsWith("**Design review: changes needed**", comment);
        Assert.Contains("- **High**, Header: The logo touches the edge. Add 16 px of padding. (header.png)", comment);
        Assert.Contains("Checked: light theme, dark theme.", comment);
        Assert.Contains("Reviewed on the running product.", comment);
        Assert.EndsWith("![header.png](header.png)" + Environment.NewLine, comment);
        Assert.Equal("https://github.com/EvotecIT/Marquee/pull/12#issuecomment-1",
            (string)JsonNode.Parse(AgentService.WithNote(result, "pullRequestComment", "https://github.com/EvotecIT/Marquee/pull/12#issuecomment-1"))!["pullRequestComment"]!);
        Assert.EndsWith("\n\nnot posted", AgentService.WithNote("Plain text.", "pullRequestComment", "not posted"));
    }

    private static CoordinationAsk Ask(string id, string machine = "mac") =>
        new(id, "Review the dashboard layout.", "sample", "main", Commit, "codex", "requester-thread", machine);

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
