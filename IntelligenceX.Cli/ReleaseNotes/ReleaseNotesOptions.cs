using System;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Cli.ReleaseNotes;

internal sealed class ReleaseNotesOptions {
    public string? FromTag { get; set; }
    public string? ToRef { get; set; }
    public string? Version { get; set; }
    public string? OutputPath { get; set; }
    public string? ChangelogPath { get; set; }
    public bool UpdateChangelog { get; set; }
    public int MaxCommits { get; set; } = 200;
    public string? Model { get; set; }
    public OpenAITransportKind? Transport { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int RetryMaxDelaySeconds { get; set; } = 30;
    public bool Commit { get; set; }
    public bool CommitSet { get; set; }
    public bool CreatePr { get; set; }
    public bool CreatePrSet { get; set; }
    public string? PrBranch { get; set; }
    public string? PrTitle { get; set; }
    public string? PrBody { get; set; }
    public string? PrLabels { get; set; }
    public bool SkipReview { get; set; }
    public bool SkipReviewSet { get; set; }
    public string? RepoSlug { get; set; }
    public bool DryRun { get; set; }
    public bool ShowHelp { get; set; }
    public string? RepoPath { get; set; }

    public static ReleaseNotesOptions Parse(string[] args) {
        var options = new ReleaseNotesOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg is "-h" or "--help") {
                options.ShowHelp = true;
                return options;
            }
            switch (arg) {
                case "--from":
                    options.FromTag = ReadValue(args, ref i);
                    break;
                case "--to":
                    options.ToRef = ReadValue(args, ref i);
                    break;
                case "--version":
                    options.Version = ReadValue(args, ref i);
                    break;
                case "--output":
                    options.OutputPath = ReadValue(args, ref i);
                    break;
                case "--changelog":
                    options.ChangelogPath = ReadValue(args, ref i);
                    break;
                case "--update-changelog":
                    options.UpdateChangelog = true;
                    break;
                case "--max-commits":
                    options.MaxCommits = ReadIntValue(args, ref i, options.MaxCommits);
                    break;
                case "--model":
                    options.Model = ReadValue(args, ref i);
                    break;
                case "--transport":
                    options.Transport = ParseTransportValue(ReadValue(args, ref i));
                    break;
                case "--reasoning-effort":
                    options.ReasoningEffort = ChatEnumParser.ParseReasoningEffort(ReadValue(args, ref i));
                    break;
                case "--reasoning-summary":
                    options.ReasoningSummary = ChatEnumParser.ParseReasoningSummary(ReadValue(args, ref i));
                    break;
                case "--retry-count":
                    options.RetryCount = ReadIntValue(args, ref i, options.RetryCount);
                    break;
                case "--retry-delay-seconds":
                    options.RetryDelaySeconds = ReadIntValue(args, ref i, options.RetryDelaySeconds);
                    break;
                case "--retry-max-delay-seconds":
                    options.RetryMaxDelaySeconds = ReadIntValue(args, ref i, options.RetryMaxDelaySeconds);
                    break;
                case "--commit":
                    options.CommitSet = true;
                    options.Commit = ReadBoolFlag(args, ref i, "--commit", true);
                    break;
                case "--create-pr":
                    options.CreatePrSet = true;
                    options.CreatePr = ReadBoolFlag(args, ref i, "--create-pr", true);
                    break;
                case "--pr-branch":
                    options.PrBranch = ReadValue(args, ref i);
                    break;
                case "--pr-title":
                    options.PrTitle = ReadValue(args, ref i);
                    break;
                case "--pr-body":
                    options.PrBody = ReadValue(args, ref i);
                    break;
                case "--pr-labels":
                    options.PrLabels = ReadValue(args, ref i);
                    break;
                case "--skip-review":
                    options.SkipReviewSet = true;
                    options.SkipReview = ReadBoolFlag(args, ref i, "--skip-review", true);
                    break;
                case "--repo-slug":
                    options.RepoSlug = ReadValue(args, ref i);
                    break;
                case "--repo":
                    options.RepoPath = ReadValue(args, ref i);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
            }
        }

        return options;
    }

    private static string ReadValue(string[] args, ref int index) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"Missing value for {args[index]}.");
        }
        index++;
        return args[index];
    }

    private static int ReadIntValue(string[] args, ref int index, int fallback) {
        var value = ReadValue(args, ref index);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ReadBoolFlag(string[] args, ref int index, string flagName, bool defaultValue) {
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
            var raw = args[++index];
            if (string.IsNullOrWhiteSpace(raw)) {
                return defaultValue;
            }
            if (bool.TryParse(raw, out var parsed)) {
                return parsed;
            }
            throw new InvalidOperationException($"Invalid value for {flagName}: {raw}");
        }
        return defaultValue;
    }

    internal static OpenAITransportKind? ParseTransportValue(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => null
        };
    }

    public static void ApplyEnvDefaults(ReleaseNotesOptions options) {
        if (options is null) {
            return;
        }

        options.FromTag ??= ReadEnv("INTELLIGENCEX_RELEASE_FROM");
        options.ToRef ??= ReadEnv("INTELLIGENCEX_RELEASE_TO");
        options.Version ??= ReadEnv("INTELLIGENCEX_RELEASE_VERSION");
        options.OutputPath ??= ReadEnv("INTELLIGENCEX_RELEASE_OUTPUT");
        options.ChangelogPath ??= ReadEnv("INTELLIGENCEX_RELEASE_CHANGELOG");
        if (!options.UpdateChangelog) {
            options.UpdateChangelog = ReadEnvBool("INTELLIGENCEX_RELEASE_UPDATE_CHANGELOG") ?? options.UpdateChangelog;
        }
        options.PrBranch ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_BRANCH");
        options.PrTitle ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_TITLE");
        options.PrBody ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_BODY");
        options.PrLabels ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_LABELS");
        options.RepoSlug ??= ReadEnv("INTELLIGENCEX_RELEASE_REPO_SLUG");

        if (!options.CommitSet) {
            var commit = ReadEnvBool("INTELLIGENCEX_RELEASE_COMMIT");
            if (commit.HasValue) {
                options.Commit = commit.Value;
            }
        }
        if (!options.CreatePrSet) {
            var create = ReadEnvBool("INTELLIGENCEX_RELEASE_CREATE_PR");
            if (create.HasValue) {
                options.CreatePr = create.Value;
            }
        }
        if (!options.SkipReviewSet) {
            var skip = ReadEnvBool("INTELLIGENCEX_RELEASE_SKIP_REVIEW");
            if (skip.HasValue) {
                options.SkipReview = skip.Value;
                options.SkipReviewSet = true;
            }
        }
    }

    private static string? ReadEnv(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? ReadEnvBool(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }
}
