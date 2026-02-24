using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

#if DNSCLIENTX_ENABLED
using DnsClientX;
#endif

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// Queries DNS records using DnsClientX with bounded timeout/retry controls.
/// </summary>
public sealed class DnsClientXQueryTool : DnsClientXToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "dnsclientx_query",
        "Query DNS records for a name using DnsClientX (read-only).",
        ToolSchema.Object(
                ("name", ToolSchema.String("DNS name or IP to query (for PTR use reverse notation).")),
                ("type", ToolSchema.String("DNS record type (for example: A, AAAA, MX, TXT, CNAME, PTR). Default: A.")),
                ("endpoint", ToolSchema.String("DnsClientX endpoint name (for example: System, Cloudflare, Google, Quad9). Default: System.")),
                ("timeout_ms", ToolSchema.Integer("Query timeout in milliseconds (capped by pack options).")),
                ("retry_on_transient", ToolSchema.Boolean("Retry on transient transport failures (default: true).")),
                ("max_retries", ToolSchema.Integer("Maximum retries for transient errors (capped by pack options).")),
                ("request_dnssec", ToolSchema.Boolean("Request DNSSEC records when supported (default: false).")),
                ("validate_dnssec", ToolSchema.Boolean("Validate DNSSEC chain when supported (default: false).")),
                ("typed_records", ToolSchema.Boolean("Request typed answer parsing from DnsClientX (default: false).")),
                ("parse_typed_txt_records", ToolSchema.Boolean("Enable TXT-specific typed parsing in DnsClientX (default: false).")))
            .Required("name")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClientXQueryTool"/> class.
    /// </summary>
    public DnsClientXQueryTool(DnsClientXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var name = ToolArgs.GetOptionalTrimmed(arguments, "name");
        if (string.IsNullOrWhiteSpace(name)) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "name is required.",
                hints: new[] { "Provide a DNS name such as example.com and optionally set type (A/AAAA/MX/TXT/CNAME/PTR)." },
                isTransient: false);
        }

        var recordTypeText = ToolArgs.GetTrimmedOrDefault(arguments, "type", "A");
        var endpointText = ToolArgs.GetTrimmedOrDefault(arguments, "endpoint", "System");
        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", Options.DefaultTimeoutMs, 100, Options.MaxTimeoutMs);
        var retryOnTransient = ToolArgs.GetBoolean(arguments, "retry_on_transient", defaultValue: true);
        var maxRetries = ToolArgs.GetCappedInt32(arguments, "max_retries", 1, 0, Options.MaxRetries);
        var requestDnsSec = ToolArgs.GetBoolean(arguments, "request_dnssec", defaultValue: false);
        var validateDnsSec = ToolArgs.GetBoolean(arguments, "validate_dnssec", defaultValue: false);
        var typedRecords = ToolArgs.GetBoolean(arguments, "typed_records", defaultValue: false);
        var parseTypedTxtRecords = ToolArgs.GetBoolean(arguments, "parse_typed_txt_records", defaultValue: false);

#if !DNSCLIENTX_ENABLED
        return ToolResponse.Error(
            errorCode: "dependency_unavailable",
            error: "DnsClientX dependency is not available in this build.",
            hints: new[] {
                "Provide DnsClientX as a sibling source checkout or package reference.",
                "Disable the dnsclientx pack when running in builds without the dependency."
            },
            isTransient: false);
