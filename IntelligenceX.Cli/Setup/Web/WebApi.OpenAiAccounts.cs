using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Auth;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task HandleOpenAIAccountsAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }

        OpenAIAccountsRequest request;
        try {
            request = JsonSerializer.Deserialize<OpenAIAccountsRequest>(body, _jsonOptions) ?? new OpenAIAccountsRequest();
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
                await File.WriteAllTextAsync(tempPath, content).ConfigureAwait(false);
                TryHardenTempFile(tempPath);
                tempFile = new TempFile(tempPath);
                authPath = tempPath;
            }

            if (string.IsNullOrWhiteSpace(authPath) || !File.Exists(authPath)) {
                await WriteJsonOkAsync(context, new OpenAIAccountsResponse {
                    Accounts = new List<OpenAIAccountItem>(),
                    Source = "missing",
                    Error = "Auth bundle path not found."
                }).ConfigureAwait(false);
                return;
            }

            var rawStore = await File.ReadAllTextAsync(authPath).ConfigureAwait(false);
            var decrypted = AuthStoreUtils.DecryptAuthStoreIfNeeded(rawStore);
            var entries = AuthStoreUtils.ParseAuthStoreEntries(decrypted);

            var byAccount = new Dictionary<string, OpenAIAccountItem>(StringComparer.OrdinalIgnoreCase);
            var selectedAccountId = string.Empty;
            var selectedWeight = -1;
            var selectedExpiry = DateTimeOffset.MinValue;
            foreach (var entry in entries) {
                if (!IsOpenAiAuthProvider(entry.Provider) || string.IsNullOrWhiteSpace(entry.AccountId)) {
                    continue;
                }
                var accountId = entry.AccountId!.Trim();
                if (accountId.Length == 0) {
                    continue;
                }

                var candidate = new OpenAIAccountItem {
                    AccountId = accountId,
                    Provider = entry.Provider,
                    ExpiresAt = entry.ExpiresAt?.ToUniversalTime().ToString("u")
                };
                if (!byAccount.TryGetValue(accountId, out var existing)) {
                    byAccount[accountId] = candidate;
                } else {
                    var existingExpiry = ParseExpiresAt(existing.ExpiresAt);
                    var candidateExpiry = entry.ExpiresAt ?? DateTimeOffset.MinValue;
                    if (candidateExpiry > existingExpiry) {
                        byAccount[accountId] = candidate;
                    }
                }

                var providerWeight = string.Equals(entry.Provider, "openai-codex", StringComparison.OrdinalIgnoreCase) ? 2
                    : string.Equals(entry.Provider, "openai", StringComparison.OrdinalIgnoreCase) ? 1
                    : 0;
                var expiry = entry.ExpiresAt ?? DateTimeOffset.MinValue;
                if (providerWeight > selectedWeight ||
                    (providerWeight == selectedWeight && expiry > selectedExpiry)) {
                    selectedWeight = providerWeight;
                    selectedExpiry = expiry;
                    selectedAccountId = accountId;
                }
            }

            var accounts = byAccount.Values
                .OrderBy(item => item.AccountId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            await WriteJsonOkAsync(context, new OpenAIAccountsResponse {
                Accounts = accounts,
                SelectedAccountId = string.IsNullOrWhiteSpace(selectedAccountId) ? null : selectedAccountId,
                Source = authPath
            }).ConfigureAwait(false);
        } catch (FormatException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid base64 auth bundle." }).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteJsonOkAsync(context, new OpenAIAccountsResponse {
                Accounts = new List<OpenAIAccountItem>(),
                Source = "error",
                Error = ex.Message
            }).ConfigureAwait(false);
        } finally {
            tempFile?.Dispose();
        }
    }

    private static bool IsOpenAiAuthProvider(string provider) {
        if (string.IsNullOrWhiteSpace(provider)) {
            return false;
        }
        return string.Equals(provider, "openai-codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "chatgpt", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset ParseExpiresAt(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return DateTimeOffset.MinValue;
        }
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }
}
