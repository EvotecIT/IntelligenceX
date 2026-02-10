using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Setup.Web;

    internal sealed partial class WebApi {
        private async Task HandleOpenAILoginAsync(System.Net.HttpListenerContext context) {
            var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
            if (body is null) {
                return;
            }
            OpenAILoginRequest request;
            try {
                request = JsonSerializer.Deserialize<OpenAILoginRequest>(body, _jsonOptions) ?? new OpenAILoginRequest();
            } catch (JsonException) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
                return;
            }

        try {
            var config = OAuthConfig.FromEnvironment();
            if (!string.IsNullOrWhiteSpace(request.ClientId)) {
                config.ClientId = request.ClientId!;
            }
            if (request.RedirectPort < 0 || request.RedirectPort > 65535) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "RedirectPort must be 0 (default) or between 1 and 65535." }).ConfigureAwait(false);
                return;
            }
            if (request.TimeoutSeconds < 0) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "TimeoutSeconds must be 0 (default) or a positive integer." }).ConfigureAwait(false);
                return;
            }
            if (request.RedirectPort > 0) {
                config.RedirectPort = request.RedirectPort;
            }

            var service = new OAuthLoginService();
            var options = new OAuthLoginOptions(config) {
                UseLocalListener = true,
                Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 180),
                OnAuthUrl = url => {
                    OpenBrowser(url);
                    return Task.CompletedTask;
                }
            };

            var result = await service.LoginAsync(options).ConfigureAwait(false);
            var json = AuthBundleSerializer.Serialize(result.Bundle);
            var authB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            await WriteJsonAsync(context, new {
                authB64,
                accountId = result.Bundle.AccountId,
                expiresAt = result.Bundle.ExpiresAt?.ToUniversalTime().ToString("u")
            }).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            context.Response.StatusCode = 408;
            await WriteJsonAsync(context, new { error = "Login timed out. Please try again." }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            Trace.TraceError($"HandleOpenAILoginAsync failed: {ex}");
            await WriteJsonAsync(context, new { error = "Internal server error." }).ConfigureAwait(false);
        }
    }

    private static void OpenBrowser(string url) {
        if (!TryNormalizeHttpUrl(url, out var safeUrl)) {
            return;
        }
        try {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Process.Start(new ProcessStartInfo(safeUrl) { UseShellExecute = true });
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                Process.Start("open", safeUrl);
            } else {
                Process.Start("xdg-open", safeUrl);
            }
        } catch (Exception ex) {
            Trace.TraceWarning($"Failed to open browser: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
