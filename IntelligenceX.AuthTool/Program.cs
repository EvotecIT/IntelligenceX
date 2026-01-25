using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IntelligenceX.Auth;

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
            "export" => await RunExportAsync().ConfigureAwait(false),
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
        Console.WriteLine("  export  Export stored credentials (json or base64)");
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

    private static async Task<int> RunExportAsync() {
        try {
            var format = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_EXPORT_FORMAT") ?? "json";
            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine("No auth bundle found.");
                return 1;
            }
            var json = AuthBundleSerializer.Serialize(bundle);
            if (format.Equals("base64", StringComparison.OrdinalIgnoreCase)) {
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
