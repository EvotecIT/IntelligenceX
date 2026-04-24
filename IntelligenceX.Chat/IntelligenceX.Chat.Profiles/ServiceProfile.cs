using System.Collections.Generic;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;

namespace IntelligenceX.Chat.Profiles;

/// <summary>
/// Serializable session profile (config preset) for the chat service.
/// </summary>
internal sealed class ServiceProfile {
    public string Model { get; set; } = OpenAIModelCatalog.DefaultModel;

    // Provider transport selection and transport-specific settings.
    public OpenAITransportKind OpenAITransport { get; set; } = OpenAITransportKind.Native;
    public string? OpenAIBaseUrl { get; set; }
    public OpenAICompatibleHttpAuthMode OpenAIAuthMode { get; set; } = OpenAICompatibleHttpAuthMode.Bearer;
    public string? OpenAIApiKey { get; set; }
    public string? OpenAIBasicUsername { get; set; }
    public string? OpenAIBasicPassword { get; set; }
    public string? OpenAIAccountId { get; set; }
    public bool OpenAIStreaming { get; set; } = true;
    public bool OpenAIAllowInsecureHttp { get; set; }
    public bool OpenAIAllowInsecureHttpNonLoopback { get; set; }

    // Chat shaping hints.
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public TextVerbosity? TextVerbosity { get; set; }
    public double? Temperature { get; set; }

    public int MaxToolRounds { get; set; } = 24;
    public bool ParallelTools { get; set; } = true;
    public bool AllowMutatingParallelToolCalls { get; set; }
    public int TurnTimeoutSeconds { get; set; }
    public int ToolTimeoutSeconds { get; set; }
    public int SessionExecutionQueueLimit { get; set; } = 32;
    public int GlobalExecutionLaneConcurrency { get; set; }
    public bool EnableBackgroundSchedulerDaemon { get; set; }
    public int BackgroundSchedulerPollSeconds { get; set; } = 30;
    public int BackgroundSchedulerBurstLimit { get; set; } = 4;
    public int BackgroundSchedulerFailureThreshold { get; set; } = 5;
    public int BackgroundSchedulerFailurePauseSeconds { get; set; } = 300;
    public bool BackgroundSchedulerStartPaused { get; set; }
    public int BackgroundSchedulerStartupPauseSeconds { get; set; }
    public List<string> BackgroundSchedulerMaintenanceWindows { get; set; } = new();
    public List<string> BackgroundSchedulerAllowedPackIds { get; set; } = new();
    public List<string> BackgroundSchedulerBlockedPackIds { get; set; } = new();
    public List<string> BackgroundSchedulerAllowedThreadIds { get; set; } = new();
    public List<string> BackgroundSchedulerBlockedThreadIds { get; set; } = new();
    public List<string> AllowedRoots { get; set; } = new();

    public string? AdDomainController { get; set; }
    public string? AdDefaultSearchBaseDn { get; set; }
    public int AdMaxResults { get; set; } = 1000;
    public bool PowerShellAllowWrite { get; set; }
    public bool EnableBuiltInPackLoading { get; set; } = true;
    public bool UseDefaultBuiltInToolAssemblyNames { get; set; } = true;
    public List<string> BuiltInToolAssemblyNames { get; set; } = new();
    public bool EnableDefaultPluginPaths { get; set; } = true;
    public List<string> PluginPaths { get; set; } = new();
    public List<string> DisabledPackIds { get; set; } = new();
    public List<string> EnabledPackIds { get; set; } = new();
    public string WriteGovernanceMode { get; set; } = "enforced";
    public bool RequireWriteGovernanceRuntime { get; set; } = true;
    public bool RequireWriteAuditSinkForWriteOperations { get; set; }
    public string WriteAuditSinkMode { get; set; } = "none";
    public string? WriteAuditSinkPath { get; set; }
    public string AuthenticationRuntimePreset { get; set; } = "default";
    public bool RequireExplicitRoutingMetadata { get; set; } = true;
    public bool RequireAuthenticationRuntime { get; set; }
    public string? RunAsProfilePath { get; set; }
    public string? AuthenticationProfilePath { get; set; }

    public string? InstructionsFile { get; set; }
    public int MaxTableRows { get; set; }
    public int MaxSample { get; set; }
    public bool Redact { get; set; }
}
