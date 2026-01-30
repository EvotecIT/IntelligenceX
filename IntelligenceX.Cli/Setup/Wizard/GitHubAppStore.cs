using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IntelligenceX.Cli.Setup.Wizard;

internal sealed class GitHubAppStore {
    private readonly string _path;

    public GitHubAppStore(string? path = null) {
        _path = path ?? ResolvePath();
    }

    public GitHubAppProfile? LoadDefault() {
        try {
            if (!File.Exists(_path)) {
                return null;
            }
            var json = File.ReadAllText(_path);
            var store = JsonSerializer.Deserialize<GitHubAppStoreFile>(json);
            if (store is null || store.Profiles.Count == 0) {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(store.DefaultProfile) &&
                store.Profiles.TryGetValue(store.DefaultProfile!, out var profile)) {
                return profile;
            }
            foreach (var pair in store.Profiles) {
                return pair.Value;
            }
            return null;
        } catch {
            return null;
        }
    }

    public GitHubAppProfile? LoadByName(string name) {
        try {
            if (!File.Exists(_path)) {
                return null;
            }
            var json = File.ReadAllText(_path);
            var store = JsonSerializer.Deserialize<GitHubAppStoreFile>(json);
            if (store is null || store.Profiles.Count == 0) {
                return null;
            }
            return store.Profiles.TryGetValue(name, out var profile) ? profile : null;
        } catch {
            return null;
        }
    }

    public IReadOnlyList<GitHubAppProfile> LoadAll() {
        try {
            if (!File.Exists(_path)) {
                return Array.Empty<GitHubAppProfile>();
            }
            var json = File.ReadAllText(_path);
            var store = JsonSerializer.Deserialize<GitHubAppStoreFile>(json);
            if (store is null || store.Profiles.Count == 0) {
                return Array.Empty<GitHubAppProfile>();
            }
            return store.Profiles.Values.ToList();
        } catch {
            return Array.Empty<GitHubAppProfile>();
        }
    }

    public void Save(GitHubAppProfile profile, bool makeDefault) {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        var store = LoadStore();
        if (string.IsNullOrWhiteSpace(profile.Name)) {
            profile.Name = $"app-{profile.AppId}";
        }
        store.Profiles[profile.Name!] = profile;
        if (makeDefault) {
            store.DefaultProfile = profile.Name;
        }
        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private GitHubAppStoreFile LoadStore() {
        try {
            if (!File.Exists(_path)) {
                return new GitHubAppStoreFile();
            }
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<GitHubAppStoreFile>(json) ?? new GitHubAppStoreFile();
        } catch {
            return new GitHubAppStoreFile();
        }
    }

    private static string ResolvePath() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }
        return Path.Combine(home, ".intelligencex", "github-app.json");
    }
}

internal sealed class GitHubAppProfile {
    public string? Name { get; set; }
    public long AppId { get; set; }
    public string? KeyPath { get; set; }
    public long? DefaultInstallationId { get; set; }
}

internal sealed class GitHubAppStoreFile {
    public string? DefaultProfile { get; set; }
    public Dictionary<string, GitHubAppProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
