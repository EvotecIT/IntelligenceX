#if !NET472
using System;
using System.Reflection;
using IntelligenceX.Reviewer;
#endif

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestReviewerGraphQlMutationDetection() {
        var method = typeof(GitHubClient).GetMethod("LooksLikeGraphQlMutation", BindingFlags.NonPublic | BindingFlags.Static);
        AssertEqual(true, method is not null, "mutation detector method exists");
        bool IsMutation(string value) {
            return (bool)(method!.Invoke(null, new object[] { value }) ?? false);
        }

        AssertEqual(true, IsMutation("mutation{ a }"), "detect mutation without whitespace");
        AssertEqual(true, IsMutation("mutation { a }"), "detect mutation with whitespace");
        AssertEqual(true, IsMutation("mutation Foo { a }"), "detect mutation with operation name");
        AssertEqual(true, IsMutation("mutation($x:Int){ a }"), "detect mutation with variables");
        AssertEqual(true, IsMutation("#comment\nmutation{ a }"), "detect mutation after leading comment (LF)");
        AssertEqual(true, IsMutation("#comment\r\nmutation{ a }"), "detect mutation after leading comment (CRLF)");

        AssertEqual(false, IsMutation("query{ a }"), "reject query");
        AssertEqual(false, IsMutation("query Foo { a }"), "reject query with operation name");
        AssertEqual(false, IsMutation("mutationX{ a }"), "reject non-mutation keyword prefix");
    }
#endif
}
