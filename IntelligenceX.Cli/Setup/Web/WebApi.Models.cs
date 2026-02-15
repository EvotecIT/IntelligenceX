using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using IntelligenceX.Cli.Setup;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private sealed class RepoListRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class RepoStatusRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
        public List<string>? Repos { get; set; }
    }

    private sealed class RepoConfigRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? Repo { get; set; }
    }

    private sealed class RepoWorkflowRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? Repo { get; set; }
    }

    private sealed class DeviceCodeRequest {
        public string? ClientId { get; set; }
        public string? AuthBaseUrl { get; set; } = "https://github.com";
        public string? Scopes { get; set; } = IntelligenceXDefaults.GitHubScopes;

        public string GetEffectiveClientId() {
            if (!string.IsNullOrWhiteSpace(ClientId)) {
                return ClientId!;
            }
            return IntelligenceXDefaults.GetEffectiveGitHubClientId();
        }
    }

    private sealed class DevicePollRequest {
        public string? ClientId { get; set; }
        public string? DeviceCode { get; set; }
        public string? AuthBaseUrl { get; set; } = "https://github.com";
        public int IntervalSeconds { get; set; } = 5;
        public int ExpiresIn { get; set; }

        public string GetEffectiveClientId() {
            if (!string.IsNullOrWhiteSpace(ClientId)) {
                return ClientId!;
            }
            return IntelligenceXDefaults.GetEffectiveGitHubClientId();
        }
    }

    private sealed class AppManifestRequest {
        public string? AppName { get; set; }
        public string? Owner { get; set; }
        public string? AuthBaseUrl { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class AppInstallationRequest {
        public long AppId { get; set; }
        public string? Pem { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class AppTokenRequest {
        public long AppId { get; set; }
        public long InstallationId { get; set; }
        public string? Pem { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class SetupRequest {
        public string? Repo { get; set; }
        public List<string>? Repos { get; set; }
        public string? GitHubToken { get; set; }
        public string? GitHubClientId { get; set; }
        public bool WithConfig { get; set; }
        public bool TriageBootstrap { get; set; }
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public string? Provider { get; set; }
        public string? OpenAIAccountId { get; set; }
        public string? OpenAIAccountIds { get; set; }
        public string? OpenAIAccountRotation { get; set; }
        public bool? OpenAIAccountFailover { get; set; }
        public string? ConfigJson { get; set; }
        public string? ConfigPath { get; set; }
        public string? ReviewProfile { get; set; }
        public string? ReviewMode { get; set; }
        public string? ReviewCommentMode { get; set; }
        public bool? AnalysisEnabled { get; set; }
        public bool? AnalysisGateEnabled { get; set; }
        public bool? AnalysisRunStrict { get; set; }
        public string? AnalysisPacks { get; set; }
        public string? AnalysisExportPath { get; set; }
        public string? SecretTarget { get; set; }
        public string? SecretOrg { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool ExplicitSecrets { get; set; }
        public bool Upgrade { get; set; }
        public bool Force { get; set; }
        public bool Cleanup { get; set; }
        public bool KeepSecret { get; set; }
        public bool UpdateSecret { get; set; }
        public bool DryRun { get; set; }
        public string? BranchName { get; set; }
    }

    private sealed class SetupAutodetectRequest {
        public string? Workspace { get; set; }
        public string? RepoHint { get; set; }
    }

    private sealed class UsageRequest {
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public string? AccountId { get; set; }
        public bool IncludeEvents { get; set; }
        public string? AuthKey { get; set; }
        public string? ChatGptApiBaseUrl { get; set; }
    }

    private sealed class OpenAILoginRequest {
        public string? ClientId { get; set; }
        public int RedirectPort { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    private sealed class OpenAIAccountsRequest {
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public string? AuthKey { get; set; }
    }

    private sealed class SetupResponse {
        public string Repo { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string? PullRequestUrl { get; set; }
        public SetupPostApplyVerification? Verify { get; set; }
    }

    private sealed class RepoStatusResponse {
        public string Repo { get; set; } = string.Empty;
        public string? DefaultBranch { get; set; }
        public bool WorkflowExists { get; set; }
        public bool WorkflowManaged { get; set; }
        public bool ConfigExists { get; set; }
        public string? Error { get; set; }
    }

    private sealed class TempFile : IDisposable {
        private readonly string _path;

        public TempFile(string path) {
            _path = path;
        }

        public void Dispose() {
            if (string.IsNullOrWhiteSpace(_path)) {
                return;
            }
            try {
                if (File.Exists(_path)) {
                    File.Delete(_path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Temp file cleanup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static void TryHardenTempFile(string path) {
        try {
            if (!File.Exists(path)) {
                return;
            }
            try {
                var attrs = File.GetAttributes(path);
                File.SetAttributes(path, attrs | FileAttributes.Temporary | FileAttributes.Hidden);
            } catch (Exception ex) {
                Trace.TraceWarning($"Temp file harden attributes failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (OperatingSystem.IsWindows()) {
                try {
                    var sid = WindowsIdentity.GetCurrent().User;
                    if (sid is not null) {
                        var security = new FileSecurity();
                        security.SetOwner(sid);
                        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
                        new FileInfo(path).SetAccessControl(security);
                    }
                } catch (Exception ex) {
                    Trace.TraceWarning($"Temp file harden ACL failed: {ex.GetType().Name}: {ex.Message}");
                }
            } else {
                try {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                } catch (Exception ex) {
                    Trace.TraceWarning($"Temp file harden permissions failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        } catch (Exception ex) {
            Trace.TraceWarning($"Temp file harden failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class UsageResponse {
        public UsageSnapshot? Usage { get; set; }
        public List<UsageEvent>? Events { get; set; }
        public string? UpdatedAt { get; set; }
    }

    private sealed class OpenAIAccountsResponse {
        public List<OpenAIAccountItem> Accounts { get; set; } = new();
        public string? SelectedAccountId { get; set; }
        public string? Source { get; set; }
        public string? Error { get; set; }
    }

    private sealed class OpenAIAccountItem {
        public string AccountId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string? ExpiresAt { get; set; }
    }

    private sealed class UsageSnapshot {
        public string? PlanType { get; set; }
        public string? Email { get; set; }
        public string? AccountId { get; set; }
        public UsageRateLimit? RateLimit { get; set; }
        public UsageRateLimit? CodeReviewRateLimit { get; set; }
        public UsageCredits? Credits { get; set; }

        public static UsageSnapshot From(ChatGptUsageSnapshot snapshot) {
            return new UsageSnapshot {
                PlanType = snapshot.PlanType,
                Email = snapshot.Email,
                AccountId = snapshot.AccountId,
                RateLimit = UsageRateLimit.From(snapshot.RateLimit),
                CodeReviewRateLimit = UsageRateLimit.From(snapshot.CodeReviewRateLimit),
                Credits = UsageCredits.From(snapshot.Credits)
            };
        }
    }

    private sealed class UsageRateLimit {
        public bool Allowed { get; set; }
        public bool LimitReached { get; set; }
        public UsageRateLimitWindow? Primary { get; set; }
        public UsageRateLimitWindow? Secondary { get; set; }

        public static UsageRateLimit? From(ChatGptRateLimitStatus? status) {
            if (status is null) {
                return null;
            }
            return new UsageRateLimit {
                Allowed = status.Allowed,
                LimitReached = status.LimitReached,
                Primary = UsageRateLimitWindow.From(status.PrimaryWindow),
                Secondary = UsageRateLimitWindow.From(status.SecondaryWindow)
            };
        }
    }

    private sealed class UsageRateLimitWindow {
        public double? UsedPercent { get; set; }
        public long? LimitWindowSeconds { get; set; }
        public long? ResetAfterSeconds { get; set; }
        public long? ResetAt { get; set; }

        public static UsageRateLimitWindow? From(ChatGptRateLimitWindow? window) {
            if (window is null) {
                return null;
            }
            return new UsageRateLimitWindow {
                UsedPercent = window.UsedPercent,
                LimitWindowSeconds = window.LimitWindowSeconds,
                ResetAfterSeconds = window.ResetAfterSeconds,
                ResetAt = window.ResetAtUnixSeconds
            };
        }
    }

    private sealed class UsageCredits {
        public bool HasCredits { get; set; }
        public bool Unlimited { get; set; }
        public double? Balance { get; set; }
        public int[]? ApproxLocalMessages { get; set; }
        public int[]? ApproxCloudMessages { get; set; }

        public static UsageCredits? From(ChatGptCreditsSnapshot? credits) {
            if (credits is null) {
                return null;
            }
            return new UsageCredits {
                HasCredits = credits.HasCredits,
                Unlimited = credits.Unlimited,
                Balance = credits.Balance,
                ApproxLocalMessages = credits.ApproxLocalMessages,
                ApproxCloudMessages = credits.ApproxCloudMessages
            };
        }
    }

    private sealed class UsageEvent {
        public string? Date { get; set; }
        public string? ProductSurface { get; set; }
        public double? CreditAmount { get; set; }
        public string? UsageId { get; set; }

        public static List<UsageEvent> From(IReadOnlyList<ChatGptCreditUsageEvent> events) {
            var result = new List<UsageEvent>();
            foreach (var evt in events) {
                result.Add(new UsageEvent {
                    Date = evt.Date,
                    ProductSurface = evt.ProductSurface,
                    CreditAmount = evt.CreditAmount,
                    UsageId = evt.UsageId
                });
            }
            return result;
        }
    }

}
