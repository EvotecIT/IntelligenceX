using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>
/// Reads GitHub, Codex and Claude sign-in facts on this machine for the reporter to send with its health: the gh
/// account, token source and scopes, environment variables that override the stored sign-in, every Codex and Claude
/// binary found and its version, and when configuration and credential files were last written. It runs the official
/// CLIs read-only (<c>gh auth status --json hosts</c>, <c>--version</c>) and reads only file times, one config hash
/// and one config setting. Credential files are never opened.
/// <para>
/// Everything is read again every 15 minutes, or at once when one of the watched files or binaries changes, which a
/// cheap check each minute notices. <c>gh auth status</c> makes one GitHub request per check.
/// </para>
/// </summary>
internal sealed partial class AuthStateCollector {
    internal static readonly TimeSpan FullInterval = TimeSpan.FromMinutes(15);
    private static readonly string[] TokenVariables = ["GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "GITHUB_ENTERPRISE_TOKEN"];
    private readonly AuthPaths _paths;
    private readonly Func<string, string[], CancellationToken, Task<(int? Code, string Output)>> _run;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, (string Stamp, string? Version)> _versions = new(StringComparer.Ordinal);
    private AuthFact[] _last = [];
    private string? _stamps;
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    public AuthStateCollector(AuthPaths paths, Func<string, string[], CancellationToken, Task<(int? Code, string Output)>>? run = null,
        Func<DateTimeOffset>? clock = null) {
        _paths = paths;
        _run = run ?? RunAsync;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The latest facts, read again when they are due or a watched file changed.</summary>
    public async Task<AuthFact[]> CollectAsync(CancellationToken token) {
        var now = _clock();
        var stamps = Stamps();
        if (_stamps == stamps && now - _checkedAt < FullInterval) return _last;
        _stamps = stamps;
        _checkedAt = now;
        var facts = new List<AuthFact> {
            await GitHubAsync(now, token).ConfigureAwait(false),
            await CodexAsync(now, token).ConfigureAwait(false),
            await ClaudeAsync(now, token).ConfigureAwait(false)
        };
        return _last = facts.Select(Fit).ToArray();
    }

    /// <summary>File times and sizes of everything watched, so a change shows up within a minute without running a CLI.</summary>
    private string Stamps() => string.Join("|", new[] { _paths.GitHubHosts, _paths.CodexConfig, _paths.CodexCredentials, _paths.ClaudeCredentials }
        .Concat(_paths.Candidates("gh")).Concat(_paths.Candidates("codex")).Concat(_paths.Candidates("claude"))
        .Select(path => path + "=" + Stamp(path)));

    private static string Stamp(string path) {
        try {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "-";
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return "?"; }
    }

    private async Task<AuthFact> GitHubAsync(DateTimeOffset now, CancellationToken token) {
        var binaries = _paths.Candidates("gh").Where(File.Exists).ToArray();
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        AddWritten(facts, "hosts-written", _paths.GitHubHosts);
        if (EnvironmentOverrides() is { Length: > 0 } overrides) facts["env-override"] = overrides;
        if (binaries.Length == 0) return new("github", "not-installed", now) { Facts = facts };
        facts["binary"] = binaries[0];
        if (await VersionAsync(binaries[0], token).ConfigureAwait(false) is { } version) facts["version"] = version;
        var (code, output) = await _run(binaries[0], ["auth", "status", "--json", "hosts"], token).ConfigureAwait(false);
        if (code is null) { facts["check"] = "not-run"; return new("github", "unknown", now) { Facts = facts }; }
        var state = ParseGitHub(output, facts);
        if (code != 0) facts["check"] = "exit-" + code;
        return new("github", state, now) { Facts = facts };
    }

    /// <summary>Reads <c>gh auth status --json hosts</c> for github.com: the active account's state, login, token source
    /// and scopes, and the other accounts signed in. Returns the state; an answer that can't be read is unknown.</summary>
    internal static string ParseGitHub(string output, Dictionary<string, string> facts) {
        try {
            using var json = JsonDocument.Parse(output);
            if (!json.RootElement.TryGetProperty("hosts", out var hosts) || hosts.ValueKind != JsonValueKind.Object) return "unknown";
            if (!hosts.TryGetProperty("github.com", out var accounts) || accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() == 0)
                return "signed-out";
            JsonElement? active = null;
            var logins = new List<string>();
            foreach (var account in accounts.EnumerateArray()) {
                if (Text(account, "login") is { } login) logins.Add(login);
                if (account.TryGetProperty("active", out var flag) && flag.ValueKind == JsonValueKind.True) active = account;
            }
            if (logins.Count > 1) facts["accounts"] = string.Join(", ", logins.Order(StringComparer.OrdinalIgnoreCase));
            if (active is not { } entry) return "signed-out";
            if (Text(entry, "login") is { } name) facts["account"] = name;
            if (Text(entry, "tokenSource") is { } source) facts["token-source"] = source;
            if (Text(entry, "gitProtocol") is { } protocol) facts["git-protocol"] = protocol;
            if (Text(entry, "scopes") is { } scopes)
                facts["scopes"] = string.Join(", ", scopes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Order(StringComparer.Ordinal));
            var state = Text(entry, "state");
            if (state is not (null or "success")) facts["check"] = state;
            return state switch { "success" => "signed-in", "error" => "signed-out", _ => "unknown" };
        } catch (JsonException) {
            return "unknown";
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    /// <summary>Names of token variables set for this process, the user or the machine; their values are never read out.</summary>
    internal string EnvironmentOverrides() {
        var found = new List<string>();
        foreach (var name in TokenVariables) {
            var where = new List<string>();
            if (!string.IsNullOrEmpty(_paths.Variable(name, EnvironmentVariableTarget.Process))) where.Add("process");
            if (OperatingSystem.IsWindows()) {
                if (!string.IsNullOrEmpty(_paths.Variable(name, EnvironmentVariableTarget.User))) where.Add("user");
                if (!string.IsNullOrEmpty(_paths.Variable(name, EnvironmentVariableTarget.Machine))) where.Add("machine");
            }
            if (where.Count > 0) found.Add(name + " (" + string.Join(", ", where) + ")");
        }
        return string.Join("; ", found);
    }

    private async Task<AuthFact> CodexAsync(DateTimeOffset now, CancellationToken token) {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        AddWritten(facts, "config-written", _paths.CodexConfig);
        if (Fingerprint(_paths.CodexConfig) is { } fingerprint) facts["config-fingerprint"] = fingerprint;
        var store = CredentialStore(_paths.CodexConfig);
        if (store is not null) facts["credential-store"] = store;
        var hasFile = File.Exists(_paths.CodexCredentials);
        facts["credentials"] = hasFile ? "file" : "none";
        AddWritten(facts, "credentials-written", _paths.CodexCredentials);
        var found = await BinariesAsync("codex", facts, token).ConfigureAwait(false);
        // A missing auth.json means signed out only when Codex keeps its sign-in in that file.
        var state = !found ? "not-installed" : !hasFile && store is null or "file" ? "signed-out" : "unknown";
        return new("codex", state, now) { Facts = facts };
    }

    private async Task<AuthFact> ClaudeAsync(DateTimeOffset now, CancellationToken token) {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (OperatingSystem.IsMacOS()) facts["credentials"] = "keychain";
        else {
            facts["credentials"] = File.Exists(_paths.ClaudeCredentials) ? "file" : "none";
            AddWritten(facts, "credentials-written", _paths.ClaudeCredentials);
        }
        var found = await BinariesAsync("claude", facts, token).ConfigureAwait(false);
        return new("claude", found ? "unknown" : "not-installed", now) { Facts = facts };
    }

    /// <summary>Every distinct binary of a tool and its version. The first is the one the agent service uses, when set.</summary>
    private async Task<bool> BinariesAsync(string tool, Dictionary<string, string> facts, CancellationToken token) {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var rows = new List<(string Path, string? Version)>();
        foreach (var path in _paths.Candidates(tool)) {
            if (!File.Exists(path) || !seen.Add(Resolve(path))) continue;
            rows.Add((path, await VersionAsync(path, token).ConfigureAwait(false)));
            if (rows.Count == 6) break;
        }
        if (rows.Count == 0) return false;
        facts["binary"] = rows[0].Path;
        if (rows[0].Version is { } version) facts["version"] = version;
        if (rows.Count > 1) facts["binaries"] = string.Join("; ", rows.Select(row => row.Path + " " + (row.Version ?? "?")));
        var versions = rows.Select(row => row.Version).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (versions.Length > 1) facts["versions"] = string.Join(", ", versions);
        return true;
    }

    private static string Resolve(string path) {
        try { return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Path.GetFullPath(path); }
    }

    /// <summary>The binary's <c>--version</c>, run again only when the file changes.</summary>
    private async Task<string?> VersionAsync(string path, CancellationToken token) {
        var stamp = Stamp(path);
        if (_versions.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Version;
        var (code, output) = await _run(path, ["--version"], token).ConfigureAwait(false);
        var version = code == 0 ? ParseVersion(output) : null;
        _versions[path] = (stamp, version);
        return version;
    }

    internal static string? ParseVersion(string output) => VersionPattern().Match(output) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:[-+.][0-9A-Za-z.-]+)?")]
    private static partial Regex VersionPattern();

    /// <summary>Codex's <c>cli_auth_credentials_store</c> setting, or null when it isn't set.</summary>
    internal static string? CredentialStore(string configPath) {
        try {
            if (!File.Exists(configPath) || new FileInfo(configPath).Length > 1_048_576) return null;
            foreach (var line in File.ReadLines(configPath)) {
                if (line.TrimStart().StartsWith('[')) break; // Only a top-level key counts.
                if (StorePattern().Match(line) is { Success: true } match) return match.Groups[1].Value;
            }
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return null;
    }

    [GeneratedRegex("""^\s*cli_auth_credentials_store\s*=\s*"([a-z]+)"\s*(?:#.*)?$""")]
    private static partial Regex StorePattern();

    /// <summary>The first 12 hex digits of the file's SHA-256, so a rewrite shows without its contents leaving the machine.</summary>
    internal static string? Fingerprint(string path) {
        try {
            if (!File.Exists(path) || new FileInfo(path).Length > 1_048_576) return null;
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream))[..12];
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void AddWritten(Dictionary<string, string> facts, string key, string path) {
        try {
            var info = new FileInfo(path);
            if (info.Exists) facts[key] = info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    /// <summary>Keeps a fact within the service's bounds, so one long path can't get the whole health report rejected.</summary>
    private static AuthFact Fit(AuthFact fact) => fact with {
        Facts = fact.Facts?.Take(AuthStateValidation.MaximumFacts).ToDictionary(pair => pair.Key,
            pair => pair.Value.Length <= AuthStateValidation.MaximumValue ? pair.Value : pair.Value[..(AuthStateValidation.MaximumValue - 1)] + "…",
            StringComparer.Ordinal)
    };

    private static async Task<(int? Code, string Output)> RunAsync(string executable, string[] arguments, CancellationToken stop) {
        try {
            var start = new ProcessStartInfo(executable) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // gh asks nothing and opens no browser for a status read; this keeps it that way.
            start.Environment["GH_PROMPT_DISABLED"] = "1";
            start.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
            using var process = Process.Start(start);
            if (process is null) return (null, "");
            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var end = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            var output = await process.StandardOutput.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            return (process.ExitCode, output);
        } catch (System.ComponentModel.Win32Exception) {
            return (null, "");
        } catch (OperationCanceledException) when (!stop.IsCancellationRequested) {
            return (null, "");
        }
    }
}

/// <summary>Where this machine keeps each tool's files and binaries. Tests point it at a folder of their own.</summary>
internal sealed class AuthPaths {
    private readonly Func<string, EnvironmentVariableTarget, string?> _variable;
    private readonly Dictionary<string, string[]> _configured;
    private readonly string _home;
    private readonly bool _installFolders;

    public AuthPaths(string home, Func<string, EnvironmentVariableTarget, string?>? variable = null, Dictionary<string, string[]>? configured = null,
        bool installFolders = true) {
        _home = home;
        _installFolders = installFolders;
        _variable = variable ?? Environment.GetEnvironmentVariable;
        _configured = configured ?? [];
        var appData = OperatingSystem.IsWindows() ? Variable("APPDATA", EnvironmentVariableTarget.Process) ?? Path.Combine(home, "AppData", "Roaming") : null;
        var ghDirectory = Variable("GH_CONFIG_DIR", EnvironmentVariableTarget.Process) ??
            (appData is not null ? Path.Combine(appData, "GitHub CLI")
                : Path.Combine(Variable("XDG_CONFIG_HOME", EnvironmentVariableTarget.Process) ?? Path.Combine(home, ".config"), "gh"));
        GitHubHosts = Path.Combine(ghDirectory, "hosts.yml");
        var codexHome = CodexHome = Variable("CODEX_HOME", EnvironmentVariableTarget.Process) ?? Path.Combine(home, ".codex");
        CodexConfig = Path.Combine(codexHome, "config.toml");
        CodexCredentials = Path.Combine(codexHome, "auth.json");
        ClaudeCredentials = Path.Combine(home, ".claude", ".credentials.json");
    }

    public string CodexHome { get; }
    public string GitHubHosts { get; }
    public string CodexConfig { get; }
    public string CodexCredentials { get; }
    public string ClaudeCredentials { get; }

    public string? Variable(string name, EnvironmentVariableTarget target) {
        try { return _variable(name, target) is { Length: > 0 } value ? value : null; }
        catch (System.Security.SecurityException) { return null; }
    }

    /// <summary>The paths a tool may be at, in order: the agent service's own setting, then PATH, then the usual install
    /// folders. A LaunchAgent's PATH is short, so the install folders matter on a Mac.</summary>
    public IEnumerable<string> Candidates(string tool) {
        foreach (var path in _configured.GetValueOrDefault(tool) ?? []) yield return path;
        if (tool == "codex" && Variable("CODEX_BIN", EnvironmentVariableTarget.Process) is { } bin) yield return bin;
        var names = OperatingSystem.IsWindows() ? new[] { tool + ".exe", tool + ".cmd" } : [tool];
        foreach (var directory in (Variable("PATH", EnvironmentVariableTarget.Process) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in names) yield return Path.Combine(directory.Trim('"'), name);
        if (_installFolders) foreach (var path in KnownLocations(tool)) yield return path;
    }

    private IEnumerable<string> KnownLocations(string tool) {
        // The Codex app's background service keeps its own managed CLI; "current" points at the release it runs.
        if (tool == "codex") yield return Path.Combine(CodexHome, "packages", "app-server-daemon", "current", "bin", OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        if (OperatingSystem.IsWindows()) {
            if (tool == "codex" && Variable("LOCALAPPDATA", EnvironmentVariableTarget.Process) is { } local)
                yield return Path.Combine(local, "OpenAI", "Codex", "bin", "codex.exe");
            var appData = Variable("APPDATA", EnvironmentVariableTarget.Process) ?? Path.Combine(_home, "AppData", "Roaming");
            if (tool == "gh") yield return Path.Combine(Variable("ProgramFiles", EnvironmentVariableTarget.Process) ?? @"C:\Program Files", "GitHub CLI", "gh.exe");
            else {
                yield return Path.Combine(appData, "npm", tool + ".cmd");
                yield return Path.Combine(_home, ".local", "bin", tool + ".exe");
            }
            yield break;
        }
        foreach (var directory in new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(_home, ".local", "bin"), Path.Combine(_home, ".npm-global", "bin") })
            yield return Path.Combine(directory, tool);
        if (tool == "codex") yield return "/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex";
        if (tool == "claude") yield return Path.Combine(_home, ".claude", "local", "claude");
    }

    /// <summary>The paths for this user, with the agent service's Codex and Claude binaries from its settings beside the
    /// reporter's, when it has them.</summary>
    public static AuthPaths ForReporter(string reporterConfig) {
        var configured = new Dictionary<string, string[]>(StringComparer.Ordinal);
        try {
            var agent = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reporterConfig))!, "agent.json");
            if (File.Exists(agent) && new FileInfo(agent).Length < 65_536) {
                using var json = JsonDocument.Parse(File.ReadAllText(agent));
                foreach (var (tool, key) in new[] { ("codex", "codexExecutable"), ("claude", "claudeExecutable") })
                    if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } path)
                        configured[tool] = [path];
            }
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), configured: configured);
    }
}
