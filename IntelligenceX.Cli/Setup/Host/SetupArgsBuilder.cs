using System.Collections.Generic;

namespace IntelligenceX.Cli.Setup.Host;

internal static class SetupArgsBuilder {
    public static string[] FromPlan(SetupPlan plan) {
        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(plan.ConfigPath) && !string.IsNullOrWhiteSpace(plan.ConfigJson)) {
            throw new InvalidOperationException("Choose only one of --config-path or --config-json.");
        }

        if (!string.IsNullOrWhiteSpace(plan.AuthB64) && !string.IsNullOrWhiteSpace(plan.AuthB64Path)) {
            throw new InvalidOperationException("Choose only one of --auth-b64 or --auth-b64-path.");
        }

        if (!string.IsNullOrWhiteSpace(plan.RepoFullName)) {
            args.Add("--repo");
            args.Add(plan.RepoFullName);
        }

        if (!string.IsNullOrWhiteSpace(plan.GitHubClientId)) {
            args.Add("--github-client-id");
            args.Add(plan.GitHubClientId);
        }

        if (!string.IsNullOrWhiteSpace(plan.GitHubToken)) {
            args.Add("--github-token");
            args.Add(plan.GitHubToken);
        }

        if (plan.WithConfig) {
            args.Add("--with-config");
        }

        if (!string.IsNullOrWhiteSpace(plan.ConfigPath)) {
            args.Add("--config-path");
            args.Add(plan.ConfigPath);
        }

        if (!string.IsNullOrWhiteSpace(plan.ConfigJson)) {
            args.Add("--config-json");
            args.Add(plan.ConfigJson);
        }

        if (!string.IsNullOrWhiteSpace(plan.AuthB64)) {
            args.Add("--auth-b64");
            args.Add(plan.AuthB64);
        }

        if (!string.IsNullOrWhiteSpace(plan.AuthB64Path)) {
            args.Add("--auth-b64-path");
            args.Add(plan.AuthB64Path);
        }

        if (!string.IsNullOrWhiteSpace(plan.Provider)) {
            args.Add("--provider");
            args.Add(plan.Provider);
        }

        if (!string.IsNullOrWhiteSpace(plan.ReviewProfile)) {
            args.Add("--review-profile");
            args.Add(plan.ReviewProfile);
        }

        if (!string.IsNullOrWhiteSpace(plan.ReviewMode)) {
            args.Add("--review-mode");
            args.Add(plan.ReviewMode);
        }

        if (!string.IsNullOrWhiteSpace(plan.ReviewCommentMode)) {
            args.Add("--review-comment-mode");
            args.Add(plan.ReviewCommentMode);
        }

        if (plan.SkipSecret) {
            args.Add("--skip-secret");
        }

        if (plan.ManualSecret) {
            args.Add("--manual-secret");
        }

        if (plan.ExplicitSecrets) {
            args.Add("--explicit-secrets");
        }

        if (plan.Upgrade) {
            args.Add("--upgrade");
        }

        if (plan.Force) {
            args.Add("--force");
        }

        if (plan.UpdateSecret) {
            args.Add("--update-secret");
        }

        if (plan.Cleanup) {
            args.Add("--cleanup");
        }

        if (plan.KeepSecret) {
            args.Add("--keep-secret");
        }

        if (plan.DryRun) {
            args.Add("--dry-run");
        }

        if (!string.IsNullOrWhiteSpace(plan.BranchName)) {
            args.Add("--branch");
            args.Add(plan.BranchName);
        }

        return args.ToArray();
    }
}
