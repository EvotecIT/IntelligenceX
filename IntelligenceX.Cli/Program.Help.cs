using System;

namespace IntelligenceX.Cli;

internal static partial class Program {
    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp() {
        Console.WriteLine("IntelligenceX CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex auth <command>");
        Console.WriteLine("  intelligencex reviewer run [options]");
        Console.WriteLine("  intelligencex analyze <command>");
        Console.WriteLine("  intelligencex ci <command>");
        Console.WriteLine("  intelligencex setup [options]");
        Console.WriteLine("  intelligencex setup wizard [options]");
        Console.WriteLine("  intelligencex setup web [url]");
        Console.WriteLine("  intelligencex setup autodetect [options]");
        Console.WriteLine("  intelligencex manage [options]");
        Console.WriteLine("  intelligencex doctor [options]");
        Console.WriteLine("  intelligencex todo <command>");
        Console.WriteLine("  intelligencex telemetry <command>");
        Console.WriteLine("  intelligencex release <command>");
        Console.WriteLine("  intelligencex models <command>");
        Console.WriteLine("  intelligencex usage [options]");
        Console.WriteLine("  intelligencex heatmap <provider>");
        Console.WriteLine();
        Console.WriteLine("Tip:");
        Console.WriteLine("  Run intelligencex with no arguments in an interactive terminal to open the management hub.");
        Console.WriteLine();
        Console.WriteLine("Onboarding quick start:");
        Console.WriteLine("  intelligencex setup web                              # guided browser onboarding");
        Console.WriteLine("  intelligencex setup autodetect --json               # run preflight and get path recommendation");
        Console.WriteLine("  intelligencex setup wizard --path new-setup --repo owner/name");
        Console.WriteLine("  intelligencex setup wizard --path refresh-auth --repo owner/name");
        Console.WriteLine("  intelligencex setup wizard --path cleanup --repo owner/name --dry-run");
        Console.WriteLine();
        Console.WriteLine("Auth commands:");
        Console.WriteLine("  auth login       Start OAuth login flow and store credentials");
        Console.WriteLine("  auth export      Export stored credentials (json or base64)");
        Console.WriteLine("  auth sync-codex  Write tokens to CODEX_HOME/auth.json");
        Console.WriteLine();
        // Keep analyze subcommand help in a single place: `intelligencex analyze --help`.
        Console.WriteLine("Analyze:");
        Console.WriteLine("  analyze  Static analysis commands (run `intelligencex analyze --help` for subcommands)");
        Console.WriteLine();
        Console.WriteLine("CI:");
        Console.WriteLine("  ci       Workflow helper commands (run `intelligencex ci --help` for subcommands)");
        Console.WriteLine();
        Console.WriteLine("Reviewer commands:");
        Console.WriteLine("  reviewer run     Run reviewer using GitHub event payload or inputs");
        Console.WriteLine("  reviewer resolve-threads   Auto-resolve IntelligenceX bot review threads");
        Console.WriteLine();
        Console.WriteLine("Setup:");
        Console.WriteLine("  setup            Configure GitHub Actions workflow and secrets");
        Console.WriteLine();
        Console.WriteLine("Manage:");
        Console.WriteLine("  manage           Open interactive operations hub (auth, doctor, setup, reviewer)");
        Console.WriteLine();
        Console.WriteLine("Doctor:");
        Console.WriteLine("  doctor           Preflight checks for auth/config/GitHub access");
        Console.WriteLine();
        Console.WriteLine("TODO:");
        Console.WriteLine("  todo sync-bot-feedback   Sync bot checklist items into TODO.md (optional issue creation)");
        Console.WriteLine("  todo build-triage-index  Build PR/Issue triage index with duplicate clusters and best PR ranking");
        Console.WriteLine("  todo backtest-pr-signals Backtest PR operational signal calibration on closed/merged historical PRs");
        Console.WriteLine("  todo issue-review        Review open issues for infra applicability with proposed actions/confidence and optional auto-close");
        Console.WriteLine("  todo vision-check        Compare PR backlog against VISION.md and flag likely out-of-scope work");
        Console.WriteLine("  todo project-init        Create or initialize a GitHub Project with IX triage/vision fields");
        Console.WriteLine("  todo project-sync        Sync triage/vision artifacts into GitHub Project items and fields");
        Console.WriteLine("  todo project-bootstrap   Bootstrap project + workflow + VISION.md for GitHub-native maintainer triage");
        Console.WriteLine("  todo project-view-checklist  Build maintainer checklist for missing/default GitHub Project views");
        Console.WriteLine("  todo project-view-apply  Generate deterministic apply plan for missing GitHub Project views");
        Console.WriteLine("  todo pr-watch            Observe PR CI/review/mergeability state with deterministic action recommendations");
        Console.WriteLine("  todo pr-watch-monitor    Run observe-mode PR babysit sweep and emit monitor rollup artifacts");
        Console.WriteLine("  todo pr-watch-assist-retry   Run guarded retry assist for a single PR and emit assist artifacts");
        Console.WriteLine("  todo pr-watch-consolidate Build nightly/weekly PR-babysit rollups, metrics, and tracker issue updates");
        Console.WriteLine();
        Console.WriteLine("Telemetry:");
        Console.WriteLine("  telemetry usage   Manage provider-neutral usage telemetry roots, imports, and stats");
        Console.WriteLine();
        Console.WriteLine("Release commands:");
        Console.WriteLine("  release notes    Generate release notes from git tags/commits");
        Console.WriteLine("  release reviewer Build and publish reviewer release assets");
        Console.WriteLine();
        Console.WriteLine("Model commands:");
        Console.WriteLine("  models list      List available models and supported options");
        Console.WriteLine();
        Console.WriteLine("Heatmap commands:");
        Console.WriteLine("  heatmap chatgpt  Render ChatGPT daily usage as SVG or JSON");
        Console.WriteLine("  heatmap github   Render GitHub contribution calendar as SVG or JSON");
        Console.WriteLine();
        Console.WriteLine("Environment variables (optional overrides):");
        Console.WriteLine("  OPENAI_AUTH_AUTHORIZE_URL, OPENAI_AUTH_TOKEN_URL, OPENAI_AUTH_CLIENT_ID");
        Console.WriteLine("  OPENAI_AUTH_SCOPES, OPENAI_AUTH_REDIRECT_URL");
        Console.WriteLine("  INTELLIGENCEX_AUTH_PATH (optional)");
        Console.WriteLine("  INTELLIGENCEX_AUTH_KEY (base64 32 bytes to encrypt store)");
        Console.WriteLine("  CODEX_HOME (used by sync-codex)");
    }

