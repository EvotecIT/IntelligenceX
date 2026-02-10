using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task HandleAppManifestAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<AppManifestRequest>(body, _jsonOptions) ?? new AppManifestRequest();
        if (string.IsNullOrWhiteSpace(request.AppName)) {
            request.AppName = "IntelligenceX Reviewer";
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            var result = await GitHubAppManifestFlow.RunAsync(new GitHubAppManifestOptions {
                AppName = request.AppName!,
                Owner = request.Owner,
                AuthBaseUrl = ResolveAuthBaseUrl(request.AuthBaseUrl),
                ApiBaseUrl = apiBaseUrl
            }, CancellationToken.None).ConfigureAwait(false);

            if (result is null) {
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = "Manifest flow failed." }).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context, new {
                appId = result.AppId,
                pem = result.Pem
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleAppManifestAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

    private async Task HandleAppInstallationsAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<AppInstallationRequest>(body, _jsonOptions) ?? new AppInstallationRequest();
        if (request.AppId <= 0 || string.IsNullOrWhiteSpace(request.Pem)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing appId or pem" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubAppClient(request.AppId, request.Pem!, apiBaseUrl);
            var installs = await client.ListInstallationsAsync().ConfigureAwait(false);
            await WriteJsonAsync(context, new {
                installations = installs.ConvertAll(i => new { id = i.Id, login = i.AccountLogin })
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleAppInstallationsAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

    private async Task HandleAppTokenAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<AppTokenRequest>(body, _jsonOptions) ?? new AppTokenRequest();
        if (request.AppId <= 0 || string.IsNullOrWhiteSpace(request.Pem) || request.InstallationId <= 0) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing appId, pem, or installationId" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubAppClient(request.AppId, request.Pem!, apiBaseUrl);
            var token = await client.CreateInstallationTokenAsync(request.InstallationId).ConfigureAwait(false);
            await WriteJsonAsync(context, new { token }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleAppTokenAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

}
