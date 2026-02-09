using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal sealed partial class GitHubClient {
    private static string ParseRepoFullName(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return string.Empty;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return string.Empty;
        }
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        // Prefer API URL shapes:
        // - https://api.github.com/repos/{owner}/{repo}
        // - https://{ghe}/api/v3/repos/{owner}/{repo}
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var reposIndex = Array.FindIndex(segments, s => string.Equals(s, "repos", StringComparison.OrdinalIgnoreCase));
        if (reposIndex >= 0 && reposIndex + 2 < segments.Length) {
            var owner = segments[reposIndex + 1].Trim();
            var repo = segments[reposIndex + 2].Trim();
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
                repo = repo[..^4];
            }
            if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo)) {
                return $"{owner}/{repo}";
            }
            return string.Empty;
        }

        // Fall back to a conservative "last two segments" parse only for known GitHub web URLs.
        var host = uri.Host;
        if (!string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }
        // Only accept the canonical repo URL shape: https://github.com/{owner}/{repo}
        if (segments.Length != 2) {
            return string.Empty;
        }
        var fallbackOwner = segments[0].Trim();
        var fallbackRepo = segments[1].Trim();
        if (fallbackRepo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
            fallbackRepo = fallbackRepo[..^4];
        }
        if (string.IsNullOrWhiteSpace(fallbackOwner) || string.IsNullOrWhiteSpace(fallbackRepo)) {
            return string.Empty;
        }
        // Avoid mis-parsing URLs like /pull/123 or /issues/...
        if (string.Equals(fallbackOwner, "pull", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fallbackOwner, "issues", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }
        return $"{fallbackOwner}/{fallbackRepo}";
    }

    private static string BuildPullRequestKey(string owner, string repo, int number) {
        return $"{owner}/{repo}#{number}";
    }

    private static string BuildCompareKey(string owner, string repo, string baseSha, string headSha) {
        return $"{owner}/{repo}@{baseSha}..{headSha}";
    }

    /// <summary>
    /// Represents compare API results along with truncation metadata.
    /// </summary>
    internal readonly struct CompareFilesResult {
        public CompareFilesResult(IReadOnlyList<PullRequestFile> files, bool isTruncated) {
            Files = files;
            IsTruncated = isTruncated;
        }

        public IReadOnlyList<PullRequestFile> Files { get; }
        public bool IsTruncated { get; }
    }

    private async Task<T> WithGateAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return await action().ConfigureAwait(false);
        } finally {
            _requestGate.Release();
        }
    }
}