    private static int PrintAuthHelpReturn() {
        PrintAuthHelp();
        return 1;
    }

    private static void PrintAuthHelp() {
        Console.WriteLine("Auth commands:");
        Console.WriteLine("  intelligencex auth login [options]");
        Console.WriteLine("  intelligencex auth list");
        Console.WriteLine("  intelligencex auth export");
        Console.WriteLine("  intelligencex auth sync-codex [options]");
        Console.WriteLine();
        Console.WriteLine("Auth login options:");
        Console.WriteLine("  --export [format]              Export auth store after login (default: store-base64)");
        Console.WriteLine("  --out <path>                   Write export to file");
        Console.WriteLine("  --print                        Print export to stdout");
        Console.WriteLine("  --set-github-secret [name]     Upload export to GitHub Actions secret (default name: INTELLIGENCEX_AUTH_B64)");
        Console.WriteLine("  --repo <owner/name>            Target repository secret");
        Console.WriteLine("  --org <org>                    Target organization secret (visibility defaults to all)");
        Console.WriteLine("  --visibility <all|private|selected>     Org secret visibility");
        Console.WriteLine("  --github-token <token>         Token for GitHub API (or set INTELLIGENCEX_GITHUB_TOKEN/GITHUB_TOKEN/GH_TOKEN)");
        Console.WriteLine();
        Console.WriteLine("Auth export options:");
        Console.WriteLine("  --format <json|base64|store|store-base64>");
        Console.WriteLine("  --provider <id>                Filter to a provider (e.g. openai-codex)");
        Console.WriteLine("  --account-id <id>              Filter to a ChatGPT account id");
        Console.WriteLine();
        Console.WriteLine("Auth sync-codex options:");
        Console.WriteLine("  --provider <id>                Provider to export to CODEX_HOME/auth.json");
        Console.WriteLine("  --account-id <id>              Select a specific account id");
    }

    private static void PrintReviewerHelp() {
        Console.WriteLine("Reviewer commands:");
        Console.WriteLine("  intelligencex reviewer run [options]");
        Console.WriteLine("  intelligencex reviewer resolve-threads [options]");
        Console.WriteLine("  intelligencex reviewer threads resolve [options]");
        Console.WriteLine();
        Console.WriteLine("Reviewer run options: run `intelligencex reviewer run --help` for the full list.");
    }

    private static int PrintReleaseHelpReturn() {
        PrintReleaseHelp();
        return 1;
    }

    private static void PrintReleaseHelp() {
        Console.WriteLine("Release commands:");
        Console.WriteLine("  release notes    Generate release notes from git tags/commits");
        Console.WriteLine("  release reviewer Build and publish reviewer release assets");
        Console.WriteLine("  release help");
    }
}
