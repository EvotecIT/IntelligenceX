#if !NET472
using System;
using System.IO;
using IntelligenceX.Cli.Ci;
#endif

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestCiPathSafetyUnderRootPhysicalRejectsNonexistentDirectoryLeaf() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var newDir = Path.Combine(root, "artifacts");
            AssertEqual(false, Directory.Exists(newDir), "artifacts directory does not exist");
            AssertEqual(false, CiPathSafety.IsUnderRootPhysical(newDir, root), "IsUnderRootPhysical rejects non-existent directory leaf");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiPathSafetyTryEnsureSafeDirectoryAllowsNewDirectoryLeaf() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-ensure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var newDir = Path.Combine(root, "artifacts");
            AssertEqual(false, Directory.Exists(newDir), "ensure target dir does not exist");

            AssertEqual(true, CiPathSafety.TryEnsureSafeDirectory(newDir, root, out var error), "TryEnsureSafeDirectory ok");
            AssertEqual(string.Empty, error, "TryEnsureSafeDirectory error empty");

            AssertEqual(true, Directory.Exists(newDir), "ensure target dir exists");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(newDir, root), "IsUnderRootPhysical ok after ensure");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiPathSafetyUnderRootPhysicalTrailingSeparators() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-seps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var artifacts = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(artifacts);

            var sep = Path.DirectorySeparatorChar.ToString();
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts, root), "physical under root baseline");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts + sep, root), "path trailing sep");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts, root + sep), "root trailing sep");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts + sep, root + sep), "both trailing sep");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(root + sep, root), "root self trailing sep");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiPathSafetyUnderRootPhysicalRejectsSymlinkTraversal() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-symlink-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "ix-ci-path-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try {
            var link = Path.Combine(root, "link");
            try {
                Directory.CreateSymbolicLink(link, outside);
            } catch (Exception ex) {
                // Symlink creation can be restricted (for example, Windows without Developer Mode/admin privileges).
                // Treat this as a skipped test so CI stays portable across runners.
                Console.Error.WriteLine($"Skipping symlink traversal test (symlink creation not supported): {ex.Message}");
                return;
            }
            AssertEqual(true, Directory.Exists(link), "symlink directory exists");

            var target = Path.Combine(link, "x.txt");
            AssertEqual(false, CiPathSafety.IsUnderRootPhysical(target, root), "reject symlink traversal");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    private static void TestCiPathSafetyUnderRootPhysicalAllowsNestedNonexistentSegments() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var nested = Path.Combine(root, "artifacts", "nested", "changed-files.txt");
            AssertEqual(false, CiPathSafety.IsUnderRootPhysical(nested, root), "nested non-existent segments rejected until ensured");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiChangedFilesWritesIntoNewDirectory() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-changed-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var outputPath = Path.Combine(root, "artifacts", "changed-files.txt");
            AssertEqual(false, File.Exists(outputPath), "changed-files output does not exist");

            var exit = CiChangedFilesCommand.RunAsync(new[] { "--workspace", root, "--out", "artifacts/changed-files.txt" })
                .GetAwaiter().GetResult();
            AssertEqual(0, exit, "changed-files exit code");
            AssertEqual(true, File.Exists(outputPath), "changed-files output exists");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiChangedFilesStrictFailsWhenDiffFailsEvenIfFallbackSucceeds() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-changed-files-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root, "a.txt"), "x\n");
            var (initExit, _, _) = GitCli.RunAsync(root, "init").GetAwaiter().GetResult();
            AssertEqual(0, initExit, "git init exit");

            var (addExit, _, _) = GitCli.RunAsync(root, "add", "a.txt").GetAwaiter().GetResult();
            AssertEqual(0, addExit, "git add exit");

            var exit = CiChangedFilesCommand.RunAsync(new[] {
                    "--workspace", root,
                    "--out", "artifacts/changed-files.txt",
                    "--base", "deadbeef",
                    "--strict"
                })
                .GetAwaiter().GetResult();
            AssertEqual(1, exit, "changed-files strict exit code");

            var outputPath = Path.Combine(root, "artifacts", "changed-files.txt");
            AssertEqual(true, File.Exists(outputPath), "changed-files strict output exists");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiTuneReviewerBudgetsRejectsOutEnvOutsideWorkspaceWhenGitHubEnvMissing() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-tune-budgets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var changedFilesPath = Path.Combine(root, "artifacts", "changed-files.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(changedFilesPath)!);
            File.WriteAllText(changedFilesPath, "a.txt\n");

            var outside = Path.Combine(Path.GetTempPath(), "ix-ci-outside-" + Guid.NewGuid().ToString("N"), "env.txt");
            var originalWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
            var originalEnv = Environment.GetEnvironmentVariable("GITHUB_ENV");
            var originalActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
            try {
                Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", root);
                Environment.SetEnvironmentVariable("GITHUB_ENV", null);
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);

                var exit = CiTuneReviewerBudgetsCommand.RunAsync(new[] {
                        "--changed-files", "artifacts/changed-files.txt",
                        "--changed-threshold", "0",
                        "--out-env", outside
                    })
                    .GetAwaiter().GetResult();
                AssertEqual(1, exit, "tune-reviewer-budgets exit code rejects out-env outside workspace when GITHUB_ENV missing");
                AssertEqual(false, File.Exists(outside), "out-env file not created outside workspace");
            } finally {
                Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", originalWorkspace);
                Environment.SetEnvironmentVariable("GITHUB_ENV", originalEnv);
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", originalActions);
            }
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiReviewFailOpenSummaryUpdatesExistingComment() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-fail-open-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var logPath = Path.Combine(root, "reviewer.log");
            File.WriteAllText(logPath,
                "OAuth token request failed (401): refresh_token_reused. Your refresh token has already been used to generate a new access token. Please try signing in again.");

            string? updatedBody = null;
            var patchHits = 0;
            using var server = new LocalHttpServer(request => {
                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(request.Path, "/repos/owner/repo/pulls/123", StringComparison.OrdinalIgnoreCase)) {
                    return new HttpResponse("""
{
  "title": "Workflow hardening",
  "body": "Body",
  "draft": false,
  "number": 123,
  "head": {
    "sha": "head-sha",
    "repo": {
      "full_name": "owner/repo",
      "fork": false
    }
  },
  "base": {
    "sha": "base-sha",
    "ref": "master",
    "repo": {
      "full_name": "owner/repo"
    }
  },
  "labels": []
}
""");
                }

                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    request.Path.StartsWith("/repos/owner/repo/issues/123/comments", StringComparison.OrdinalIgnoreCase)) {
                    return new HttpResponse("""
[
  {
    "id": 41,
    "body": "<!-- intelligencex:summary -->\nuser body",
    "user": {
      "login": "alice"
    }
  },
  {
    "id": 42,
    "body": "<!-- intelligencex:summary -->\nold body",
    "user": {
      "login": "intelligencex-review[bot]"
    }
  }
]
""");
                }

                if (string.Equals(request.Method, "PATCH", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(request.Path, "/repos/owner/repo/issues/comments/42", StringComparison.OrdinalIgnoreCase)) {
                    patchHits++;
                    updatedBody = request.Body;
                    return new HttpResponse("{\"id\":42,\"body\":\"ok\"}");
                }

                return null;
            });

            var exit = CiReviewFailOpenSummaryCommand.RunAsync(new[] {
                    "--repo", "owner/repo",
                    "--pr-number", "123",
                    "--reviewer-source", "source",
                    "--source-log", logPath,
                    "--github-token", "token",
                    "--github-base-url", server.BaseUri.ToString().TrimEnd('/')
                })
                .GetAwaiter().GetResult();

            AssertEqual(0, exit, "review-fail-open-summary exit code");
            AssertEqual(1, patchHits, "review-fail-open-summary updates existing comment");
            AssertEqual(false, string.IsNullOrWhiteSpace(updatedBody), "review-fail-open-summary updates trusted comment body");
            AssertContainsText(updatedBody ?? string.Empty, "## IntelligenceX Review (failed open)",
                "review-fail-open-summary uses fail-open heading");
            AssertContainsText(updatedBody ?? string.Empty, "Reviewing this pull request: **Workflow hardening**",
                "review-fail-open-summary uses PR title");
            AssertContainsText(updatedBody ?? string.Empty, "- Reviewer source: source",
                "review-fail-open-summary records reviewer source");
            AssertContainsText(updatedBody ?? string.Empty, "intelligencex auth login --set-github-secret --repo owner/repo",
                "review-fail-open-summary includes reauth guidance");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiReviewFailOpenSummarySkipsWhenPrNumberUnavailable() {
        var previousEventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        try {
            Environment.SetEnvironmentVariable("GITHUB_EVENT_PATH", null);
            var exit = CiReviewFailOpenSummaryCommand.RunAsync(new[] {
                    "--repo", "owner/repo",
                    "--reviewer-source", "source"
                })
                .GetAwaiter().GetResult();
            AssertEqual(0, exit, "review-fail-open-summary skip exit code");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_EVENT_PATH", previousEventPath);
        }
    }

    private static void TestCiReviewFailOpenSummaryPrefersReviewerTokenOverGitHubToken() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-fail-open-summary-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousReviewerToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN");
        var previousGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        try {
            var logPath = Path.Combine(root, "reviewer.log");
            File.WriteAllText(logPath, "Unhandled exception: reviewer-runtime");

            string? authorizationHeader = null;
            using var server = new LocalHttpServer(request => {
                authorizationHeader = request.Headers.TryGetValue("Authorization", out var value) ? value : authorizationHeader;
                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(request.Path, "/repos/owner/repo/pulls/123", StringComparison.OrdinalIgnoreCase)) {
                    return new HttpResponse("""
{
  "title": "Workflow hardening",
  "body": "Body",
  "draft": false,
  "number": 123,
  "head": { "sha": "head-sha", "repo": { "full_name": "owner/repo", "fork": false } },
  "base": { "sha": "base-sha", "ref": "master", "repo": { "full_name": "owner/repo" } },
  "labels": []
}
""");
                }

                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    request.Path.StartsWith("/repos/owner/repo/issues/123/comments", StringComparison.OrdinalIgnoreCase)) {
                    return new HttpResponse("""
[
  {
    "id": 42,
    "body": "<!-- intelligencex:summary -->\nold body",
    "user": { "login": "intelligencex-review[bot]" }
  }
]
""");
                }

                if (string.Equals(request.Method, "PATCH", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(request.Path, "/repos/owner/repo/issues/comments/42", StringComparison.OrdinalIgnoreCase)) {
                    return new HttpResponse("{\"id\":42,\"body\":\"ok\"}");
                }

                return null;
            });

            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN", "reviewer-token");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "github-token");

            var exit = CiReviewFailOpenSummaryCommand.RunAsync(new[] {
                    "--repo", "owner/repo",
                    "--pr-number", "123",
                    "--reviewer-source", "source",
                    "--source-log", logPath,
                    "--github-base-url", server.BaseUri.ToString().TrimEnd('/')
                })
                .GetAwaiter().GetResult();

            AssertEqual(0, exit, "review-fail-open-summary token precedence exit code");
            AssertContainsText(authorizationHeader ?? string.Empty, "Bearer reviewer-token",
                "review-fail-open-summary prefers INTELLIGENCEX_GITHUB_TOKEN over GITHUB_TOKEN");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN", previousReviewerToken);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHubToken);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
#endif
}
