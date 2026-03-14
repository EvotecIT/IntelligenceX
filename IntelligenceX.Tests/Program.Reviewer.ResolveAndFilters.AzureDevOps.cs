namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAzureDevOpsChangesPagination() {
        var project = "project";
        var repo = "repo";
        var prId = 42;

        var page1 = "{\"changes\":[{\"item\":{\"path\":\"/src/A.cs\"},\"changeType\":\"edit\"},{\"item\":{\"path\":\"/src/B.cs\"},\"changeType\":\"add\"}]}";
        var page2 = "{\"changes\":[{\"item\":{\"path\":\"/src/C.cs\"},\"changeType\":\"delete\"}]}";

        using var server = new LocalHttpServer(request => {
            if (!request.Path.StartsWith($"/{project}/_apis/git/repositories/{repo}/pullRequests/{prId}/changes",
                    StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            if (request.Path.Contains("continuationToken=token1", StringComparison.OrdinalIgnoreCase)) {
                return new HttpResponse(page2);
            }

            return new HttpResponse(page1, new Dictionary<string, string> {
                ["x-ms-continuationtoken"] = "token1"
            });
        });

        using var client = new AzureDevOpsClient(server.BaseUri, "token", AzureDevOpsAuthScheme.Bearer);
        var files = client.GetPullRequestChangesAsync(project, repo, prId, CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEqual(3, files.Count, "ado page count");
        AssertSequenceEqual(new[] { "src/A.cs", "src/B.cs", "src/C.cs" }, GetFilenames(files), "ado page files");
    }

    private static void TestAzureDevOpsDiffNoteZeroIterations() {
        var note = AzureDevOpsReviewRunner.BuildDiffNote(Array.Empty<int>());
        AssertEqual("pull request changes", note, "ado diff note zero");
    }

    private static void TestAzureDevOpsInlinePatchLineMapParsesAddedLines() {
        var patch = string.Join("\n", new[] {
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -1,2 +10,4 @@",
            " line1",
            "+added1",
            "+added2",
            " line2",
            "@@ -20,1 +99,2 @@",
            "+added3",
            " line3"
        });

        var result = AzureDevOpsReviewRunner.ParsePatchLines(patch);
        AssertEqual(true, result.Contains(11), "ado patch contains line 11");
        AssertEqual(true, result.Contains(12), "ado patch contains line 12");
        AssertEqual(true, result.Contains(99), "ado patch contains line 99");
    }

    private static void TestAzureDevOpsInlinePatchLineMapHandlesCrlfAndDeletions() {
        var patch = string.Join("\r\n", new[] {
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -1,3 +1,3 @@",
            "-line1",
            "+line1changed",
            " line2",
            " line3",
            "\\ No newline at end of file"
        });

        var result = AzureDevOpsReviewRunner.ParsePatchLines(patch);
        AssertEqual(true, result.Contains(1), "ado patch contains line 1");
        AssertEqual(false, result.Contains(2), "ado patch does not include context line 2");
        AssertEqual(false, result.Contains(3), "ado patch does not include context line 3");
    }

    private static void TestAzureDevOpsInlinePatchLineMapHandlesPlusPlusAndDashDashContent() {
        var patch = string.Join("\n", new[] {
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -1,1 +1,1 @@",
            // Removed line whose content begins with "--" => patch line begins with "---" (should not be treated as a header).
            "---bye",
            // Added line whose content begins with "++" => patch line begins with "+++" (should not be treated as a header).
            "+++hello"
        });

        var result = AzureDevOpsReviewRunner.ParsePatchLines(patch);
        AssertEqual(true, result.Contains(1), "ado patch includes +++ content line as added");
    }

    private static void TestAzureDevOpsInlineThreadContextUsesOneBasedLineAndZeroBasedOffset() {
        var project = "proj";
        var repo = "repo";
        var prId = 42;
        string capturedBody = string.Empty;

        using var server = new LocalHttpServer(request => {
            if (!request.Path.StartsWith($"/{project}/_apis/git/repositories/{repo}/pullRequests/{prId}/threads",
                    StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            capturedBody = request.Body;
            return new HttpResponse("{}");
        });

        using var client = new AzureDevOpsClient(server.BaseUri, "token", AzureDevOpsAuthScheme.Bearer);
        client.CreatePullRequestInlineThreadAsync(project, repo, prId, "src/A.cs", 5, "Hello", CancellationToken.None)
            .GetAwaiter().GetResult();

        var json = JsonLite.Parse(capturedBody);
        var obj = json?.AsObject();
        AssertNotNull(obj, "ado inline thread payload");

        var context = obj!.GetObject("threadContext");
        AssertNotNull(context, "ado threadContext");
        AssertEqual("/src/A.cs", context!.GetString("filePath"), "ado threadContext filePath");
        AssertEqual(5L, context.GetObject("rightFileStart")!.GetInt64("line"), "ado rightFileStart line");
        AssertEqual(0L, context.GetObject("rightFileStart")!.GetInt64("offset"), "ado rightFileStart offset");
        AssertEqual(5L, context.GetObject("rightFileEnd")!.GetInt64("line"), "ado rightFileEnd line");
        AssertEqual(0L, context.GetObject("rightFileEnd")!.GetInt64("offset"), "ado rightFileEnd offset");
    }

    private static void TestAzureDevOpsErrorSanitization() {
        var errorJson = "{\"message\":\"Authorization: Bearer abc123\"}";
        var sanitized = CallAzureDevOpsSanitize(errorJson);
        AssertContainsText(sanitized, "***", "sanitized token");
        if (sanitized.Contains("abc123", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected token value to be redacted.");
        }
    }

    private static string CallAzureDevOpsSanitize(string content) {
        var method = typeof(AzureDevOpsClient).GetMethod("SanitizeErrorContent", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("SanitizeErrorContent method not found.");
        }
        var result = method.Invoke(null, new object?[] { content }) as string;
        return result ?? string.Empty;
    }
}
#endif
