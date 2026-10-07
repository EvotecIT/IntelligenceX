using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Cli.Usage;

internal static partial class UsageRunner {
    private static async Task<int> PrintAllAccountsAsync(OpenAINativeOptions options, bool json) {
        var snapshot = await new ProviderLimitSnapshotService().FetchOpenAiAccountsAsync(options).ConfigureAwait(false);
        if (json) {
            Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        } else {
            Console.WriteLine("Saved IX accounts: " + snapshot.Accounts.Count);
            foreach (var account in snapshot.Accounts) {
                Console.WriteLine();
                Console.WriteLine((account.AccountLabel ?? account.AccountId ?? "Unresolved account") + " · " + (account.PlanLabel ?? "plan unknown"));
                Console.WriteLine("Checked: " + ChatGptResetCreditsFormatter.FormatTimestamp(account.RetrievedAtUtc));
                Console.WriteLine(account.Summary);
                if (!string.IsNullOrWhiteSpace(account.DetailMessage)) Console.WriteLine(account.DetailMessage);
                foreach (var window in account.Windows) {
                    Console.WriteLine(window.Label + ": " + (window.UsedPercent?.ToString("0.#") ?? "unknown") + "% used · resets "
                        + ChatGptResetCreditsFormatter.FormatTimestamp(window.ResetsAt));
                }
                Console.WriteLine(ChatGptResetCreditsFormatter.Format(account.ResetCredits, account.ResetCreditsError));
                Console.WriteLine(ChatGptAccountAnalyticsFormatter.Format(account.AccountAnalytics));
            }
        }
        return snapshot.Accounts.Count > 0 && snapshot.Accounts.All(static account => account.IsAvailable) ? 0 : 1;
    }
}
