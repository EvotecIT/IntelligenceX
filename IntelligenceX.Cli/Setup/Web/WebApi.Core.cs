using System;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private const string SetupCsrfHeaderName = "X-IntelligenceX-Setup-Request";
    private const string SetupCsrfHeaderValue = "1";
    private static readonly Regex RepoSegmentRegex = new("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(System.Net.HttpListenerContext context) {
        try {
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
                if (!await EnsureLocalPostSetupRequestAsync(context).ConfigureAwait(false)) {
                    return;
                }
                await HandleSetupAsync(context, dryRun: true).ConfigureAwait(false);
                return;
            }
            if (normalizedPath.Equals("/api/setup/effective-config", StringComparison.OrdinalIgnoreCase)) {
                if (!await EnsureLocalPostSetupRequestAsync(context).ConfigureAwait(false)) {
                    return;
                }
                await HandleSetupEffectiveConfigAsync(context).ConfigureAwait(false);
                return;
            }
            if (normalizedPath.Equals("/api/setup/apply", StringComparison.OrdinalIgnoreCase)) {
                if (!await EnsureLocalPostSetupRequestAsync(context).ConfigureAwait(false)) {
                    return;
                }
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
        } finally {
            try {
                context.Response.Close();
            } catch {
                // Best effort close.
            }
        }
    }

    private static bool IsLoopbackRequest(System.Net.HttpListenerRequest request) {
        if (request.IsLocal) {
            return true;
        }
        var remote = request.RemoteEndPoint;
        return remote is not null && IPAddress.IsLoopback(remote.Address);
    }

    private async Task<bool> EnsureLocalPostSetupRequestAsync(System.Net.HttpListenerContext context) {
        if (!IsLoopbackRequest(context.Request)) {
            context.Response.StatusCode = 403;
            await WriteJsonAsync(context, new { error = "Local requests only." }).ConfigureAwait(false);
            return false;
        }
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return false;
        }
        var csrfHeaders = context.Request.Headers.GetValues(SetupCsrfHeaderName);
        var csrfHeader = csrfHeaders is { Length: 1 } ? csrfHeaders[0] : null;
        if (!string.Equals(csrfHeader, SetupCsrfHeaderValue, StringComparison.Ordinal)) {
            context.Response.StatusCode = 403;
            await WriteJsonAsync(context, new { error = "Missing or invalid setup CSRF header." }).ConfigureAwait(false);
            return false;
        }

        return true;
    }
}
