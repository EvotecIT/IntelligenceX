using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Context;

/// <summary>Bounded retrieval of explicitly enrolled maintained sources, without model calls.</summary>
public sealed class ProjectContextService(string? catalogPath, TimeProvider? timeProvider = null) {
    private const int MaximumCatalogBytes = 1024 * 1024;
    private const int MaximumDocumentBytes = 1024 * 1024;
    private const int MaximumTotalBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DirectoryFreshness = TimeSpan.FromMinutes(10);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;

    private sealed record Section(string Project, KnowledgeSource Source, string Revision, string Heading, string Text, int Line);
    private sealed record Snapshot(DateTimeOffset LoadedAt, ProjectCatalog Catalog, Section[] Sections, string[] Coverage);

    /// <summary>Returns exact local labels. Labels carry no verdict or liveness policy.</summary>
    public async Task<IReadOnlyDictionary<string, WorkLabel>> LabelsAsync(CancellationToken token) {
        var snapshot = await LoadAsync(token);
        return snapshot.Catalog.WorkLabels.ToDictionary(x => x.ItemId, StringComparer.Ordinal);
    }

    /// <summary>Lists enrolled projects with directory freshness and source-coverage notes.</summary>
    public async Task<ProjectDirectoryView> ProjectsAsync(CancellationToken token = default) {
        var snapshot = await LoadAsync(token);
        var coverage = snapshot.Coverage.ToList();
        if (snapshot.Catalog.ObservedAtUtc != default && _time.GetUtcNow() - snapshot.Catalog.ObservedAtUtc > DirectoryFreshness)
            coverage.Add("directory-source-stale");
        return new(snapshot.LoadedAt, snapshot.Catalog.Projects.Select(project => View(snapshot, project, [])).ToArray(), coverage.ToArray());
    }

    /// <summary>Returns one project with optional bounded supporting excerpts.</summary>
    public async Task<ProjectContextView?> ProjectAsync(string id, string? query, CancellationToken token = default) {
        var snapshot = await LoadAsync(token);
        RequireDirectory(snapshot);
        var project = snapshot.Catalog.Projects.FirstOrDefault(x => x.Id == id);
        if (project is null) return null;
        var hits = query is null ? [] : Search(snapshot, id, query).Hits.Take(2).ToArray();
        return View(snapshot, project, hits);
    }

    /// <summary>Searches within a single enrolled project, with source citations for every excerpt.</summary>
    public async Task<KnowledgeSearchView?> SearchAsync(string project, string query, CancellationToken token = default) {
        var snapshot = await LoadAsync(token);
        RequireDirectory(snapshot);
        return snapshot.Catalog.Projects.Any(x => x.Id == project) ? Search(snapshot, project, query) : null;
    }

    private static void RequireDirectory(Snapshot snapshot) {
        if (snapshot.Catalog.ObservedAtUtc == default) throw new ProjectDirectoryUnavailableException();
    }

    private ProjectContextView View(Snapshot snapshot, ProjectDefinition project, KnowledgeHit[] hits) =>
        new(project.Id, project.Name, project.Repository, snapshot.Catalog.ObservedAtUtc, project.Revision,
            project.Responsibilities, project.Dependencies, hits, Coverage(snapshot, project.Id));

    private string[] Coverage(Snapshot snapshot, string project) {
        var notes = snapshot.Coverage.Where(x => !x.Contains(':') || x.StartsWith(project + ":", StringComparison.Ordinal)).ToList();
        if (_time.GetUtcNow() - snapshot.Catalog.ObservedAtUtc > DirectoryFreshness) notes.Add("directory-source-stale");
        var definition = snapshot.Catalog.Projects.First(x => x.Id == project);
        notes.AddRange(definition.Coverage);
        if (definition.Responsibilities.Any(x => _time.GetUtcNow() - x.ObservedAtUtc > DirectoryFreshness))
            notes.Add("responsibility-evidence-stale");
        return notes.ToArray();
    }

