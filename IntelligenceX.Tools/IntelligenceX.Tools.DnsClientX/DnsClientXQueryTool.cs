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
    private sealed record QueryRequest(
        string Name,
        string RecordTypeText,
        string EndpointText,
        int TimeoutMs,
        bool RetryOnTransient,
        int MaxRetries,
        bool RequestDnsSec,
        bool ValidateDnsSec,
        bool TypedRecords,
        bool ParseTypedTxtRecords);

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
            .NoAdditionalProperties(),
        category: "dns",
        tags: new[] {
            "resolver",
            "dns"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClientXQueryTool"/> class.
    /// </summary>
    public DnsClientXQueryTool(DnsClientXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<QueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("name", out var name, out var nameError)) {
                return ToolRequestBindingResult<QueryRequest>.Failure(
                    error: nameError,
                    hints: new[] { "Provide a DNS name such as example.com and optionally set type (A/AAAA/MX/TXT/CNAME/PTR)." });
            }

            return ToolRequestBindingResult<QueryRequest>.Success(new QueryRequest(
                Name: name,
                RecordTypeText: reader.OptionalString("type") ?? "A",
                EndpointText: reader.OptionalString("endpoint") ?? "System",
                TimeoutMs: reader.CappedInt32("timeout_ms", Options.DefaultTimeoutMs, 100, Options.MaxTimeoutMs),
                RetryOnTransient: reader.Boolean("retry_on_transient", defaultValue: true),
                MaxRetries: reader.CappedInt32("max_retries", 1, 0, Options.MaxRetries),
                RequestDnsSec: reader.Boolean("request_dnssec", defaultValue: false),
                ValidateDnsSec: reader.Boolean("validate_dnssec", defaultValue: false),
                TypedRecords: reader.Boolean("typed_records", defaultValue: false),
                ParseTypedTxtRecords: reader.Boolean("parse_typed_txt_records", defaultValue: false)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<QueryRequest> context, CancellationToken cancellationToken) {
        var request = context.Request;

#if !DNSCLIENTX_ENABLED
        return ToolResultV2.Error(
            errorCode: "dependency_unavailable",
            error: "DnsClientX dependency is not available in this build.",
            hints: new[] {
                "Provide DnsClientX as a sibling source checkout or package reference.",
                "Disable the dnsclientx pack when running in builds without the dependency."
            },
            isTransient: false);
#else
        if (!Enum.TryParse<DnsRecordType>(request.RecordTypeText, ignoreCase: true, out var recordType)) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported DNS record type '{request.RecordTypeText}'.",
                hints: new[] { "Use values from DnsClientX.DnsRecordType (for example: A, AAAA, MX, TXT, CNAME, PTR, NS, SOA)." },
                isTransient: false);
        }

        if (!Enum.TryParse<DnsEndpoint>(request.EndpointText, ignoreCase: true, out var endpoint)) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unsupported DnsClientX endpoint '{request.EndpointText}'.",
                hints: new[] { "Use a known endpoint such as System, Cloudflare, Google, Quad9, or RootServer." },
                isTransient: false);
        }

        DnsResponse response;
        try {
            response = await ClientX.QueryDns(
                request.Name,
                recordType,
                new DnsQueryOptions {
                    DnsEndpoint = endpoint,
                    DnsSelectionStrategy = DnsSelectionStrategy.First,
                    TimeOutMilliseconds = request.TimeoutMs,
                    RetryOnTransient = request.RetryOnTransient,
                    MaxRetries = request.MaxRetries,
                    RetryDelayMs = 200,
                    RequestDnsSec = request.RequestDnsSec,
                    ValidateDnsSec = request.ValidateDnsSec,
                    TypedRecords = request.TypedRecords,
                    ParseTypedTxtRecords = request.ParseTypedTxtRecords
                },
                cancellationToken);
        } catch (OperationCanceledException) {
            return ToolResultV2.Error(
                errorCode: "timeout",
                error: $"DNS query timed out or was cancelled after timeout_ms={request.TimeoutMs}.",
                hints: new[] {
                    "Increase timeout_ms for slow resolvers.",
                    "Try a different endpoint (for example System or Cloudflare)."
                },
                isTransient: true);
        } catch (Exception ex) {
            return ToolResultV2.Error(
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
        var questions = MapQuestions(response.Questions);
        var truncated = answersTruncated || authoritiesTruncated || additionalTruncated;

        if (response.ErrorCode == DnsQueryErrorCode.None
            && DnsQueryDiagnostics.IsSuspiciousEmptySuccess(response)) {
            return ToolResultV2.Error(
                errorCode: "query_failed",
                error: "Resolver returned an empty response envelope without question/answer sections.",
                hints: new[] {
                    $"Resolver status={response.Status}; endpoint={endpoint}.",
                    "Retry with a different endpoint (for example System or Cloudflare).",
                    "Retry with a larger timeout_ms when resolver path latency is high."
                },
                isTransient: true);
        }

        if (response.ErrorCode != DnsQueryErrorCode.None && answers.Count == 0) {
            var transient = response.ErrorCode is DnsQueryErrorCode.Timeout or DnsQueryErrorCode.Network;
            var message = string.IsNullOrWhiteSpace(response.Error)
                ? $"DnsClientX query failed with error '{response.ErrorCode}'."
                : response.Error;

            return ToolResultV2.Error(
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
                Name = request.Name,
                RecordType = recordType.ToString(),
                Endpoint = endpoint.ToString(),
                TimeoutMs = request.TimeoutMs,
                RetryOnTransient = request.RetryOnTransient,
                MaxRetries = request.MaxRetries,
                RequestDnsSec = request.RequestDnsSec,
                ValidateDnsSec = request.ValidateDnsSec,
                TypedRecords = request.TypedRecords,
                ParseTypedTxtRecords = request.ParseTypedTxtRecords
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
            Questions = questions,
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

        return ToolOutputEnvelope.OkFlatWithRenderValue(
            root: ToolJson.ToJsonObjectSnakeCase(result),
            meta: meta,
            summaryMarkdown: summary,
            render: BuildRenderHints(
                answerCount: result.Answers.Count,
                authorityCount: result.Authorities.Count,
                additionalCount: result.Additional.Count,
                questionCount: result.Questions.Count));
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

    private static JsonValue? BuildRenderHints(
        int answerCount,
        int authorityCount,
        int additionalCount,
        int questionCount) {
        var hints = new JsonArray();

        if (answerCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "answers",
                    new ToolColumn("name", "Name", "string"),
                    new ToolColumn("type", "Type", "string"),
                    new ToolColumn("ttl", "TTL", "int"),
                    new ToolColumn("data", "Data", "string"))
                .Add("priority", 400));
        }

        if (authorityCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "authorities",
                    new ToolColumn("name", "Name", "string"),
                    new ToolColumn("type", "Type", "string"),
                    new ToolColumn("ttl", "TTL", "int"),
                    new ToolColumn("data", "Data", "string"))
                .Add("priority", 300));
        }

        if (additionalCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "additional",
                    new ToolColumn("name", "Name", "string"),
                    new ToolColumn("type", "Type", "string"),
                    new ToolColumn("ttl", "TTL", "int"),
                    new ToolColumn("data", "Data", "string"))
                .Add("priority", 200));
        }

        if (questionCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "questions",
                    new ToolColumn("name", "Name", "string"),
                    new ToolColumn("type", "Type", "string"))
                .Add("priority", 100));
        }

        if (hints.Count == 0) {
            return null;
        }

        return JsonValue.From(hints);
    }

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
