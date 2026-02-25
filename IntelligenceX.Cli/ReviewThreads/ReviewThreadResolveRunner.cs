using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Cli.ReviewThreads;

internal static class ReviewThreadResolveRunner {
    private static readonly IReadOnlyList<string> DefaultBotLogins = new[] {
        "intelligencex-review",
        "chatgpt-codex-connector",
        "copilot-pull-request-reviewer",
        "github-actions"
    };
    private const int FullThreadCommentScanLimit = 500;

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        if (!TryResolveRepo(options, out var owner, out var repo)) {
            Console.Error.WriteLine("Missing repo. Use --repo owner/name or set GITHUB_REPOSITORY.");
            return 1;
        }

        if (!TryResolvePrNumber(options, out var prNumber)) {
            Console.Error.WriteLine("Missing PR number. Use --pr <number> or set INTELLIGENCEX_PR_NUMBER.");
            return 1;
        }

        var token = ResolveGitHubToken(options.Token);
        if (string.IsNullOrWhiteSpace(token)) {
            Console.Error.WriteLine("Missing GitHub token. Use --github-token (or --token) or set INTELLIGENCEX_GITHUB_TOKEN/GITHUB_TOKEN/GH_TOKEN.");
            return 1;
        }

        var botLogins = ResolveBotLogins(options);
        using var client = new ReviewThreadClient(token!, options.ApiBaseUrl);
        using var cts = options.TimeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds))
            : new CancellationTokenSource();
        var tokenSource = cts.Token;
        var threads = await client.ListReviewThreadsAsync(owner, repo, prNumber, options.MaxThreads, options.MaxComments,
            tokenSource).ConfigureAwait(false);

        var eligible = await FilterThreadsAsync(threads, botLogins, options, client, tokenSource).ConfigureAwait(false);
        if (eligible.Count == 0) {
            Console.WriteLine("No review threads matched the criteria.");
            return 0;
        }

        if (options.DryRun) {
            Console.WriteLine($"Dry-run: would resolve {eligible.Count} thread(s).");
            foreach (var thread in eligible) {
                Console.WriteLine($"- {thread.Id} (outdated: {thread.IsOutdated})");
            }
            return 0;
        }

        var resolved = 0;
        var failed = 0;
        foreach (var thread in eligible) {
            if (resolved >= options.ResolveMax) {
                break;
            }
            try {
                await client.ResolveThreadAsync(thread.Id, tokenSource).ConfigureAwait(false);
                resolved++;
            } catch (Exception ex) {
                failed++;
                Console.Error.WriteLine($"Failed to resolve thread {thread.Id}: {ex.Message}");
            }
        }

        Console.WriteLine($"Resolved {resolved} thread(s).");
        if (failed > 0) {
            Console.Error.WriteLine($"Failed to resolve {failed} thread(s).");
            return 1;
        }
        return 0;
    }

    private static async Task<List<ReviewThread>> FilterThreadsAsync(IEnumerable<ReviewThread> threads,
        IReadOnlyList<string> botLogins, Options options, ReviewThreadClient client, CancellationToken cancellationToken) {
        var eligible = new List<ReviewThread>();
        foreach (var thread in threads) {
            if (thread.IsResolved) {
                continue;
            }
            if (options.OnlyOutdated && !thread.IsOutdated) {
                continue;
            }
            var candidate = thread;
            if (options.BotOnly && candidate.TotalComments > candidate.Comments.Count) {
                candidate = await client.HydrateThreadCommentsAsync(candidate, FullThreadCommentScanLimit, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate.TotalComments > candidate.Comments.Count) {
                    Console.Error.WriteLine(
                        $"Skipping thread {thread.Id}: comment view incomplete ({candidate.Comments.Count}/{candidate.TotalComments}).");
                    continue;
                }
            }
            if (options.BotOnly && !ThreadHasOnlyBotComments(candidate, botLogins)) {
                continue;
            }
            eligible.Add(candidate);
        }
        return eligible;
    }

    private static bool ThreadHasOnlyBotComments(ReviewThread thread, IReadOnlyList<string> botLogins) {
        if (thread.Comments.Count == 0) {
            return false;
        }
        return thread.Comments.All(comment =>
            IsBotMatch(comment.Author, botLogins));
    }

    private static bool IsBotMatch(string? author, IReadOnlyList<string> botLogins) {
        if (string.IsNullOrWhiteSpace(author) || botLogins.Count == 0) {
            return false;
        }
        var normalizedAuthor = NormalizeBotLogin(author);
        if (string.IsNullOrWhiteSpace(normalizedAuthor)) {
            return false;
        }
        foreach (var login in botLogins) {
            if (string.IsNullOrWhiteSpace(login)) {
                continue;
            }
            var normalizedBot = NormalizeBotLogin(login);
            if (string.IsNullOrWhiteSpace(normalizedBot)) {
                continue;
            }
            if (string.Equals(normalizedAuthor, normalizedBot, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeBotLogin(string login) {
        var trimmed = login.Trim();
        if (trimmed.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(0, trimmed.Length - "[bot]".Length).TrimEnd();
        }
        return trimmed;
    }

    internal static IReadOnlyList<string> ResolveBotLogins(Options options) {
        var source = options.BotLogins.Count > 0
            ? options.BotLogins
            : DefaultBotLogins;
        var normalized = new List<string>(source.Count);
        foreach (var bot in source) {
            var login = NormalizeBotLogin(bot);
            if (string.IsNullOrWhiteSpace(login)) {
                continue;
            }
            if (!normalized.Contains(login, StringComparer.OrdinalIgnoreCase)) {
                normalized.Add(login);
            }
        }
        return normalized;
    }

    private static bool TryResolveRepo(Options options, out string owner, out string repo) {
        owner = string.Empty;
        repo = string.Empty;
        var source = options.Repo
                     ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
                     ?? ReadRepoFromEventPath();
        if (string.IsNullOrWhiteSpace(source)) {
            return false;
        }
        return TryParseRepo(source, out owner, out repo);
    }

    private static string? ReadRepoFromEventPath() {
        var path = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return null;
        }
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("repository", out var repoObj) &&
                repoObj.TryGetProperty("full_name", out var fullName)) {
                return fullName.GetString();
            }
        } catch {
            return null;
        }
        return null;
    }

    private static bool TryResolvePrNumber(Options options, out int prNumber) {
        prNumber = 0;
        if (options.PrNumber > 0) {
            prNumber = options.PrNumber;
            return true;
        }
        var envValue = Environment.GetEnvironmentVariable("INTELLIGENCEX_PR_NUMBER")
                       ?? Environment.GetEnvironmentVariable("PR_NUMBER");
        if (!string.IsNullOrWhiteSpace(envValue) && int.TryParse(envValue, out prNumber) && prNumber > 0) {
            return true;
        }
        var fromEvent = ReadPrNumberFromEventPath();
        if (fromEvent > 0) {
            prNumber = fromEvent;
            return true;
        }
        return false;
    }

    private static int ReadPrNumberFromEventPath() {
        var path = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return 0;
        }
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("pull_request", out var prObj) &&
                prObj.TryGetProperty("number", out var number)) {
                return number.GetInt32();
            }
            if (doc.RootElement.TryGetProperty("number", out var fallback)) {
                return fallback.GetInt32();
            }
        } catch {
            return 0;
        }
        return 0;
    }

    private static bool TryParseRepo(string repo, out string owner, out string name) {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(repo)) {
            return false;
        }
        var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        name = parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(name);
    }

    private static string? ResolveGitHubToken(string? direct) {
        if (!string.IsNullOrWhiteSpace(direct)) {
            return direct;
        }
        return Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GH_TOKEN");
    }

    internal static Options ParseOptions(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--pr":
                case "--pull":
                case "--pr-number":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var prNumber)) {
                        options.PrNumber = prNumber;
                    }
                    break;
                case "--token":
                case "--github-token":
                    if (i + 1 < args.Length) {
                        options.Token = args[++i];
                    }
                    break;
                case "--bot":
                case "--bot-login":
                    if (i + 1 < args.Length) {
                        AddBotLogins(options.BotLogins, args[++i]);
                    }
                    break;
                case "--include-human":
                    options.BotOnly = false;
                    break;
                case "--include-current":
                    options.OnlyOutdated = false;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--max-threads":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var maxThreads)) {
                        options.MaxThreads = Math.Max(1, maxThreads);
                    }
                    break;
                case "--max-comments":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var maxComments)) {
                        options.MaxComments = Math.Max(1, maxComments);
                    }
                    break;
                case "--resolve-max":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var resolveMax)) {
                        options.ResolveMax = Math.Max(1, resolveMax);
                    }
                    break;
                case "--timeout":
                case "--timeout-seconds":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var timeoutSeconds)) {
                        options.TimeoutSeconds = Math.Max(1, timeoutSeconds);
                    }
                    break;
                case "--api-base-url":
                    if (i + 1 < args.Length) {
                        options.ApiBaseUrl = args[++i];
                    }
                    break;
                case "help":
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
            }
        }
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Reviewer thread resolver:");
        Console.WriteLine("  intelligencex reviewer resolve-threads [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>      Target repo (defaults to GITHUB_REPOSITORY)");
        Console.WriteLine("  --pr <number>            Pull request number");
        Console.WriteLine("  --github-token <token>   GitHub token (or set INTELLIGENCEX_GITHUB_TOKEN/GITHUB_TOKEN/GH_TOKEN)");
        Console.WriteLine("  --token <token>          Alias for --github-token");
        Console.WriteLine("  --bot <login>            Bot login to match (repeatable or comma-separated)");
        Console.WriteLine("  --include-human          Allow resolving threads with non-bot comments");
        Console.WriteLine("  --include-current        Resolve non-outdated threads too");
        Console.WriteLine("  --max-threads <n>         Max threads to scan (default: 50)");
        Console.WriteLine("  --max-comments <n>        Max comments per thread (default: 20)");
        Console.WriteLine("  --resolve-max <n>         Max threads to resolve (default: 20)");
        Console.WriteLine("  --timeout-seconds <n>     Overall timeout (default: 60)");
        Console.WriteLine("  --api-base-url <url>     GitHub API base (default: https://api.github.com)");
        Console.WriteLine("  --dry-run                Preview without resolving");
    }

    internal sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Repo { get; set; }
        public int PrNumber { get; set; }
        public string? Token { get; set; }
        public List<string> BotLogins { get; } = new();
        public bool BotOnly { get; set; } = true;
        public bool OnlyOutdated { get; set; } = true;
        public bool DryRun { get; set; }
        public int MaxThreads { get; set; } = 50;
        public int MaxComments { get; set; } = 20;
        public int ResolveMax { get; set; } = 20;
        public int TimeoutSeconds { get; set; } = 60;
        public string? ApiBaseUrl { get; set; }
    }

    private static void AddBotLogins(List<string> botLogins, string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts) {
            if (!string.IsNullOrWhiteSpace(part)) {
                botLogins.Add(part);
            }
        }
    }

    private sealed class ReviewThreadClient : IDisposable {
        private readonly HttpClient _http;
        private readonly string _graphQlPath;

        public ReviewThreadClient(string token, string? apiBaseUrl) {
            var (baseUri, graphQlPath) = ResolveGraphQlEndpoint(apiBaseUrl);
            _http = new HttpClient { BaseAddress = baseUri };
            _graphQlPath = graphQlPath;
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<IReadOnlyList<ReviewThread>> ListReviewThreadsAsync(string owner, string repo, int number,
            int maxThreads, int maxComments, CancellationToken cancellationToken) {
            if (maxThreads <= 0 || maxComments <= 0) {
                return Array.Empty<ReviewThread>();
            }
            var threads = new List<ReviewThread>();
            var commentLimit = Math.Min(maxComments, 100);
            string? cursor = null;
            while (threads.Count < maxThreads) {
                var payload = new JsonObject()
                    .Add("query", @"query($owner:String!,$name:String!,$number:Int!,$cursor:String,$commentLimit:Int!){
  repository(owner:$owner,name:$name){
    pullRequest(number:$number){
      reviewThreads(first:50, after:$cursor){
        nodes{
          id
          isResolved
          isOutdated
          comments(first:$commentLimit){
            totalCount
            nodes{
              author{ login }
            }
          }
        }
        pageInfo{ hasNextPage endCursor }
      }
    }
  }
}")
                    .Add("variables", new JsonObject()
                        .Add("owner", owner)
                        .Add("name", repo)
                        .Add("number", number)
                        .Add("cursor", cursor)
                        .Add("commentLimit", commentLimit));

                var response = await PostGraphQlAsync(payload, cancellationToken).ConfigureAwait(false);
                var root = response.AsObject();
                var nodes = root?.GetObject("data")?
                    .GetObject("repository")?
                    .GetObject("pullRequest")?
                    .GetObject("reviewThreads")?
                    .GetArray("nodes");
                if (nodes is null || nodes.Count == 0) {
                    break;
                }

                foreach (var node in nodes) {
                    if (threads.Count >= maxThreads) {
                        break;
                    }
                    var obj = node.AsObject();
                    if (obj is null) {
                        continue;
                    }
                    var id = obj.GetString("id") ?? string.Empty;
                    var isResolved = obj.GetBoolean("isResolved");
                    var isOutdated = obj.GetBoolean("isOutdated");
                    var commentsObj = obj.GetObject("comments");
                    var totalComments = (int)(commentsObj?.GetInt64("totalCount") ?? 0);
                    var commentNodes = commentsObj?.GetArray("nodes") ?? new JsonArray();
                    var comments = new List<ReviewThreadComment>();
                    foreach (var comment in commentNodes) {
                        if (comments.Count >= commentLimit) {
                            break;
                        }
                        var commentObj = comment.AsObject();
                        if (commentObj is null) {
                            continue;
                        }
                        var author = commentObj.GetObject("author")?.GetString("login");
                        comments.Add(new ReviewThreadComment(author));
                    }
                    threads.Add(new ReviewThread(id, isResolved, isOutdated, totalComments, comments));
                }

                var pageInfo = root?.GetObject("data")?
                    .GetObject("repository")?
                    .GetObject("pullRequest")?
                    .GetObject("reviewThreads")?
                    .GetObject("pageInfo");
                var hasNext = pageInfo?.GetBoolean("hasNextPage") ?? false;
                if (!hasNext) {
                    break;
                }
                cursor = pageInfo?.GetString("endCursor");
                if (string.IsNullOrWhiteSpace(cursor)) {
                    break;
                }
            }
            return threads;
        }

        public async Task<ReviewThread> HydrateThreadCommentsAsync(ReviewThread thread, int maxComments,
            CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(thread.Id) || maxComments <= 0) {
                return thread;
            }
            var comments = new List<ReviewThreadComment>();
            var effectiveMaxComments = Math.Max(1, maxComments);
            string? cursor = null;
            var totalComments = thread.TotalComments;
            var isResolved = thread.IsResolved;
            var isOutdated = thread.IsOutdated;
            while (comments.Count < effectiveMaxComments) {
                var first = Math.Min(100, effectiveMaxComments - comments.Count);
                var payload = new JsonObject()
                    .Add("query", @"query($id:ID!,$cursor:String,$first:Int!){
  node(id:$id){
    ... on PullRequestReviewThread{
      id
      isResolved
      isOutdated
      comments(first:$first, after:$cursor){
        totalCount
        nodes{
          author{ login }
        }
        pageInfo{ hasNextPage endCursor }
      }
    }
  }
}")
                    .Add("variables", new JsonObject()
                        .Add("id", thread.Id)
                        .Add("cursor", cursor)
                        .Add("first", first));
                var response = await PostGraphQlAsync(payload, cancellationToken).ConfigureAwait(false);
                var threadNode = response.AsObject()?
                    .GetObject("data")?
                    .GetObject("node");
                if (threadNode is null) {
                    break;
                }

                isResolved = threadNode.GetBoolean("isResolved");
                isOutdated = threadNode.GetBoolean("isOutdated");
                var commentsObj = threadNode.GetObject("comments");
                totalComments = (int)(commentsObj?.GetInt64("totalCount") ?? totalComments);
                var nodes = commentsObj?.GetArray("nodes");
                if (nodes is not null) {
                    foreach (var node in nodes) {
                        if (comments.Count >= effectiveMaxComments) {
                            break;
                        }
                        var obj = node.AsObject();
                        if (obj is null) {
                            continue;
                        }
                        var author = obj.GetObject("author")?.GetString("login");
                        comments.Add(new ReviewThreadComment(author));
                    }
                }

                var pageInfo = commentsObj?.GetObject("pageInfo");
                var hasNext = pageInfo?.GetBoolean("hasNextPage") ?? false;
                if (!hasNext || comments.Count >= totalComments) {
                    break;
                }
                cursor = pageInfo?.GetString("endCursor");
                if (string.IsNullOrWhiteSpace(cursor)) {
                    break;
                }
            }

            return new ReviewThread(thread.Id, isResolved, isOutdated, totalComments, comments);
        }

        public async Task ResolveThreadAsync(string threadId, CancellationToken cancellationToken) {
            var payload = new JsonObject()
                .Add("query", @"mutation($id:ID!){
  resolveReviewThread(input:{threadId:$id}){ thread{ id isResolved } }
}")
                .Add("variables", new JsonObject().Add("id", threadId));
            await PostGraphQlAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        private async Task<JsonValue> PostGraphQlAsync(JsonObject payload, CancellationToken cancellationToken) {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_graphQlPath, content, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
            var parsed = JsonLite.Parse(responseText) ?? JsonValue.Null;
            var errors = parsed.AsObject()?.GetArray("errors");
            if (errors is not null && errors.Count > 0) {
                throw new InvalidOperationException($"GitHub GraphQL request returned errors: {responseText}");
            }
            return parsed;
        }

        public void Dispose() => _http.Dispose();
    }

    internal static (Uri BaseUri, string GraphQlPath) ResolveGraphQlEndpoint(string? apiBaseUrl) {
        var raw = string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.github.com" : apiBaseUrl!.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) {
            return (new Uri("https://api.github.com"), "/graphql");
        }
        var path = uri.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase)) {
            var rootPath = path.Substring(0, path.Length - "/api/v3".Length);
            return (uri, $"{NormalizePathPrefix(rootPath)}/api/graphql");
        }

        if (path.EndsWith("/api/graphql", StringComparison.OrdinalIgnoreCase)) {
            var rootPath = path.Substring(0, path.Length - "/api/graphql".Length);
            return (uri, $"{NormalizePathPrefix(rootPath)}/api/graphql");
        }

        if (path.EndsWith("/graphql", StringComparison.OrdinalIgnoreCase)) {
            var rootPath = path.Substring(0, path.Length - "/graphql".Length);
            return (uri, $"{NormalizePathPrefix(rootPath)}/graphql");
        }

        return (uri, "/graphql");
    }

    private static string NormalizePathPrefix(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }
        if (!path.StartsWith("/", StringComparison.Ordinal)) {
            return "/" + path;
        }
        return path;
    }

    private sealed record ReviewThread(string Id, bool IsResolved, bool IsOutdated, int TotalComments,
        IReadOnlyList<ReviewThreadComment> Comments);

    private sealed record ReviewThreadComment(string? Author);
}
