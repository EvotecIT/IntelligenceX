using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private static readonly Regex RepoSegmentRegex = new("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(System.Net.HttpListenerContext context) {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var normalizedPath = path.Length > 1 ? path.TrimEnd('/') : path;
        if (normalizedPath.Equals("/api/repos", StringComparison.OrdinalIgnoreCase)) {
            await HandleReposAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/repo-status", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoStatusAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/repo-config", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoConfigAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/repo-workflow", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoWorkflowAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/device-code", StringComparison.OrdinalIgnoreCase)) {
            await HandleDeviceCodeAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/device-poll", StringComparison.OrdinalIgnoreCase)) {
            await HandleDevicePollAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/app-manifest", StringComparison.OrdinalIgnoreCase)) {
            await HandleAppManifestAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/app-installations", StringComparison.OrdinalIgnoreCase)) {
            await HandleAppInstallationsAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/app-token", StringComparison.OrdinalIgnoreCase)) {
            await HandleAppTokenAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/setup/plan", StringComparison.OrdinalIgnoreCase)) {
            await HandleSetupAsync(context, dryRun: true).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/setup/apply", StringComparison.OrdinalIgnoreCase)) {
            await HandleSetupAsync(context, dryRun: false).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/usage-cache", StringComparison.OrdinalIgnoreCase)) {
            await HandleUsageCacheAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/usage", StringComparison.OrdinalIgnoreCase)) {
            await HandleUsageAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/openai-login", StringComparison.OrdinalIgnoreCase)) {
            await HandleOpenAILoginAsync(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = 404;
        await WriteJsonAsync(context, new { error = "Not found" }).ConfigureAwait(false);
    }
}
