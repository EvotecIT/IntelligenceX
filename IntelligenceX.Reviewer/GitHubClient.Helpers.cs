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
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) {
            return string.Empty;
        }
        return $"{segments[^2]}/{segments[^1]}";
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

