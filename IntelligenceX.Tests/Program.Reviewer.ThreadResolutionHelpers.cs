namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestResolveThreadPayloadParserRejectsInvalidJson() {
        var emptyPayloadResult = TryGetResolveThreadIdFromGraphQlPayload(string.Empty, out var emptyPayloadThreadId);
        AssertEqual(false, emptyPayloadResult, "resolve payload empty rejected");
        AssertEqual<string?>(null, emptyPayloadThreadId, "resolve payload empty id");

        var malformedPayloadResult = TryGetResolveThreadIdFromGraphQlPayload("{not-json}", out var malformedPayloadThreadId);
        AssertEqual(false, malformedPayloadResult, "resolve payload malformed rejected");
        AssertEqual<string?>(null, malformedPayloadThreadId, "resolve payload malformed id");

        const string noThreadIdPayload = "{\"query\":\"mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ id } } }\",\"variables\":{}}";
        var noThreadIdResult = TryGetResolveThreadIdFromGraphQlPayload(noThreadIdPayload, out var noThreadId);
        AssertEqual(false, noThreadIdResult, "resolve payload missing id rejected");
        AssertEqual<string?>(null, noThreadId, "resolve payload missing id value");
    }

    private static void TestThreadResolveIntegrationForbiddenDetection() {
        var ex = new InvalidOperationException("GitHub GraphQL request returned errors: {\"errors\":[{\"type\":\"INSUFFICIENT_SCOPES\"}]}");
        var forbidden = CallIsIntegrationForbidden(ex);
        AssertEqual(true, forbidden, "integration forbidden detects insufficient scopes");
    }

    private static void TestThreadResolveErrorFormattingIncludesFallback() {
        var message = CallBuildThreadResolveError(new InvalidOperationException("primary failed"),
            new InvalidOperationException("fallback failed"));
        AssertContainsText(message, "primary: primary failed", "thread resolve error includes primary");
        AssertContainsText(message, "fallback: fallback failed", "thread resolve error includes fallback");
    }

    private static void TestAutoResolvePermissionNoteMentionsWorkflowPermissions() {
        var emptyMessage = CallBuildAutoResolvePermissionNote(0);
        AssertEqual(string.Empty, emptyMessage, "auto-resolve permission note omitted when no failures");

        var message = CallBuildAutoResolvePermissionNote(1, "GITHUB_TOKEN", "INTELLIGENCEX_GITHUB_TOKEN");
        AssertContainsText(message, "Workflow permissions", "auto-resolve permission note mentions workflow permissions");
        AssertContainsText(message, "Read and write permissions", "auto-resolve permission note mentions read/write");
        AssertContainsText(message, "Pull requests: Read & write", "auto-resolve permission note mentions app pull-request scope");
        AssertContainsText(message, "`GITHUB_TOKEN` and `INTELLIGENCEX_GITHUB_TOKEN`",
            "auto-resolve permission note includes token sources");
    }

    private static bool TryGetResolveThreadIdFromGraphQlPayload(string body, out string? threadId) {
        threadId = null;
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        JsonObject? payload;
        try {
            payload = JsonLite.Parse(body).AsObject();
        } catch (FormatException) {
            return false;
        } catch (ArgumentNullException) {
            return false;
        }
        if (payload is null) {
            return false;
        }
        var query = payload.GetString("query");
        if (string.IsNullOrWhiteSpace(query) || !query.Contains("resolveReviewThread", StringComparison.Ordinal)) {
            return false;
        }
        threadId = payload.GetObject("variables")?.GetString("id");
        return !string.IsNullOrWhiteSpace(threadId);
    }

    private static string BuildGraphQlHydratedThreadResponse(string threadId,
        params (string Body, string Path, int Line, string Author)[] comments) {
        return BuildGraphQlHydratedThreadResponse(threadId, false, false, comments);
    }

    private static string BuildGraphQlHydratedThreadResponse(string threadId, bool isResolved, bool isOutdated,
        params (string Body, string Path, int Line, string Author)[] comments) {
        var sb = new StringBuilder();
        sb.Append("{\"data\":{\"node\":{\"id\":\"")
            .Append(EscapeJson(threadId))
            .Append("\",\"isResolved\":")
            .Append(isResolved ? "true" : "false")
            .Append(",\"isOutdated\":")
            .Append(isOutdated ? "true" : "false")
            .Append(",\"comments\":{\"totalCount\":")
            .Append(comments.Length)
            .Append(",\"nodes\":[");
        for (var i = 0; i < comments.Length; i++) {
            if (i > 0) {
                sb.Append(',');
            }
            var comment = comments[i];
            sb.Append("{\"databaseId\":")
                .Append(i + 1)
                .Append(",\"createdAt\":\"2024-01-01T00:00:00Z\",\"body\":\"")
                .Append(EscapeJson(comment.Body))
                .Append("\",\"path\":\"")
                .Append(EscapeJson(comment.Path))
                .Append("\",\"line\":")
                .Append(comment.Line.ToString())
                .Append(",\"author\":{\"login\":\"")
                .Append(EscapeJson(comment.Author))
                .Append("\"}}");
        }
        sb.Append("],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}");
        return sb.ToString();
    }
}
#endif