#else
        if (!Enum.TryParse<DnsRecordType>(recordTypeText, ignoreCase: true, out var recordType)) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported DNS record type '{recordTypeText}'.",
                hints: new[] { "Use values from DnsClientX.DnsRecordType (for example: A, AAAA, MX, TXT, CNAME, PTR, NS, SOA)." },
                isTransient: false);
        }

        if (!Enum.TryParse<DnsEndpoint>(endpointText, ignoreCase: true, out var endpoint)) {
            return ToolResponse.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported DnsClientX endpoint '{endpointText}'.",
                hints: new[] { "Use a known endpoint such as System, Cloudflare, Google, Quad9, or RootServer." },
                isTransient: false);
        }

        DnsResponse response;
        try {
            response = await ClientX.QueryDns(
                name: name,
                recordType: recordType,
                dnsEndpoint: endpoint,
                dnsSelectionStrategy: DnsSelectionStrategy.First,
                timeOutMilliseconds: timeoutMs,
                retryOnTransient: retryOnTransient,
                maxRetries: maxRetries,
                retryDelayMs: 200,
                requestDnsSec: requestDnsSec,
                validateDnsSec: validateDnsSec,
                typedRecords: typedRecords,
                parseTypedTxtRecords: parseTypedTxtRecords,
                cancellationToken: cancellationToken);
        } catch (OperationCanceledException) {
            return ToolResponse.Error(
                errorCode: "timeout",
                error: $"DNS query timed out or was cancelled after timeout_ms={timeoutMs}.",
                hints: new[] {
                    "Increase timeout_ms for slow resolvers.",
                    "Try a different endpoint (for example System or Cloudflare)."
                },
                isTransient: true);
        } catch (Exception ex) {
            return ToolResponse.Error(
                errorCode: "query_failed",
                error: $"DnsClientX query failed: {ex.Message}",
                hints: new[] {
                    "Verify network connectivity to the selected resolver endpoint.",
                    "Retry with retry_on_transient=true and a larger timeout_ms."
                },
                isTransient: true);
        }

        var answers = MapAnswers(response.Answers, Options.MaxAnswersPerSection, out var answersTruncated);
        var authorities = MapAnswers(response.Authorities, Options.MaxAnswersPerSection, out var authoritiesTruncated);
        var additional = MapAnswers(response.Additional, Options.MaxAnswersPerSection, out var additionalTruncated);
        var truncated = answersTruncated || authoritiesTruncated || additionalTruncated;

        if (response.ErrorCode != DnsQueryErrorCode.None && answers.Count == 0) {
            var transient = response.ErrorCode is DnsQueryErrorCode.Timeout or DnsQueryErrorCode.Network;
            var message = string.IsNullOrWhiteSpace(response.Error)
                ? $"DnsClientX query failed with error '{response.ErrorCode}'."
                : response.Error;

            return ToolResponse.Error(
                errorCode: "query_failed",
                error: message,
                hints: new[] {
                    $"Resolver status={response.Status}; endpoint={endpoint}.",
                    "Retry with a larger timeout_ms or different endpoint."
                },
                isTransient: transient);
        }

        var warnings = new List<string>();
        if (answersTruncated || authoritiesTruncated || additionalTruncated) {
            warnings.Add($"Result sections were capped to max {Options.MaxAnswersPerSection} records each.");
        }

        var result = new DnsClientXQueryResultModel {
            Query = new DnsClientXQueryContextModel {
                Name = name,
                RecordType = recordType.ToString(),
                Endpoint = endpoint.ToString(),
                TimeoutMs = timeoutMs,
                RetryOnTransient = retryOnTransient,
                MaxRetries = maxRetries,
                RequestDnsSec = requestDnsSec,
                ValidateDnsSec = validateDnsSec,
                TypedRecords = typedRecords,
                ParseTypedTxtRecords = parseTypedTxtRecords
            },
            Status = response.Status.ToString(),
            ErrorCode = response.ErrorCode == DnsQueryErrorCode.None ? null : response.ErrorCode.ToString(),
            Error = string.IsNullOrWhiteSpace(response.Error) ? null : response.Error,
            Comment = string.IsNullOrWhiteSpace(response.Comments) ? null : response.Comments,
            RetryCount = response.RetryCount,
            IsTruncated = response.IsTruncated,
            IsRecursionAvailable = response.IsRecursionAvailable,
            AuthenticData = response.AuthenticData,
            ServerAddress = response.ServerAddress,
            UsedTransport = response.UsedTransport.ToString(),
            RoundTripMilliseconds = response.RoundTripTime.TotalMilliseconds,
            TtlMin = response.TtlMin,
            TtlAvg = response.TtlAvg,
            Questions = MapQuestions(response.Questions),
            Answers = answers,
            Authorities = authorities,
            Additional = additional,
            Truncated = truncated,
            Warnings = warnings
        };

        var summary = ToolMarkdown.SummaryFacts(
            title: "DnsClientX query",
            facts: new[] {
                ("Name", result.Query.Name),
                ("Type", result.Query.RecordType),
                ("Endpoint", result.Query.Endpoint),
                ("Status", result.Status),
                ("Answers", result.Answers.Count.ToString(CultureInfo.InvariantCulture)),
                ("Round trip (ms)", result.RoundTripMilliseconds.ToString("0.##", CultureInfo.InvariantCulture))
            });

        var meta = ToolOutputHints.Meta(count: result.Answers.Count, truncated: result.Truncated)
            .Add("status", result.Status)
            .Add("record_type", result.Query.RecordType)
            .Add("endpoint", result.Query.Endpoint)
            .Add("authority_count", result.Authorities.Count)
            .Add("additional_count", result.Additional.Count);

        if (!string.IsNullOrWhiteSpace(result.ErrorCode)) {
            meta.Add("error_code", result.ErrorCode);
        }

        return ToolResponse.OkModel(result, meta: meta, summaryMarkdown: summary);
