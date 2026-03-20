using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Applies governed Windows Event Collector subscription changes (dry-run by default).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogCollectorSubscriptionSetTool : EventLogToolBase, ITool {
    private const string RestartWarning =
        "Collector subscription changes may require the Windows Event Collector (WecSvc) service to restart before all subscribers observe the new configuration.";

    private sealed record CollectorSubscriptionSetRequest(
        string? MachineName,
        string TargetMachineName,
        string SubscriptionName,
        bool? RequestedIsEnabled,
        string? RequestedSubscriptionXml,
        CollectorSubscriptionXmlDetails? RequestedSubscriptionXmlDetails,
        bool Apply);

    private sealed record CollectorSubscriptionSnapshot(
        string SubscriptionName,
        string MachineName,
        string? Description,
        bool? IsEnabled,
        string? ContentFormat,
        string? DeliveryMode,
        string? RawXml,
        bool HasXml,
        int QueryCount,
        IReadOnlyList<string> Queries);

    private sealed record CollectorSubscriptionRollbackArguments(
        string SubscriptionName,
        string? MachineName,
        bool? IsEnabled,
        string? SubscriptionXml,
        bool Apply);

    private sealed record CollectorSubscriptionXmlDetails(
        string NormalizedXml,
        string? Description,
        IReadOnlyList<string> Queries);

    private sealed record CollectorSubscriptionApplyDetails(
        bool Success,
        bool PartialSuccess,
        IReadOnlyList<string> AppliedChanges,
        IReadOnlyList<string> FailedChanges,
        IReadOnlyList<string> Errors);

    private sealed record CollectorSubscriptionSetResult(
        string SubscriptionName,
        string MachineName,
        bool Apply,
        bool Changed,
        bool CanApply,
        bool PostChangeVerified,
        bool WriteExecuted,
        bool PartialSuccess,
        string Message,
        bool? RequestedIsEnabled,
        bool RequestedSubscriptionXml,
        IReadOnlyList<string> RequestedChanges,
        IReadOnlyList<string> Warnings,
        CollectorSubscriptionSnapshot Before,
        CollectorSubscriptionSnapshot After,
        CollectorSubscriptionRollbackArguments RollbackArguments,
        CollectorSubscriptionApplyDetails? ApplyDetails);

    private sealed record CollectorSubscriptionMutationPlan(
        bool CanApply,
        bool Changed,
        CollectorSubscriptionSnapshot PredictedAfter,
        IReadOnlyList<string> RequestedChanges,
        string PreviewMessage,
        string ApplyMessage,
        IReadOnlyList<string> Warnings);

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_collector_subscription_set",
        "Governed Windows Event Collector subscription changes for enable/disable and XML updates. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for the local machine.")),
                ("subscription_name", ToolSchema.String("Exact Windows Event Collector subscription name to manage.")),
                ("is_enabled", ToolSchema.Boolean("Optional target enabled state for the collector subscription.")),
                ("subscription_xml", ToolSchema.String("Optional full subscription XML payload to replace on the collector subscription.")),
                ("apply", ToolSchema.Boolean("When true, performs the collector subscription write. Otherwise returns a dry-run preview.")))
            .Required("subscription_name")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogCollectorSubscriptionSetTool"/> class.
    /// </summary>
    public EventLogCollectorSubscriptionSetTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<CollectorSubscriptionSetRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var machineName = reader.OptionalString("machine_name");
            if (!reader.TryReadRequiredString("subscription_name", out var subscriptionName, out var subscriptionNameError)) {
                return ToolRequestBindingResult<CollectorSubscriptionSetRequest>.Failure(subscriptionNameError);
            }

            var requestedIsEnabled = reader.OptionalBoolean("is_enabled");
            var subscriptionXml = ToolArgs.NormalizeOptional(reader.OptionalString("subscription_xml"));
            CollectorSubscriptionXmlDetails? xmlDetails = null;
            if (!string.IsNullOrWhiteSpace(subscriptionXml)) {
                if (!TryParseSubscriptionXml(subscriptionXml, out xmlDetails, out var xmlError)) {
                    return ToolRequestBindingResult<CollectorSubscriptionSetRequest>.Failure(xmlError);
                }

                subscriptionXml = xmlDetails!.NormalizedXml;
            }

            if (requestedIsEnabled is null && string.IsNullOrWhiteSpace(subscriptionXml)) {
                return ToolRequestBindingResult<CollectorSubscriptionSetRequest>.Failure(
                    "Provide at least one requested change: is_enabled or subscription_xml.");
            }

            return ToolRequestBindingResult<CollectorSubscriptionSetRequest>.Success(new CollectorSubscriptionSetRequest(
                MachineName: machineName,
                TargetMachineName: string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim(),
                SubscriptionName: subscriptionName,
                RequestedIsEnabled: requestedIsEnabled,
                RequestedSubscriptionXml: subscriptionXml,
                RequestedSubscriptionXmlDetails: xmlDetails,
                Apply: reader.Boolean("apply", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(
        ToolPipelineContext<CollectorSubscriptionSetRequest> context,
        CancellationToken cancellationToken) {
        return Task.Run(() => ExecuteSync(context, cancellationToken), cancellationToken);
    }

    private string ExecuteSync(
        ToolPipelineContext<CollectorSubscriptionSetRequest> context,
        CancellationToken cancellationToken) {
        if (!OperatingSystem.IsWindows()) {
            return ToolResultV2.Error(
                errorCode: "platform_not_supported",
                error: "eventlog_collector_subscription_set is supported only on Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        var beforeAttempt = TryGetCollectorSubscriptionSnapshot(
            request.SubscriptionName,
            request.MachineName,
            request.TargetMachineName);
        if (beforeAttempt.ErrorResponse is not null) {
            return beforeAttempt.ErrorResponse;
        }

        if (beforeAttempt.Snapshot is null) {
            return ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Collector subscription '{request.SubscriptionName}' was not found or could not be read on '{request.TargetMachineName}'.",
                hints: new[] {
                    "Call eventlog_collector_subscriptions_list to confirm the exact subscription_name on the target collector host.",
                    "Use eventlog_connectivity_probe when remote reachability or permissions are uncertain."
                });
        }

        var before = beforeAttempt.Snapshot;
        var plan = BuildMutationPlan(request, before);
        if (request.Apply && !plan.CanApply) {
            return ToolResultV2.Error(
                errorCode: "precondition_failed",
                error: plan.ApplyMessage,
                hints: plan.Warnings);
        }

        var after = plan.PredictedAfter;
        var postChangeVerified = true;
        var writeExecuted = false;
        var partialSuccess = false;
        var warnings = new List<string>(plan.Warnings);
        CollectorSubscriptionApplyDetails? applyDetails = null;

        if (request.Apply && plan.Changed) {
            var appliedChanges = new List<string>(2);
            var failedChanges = new List<string>(2);
            var errors = new List<string>(2);

            if (request.RequestedIsEnabled.HasValue
                && request.RequestedIsEnabled != before.IsEnabled) {
                var enabledSuccess = SearchEvents.SetCollectorSubscriptionEnabled(
                    request.SubscriptionName,
                    request.RequestedIsEnabled.Value,
                    request.MachineName);
                if (enabledSuccess) {
                    appliedChanges.Add("is_enabled");
                } else {
                    failedChanges.Add("is_enabled");
                    errors.Add("Failed to update the collector subscription enabled state.");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.RequestedSubscriptionXml)
                && !AreEquivalentXml(before.RawXml, request.RequestedSubscriptionXml)) {
                var xmlSuccess = SearchEvents.SetCollectorSubscriptionXml(
                    request.SubscriptionName,
                    request.RequestedSubscriptionXml,
                    request.MachineName);
                if (xmlSuccess) {
                    appliedChanges.Add("subscription_xml");
                } else {
                    failedChanges.Add("subscription_xml");
                    errors.Add("Failed to update the collector subscription XML payload.");
                }
            }

            writeExecuted = appliedChanges.Count > 0;
            partialSuccess = failedChanges.Count > 0 && appliedChanges.Count > 0;
            warnings.AddRange(errors);
            applyDetails = new CollectorSubscriptionApplyDetails(
                Success: failedChanges.Count == 0 && appliedChanges.Count > 0,
                PartialSuccess: partialSuccess,
                AppliedChanges: appliedChanges.ToArray(),
                FailedChanges: failedChanges.ToArray(),
                Errors: errors.ToArray());

            if (failedChanges.Count > 0 && appliedChanges.Count == 0) {
                return ToolResultV2.Error(
                    errorCode: "action_failed",
                    error: $"The requested collector subscription write failed for '{request.SubscriptionName}' on '{request.TargetMachineName}'.",
                    hints: BuildApplyFailureHints());
            }

            var afterAttempt = TryGetCollectorSubscriptionSnapshot(
                request.SubscriptionName,
                request.MachineName,
                request.TargetMachineName);
            if (afterAttempt.ErrorResponse is not null || afterAttempt.Snapshot is null) {
                postChangeVerified = false;
                warnings.Add("Post-change verification could not re-read the collector subscription after the write.");
            } else {
                after = afterAttempt.Snapshot;
            }
        }

        var result = new CollectorSubscriptionSetResult(
            SubscriptionName: before.SubscriptionName,
            MachineName: before.MachineName,
            Apply: request.Apply,
            Changed: plan.Changed,
            CanApply: plan.CanApply,
            PostChangeVerified: postChangeVerified,
            WriteExecuted: writeExecuted,
            PartialSuccess: partialSuccess,
            Message: request.Apply ? plan.ApplyMessage : plan.PreviewMessage,
            RequestedIsEnabled: request.RequestedIsEnabled,
            RequestedSubscriptionXml: !string.IsNullOrWhiteSpace(request.RequestedSubscriptionXml),
            RequestedChanges: plan.RequestedChanges,
            Warnings: warnings,
            Before: before,
            After: after,
            RollbackArguments: BuildRollbackArguments(before, request.MachineName),
            ApplyDetails: applyDetails);

        return CreateSuccessResponse(result);
    }

    private static (CollectorSubscriptionSnapshot? Snapshot, string? ErrorResponse) TryGetCollectorSubscriptionSnapshot(
        string subscriptionName,
        string? machineName,
        string targetMachineName) {
        try {
            var subscription = SearchEvents.GetCollectorSubscriptions(machineName)
                .FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.Name)
                    && string.Equals(item.Name, subscriptionName, StringComparison.OrdinalIgnoreCase));
            return subscription is null
                ? (null, null)
                : (CreateSnapshot(subscription, targetMachineName), null);
        } catch (Exception ex) {
            return (null, ErrorFromException(
                ex,
                defaultMessage: "Collector subscription query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed"));
        }
    }

    private static CollectorSubscriptionMutationPlan BuildMutationPlan(
        CollectorSubscriptionSetRequest request,
        CollectorSubscriptionSnapshot before) {
        var requestedChanges = new List<string>(2);
        var warnings = new List<string>();
        var after = before;

        if (request.RequestedIsEnabled.HasValue) {
            requestedChanges.Add("is_enabled");
            after = after with { IsEnabled = request.RequestedIsEnabled.Value };
        }

        if (!string.IsNullOrWhiteSpace(request.RequestedSubscriptionXml)) {
            requestedChanges.Add("subscription_xml");
            after = after with {
                RawXml = request.RequestedSubscriptionXml,
                HasXml = true,
                Description = request.RequestedSubscriptionXmlDetails?.Description,
                QueryCount = request.RequestedSubscriptionXmlDetails?.Queries.Count ?? 0,
                Queries = request.RequestedSubscriptionXmlDetails?.Queries ?? Array.Empty<string>()
            };
        }

        var changed =
            (request.RequestedIsEnabled.HasValue && request.RequestedIsEnabled != before.IsEnabled)
            || (!string.IsNullOrWhiteSpace(request.RequestedSubscriptionXml)
                && !AreEquivalentXml(before.RawXml, request.RequestedSubscriptionXml));

        if (changed) {
            warnings.Add(RestartWarning);
        }

        if (!changed) {
            const string noChangeMessage = "Requested collector subscription state already matches the current state. No change required.";
            return new CollectorSubscriptionMutationPlan(
                CanApply: true,
                Changed: false,
                PredictedAfter: before,
                RequestedChanges: requestedChanges,
                PreviewMessage: noChangeMessage,
                ApplyMessage: "Collector subscription already matched the requested state. No change applied.",
                Warnings: warnings.ToArray());
        }

        return new CollectorSubscriptionMutationPlan(
            CanApply: true,
            Changed: true,
            PredictedAfter: after,
            RequestedChanges: requestedChanges,
            PreviewMessage: $"Preview only: collector subscription '{request.SubscriptionName}' would be updated.",
            ApplyMessage: $"Collector subscription '{request.SubscriptionName}' updated.",
            Warnings: warnings.ToArray());
    }

    private static CollectorSubscriptionSnapshot CreateSnapshot(SubscriptionInfo subscription, string targetMachineName) {
        var queries = subscription.Queries?
            .Where(static query => !string.IsNullOrWhiteSpace(query))
            .Select(static query => query.Trim())
            .ToArray() ?? Array.Empty<string>();

        return new CollectorSubscriptionSnapshot(
            SubscriptionName: subscription.Name,
            MachineName: string.IsNullOrWhiteSpace(subscription.MachineName) ? targetMachineName : subscription.MachineName,
            Description: ToolArgs.NormalizeOptional(subscription.Description),
            IsEnabled: subscription.Enabled,
            ContentFormat: ToolArgs.NormalizeOptional(subscription.ContentFormat),
            DeliveryMode: ToolArgs.NormalizeOptional(subscription.DeliveryMode),
            RawXml: ToolArgs.NormalizeOptional(subscription.RawXml),
            HasXml: !string.IsNullOrWhiteSpace(subscription.RawXml),
            QueryCount: queries.Length,
            Queries: queries);
    }

    private static CollectorSubscriptionRollbackArguments BuildRollbackArguments(
        CollectorSubscriptionSnapshot before,
        string? machineName) {
        return new CollectorSubscriptionRollbackArguments(
            SubscriptionName: before.SubscriptionName,
            MachineName: machineName,
            IsEnabled: before.IsEnabled,
            SubscriptionXml: before.RawXml,
            Apply: true);
    }

    private static IReadOnlyList<string> BuildApplyFailureHints() {
        return new[] {
            "Call eventlog_connectivity_probe to confirm host reachability and runtime permissions before retrying.",
            "Call eventlog_collector_subscriptions_list to verify the collector subscription exists on the target host before retrying.",
            "Verify Remote Registry access is available when writing remotely.",
            "If the XML payload changed, validate it against the expected Windows Event Collector subscription format before retrying."
        };
    }

    private static string CreateSuccessResponse(CollectorSubscriptionSetResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Mode", result.Apply ? "apply" : "dry-run"),
            ("Machine", result.MachineName),
            ("Subscription", result.SubscriptionName),
            ("Enabled", FormatNullableBoolean(result.Before.IsEnabled) + " -> " + FormatNullableBoolean(result.After.IsEnabled)),
            ("Query count", result.Before.QueryCount.ToString(CultureInfo.InvariantCulture) + " -> " + result.After.QueryCount.ToString(CultureInfo.InvariantCulture)),
            ("XML present", FormatBoolean(result.Before.HasXml) + " -> " + FormatBoolean(result.After.HasXml)),
            ("Message", result.Message)
        };
        if (result.RequestedChanges.Count > 0) {
            facts.Add(("Requested changes", string.Join(", ", result.RequestedChanges)));
        }
        if (result.PartialSuccess) {
            facts.Add(("Apply outcome", "partial_success"));
        }
        if (result.Warnings.Count > 0) {
            facts.Add(("Warnings", string.Join(" | ", result.Warnings)));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("subscription_name", result.SubscriptionName)
            .Add("machine_name", result.MachineName)
            .Add("write_candidate", true)
            .Add("changed", result.Changed)
            .Add("can_apply", result.CanApply)
            .Add("post_change_verified", result.PostChangeVerified)
            .Add("write_executed", result.WriteExecuted)
            .Add("partial_success", result.PartialSuccess)
            .Add("requested_change_count", result.RequestedChanges.Count)
            .Add("requested_subscription_xml", result.RequestedSubscriptionXml);
        if (result.RequestedIsEnabled.HasValue) {
            meta.Add("requested_is_enabled", result.RequestedIsEnabled.Value);
        }
        if (result.Warnings.Count > 0) {
            meta.Add("warning_count", result.Warnings.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: "eventlog_collector_subscription_set",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "Event Log collector subscription");
    }

    private static bool TryParseSubscriptionXml(
        string subscriptionXml,
        out CollectorSubscriptionXmlDetails? details,
        out string error) {
        details = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(subscriptionXml)) {
            error = "subscription_xml cannot be blank when supplied.";
            return false;
        }

        try {
            using var reader = XmlReader.Create(new System.IO.StringReader(subscriptionXml), CreateSubscriptionXmlReaderSettings());
            if (!TryReadSubscriptionXmlDetails(reader, out var description, out var queries, out error)) {
                return false;
            }

            details = new CollectorSubscriptionXmlDetails(
                NormalizedXml: NormalizeXml(subscriptionXml),
                Description: description,
                Queries: queries);
            return true;
        } catch (XmlException ex) {
            error = $"subscription_xml is not valid XML: {ex.Message}";
            return false;
        } catch (ArgumentException ex) {
            error = $"subscription_xml is invalid: {ex.Message}";
            return false;
        }
    }

    private static XmlReaderSettings CreateSubscriptionXmlReaderSettings() {
        return new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
    }

    private static bool TryReadSubscriptionXmlDetails(
        XmlReader reader,
        out string? description,
        out IReadOnlyList<string> queries,
        out string error) {
        description = null;
        var collectedQueries = new List<string>();
        error = string.Empty;

        reader.MoveToContent();
        if (!reader.LocalName.Equals("Subscription", StringComparison.OrdinalIgnoreCase)) {
            error = "subscription_xml root element must be <Subscription>.";
            queries = Array.Empty<string>();
            return false;
        }

        while (reader.Read()) {
            if (reader.NodeType != XmlNodeType.Element) {
                continue;
            }

            if (reader.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase)) {
                description = ToolArgs.NormalizeOptional(reader.ReadElementContentAsString());
                continue;
            }

            if (reader.LocalName.Equals("Select", StringComparison.OrdinalIgnoreCase)) {
                var query = ToolArgs.NormalizeOptional(reader.ReadElementContentAsString());
                if (!string.IsNullOrWhiteSpace(query)) {
                    collectedQueries.Add(query);
                }
            }
        }

        queries = collectedQueries.ToArray();
        return true;
    }

    private static string NormalizeXml(string subscriptionXml) {
        using var reader = XmlReader.Create(new System.IO.StringReader(subscriptionXml), CreateSubscriptionXmlReaderSettings());
        using var writerBuffer = new System.IO.StringWriter(CultureInfo.InvariantCulture);
        using var writer = XmlWriter.Create(writerBuffer, new XmlWriterSettings {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.None
        });

        while (reader.Read()) {
            writer.WriteNode(reader, true);
        }

        writer.Flush();
        return writerBuffer.ToString();
    }

    private static bool AreEquivalentXml(string? left, string? right) {
        return string.Equals(
            NormalizeXmlForComparison(left),
            NormalizeXmlForComparison(right),
            StringComparison.Ordinal);
    }

    private static string? NormalizeXmlForComparison(string? xml) {
        if (string.IsNullOrWhiteSpace(xml)) {
            return null;
        }

        return TryParseSubscriptionXml(xml, out var details, out _)
            ? details!.NormalizedXml
            : xml.Trim();
    }

    private static string FormatBoolean(bool value) {
        return value ? "true" : "false";
    }

    private static string FormatNullableBoolean(bool? value) {
        return value.HasValue ? FormatBoolean(value.Value) : "unknown";
    }
}
