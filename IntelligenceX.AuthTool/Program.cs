using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.AuthTool;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        if (args.Length == 0) {
            PrintHelp();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        return command switch {
            "login" => await RunLoginAsync().ConfigureAwait(false),
            "export" => await RunExportAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "sync-codex" => await RunSyncCodexAsync().ConfigureAwait(false),
            "help" or "-h" or "--help" => PrintHelpReturn(),
            _ => PrintHelpReturn()
        };
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp() {
        Console.WriteLine("IntelligenceX.AuthTool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  login   Start OAuth login flow and store credentials");
        Console.WriteLine("  export  Export stored credentials (json, base64, store, store-base64)");
        Console.WriteLine("  sync-codex  Write tokens to CODEX_HOME/auth.json");
        Console.WriteLine();
        Console.WriteLine("Environment variables (optional overrides):");
        Console.WriteLine("  OPENAI_AUTH_AUTHORIZE_URL, OPENAI_AUTH_TOKEN_URL, OPENAI_AUTH_CLIENT_ID");
        Console.WriteLine("  OPENAI_AUTH_SCOPES, OPENAI_AUTH_REDIRECT_URL");
        Console.WriteLine("  INTELLIGENCEX_AUTH_PATH (optional)");
        Console.WriteLine("  INTELLIGENCEX_AUTH_KEY (base64 32 bytes to encrypt store)");
        Console.WriteLine("  CODEX_HOME (used by sync-codex)");
    }

    private static async Task<int> RunLoginAsync() {
        try {
            var config = OAuthConfig.FromEnvironment();
            config.Validate();
            var service = new OAuthLoginService();
            var options = new OAuthLoginOptions(config) {
                OnAuthUrl = async url => {
                    TryOpenBrowser(url);
                    Console.WriteLine($"Open: {url}");
                    await Task.CompletedTask;
                },
                OnPrompt = async message => {
                    Console.Write(message + " ");
                    var input = Console.ReadLine();
                    return await Task.FromResult(input ?? string.Empty);
                }
            };

            var result = await service.LoginAsync(options).ConfigureAwait(false);
            var store = new FileAuthBundleStore();
            await store.SaveAsync(result.Bundle).ConfigureAwait(false);
            Console.WriteLine("Login complete. Credentials saved.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunExportAsync(string[] args) {
        try {
            var format = ResolveExportFormat(args)
                         ?? Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_EXPORT_FORMAT")
                         ?? "json";
            var normalized = format.Trim().ToLowerInvariant();
            if (normalized is "store" or "store-base64" or "store_b64") {
                return await ExportAuthStoreAsync(normalized).ConfigureAwait(false);
            }

            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine("No auth bundle found.");
                return 1;
            }
            var json = AuthBundleSerializer.Serialize(bundle);
            if (normalized == "base64") {
                var bytes = Encoding.UTF8.GetBytes(json);
                Console.WriteLine(Convert.ToBase64String(bytes));
            } else {
                Console.WriteLine(json);
            }
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunSyncCodexAsync() {
        try {
            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine("No auth bundle found.");
                return 1;
            }
            CodexAuthStore.WriteAuthJson(bundle, null, DateTimeOffset.UtcNow);
            Console.WriteLine($"Wrote {CodexAuthStore.ResolveAuthPath()}");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? ResolveExportFormat(string[] args) {
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                return args[i + 1];
            }
            if (arg.Equals("--store", StringComparison.OrdinalIgnoreCase)) {
                return "store";
            }
            if (arg.Equals("--store-base64", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--store-b64", StringComparison.OrdinalIgnoreCase)) {
                return "store-base64";
            }
        }
        return null;
    }

    private static async Task<int> ExportAuthStoreAsync(string format) {
        var path = AuthPaths.ResolveAuthPath();
        if (!File.Exists(path)) {
            Console.Error.WriteLine("No auth store found.");
            return 1;
        }
        var content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            Console.Error.WriteLine("Auth store is empty.");
            return 1;
        }
        if (format is "store-base64" or "store_b64") {
            var bytes = Encoding.UTF8.GetBytes(content);
            Console.WriteLine(Convert.ToBase64String(bytes));
        } else {
            Console.WriteLine(content);
        }
        return 0;
    }

    private static void TryOpenBrowser(string url) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        } catch {
            // Ignore failures.
        }
    }
}