#endif
    }

#if DNSCLIENTX_ENABLED
    private static IReadOnlyList<DnsClientXQuestionModel> MapQuestions(IReadOnlyList<DnsQuestion>? questions) {
        if (questions is null || questions.Count == 0) {
            return Array.Empty<DnsClientXQuestionModel>();
        }

        return questions
            .Select(static question => new DnsClientXQuestionModel {
                Name = question.Name,
                Type = question.Type.ToString()
            })
            .ToArray();
    }

    private static IReadOnlyList<DnsClientXAnswerModel> MapAnswers(
        IReadOnlyList<DnsAnswer>? answers,
        int maxItems,
        out bool truncated) {
        if (answers is null || answers.Count == 0) {
            truncated = false;
            return Array.Empty<DnsClientXAnswerModel>();
        }

        var take = Math.Min(answers.Count, maxItems);
        truncated = answers.Count > take;

        var rows = new List<DnsClientXAnswerModel>(take);
        for (var i = 0; i < take; i++) {
            var answer = answers[i];
            rows.Add(new DnsClientXAnswerModel {
                Name = answer.Name,
                Type = answer.Type.ToString(),
                Ttl = answer.TTL,
                Data = answer.Data,
                DataRaw = answer.DataRaw
            });
        }

        return rows;
    }
#endif

    private sealed class DnsClientXQueryResultModel {
        public DnsClientXQueryContextModel Query { get; init; } = new();
        public string Status { get; init; } = string.Empty;
        public string? ErrorCode { get; init; }
        public string? Error { get; init; }
        public string? Comment { get; init; }
        public int RetryCount { get; init; }
        public bool IsTruncated { get; init; }
        public bool IsRecursionAvailable { get; init; }
        public bool AuthenticData { get; init; }
        public string? ServerAddress { get; init; }
        public string UsedTransport { get; init; } = string.Empty;
        public double RoundTripMilliseconds { get; init; }
        public int? TtlMin { get; init; }
        public double? TtlAvg { get; init; }
        public IReadOnlyList<DnsClientXQuestionModel> Questions { get; init; } = Array.Empty<DnsClientXQuestionModel>();
        public IReadOnlyList<DnsClientXAnswerModel> Answers { get; init; } = Array.Empty<DnsClientXAnswerModel>();
        public IReadOnlyList<DnsClientXAnswerModel> Authorities { get; init; } = Array.Empty<DnsClientXAnswerModel>();
        public IReadOnlyList<DnsClientXAnswerModel> Additional { get; init; } = Array.Empty<DnsClientXAnswerModel>();
        public bool Truncated { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    private sealed class DnsClientXQueryContextModel {
        public string Name { get; init; } = string.Empty;
        public string RecordType { get; init; } = string.Empty;
        public string Endpoint { get; init; } = string.Empty;
        public int TimeoutMs { get; init; }
        public bool RetryOnTransient { get; init; }
        public int MaxRetries { get; init; }
        public bool RequestDnsSec { get; init; }
        public bool ValidateDnsSec { get; init; }
        public bool TypedRecords { get; init; }
        public bool ParseTypedTxtRecords { get; init; }
    }

    private sealed class DnsClientXQuestionModel {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
    }

    private sealed class DnsClientXAnswerModel {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int Ttl { get; init; }
        public string Data { get; init; } = string.Empty;
        public string DataRaw { get; init; } = string.Empty;
    }
}

