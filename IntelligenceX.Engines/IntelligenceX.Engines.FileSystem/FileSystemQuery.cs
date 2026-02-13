using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IntelligenceX.Engines.FileSystem;

/// <summary>
/// Parameters for directory listing queries.
/// </summary>
public sealed class FileSystemListRequest {
    /// <summary>
    /// Root directory path to enumerate.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// When true, traverses child directories recursively.
    /// </summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// When true, includes file entries.
    /// </summary>
    public bool IncludeFiles { get; set; } = true;

    /// <summary>
    /// When true, includes directory entries.
    /// </summary>
    public bool IncludeDirectories { get; set; } = true;

    /// <summary>
    /// Maximum number of entries to emit.
    /// </summary>
    public int MaxResults { get; set; } = 200;

    internal void Validate() {
        if (string.IsNullOrWhiteSpace(Path)) {
            throw new ArgumentException("Path is required.", nameof(Path));
        }
        if (MaxResults <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), "MaxResults must be positive.");
        }
    }
}

/// <summary>
/// File system row emitted by listing queries.
/// </summary>
public sealed class FileSystemListEntry {
    /// <summary>
    /// Entry kind: <c>file</c>, <c>dir</c>, or <c>error</c>.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Full path for the row.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Optional error message for <c>error</c> rows.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Directory listing result.
/// </summary>
public sealed class FileSystemListResult {
    /// <summary>
    /// Resolved root path used for enumeration.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Number of emitted rows.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Indicates whether enumeration was truncated at <see cref="Count"/>.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Emitted entries.
    /// </summary>
    public IReadOnlyList<FileSystemListEntry> Entries { get; init; } = Array.Empty<FileSystemListEntry>();
}

/// <summary>
/// Parameters for UTF-8 text file reads.
/// </summary>
public sealed class FileTextReadRequest {
    /// <summary>
    /// Full file path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Maximum bytes to read.
    /// </summary>
    public long MaxBytes { get; set; } = 256 * 1024;

    internal void Validate() {
        if (string.IsNullOrWhiteSpace(Path)) {
            throw new ArgumentException("Path is required.", nameof(Path));
        }
        if (MaxBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxBytes), "MaxBytes must be positive.");
        }
    }
}

/// <summary>
/// UTF-8 text file read result.
/// </summary>
public sealed class FileTextReadResult {
    /// <summary>
    /// Resolved full file path.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Number of bytes read.
    /// </summary>
    public int BytesRead { get; init; }

    /// <summary>
    /// Indicates whether the file content was truncated to <see cref="BytesRead"/>.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// UTF-8 decoded text content.
    /// </summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Parameters for text search queries.
/// </summary>
public sealed class FileTextSearchRequest {
    /// <summary>
    /// Root directory path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Regex pattern to search.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Case sensitivity option.
    /// </summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Maximum number of matches to return.
    /// </summary>
    public int MaxMatches { get; set; } = 200;

    /// <summary>
    /// Maximum file size that will be scanned.
    /// </summary>
    public long MaxFileBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Regex timeout.
    /// </summary>
    public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromSeconds(2);

    internal void Validate() {
        if (string.IsNullOrWhiteSpace(Path)) {
            throw new ArgumentException("Path is required.", nameof(Path));
        }
        if (string.IsNullOrWhiteSpace(Pattern)) {
            throw new ArgumentException("Pattern is required.", nameof(Pattern));
        }
        if (MaxMatches <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxMatches), "MaxMatches must be positive.");
        }
        if (MaxFileBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxFileBytes), "MaxFileBytes must be positive.");
        }
        if (RegexTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(RegexTimeout), "RegexTimeout must be positive.");
        }
    }
}

/// <summary>
/// Single text match row.
/// </summary>
public sealed class FileTextSearchMatch {
    /// <summary>
    /// Full path to the file containing the match.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Character index where the match starts.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Character length of the match.
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// Matched text value.
    /// </summary>
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Text search result.
/// </summary>
public sealed class FileTextSearchResult {
    /// <summary>
    /// Resolved root path used for enumeration.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Number of emitted matches.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Indicates whether enumeration was truncated at <see cref="Count"/>.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Number of files attempted for search.
    /// </summary>
    public int ScannedFiles { get; init; }

    /// <summary>
    /// Match rows.
    /// </summary>
    public IReadOnlyList<FileTextSearchMatch> Matches { get; init; } = Array.Empty<FileTextSearchMatch>();
}

/// <summary>
/// Engine-level filesystem queries intended for higher-level tool wrappers.
/// </summary>
public static class FileSystemQuery {
    /// <summary>
    /// Lists directory entries from a root path with optional recursion.
    /// </summary>
    /// <param name="request">Listing parameters.</param>
    /// <param name="canDescendOrIncludePath">
    /// Optional path filter callback. Return false to skip traversing or emitting a path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed listing result.</returns>
    public static FileSystemListResult List(
        FileSystemListRequest request,
        Func<string, bool>? canDescendOrIncludePath = null,
        CancellationToken cancellationToken = default) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }
        request.Validate();

        var rootPath = System.IO.Path.GetFullPath(request.Path);
        var entries = new List<FileSystemListEntry>();
        var count = 0;
        var truncated = false;
        var emittedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            if (request.IncludeDirectories && !string.Equals(dir, rootPath, StringComparison.OrdinalIgnoreCase)) {
                if (!TryAddDirectoryEntry(entries, emittedDirectories, dir, request.MaxResults, ref count, ref truncated)) {
                    break;
                }
            }

