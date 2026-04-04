namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    private static string? _temporaryAuthPathFromEnv;

    private static async Task<bool> TryWriteAuthFromEnvAsync() {
        var authJson = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_JSON");
        var authB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        if (string.IsNullOrWhiteSpace(authJson) && string.IsNullOrWhiteSpace(authB64)) {
            return true;
        }

        string content;
        if (!string.IsNullOrWhiteSpace(authJson)) {
            content = authJson!;
            SecretsAudit.Record("Auth store loaded from INTELLIGENCEX_AUTH_JSON");
        } else {
            try {
                var bytes = Convert.FromBase64String(authB64!);
                content = Encoding.UTF8.GetString(bytes);
                SecretsAudit.Record("Auth store loaded from INTELLIGENCEX_AUTH_B64");
            } catch {
                Console.Error.WriteLine("Failed to decode INTELLIGENCEX_AUTH_B64.");
                return false;
            }
        }

        if (IsEncryptedStore(content) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY"))) {
            Console.Error.WriteLine("Auth store is encrypted but INTELLIGENCEX_AUTH_KEY is not set.");
            return false;
        }

        var path = ResolveAuthWritePathForEnvImport();
        if (HasAuthStoreBundles(content)) {
            WriteAuthStoreContent(content, path);
            return true;
        }

        var bundle = AuthBundleSerializer.Deserialize(content);
        if (bundle is not null) {
            try {
                var store = new FileAuthBundleStore(path);
                await store.SaveAsync(bundle).ConfigureAwait(false);
                return true;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to write auth bundle: {ex.Message}");
                return false;
            }
        }

        Console.Error.WriteLine("Auth bundle content is invalid.");
        return false;
    }

    private static void WriteAuthStoreContent(string content, string path) {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }

    private static string ResolveAuthWritePathForEnvImport() {
        var configuredPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath)) {
            return configuredPath;
        }
        if (!string.IsNullOrWhiteSpace(_temporaryAuthPathFromEnv)) {
            return _temporaryAuthPathFromEnv!;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "intelligencex-reviewer");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"auth-{Guid.NewGuid():N}.json");
        _temporaryAuthPathFromEnv = tempPath;
        Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", tempPath);
        SecretsAudit.Record("Auth store path isolated to a temporary file for this run.");
        return tempPath;
    }

    private static void CleanupTempAuthPathFromEnv() {
        var tempPath = _temporaryAuthPathFromEnv;
        if (string.IsNullOrWhiteSpace(tempPath)) {
            return;
        }
        try {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        } catch {
            // Best-effort cleanup.
        }

        try {
            var tempDir = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrWhiteSpace(tempDir)
                && Directory.Exists(tempDir)
                && !Directory.EnumerateFileSystemEntries(tempDir).Any()) {
                Directory.Delete(tempDir);
            }
        } catch {
            // Best-effort cleanup.
        }

        var currentPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        if (string.Equals(currentPath, tempPath, StringComparison.OrdinalIgnoreCase)) {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", null);
        }
        _temporaryAuthPathFromEnv = null;
    }

    private static bool IsEncryptedStore(string content) {
        return content.TrimStart().StartsWith("{\"encrypted\":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAuthStoreBundles(string content) {
        var value = JsonLite.Parse(content);
        var obj = value?.AsObject();
        if (obj is null) {
            return false;
        }
        var bundles = obj.GetObject("bundles");
        if (bundles is null) {
            return false;
        }
        foreach (var entry in bundles) {
            if (entry.Value?.AsObject() is not null) {
                return true;
            }
        }
        return false;
    }

    private sealed class RunOptions {
        public string? Provider { get; set; }
        public string? ProviderFallback { get; set; }
        public string? CodeHost { get; set; }
        public string? AzureOrg { get; set; }
        public string? AzureProject { get; set; }
        public string? AzureRepo { get; set; }
        public string? AzureBaseUrl { get; set; }
        public string? AzureTokenEnv { get; set; }
        public bool ShowHelp { get; set; }
        public List<string> Errors { get; } = new();

        public bool HasAzureOverrides =>
            !string.IsNullOrWhiteSpace(AzureOrg) ||
            !string.IsNullOrWhiteSpace(AzureProject) ||
            !string.IsNullOrWhiteSpace(AzureRepo) ||
            !string.IsNullOrWhiteSpace(AzureBaseUrl) ||
            !string.IsNullOrWhiteSpace(AzureTokenEnv);
    }

    private sealed class RunOptionSpec {
        public RunOptionSpec(string name, string valueHint, string description, bool requiresValue, Action<RunOptions, string?> apply) {
            Name = name;
            ValueHint = valueHint;
            Description = description;
            RequiresValue = requiresValue;
            Apply = apply;
        }

        public string Name { get; }
        public string ValueHint { get; }
        public string Description { get; }
        public bool RequiresValue { get; }
        public Action<RunOptions, string?> Apply { get; }
    }

    private static readonly RunOptionSpec[] RunOptionSpecs = {
        new RunOptionSpec("--provider", "<openai|codex|chatgpt|openai-codex|openai-compatible|ollama|claude|anthropic|copilot|azure>", "AI provider or Azure DevOps code host (aliases: azuredevops, azure-devops, ado)", true,
            (options, value) => options.Provider = value),
        new RunOptionSpec("--provider-fallback", "<openai|codex|chatgpt|openai-codex|openai-compatible|ollama|claude|anthropic|copilot>", "Optional fallback AI provider when the primary provider fails", true,
            (options, value) => options.ProviderFallback = value),
        new RunOptionSpec("--code-host", "<github|azure>", "Override code host (azure/azuredevops supported)", true,
            (options, value) => options.CodeHost = value),
        new RunOptionSpec("--azure-org", "<org>", "Azure DevOps organization", true,
            (options, value) => options.AzureOrg = value),
        new RunOptionSpec("--azure-project", "<project>", "Azure DevOps project", true,
            (options, value) => options.AzureProject = value),
        new RunOptionSpec("--azure-repo", "<repo>", "Azure DevOps repository id or name", true,
            (options, value) => options.AzureRepo = value),
        new RunOptionSpec("--azure-base-url", "<url>", "Azure DevOps base URL", true,
            (options, value) => options.AzureBaseUrl = value),
        new RunOptionSpec("--azure-token-env", "<env>", "Env var holding Azure DevOps token", true,
            (options, value) => options.AzureTokenEnv = value)
    };

    private static readonly IReadOnlyDictionary<string, RunOptionSpec> RunOptionSpecMap =
        RunOptionSpecs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

    private static RunOptions ParseRunOptions(string[] args) {
        var options = new RunOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                default:
                    if (RunOptionSpecMap.TryGetValue(arg, out var spec)) {
                        var value = spec.RequiresValue ? ReadValue(args, ref i, spec.Name, options.Errors) : null;
                        if (value is not null || !spec.RequiresValue) {
                            spec.Apply(options, value);
                        }
                        break;
                    }
                    options.Errors.Add($"Unknown option: {arg}");
                    break;
            }
        }
        if (!string.IsNullOrWhiteSpace(options.Provider) && !IsValidProvider(options.Provider)) {
            options.Errors.Add(
                $"Unsupported provider '{options.Provider}'. Use openai/codex/chatgpt/openai-codex, openai-compatible/ollama/openrouter, claude/anthropic, copilot, or azure/azuredevops.");
        }
        if (!string.IsNullOrWhiteSpace(options.ProviderFallback) && !IsValidAiProvider(options.ProviderFallback)) {
            options.Errors.Add(
                $"Unsupported provider fallback '{options.ProviderFallback}'. Use openai/codex/chatgpt/openai-codex, openai-compatible/ollama/openrouter, claude/anthropic, or copilot.");
        }
        if (!string.IsNullOrWhiteSpace(options.CodeHost) && !IsValidCodeHost(options.CodeHost)) {
            options.Errors.Add($"Unsupported code host '{options.CodeHost}'. Use github or azure/azuredevops.");
        }
        return options;
    }

    private static string? ReadValue(string[] args, ref int index, string name, List<string> errors) {
        if (index + 1 >= args.Length) {
            errors.Add($"Missing value for {name}.");
            return null;
        }
        index++;
        var value = args[index];
        if (string.IsNullOrWhiteSpace(value)) {
            errors.Add($"Empty value for {name}.");
            return null;
        }
        return value;
    }

    private static bool IsValidProvider(string provider) {
        return IsValidAiProvider(provider) ||
               IsAzureProvider(provider);
    }

    private static bool IsValidAiProvider(string provider) {
        return ReviewProviderContracts.TryParseProviderAlias(provider, out _);
    }

    private static bool IsAzureProvider(string provider) {
        return provider.Equals("azure", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("azuredevops", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("azure-devops", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("ado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidCodeHost(string codeHost) {
        return codeHost.Equals("github", StringComparison.OrdinalIgnoreCase) ||
               IsAzureProvider(codeHost);
    }

    private static void ApplyRunOptions(RunOptions options) {
        var provider = options.Provider?.Trim();
        var codeHost = options.CodeHost?.Trim();
        if (!string.IsNullOrWhiteSpace(provider)) {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", provider);
            if (IsAzureProvider(provider)) {
                if (string.IsNullOrWhiteSpace(codeHost)) {
                    codeHost = "azure";
                }
            }
        }
        var providerFallback = options.ProviderFallback?.Trim();
        if (!string.IsNullOrWhiteSpace(providerFallback)) {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", providerFallback);
        }
        if (!string.IsNullOrWhiteSpace(codeHost)) {
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", codeHost);
        } else if (options.HasAzureOverrides) {
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", "azure");
        }
        if (!string.IsNullOrWhiteSpace(options.AzureOrg)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_ORG", options.AzureOrg);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureProject)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_PROJECT", options.AzureProject);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureRepo)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_REPO", options.AzureRepo);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureBaseUrl)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_BASE_URL", options.AzureBaseUrl);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureTokenEnv)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_TOKEN_ENV", options.AzureTokenEnv);
        }
    }

    private static void PrintRunHelp() {
        PrintRunHelp(Console.Out);
    }

    private static void PrintRunHelp(TextWriter writer) {
        writer.WriteLine("Reviewer run options:");
        var leftWidth = RunOptionSpecs
            .Select(spec => $"{spec.Name} {spec.ValueHint}".Length)
            .DefaultIfEmpty(0)
            .Max();
        foreach (var spec in RunOptionSpecs) {
            var left = $"{spec.Name} {spec.ValueHint}".PadRight(leftWidth);
            writer.WriteLine($"  {left}  {spec.Description}");
        }
    }

    private static async Task<bool> ValidateAuthAsync(ReviewSettings settings) {
        var provider = ReviewProviderContracts.Get(settings.Provider);
        if (provider.Provider == ReviewProvider.Claude) {
            try {
                _ = new ReviewRunner(settings).ResolveClaudeApiKeyForTests();
                return true;
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.Message);
                return false;
            }
        }
        if (!provider.RequiresOpenAiAuthStore) {
            return true;
        }

        var authPath = AuthPaths.ResolveAuthPath();
        if (!File.Exists(authPath)) {
            Console.Error.WriteLine("Missing OpenAI auth store.");
            Console.Error.WriteLine("Set INTELLIGENCEX_AUTH_B64 (store export) or run `intelligencex auth login`.");
            return false;
        }

        try {
            var store = new FileAuthBundleStore();
            var accountId = settings.OpenAiAccountId;
            var bundle = await store.GetAsync("openai-codex", accountId).ConfigureAwait(false)
                         ?? await store.GetAsync("openai", accountId).ConfigureAwait(false)
                         ?? await store.GetAsync("chatgpt", accountId).ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine(string.IsNullOrWhiteSpace(accountId)
                    ? $"No OpenAI auth bundle found in {authPath}."
                    : $"No OpenAI auth bundle found for account '{accountId}' in {authPath}.");
                Console.Error.WriteLine("Export a store bundle with `intelligencex auth export --format store-base64`.");
                return false;
            }
            SecretsAudit.Record($"OpenAI auth bundle '{bundle.Provider}' from {authPath}");
            if (bundle.ExpiresAt.HasValue && bundle.IsExpired()) {
                Console.Error.WriteLine(
                    $"OpenAI auth bundle expired at {bundle.ExpiresAt.Value.ToUniversalTime():O}.");
                Console.Error.WriteLine(
                    $"Refresh it with `{ReviewDiagnostics.BuildAuthRemediationCommand()}`.");
                return false;
            }

            if (settings.OpenAITransport == IntelligenceX.OpenAI.OpenAITransportKind.Native) {
                var nativeOptions = new OpenAI.Native.OpenAINativeOptions {
                    AuthStore = store,
                    AuthAccountId = accountId
                };
                try {
                    var authManager = new OpenAI.Native.OpenAINativeAuthManager(nativeOptions);
                    var validBundle = await authManager.TryGetValidBundleAsync(CancellationToken.None).ConfigureAwait(false);
                    if (validBundle is null) {
                        Console.Error.WriteLine("OpenAI auth bundle could not be loaded.");
                        Console.Error.WriteLine(
                            $"Refresh it with `{ReviewDiagnostics.BuildAuthRemediationCommand()}`.");
                        return false;
                    }
                } catch (Exception ex) {
                    var classification = ReviewDiagnostics.Classify(ex);
                    if (classification.Category == ReviewDiagnostics.ReviewErrorCategory.Auth) {
                        Console.Error.WriteLine(
                            $"OpenAI auth bundle is stale or no longer usable: {classification.Summary}.");
                        Console.Error.WriteLine(
                            $"Refresh it with `{ReviewDiagnostics.BuildAuthRemediationCommand()}`.");
                        return false;
                    }
                    throw;
                }
            }
            return true;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load auth store: {ex.Message}");
            if (ex.Message.Contains("INTELLIGENCEX_AUTH_KEY", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine("Set INTELLIGENCEX_AUTH_KEY to decrypt the auth store.");
            }
            return false;
        }
    }

    private static async Task<(bool Success, string? Error, bool BudgetGuardEvaluated)> TryResolveOpenAiAccountAsync(ReviewSettings settings) {
        var provider = ReviewProviderContracts.Get(settings.Provider);
        if (!provider.RequiresOpenAiAuthStore) {
            return (true, null, false);
        }

        var configuredCandidates = ResolveConfiguredOpenAiAccountCandidates(settings);
        if (configuredCandidates.Count == 0) {
            return (true, null, false);
        }

        var store = new FileAuthBundleStore();
        var available = new List<string>();
        var missing = new List<string>();
        foreach (var accountId in configuredCandidates) {
            if (await HasOpenAiBundleAsync(store, accountId).ConfigureAwait(false)) {
                available.Add(accountId);
            } else {
                missing.Add(accountId);
            }
        }

        if (available.Count == 0) {
            return (false,
                $"No OpenAI auth bundles found for configured account ids ({string.Join(", ", configuredCandidates)}).", false);
        }

        if (missing.Count > 0) {
            Console.Error.WriteLine(
                $"Configured OpenAI account ids not found in auth store: {string.Join(", ", missing)}.");
        }

        var ordered = OrderOpenAiAccounts(available, settings.OpenAiAccountRotation, settings.OpenAiAccountId,
            ResolveOpenAiRotationSeed());
        if (ordered.Count == 0) {
            return (false, "No OpenAI accounts available after applying account rotation policy.", false);
        }

        settings.OpenAiAccountIds = ordered;
        var requiresBudgetEvaluation = settings.ReviewUsageBudgetGuard &&
                                       (settings.ReviewUsageBudgetAllowCredits ||
                                        settings.ReviewUsageBudgetAllowWeeklyLimit);
        if (!requiresBudgetEvaluation) {
            settings.OpenAiAccountId = ordered[0];
            AnnounceSelectedOpenAiAccount(settings, ordered[0], "configured rotation");
            return (true, null, false);
        }

        var candidates = settings.OpenAiAccountFailover
            ? ordered
            : new[] { ordered[0] };
        var blocked = new List<string>();
        foreach (var accountId in candidates) {
            var snapshot = await TryGetUsageSnapshotAsync(settings, accountId).ConfigureAwait(false);
            if (snapshot is null) {
                settings.OpenAiAccountId = accountId;
                AnnounceSelectedOpenAiAccount(settings, accountId, "usage unavailable (allow)");
                return (true, null, true);
            }

            var budgetFailure = EvaluateUsageBudgetGuardFailure(settings, snapshot);
            if (string.IsNullOrWhiteSpace(budgetFailure)) {
                settings.OpenAiAccountId = accountId;
                AnnounceSelectedOpenAiAccount(settings, accountId, "usage budget available");
                return (true, null, true);
            }

            blocked.Add($"[{accountId}] {TrimUsageBudgetFailureMessage(budgetFailure)}");
        }

        if (!settings.OpenAiAccountFailover && ordered.Count > 1) {
            return (false, $"Primary OpenAI account '{ordered[0]}' is blocked by usage budget guard. " +
                           "Enable review.openaiAccountFailover to allow automatic fallback. " +
                           string.Join(" ", blocked), true);
        }

        return (false, "All configured OpenAI accounts are blocked by usage budget guard. " + string.Join(" ", blocked), true);
    }

    private static IReadOnlyList<string> ResolveConfiguredOpenAiAccountCandidates(ReviewSettings settings) {
        var candidates = ReviewSettings.NormalizeAccountIdList(settings.OpenAiAccountIds).ToList();
        if (!string.IsNullOrWhiteSpace(settings.OpenAiAccountId)) {
            var primary = settings.OpenAiAccountId!.Trim();
            candidates.RemoveAll(id => string.Equals(id, primary, StringComparison.OrdinalIgnoreCase));
            candidates.Insert(0, primary);
        }
        return candidates;
    }

    private static async Task<bool> HasOpenAiBundleAsync(FileAuthBundleStore store, string accountId) {
        var bundle = await store.GetAsync("openai-codex", accountId).ConfigureAwait(false)
                     ?? await store.GetAsync("openai", accountId).ConfigureAwait(false)
                     ?? await store.GetAsync("chatgpt", accountId).ConfigureAwait(false);
        return bundle is not null;
    }

    private static IReadOnlyList<string> OrderOpenAiAccounts(IReadOnlyList<string> accountIds, string rotation, string? stickyAccountId,
        long rotationSeed) {
        var normalized = ReviewSettings.NormalizeOpenAiAccountRotation(rotation, "first-available");
        var ordered = accountIds.ToList();
        if (ordered.Count <= 1) {
            return ordered;
        }

        if (normalized == "sticky") {
            if (!string.IsNullOrWhiteSpace(stickyAccountId)) {
                var pinned = ordered.FindIndex(id => string.Equals(id, stickyAccountId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (pinned > 0) {
                    var selected = ordered[pinned];
                    ordered.RemoveAt(pinned);
                    ordered.Insert(0, selected);
                }
            }
            return ordered;
        }

        if (normalized == "round-robin") {
            var normalizedSeed = rotationSeed > 0 ? rotationSeed - 1 : 0;
            var offset = PositiveModulo(normalizedSeed, ordered.Count);
            if (offset == 0) {
                return ordered;
            }
            var rotated = new List<string>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++) {
                rotated.Add(ordered[(i + offset) % ordered.Count]);
            }
            return rotated;
        }

        return ordered;
    }

    private static int PositiveModulo(long value, int modulo) {
        if (modulo <= 0) {
            return 0;
        }
        var result = value % modulo;
        if (result < 0) {
            result += modulo;
        }
        return (int)result;
    }

    private static long ResolveOpenAiRotationSeed() {
        if (TryReadPositiveInt64Env("GITHUB_RUN_NUMBER", out var runNumber)) {
            return runNumber;
        }
        if (TryReadPositiveInt64Env("GITHUB_RUN_ID", out var runId)) {
            return runId;
        }
        if (TryReadPositiveInt64Env("GITHUB_RUN_ATTEMPT", out var runAttempt)) {
            return runAttempt;
        }
        return 0;
    }

    private static bool TryReadPositiveInt64Env(string envName, out long value) {
        value = 0;
        var raw = Environment.GetEnvironmentVariable(envName);
        return !string.IsNullOrWhiteSpace(raw) &&
               long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
               value > 0;
    }

    private static string TrimUsageBudgetFailureMessage(string message) {
        const string prefix = "Usage budget guard blocked review run: ";
        var trimmed = message.Trim();
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(prefix.Length);
        }
        const string configureMarker = ". Configure reviewUsageBudgetAllowCredits/reviewUsageBudgetAllowWeeklyLimit";
        var markerIndex = trimmed.IndexOf(configureMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0) {
            trimmed = trimmed.Substring(0, markerIndex);
        }
        return trimmed.Trim();
    }

    private static void AnnounceSelectedOpenAiAccount(ReviewSettings settings, string accountId, string reason) {
        if (settings.Diagnostics || settings.OpenAiAccountIds.Count > 1) {
            Console.WriteLine($"Selected OpenAI account '{accountId}' ({reason}).");
        }
    }

    private static bool ShouldSkipByTitle(string title, IReadOnlyList<string> skipTitles) {
        if (skipTitles.Count == 0) {
            return false;
        }
        foreach (var skip in skipTitles) {
            if (!string.IsNullOrWhiteSpace(skip) && title.Contains(skip, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static bool ShouldSkipByLabels(IReadOnlyList<string> labels, IReadOnlyList<string> skipLabels) {
        if (labels.Count == 0 || skipLabels.Count == 0) {
            return false;
        }
        foreach (var label in labels) {
            foreach (var skip in skipLabels) {
                if (!string.IsNullOrWhiteSpace(skip) && label.Equals(skip, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool ShouldSkipByPaths(IReadOnlyList<PullRequestFile> files, IReadOnlyList<string> skipPaths) {
        if (files.Count == 0 || skipPaths.Count == 0) {
            return false;
        }
        var allMatch = true;
        foreach (var file in files) {
            var matches = skipPaths.Any(pattern => GlobMatcher.IsMatch(pattern, file.Filename));
            if (!matches) {
                allMatch = false;
                break;
            }
        }
        return allMatch;
    }

    internal static bool HasWorkflowChanges(IReadOnlyList<PullRequestFile> files) {
        foreach (var file in files) {
            if (IsWorkflowPath(file.Filename)) {
                return true;
            }
        }
        return false;
    }

    internal static int CountWorkflowFiles(IReadOnlyList<PullRequestFile> files) {
        return files.Count(file => IsWorkflowPath(file.Filename));
    }

    internal static IReadOnlyList<PullRequestFile> ExcludeWorkflowFiles(IReadOnlyList<PullRequestFile> files) {
        return files.Where(file => !IsWorkflowPath(file.Filename)).ToList();
    }

    internal static string BuildWorkflowGuardNote(string? headSha, int workflowFileCount, int reviewedFiles, bool skipped) {
        var normalizedWorkflowCount = Math.Max(0, workflowFileCount);
        var normalizedReviewedCount = Math.Max(0, reviewedFiles);
        var workflowLabel = normalizedWorkflowCount == 1 ? "workflow file" : "workflow files";
        var head = string.IsNullOrWhiteSpace(headSha) ? "unknown" : headSha.Trim();
        if (skipped) {
            return $"Workflow-only changes detected ({normalizedWorkflowCount} {workflowLabel}). " +
                   $"Head SHA: {head}. Review skipped to avoid self-modifying workflow runs. " +
                   "Set allowWorkflowChanges or REVIEW_ALLOW_WORKFLOW_CHANGES=true to override.";
        }
        return $"Workflow guardrail active: excluded {normalizedWorkflowCount} {workflowLabel} at commit {head}; " +
               $"reviewed {normalizedReviewedCount} non-workflow file(s).";
    }

    private static bool IsWorkflowPath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return normalized.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) PrepareFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        var list = new List<PullRequestFile>();
        var truncatedPatches = 0;
        var effectiveMaxFiles = maxFiles <= 0 ? int.MaxValue : maxFiles;
        var count = 0;
        foreach (var file in files) {
            if (count >= effectiveMaxFiles) {
                break;
            }
            var patch = file.Patch;
            if (!string.IsNullOrWhiteSpace(patch)) {
                var trimmed = TrimPatch(patch, maxPatchChars);
                if (!string.Equals(trimmed, patch, StringComparison.Ordinal)) {
                    truncatedPatches++;
                }
                patch = trimmed;
            }
            list.Add(new PullRequestFile(file.Filename, file.Status, patch));
            count++;
        }
        var budgetNote = BuildBudgetNote(files.Count, list.Count, truncatedPatches, maxPatchChars);
        return (list, budgetNote);
    }

    internal static string CombineNotes(string? first, string? second) {
        if (string.IsNullOrWhiteSpace(first)) {
            return string.IsNullOrWhiteSpace(second) ? string.Empty : second!.Trim();
        }
        if (string.IsNullOrWhiteSpace(second)) {
            return first.Trim();
        }
        return $"{first.Trim()}\n{second.Trim()}";
    }

}
