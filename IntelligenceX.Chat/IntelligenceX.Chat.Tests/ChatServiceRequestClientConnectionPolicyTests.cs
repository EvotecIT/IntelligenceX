using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceRequestClientConnectionPolicyTests {
    public static IEnumerable<object[]> RequestConnectionPolicyCases() {
        yield return new object[] { new HelloRequest { RequestId = "req_hello" }, false };
        yield return new object[] { new ListToolsRequest { RequestId = "req_tools" }, false };
        yield return new object[] { new CheckToolHealthRequest { RequestId = "req_health" }, false };
        yield return new object[] { new ListProfilesRequest { RequestId = "req_profiles" }, false };
        yield return new object[] { new SetProfileRequest { RequestId = "req_profile_set", ProfileName = "local" }, false };
        yield return new object[] { new EnsureLoginRequest { RequestId = "req_login" }, true };
        yield return new object[] { new StartChatGptLoginRequest { RequestId = "req_login_start", TimeoutSeconds = 120 }, true };
        yield return new object[] { new ListModelsRequest { RequestId = "req_models" }, true };
        yield return new object[] { new ChatRequest { RequestId = "req_chat", Text = "hello" }, true };
    }

    [Theory]
    [MemberData(nameof(RequestConnectionPolicyCases))]
    public void RequestRequiresConnectedClient_ReturnsExpected(ChatServiceRequest request, bool expected) {
        var result = ChatServiceSession.RequestRequiresConnectedClient(request);

        Assert.Equal(expected, result);
    }
}
