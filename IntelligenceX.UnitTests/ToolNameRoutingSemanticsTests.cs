using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolNameRoutingSemanticsTests {
    [Theory]
    [InlineData("custom_pack_info", false, "guide")]
    [InlineData("custom_environment_discover", false, "discover")]
    [InlineData("custom_rules_list", false, "list")]
    [InlineData("custom_inventory_search", false, "search")]
    [InlineData("custom_domain_query", false, "query")]
    [InlineData("custom_object_resolve", false, "resolve")]
    [InlineData("custom_security_summary", false, "summarize")]
    [InlineData("custom_health", false, "summarize")]
    [InlineData("custom_probe", false, "probe")]
    [InlineData("custom_ping", false, "probe")]
    [InlineData("custom_send", false, "write")]
    [InlineData("custom_run", false, "execute")]
    [InlineData("custom_run", true, "execute_write")]
    [InlineData("custom_get", false, "read")]
    [InlineData("custom_unknown", false, "read")]
    [InlineData("custom_unknown", true, "write")]
    public void InferOperation_ShouldRecognizeKnownNameShapes(string toolName, bool isWriteCapable, string expectedOperation) {
        var operation = ToolNameRoutingSemantics.InferOperation(toolName, isWriteCapable);

        Assert.Equal(expectedOperation, operation);
    }

    [Theory]
    [InlineData("dnsclientx_ping", "dns", "host")]
    [InlineData("eventlog_live_query", "eventlog", "event")]
    [InlineData("ad_object_resolve", "active_directory", "directory_object")]
    [InlineData("email_smtp_send", "email", "message")]
    [InlineData("filesystem_reader", "filesystem", "file")]
    [InlineData("custom_probe", "system", "host")]
    [InlineData("custom_probe", "reviewer_setup", "resource")]
    public void InferEntity_ShouldRecognizeKnownNameTokensAndCategoryFallbacks(string toolName, string category, string expectedEntity) {
        var entity = ToolNameRoutingSemantics.InferEntity(toolName, category);

        Assert.Equal(expectedEntity, entity);
    }
}
