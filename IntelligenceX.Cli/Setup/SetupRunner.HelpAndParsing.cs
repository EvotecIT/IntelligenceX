using System;
using System.Globalization;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private static void WriteHelp() {
        Console.WriteLine("IntelligenceX setup");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --github-client-id <id>");
        Console.WriteLine("  --github-token <token>");
        Console.WriteLine("  --github-api-base-url <url> (default https://api.github.com)");
        Console.WriteLine("  --github-auth-base-url <url> (default https://github.com)");
        Console.WriteLine($"  --actions-repo <owner/repo> (default {DefaultActionsRepo})");
        Console.WriteLine($"  --actions-ref <ref> (default {DefaultActionsRef})");
        Console.WriteLine($"  --runs-on <json-array|expression> (default {DefaultRunsOn})");
        Console.WriteLine("  --reviewer-source <source|release> (default release)");
        Console.WriteLine("  --reviewer-release-repo <owner/repo> (default EvotecIT/github-actions)");
        Console.WriteLine("  --reviewer-release-tag <tag> (default latest)");
        Console.WriteLine("  --reviewer-release-asset <name>");
        Console.WriteLine("  --reviewer-release-url <url>");
        Console.WriteLine("  --provider <openai|copilot> (default openai)");
        Console.WriteLine("  --openai-model <model>");
        Console.WriteLine("  --openai-transport <native|appserver>");
        Console.WriteLine("  --openai-account-id <id> (pin ChatGPT account when multiple are present)");
        Console.WriteLine("  --openai-account-ids <id1,id2,...> (ordered account rotation/failover list)");
        Console.WriteLine("  --openai-account-rotation <first-available|round-robin|sticky>");
        Console.WriteLine("  --openai-account-failover <true|false>");
        Console.WriteLine("  --include-issue-comments <true|false>");
        Console.WriteLine("  --include-review-comments <true|false>");
        Console.WriteLine("  --include-related-prs <true|false>");
        Console.WriteLine("  --progress-updates <true|false>");
        Console.WriteLine("  --review-intent <security|performance|maintainability>");
        Console.WriteLine("  --review-strictness <label>");
        Console.WriteLine("  --review-profile <balanced|picky|highlevel|security|performance|tests|minimal>");
        Console.WriteLine("  --review-loop-policy <strict|balanced|lenient|todo-only|vision>");
        Console.WriteLine("  --review-vision-path <path> (used by --review-loop-policy vision to infer intent/strictness)");
        Console.WriteLine("  --merge-blocker-sections <section1,section2>");
        Console.WriteLine("  --merge-blocker-require-all-sections <true|false>");
        Console.WriteLine("  --merge-blocker-require-section-match <true|false>");
        Console.WriteLine("  --review-mode <hybrid|summary|inline>");
        Console.WriteLine("  --review-comment-mode <sticky|fresh>");
        Console.WriteLine("  --analysis-enabled <true|false> (write analysis section into reviewer.json)");
        Console.WriteLine("  --analysis-gate <true|false> (when true, analysis gate can fail CI; default false)");
        Console.WriteLine("  --analysis-run-strict <true|false> (when true, analyze run fails on runner/tool errors)");
        Console.WriteLine("  --analysis-packs <id1,id2> (default all-50 when analysis is enabled)");
        Console.WriteLine("  --analysis-export-path <repo/path> (optional: export analyzer configs into PR for IDE support)");
        Console.WriteLine("  --config-path <path> (use custom config.json content)");
        Console.WriteLine("  --config-json <json> (use inline config.json content)");
        Console.WriteLine("  --auth-b64 <value> (use pre-exported auth bundle)");
        Console.WriteLine("  --auth-b64-path <path> (read pre-exported auth bundle)");
        Console.WriteLine("  --diagnostics <true|false>");
        Console.WriteLine("  --preflight <true|false>");
        Console.WriteLine("  --preflight-timeout-seconds <number>");
        Console.WriteLine("  --cleanup-enabled <true|false>");
        Console.WriteLine("  --cleanup-mode <comment|edit|hybrid>");
        Console.WriteLine("  --cleanup-scope <pr|issue|both>");
        Console.WriteLine("  --cleanup-require-label <label>");
        Console.WriteLine("  --cleanup-min-confidence <0-1>");
        Console.WriteLine("  --cleanup-allowed-edits <comma-list>");
        Console.WriteLine("  --cleanup-post-edit-comment <true|false>");
        Console.WriteLine("  --with-config (also write .intelligencex/reviewer.json)");
        Console.WriteLine("  --triage-bootstrap (also bootstrap IX triage project schema + workflow + VISION.md + labels + assistive issues + links comment)");
        Console.WriteLine("  --upgrade (update managed sections instead of skipping)");
        Console.WriteLine("  --update-secret (refresh INTELLIGENCEX_AUTH_B64 only)");
        Console.WriteLine("  --skip-secret (skip secret update during setup)");
        Console.WriteLine("  --manual-secret (write secret to local temp file instead of uploading)");
        Console.WriteLine("  --manual-secret-stdout (print secret to stdout; requires --manual-secret)");
        Console.WriteLine("  --cleanup (remove workflow/config and optionally secret)");
        Console.WriteLine("  --keep-secret (do not delete secret during cleanup)");
        Console.WriteLine("  --branch <name>");
        Console.WriteLine("  --force (overwrite existing files)");
        Console.WriteLine("  --dry-run (show changes only)");
        Console.WriteLine("  --explicit-secrets (use explicit secrets block in workflow; default true)");
        Console.WriteLine("  --help");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_CLIENT_ID");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_TOKEN (or GITHUB_TOKEN / GH_TOKEN)");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_API_BASE_URL");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_AUTH_BASE_URL");
        Console.WriteLine("  INTELLIGENCEX_OPENAI_ACCOUNT_ID");
        Console.WriteLine("  INTELLIGENCEX_OPENAI_ACCOUNT_IDS");
        Console.WriteLine("  INTELLIGENCEX_OPENAI_ACCOUNT_ROTATION");
        Console.WriteLine("  INTELLIGENCEX_OPENAI_ACCOUNT_FAILOVER");
    }

    private static bool ParseBool(string? value, bool fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return value.Trim().ToLowerInvariant() switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => fallback
        };
    }

    private static double ParseDouble(string? value, double fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }
        return fallback;
    }

    private static int ParseInt(string? value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }
        return fallback;
    }
}