    private KnowledgeSearchView Search(Snapshot snapshot, string project, string query) {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 256) throw new ArgumentException("A query of 1–256 characters is required.", nameof(query));
        var terms = Regex.Matches(query, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)
            .Select(x => x.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Take(17).ToArray();
        if (terms.Length is 0 or > 16) throw new ArgumentException("A query must contain 1–16 terms.", nameof(query));
        var ranked = snapshot.Sections.Where(x => x.Project == project && x.Source.SupersededBy is null)
            .Select(section => new { Section = section, Score = terms.Count(term =>
                section.Text.Contains(term, StringComparison.OrdinalIgnoreCase) || section.Heading.Contains(term, StringComparison.OrdinalIgnoreCase)) })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Section.Source.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Section.Line).Take(6).ToArray();
        var hits = ranked.Take(5).Select(x => {
            var section = x.Section;
            var excerpt = Excerpt(section.Text, terms);
            return new KnowledgeHit(section.Source.Id, project, section.Source.Title, section.Heading, excerpt,
                section.Source.SourceRef, section.Revision, section.Source.Outcome, section.Source.VerifiedAtUtc,
                section.Source.SupersededBy, section.Line);
        }).ToArray();
        return new(snapshot.LoadedAt, project, query, hits, Coverage(snapshot, project), ranked.Length > 5);
    }

    private static string Excerpt(string content, string[] terms) {
        var text = Regex.Replace(content, @"\s+", " ").Trim();
        var starts = new HashSet<int> { 0 };
        foreach (var term in terms) {
            var offset = 0;
            for (var count = 0; count < 64 && offset < text.Length; count++) {
                var found = text.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                var start = Math.Max(0, found - 80);
                if (start > 0) start = text.LastIndexOf(' ', start) + 1;
                starts.Add(start);
                offset = found + term.Length;
            }
        }
        return starts.Order().Select(start => text.Substring(start, Math.Min(480, text.Length - start)))
            .OrderByDescending(excerpt => terms.Count(term => excerpt.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(excerpt => terms.Select(term => excerpt.IndexOf(term, StringComparison.OrdinalIgnoreCase))
                .Where(index => index >= 0).DefaultIfEmpty(0).Max()).First();
    }

    private async Task<Snapshot> LoadAsync(CancellationToken token) {
        await _gate.WaitAsync(token);
        try {
            var now = _time.GetUtcNow();
            if (_snapshot is { } cached && now >= cached.LoadedAt && now - cached.LoadedAt < RefreshInterval) return cached;
            if (catalogPath is null) return _snapshot = new(now, new(), [], ["project-directory-not-enrolled"]);
            ProjectCatalog catalog;
            try {
                ValidateFile(catalogPath, MaximumCatalogBytes);
                catalog = JsonSerializer.Deserialize<ProjectCatalog>(await ReadBoundedAsync(catalogPath, MaximumCatalogBytes, token), LedgerJson.Options)
                    ?? throw new InvalidDataException("Project catalog is missing.");
                Validate(catalog, now);
            } catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) {
                return _snapshot = new(now, new(), [], ["project-directory-unavailable"]);
            }
            var sections = new List<Section>();
            var coverage = new List<string>();
            var bytes = 0L;
            foreach (var project in catalog.Projects) {
                foreach (var source in project.Sources) {
                    token.ThrowIfCancellationRequested();
                    try {
                        var size = ValidateFile(source.Path, MaximumDocumentBytes);
                        bytes += size;
                        if (bytes > MaximumTotalBytes) throw new InvalidDataException("Enrolled source budget exceeded.");
                        var content = await ReadBoundedAsync(source.Path, MaximumDocumentBytes, token);
                        var revision = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                        var enrolled = new List<Section>();
                        AddSections(enrolled, project.Id, source, revision, Encoding.UTF8.GetString(content));
                        if (sections.Count + enrolled.Count > 10_000) throw new InvalidDataException("Enrolled section budget exceeded.");
                        sections.AddRange(enrolled);
                    } catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) {
                        coverage.Add(project.Id + ":source-unavailable:" + source.Id);
                    }
                }
            }
            return _snapshot = new(now, catalog, sections.ToArray(), coverage.ToArray());
        } finally { _gate.Release(); }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, CancellationToken token) {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0) {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("Source grew past its bound.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static long ValidateFile(string path, int maximumBytes) {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Source paths must be absolute.");
        FileSystemInfo? current = new FileInfo(path);
        var size = ((FileInfo)current).Length;
        if (size > maximumBytes) throw new InvalidDataException("Source exceeds its bound.");
        while (current is not null) {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked sources require separate enrollment.");
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
        return size;
    }

    private static void Validate(ProjectCatalog catalog, DateTimeOffset now) {
        if (catalog.SchemaVersion != 1 || catalog.Projects is null || catalog.WorkLabels is null || catalog.Projects.Length > 128 ||
            catalog.WorkLabels.Length > 20_000 || catalog.ObservedAtUtc == default || catalog.ObservedAtUtc > now.AddMinutes(2) ||
            catalog.Projects.Any(x => x is null || !DeclarationValidation.Identifier(x.Id) || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 160 ||
                !Text(x.Repository, 200) || !Text(x.Revision, 160) || x.Responsibilities is null || x.Responsibilities.Length > 64 ||
                x.Dependencies is null || x.Dependencies.Length > 128 || x.Sources is null || x.Sources.Length > 64 ||
                x.Coverage is null || x.Coverage.Length > 32 || x.Coverage.Any(note => !Text(note, 160))) ||
            catalog.Projects.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Projects.Length ||
            catalog.WorkLabels.Any(x => x is null || !Text(x.ItemId, 256) || !Text(x.Label, 160) ||
                x.ProjectId is not null && !catalog.Projects.Any(project => project.Id == x.ProjectId)) ||
            catalog.WorkLabels.Select(x => x.ItemId).Distinct(StringComparer.Ordinal).Count() != catalog.WorkLabels.Length)
            throw new InvalidDataException("Invalid project catalog.");
        foreach (var project in catalog.Projects) {
            if (project.Responsibilities.Any(x => x is null || !Text(x.Role, 120) || !Text(x.State, 80) || !Reference(x.SourceRef) ||
                    !Observation(x.ObservedAtUtc, now) || x.WorkerId?.Length > 160 || x.MachineId?.Length > 160) ||
                project.Dependencies.Any(x => x is null || !DeclarationValidation.Identifier(x.ProjectId) || !Text(x.Kind, 80) ||
                    !Text(x.RequiredRef, 256) || !Text(x.State, 80) || !Reference(x.SourceRef) || !Observation(x.ObservedAtUtc, now)))
                throw new InvalidDataException("Invalid project evidence.");
            if (project.Sources.Any(x => x is null || !DeclarationValidation.Identifier(x.Id) || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 160 ||
                x.Outcome is not ("reference" or "accepted" or "rejected" or "superseded") ||
                !Reference(x.SourceRef) || x.SupersededBy is not null && !DeclarationValidation.Identifier(x.SupersededBy) ||
                x.VerifiedAtUtc is { } verified && !Observation(verified, now)) ||
                project.Sources.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != project.Sources.Length)
                throw new InvalidDataException("Invalid knowledge enrollment.");
        }
    }

    private static bool Text(string? value, int length) => !string.IsNullOrWhiteSpace(value) && value.Length <= length;
    private static bool Reference(string? value) => value?.Length <= 2048 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0;
    private static bool Observation(DateTimeOffset value, DateTimeOffset now) => value != default && value <= now.AddMinutes(2);

    private static void AddSections(List<Section> sections, string project, KnowledgeSource source, string revision, string content) {
        var lines = content.Split('\n');
        var heading = source.Title;
        var start = 1;
        var text = new StringBuilder();
        for (var index = 0; index <= lines.Length; index++) {
            var boundary = index == lines.Length || lines[index].StartsWith('#');
            if (boundary && text.Length > 0) {
                if (sections.Count >= 10_000) throw new InvalidDataException("Enrolled section budget exceeded.");
                sections.Add(new(project, source, revision, heading, text.ToString(), start));
                text.Clear();
            }
            if (index == lines.Length) break;
            if (boundary) { heading = lines[index].Trim().TrimStart('#', ' '); start = index + 1; }
            else text.AppendLine(lines[index]);
        }
    }
}
