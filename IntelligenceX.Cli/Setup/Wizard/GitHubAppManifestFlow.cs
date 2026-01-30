using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class GitHubAppManifestFlow {
    private const string DefaultGitHubAuth = "https://github.com";
    private const string DefaultGitHubApi = "https://api.github.com";

    public static async Task<GitHubAppManifestResult?> RunAsync(GitHubAppManifestOptions options, CancellationToken cancellationToken) {
        var state = Guid.NewGuid().ToString("N");
        var listener = await TryStartListenerAsync().ConfigureAwait(false);
        if (listener is null) {
            return null;
        }

        var baseUri = listener.Prefixes.First();
        var redirectUri = new Uri(new Uri(baseUri), "callback");
        var manifestJson = BuildManifestJson(options, redirectUri.ToString());
        var manifestPageUrl = new Uri(new Uri(baseUri), "manifest?state=" + state);

        var tcs = new TaskCompletionSource<GitHubAppManifestResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () => {
            try {
                while (listener.IsListening) {
                    var context = await listener.GetContextAsync().ConfigureAwait(false);
                    if (context.Request.Url is null) {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    if (context.Request.Url.AbsolutePath.EndsWith("/manifest", StringComparison.OrdinalIgnoreCase)) {
                        await HandleManifestAsync(context, options, state, manifestJson, options.AuthBaseUrl).ConfigureAwait(false);
                        continue;
                    }

                    if (context.Request.Url.AbsolutePath.EndsWith("/callback", StringComparison.OrdinalIgnoreCase)) {
                        var code = await ReadParameterAsync(context, "code").ConfigureAwait(false);
                        var returnedState = await ReadParameterAsync(context, "state").ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(code) || !string.Equals(returnedState, state, StringComparison.Ordinal)) {
                            await WriteHtmlAsync(context.Response, "Invalid callback.").ConfigureAwait(false);
                            tcs.TrySetResult(null);
                            listener.Stop();
                            break;
                        }

                        var result = await ConvertManifestAsync(code!, options.ApiBaseUrl).ConfigureAwait(false);
                        await WriteHtmlAsync(context.Response, result is null
                            ? "Failed to convert manifest."
                            : "GitHub App created. You can return to the CLI.");
                        tcs.TrySetResult(result);
                        listener.Stop();
                        break;
                    }

                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            } catch {
                tcs.TrySetResult(null);
            } finally {
                listener.Close();
            }
        }, cancellationToken);

        TryOpenUrl(manifestPageUrl.ToString());
        return await tcs.Task.ConfigureAwait(false);
    }

    private static async Task<HttpListener?> TryStartListenerAsync() {
        var ports = new[] { 1456, 1457, 1458, 1459, 1460 };
        foreach (var port in ports) {
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try {
                listener.Start();
                return listener;
            } catch {
                listener.Close();
            }
        }
        return null;
    }

    private static async Task HandleManifestAsync(HttpListenerContext context, GitHubAppManifestOptions options, string state,
        string manifestJson, string authBaseUrl) {
        var response = context.Response;
        var createUrl = string.IsNullOrWhiteSpace(options.Owner)
            ? $"{authBaseUrl.TrimEnd('/')}/settings/apps/new?state={state}"
            : $"{authBaseUrl.TrimEnd('/')}/organizations/{options.Owner}/settings/apps/new?state={state}";

        var html = $@"<!doctype html>
<html>
<head><meta charset=""utf-8""><title>Create GitHub App</title></head>
<body style=""font-family: sans-serif; max-width: 800px; margin: 24px;"">
<h2>Create GitHub App</h2>
<p>Click below to create the app with the recommended permissions.</p>
<form action=""{WebUtility.HtmlEncode(createUrl)}"" method=""post"">
  <input type=""hidden"" name=""manifest"" value=""{WebUtility.HtmlEncode(manifestJson)}"">
  <button type=""submit"">Create GitHub App</button>
</form>
</body>
</html>";
        await WriteHtmlAsync(response, html).ConfigureAwait(false);
    }

    private static string BuildManifestJson(GitHubAppManifestOptions options, string redirectUrl) {
        var manifest = new Dictionary<string, object?> {
            ["name"] = options.AppName,
            ["url"] = options.AppUrl,
            ["redirect_url"] = redirectUrl,
            ["public"] = false,
            ["default_permissions"] = new Dictionary<string, string> {
                ["pull_requests"] = "write",
                ["issues"] = "write",
                ["contents"] = "read"
            },
            ["default_events"] = Array.Empty<string>()
        };

        return JsonSerializer.Serialize(manifest);
    }

    private static async Task<GitHubAppManifestResult?> ConvertManifestAsync(string code, string apiBaseUrl) {
        using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var response = await http.PostAsync($"/app-manifests/{code}/conversions", new StringContent("{}", Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            return null;
        }
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var idProp) || !root.TryGetProperty("pem", out var pemProp)) {
            return null;
        }
        return new GitHubAppManifestResult(idProp.GetInt64(), pemProp.GetString() ?? string.Empty);
    }

    private static async Task<string?> ReadParameterAsync(HttpListenerContext context, string key) {
        var query = context.Request.Url?.Query ?? string.Empty;
        var values = ParseFormEncoded(query.TrimStart('?'));
        if (values.TryGetValue(key, out var found)) {
            return found;
        }

        if (context.Request.HasEntityBody) {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            var form = ParseFormEncoded(body);
            if (form.TryGetValue(key, out var formValue)) {
                return formValue;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseFormEncoded(string input) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input)) {
            return result;
        }
        var parts = input.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            var idx = part.IndexOf('=');
            if (idx < 0) {
                continue;
            }
            var key = WebUtility.UrlDecode(part.Substring(0, idx));
            var value = WebUtility.UrlDecode(part.Substring(idx + 1));
            if (!string.IsNullOrWhiteSpace(key)) {
                result[key] = value ?? string.Empty;
            }
        }
        return result;
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html) {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static void TryOpenUrl(string url) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        } catch {
            // Best effort.
        }
    }
}

internal sealed class GitHubAppManifestOptions {
    public string AppName { get; set; } = "IntelligenceX Reviewer";
    public string AppUrl { get; set; } = "https://github.com";
    public string? Owner { get; set; }
    public string AuthBaseUrl { get; set; } = "https://github.com";
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
}

internal sealed class GitHubAppManifestResult {
    public GitHubAppManifestResult(long appId, string pem) {
        AppId = appId;
        Pem = pem;
    }

    public long AppId { get; }
    public string Pem { get; }
}
