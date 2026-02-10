using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Cli.Setup.Web;

	internal sealed partial class WebApi {
	    private async Task HandleUsageAsync(System.Net.HttpListenerContext context) {
	        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
	        if (body is null) {
	            return;
	        }
	        UsageRequest request;
	        try {
	            request = JsonSerializer.Deserialize<UsageRequest>(body, _jsonOptions) ?? new UsageRequest();
	        } catch (JsonException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
            return;
        }
        TempFile? tempFile = null;
        try {
            var authPath = request.AuthB64Path;
	            if (string.IsNullOrWhiteSpace(authPath) && string.IsNullOrWhiteSpace(request.AuthB64)) {
	                authPath = AuthPaths.ResolveAuthPath();
	            }
	            if (!string.IsNullOrWhiteSpace(request.AuthB64)) {
	                var raw = Convert.FromBase64String(request.AuthB64);
	                var content = Encoding.UTF8.GetString(raw);
	                var tempPath = Path.Combine(Path.GetTempPath(), $"intelligencex-auth-{Guid.NewGuid():N}.json");
	                await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)) {
	                    var bytes = Encoding.UTF8.GetBytes(content);
	                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
	                }
	                TryHardenTempFile(tempPath);
	                tempFile = new TempFile(tempPath);
	                authPath = tempPath;
	            }
            if (string.IsNullOrWhiteSpace(authPath) || !File.Exists(authPath)) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "Auth bundle path not found." }).ConfigureAwait(false);
                return;
            }

            var options = new OpenAINativeOptions {
                AuthStore = new FileAuthBundleStore(authPath, request.AuthKey)
            };
            if (!string.IsNullOrWhiteSpace(request.ChatGptApiBaseUrl)) {
                if (!TryGetChatGptApiBaseUrl(request.ChatGptApiBaseUrl, out var chatGptBaseUrl, out var chatGptError)) {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new { error = chatGptError }).ConfigureAwait(false);
                    return;
                }
                options.ChatGptApiBaseUrl = chatGptBaseUrl;
            }

            using var service = new ChatGptUsageService(options);
            var report = await service.GetReportAsync(request.IncludeEvents, CancellationToken.None).ConfigureAwait(false);
            TrySaveCache(report.Snapshot);
            var response = BuildUsageResponse(report, DateTimeOffset.UtcNow);
            await WriteJsonAsync(context, response).ConfigureAwait(false);
        } catch (FormatException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid base64 auth bundle." }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleUsageAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        } finally {
            tempFile?.Dispose();
        }
    }

    private async Task HandleUsageCacheAsync(System.Net.HttpListenerContext context) {
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "GET required" }).ConfigureAwait(false);
            return;
        }
        try {
            if (!ChatGptUsageCache.TryLoad(out var entry) || entry is null) {
                await WriteJsonAsync(context, new UsageResponse()).ConfigureAwait(false);
                return;
            }
            var response = new UsageResponse {
                Usage = UsageSnapshot.From(entry.Snapshot),
                Events = new List<UsageEvent>(),
                UpdatedAt = entry.UpdatedAt.ToUniversalTime().ToString("u")
            };
            await WriteJsonAsync(context, response).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleUsageCacheAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

    private static UsageResponse BuildUsageResponse(ChatGptUsageReport report, DateTimeOffset updatedAt) {
        return new UsageResponse {
            Usage = UsageSnapshot.From(report.Snapshot),
            Events = UsageEvent.From(report.Events),
            UpdatedAt = updatedAt.ToUniversalTime().ToString("u")
        };
    }

    private static void TrySaveCache(ChatGptUsageSnapshot snapshot) {
        try {
            ChatGptUsageCache.Save(snapshot);
        } catch (Exception ex) {
            Trace.TraceWarning($"Failed to save usage cache: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