            IEnumerable<string> directories;
            IEnumerable<string> files;
            try {
                directories = Directory.EnumerateDirectories(dir);
                files = request.IncludeFiles ? Directory.EnumerateFiles(dir) : Array.Empty<string>();
            } catch (Exception ex) {
                if (!TryAddEntry(entries, new FileSystemListEntry { Type = "error", Path = dir, Error = ex.Message }, request.MaxResults, ref count, ref truncated)) {
                    break;
                }
                continue;
            }

            if (request.IncludeFiles) {
                foreach (var file in files) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (canDescendOrIncludePath != null && !canDescendOrIncludePath(file)) {
                        continue;
                    }
                    if (!TryAddEntry(entries, new FileSystemListEntry { Type = "file", Path = file }, request.MaxResults, ref count, ref truncated)) {
                        break;
                    }
                }
                if (truncated) {
                    break;
                }
            }

            if (request.Recursive) {
                foreach (var sub in directories) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (canDescendOrIncludePath != null && !canDescendOrIncludePath(sub)) {
                        continue;
                    }
                    stack.Push(sub);
                }
            } else if (request.IncludeDirectories) {
                foreach (var sub in directories) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (canDescendOrIncludePath != null && !canDescendOrIncludePath(sub)) {
                        continue;
                    }
                    if (!TryAddDirectoryEntry(entries, emittedDirectories, sub, request.MaxResults, ref count, ref truncated)) {
                        break;
                    }
                }
            }

            if (truncated) {
                break;
            }
        }

        return new FileSystemListResult {
            Path = rootPath,
            Count = count,
            Truncated = truncated,
            Entries = entries
        };
    }

    private static bool TryAddDirectoryEntry(
        List<FileSystemListEntry> entries,
        HashSet<string> emittedDirectories,
        string path,
        int maxResults,
        ref int count,
        ref bool truncated) {
        if (!emittedDirectories.Add(path)) {
            return true;
        }

        return TryAddEntry(
            entries,
            new FileSystemListEntry { Type = "dir", Path = path },
            maxResults,
            ref count,
            ref truncated);
    }

    /// <summary>
    /// Reads UTF-8 text from a file and truncates by byte count.
    /// </summary>
    /// <param name="request">Read parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed read result.</returns>
    public static FileTextReadResult ReadText(FileTextReadRequest request, CancellationToken cancellationToken = default) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }
        request.Validate();

        var fullPath = System.IO.Path.GetFullPath(request.Path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var maxRead = (int)Math.Min(request.MaxBytes, int.MaxValue);
        var buffer = new byte[maxRead];
        var bytesRead = 0;
        while (bytesRead < maxRead) {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, bytesRead, maxRead - bytesRead);
            if (read <= 0) {
                break;
            }
            bytesRead += read;
        }

        return new FileTextReadResult {
            Path = fullPath,
            BytesRead = bytesRead,
            Truncated = stream.Length > bytesRead,
            Text = Encoding.UTF8.GetString(buffer, 0, bytesRead)
        };
    }

    /// <summary>
    /// Searches UTF-8 text files recursively from a root path using a regex pattern.
    /// </summary>
    /// <param name="request">Search parameters.</param>
    /// <param name="canDescendOrIncludePath">
    /// Optional path filter callback. Return false to skip traversing or scanning a path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed search result.</returns>
    public static FileTextSearchResult SearchText(
        FileTextSearchRequest request,
        Func<string, bool>? canDescendOrIncludePath = null,
        CancellationToken cancellationToken = default) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }
        request.Validate();

        var rootPath = System.IO.Path.GetFullPath(request.Path);
        var regexOptions = request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        var regex = new Regex(request.Pattern, regexOptions, request.RegexTimeout);

        var matches = new List<FileTextSearchMatch>();
        var truncated = false;
        var total = 0;
        var scannedFiles = 0;

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            IEnumerable<string> directories;
            IEnumerable<string> files;
            try {
                directories = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            } catch {
                continue;
            }

            foreach (var file in files) {
                cancellationToken.ThrowIfCancellationRequested();
                if (canDescendOrIncludePath != null && !canDescendOrIncludePath(file)) {
                    continue;
                }

                long size;
                try {
                    size = new FileInfo(file).Length;
                } catch {
                    continue;
                }

                if (size <= 0 || size > request.MaxFileBytes) {
                    continue;
                }

                scannedFiles++;

                string content;
                try {
                    content = File.ReadAllText(file, Encoding.UTF8);
                } catch {
                    continue;
                }

                foreach (Match match in regex.Matches(content)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!match.Success) {
                        continue;
                    }

                    matches.Add(new FileTextSearchMatch {
                        Path = file,
                        Index = match.Index,
                        Length = match.Length,
                        Value = match.Value
                    });
                    total++;
                    if (total >= request.MaxMatches) {
                        truncated = true;
                        break;
                    }
                }

                if (truncated) {
                    break;
                }
            }

            if (truncated) {
                break;
            }

            foreach (var sub in directories) {
                cancellationToken.ThrowIfCancellationRequested();
                if (canDescendOrIncludePath != null && !canDescendOrIncludePath(sub)) {
                    continue;
                }
                stack.Push(sub);
            }
        }

        return new FileTextSearchResult {
            Path = rootPath,
            Count = total,
            Truncated = truncated,
            ScannedFiles = scannedFiles,
            Matches = matches
        };
    }

    private static bool TryAddEntry(
        List<FileSystemListEntry> entries,
        FileSystemListEntry entry,
        int maxResults,
        ref int count,
        ref bool truncated) {
        entries.Add(entry);
        count++;
        if (count >= maxResults) {
            truncated = true;
            return false;
        }
        return true;
    }
}
