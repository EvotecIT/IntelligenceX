using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolCommonCollectionGuardrailTests {
    [Fact]
    public void HardenedCollectionContracts_ShouldDefensivelyCopyAndRejectMutation() {
        foreach (var guardCase in BuildGuardCases()) {
            Assert.Equal(guardCase.ModelType, guardCase.Model.GetType());

            var property = guardCase.ModelType.GetProperty(
                guardCase.PropertyName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);

            var value = property!.GetValue(guardCase.Model);
            Assert.NotNull(value);

            guardCase.MutateSource();

            guardCase.AssertSnapshot(value!);
            guardCase.AssertImmutable(value!);
        }
    }

    private static IReadOnlyList<GuardCase> BuildGuardCases() {
        var hints = new List<string> { "first hint" };
        var binding = ToolRequestBindingResult<RequestModel>.Failure("invalid", hints: hints);

        var requestColumns = new List<string> { " id ", "name" };
        var tableRequest = new ToolTableViewRequest {
            Columns = requestColumns
        };

        var tableColumns = new List<ToolColumn> { new("id", "ID", "int") };
        var tableResultColumns = new ToolTableViewResult {
            Columns = tableColumns
        };

        var previewRow = new List<string> { "alpha" };
        var previewRows = new List<IReadOnlyList<string>> { previewRow };
        var tableResultPreview = new ToolTableViewResult {
            PreviewRows = previewRows
        };

        var nextActions = new List<ToolNextActionModel> {
            new() {
                Tool = "ad_scope_discovery",
                Reason = "find boundaries"
            }
        };
        var chain = new ToolChainContractModel {
            NextActions = nextActions
        };

        var handoff = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["contract"] = "scope"
        };
        var chainHandoff = new ToolChainContractModel {
            Handoff = handoff
        };

        var suggestedArguments = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["path"] = @"C:\docs"
        };
        var nextAction = new ToolNextActionModel {
            Tool = "officeimo_read",
            Reason = "inspect file",
            SuggestedArguments = suggestedArguments
        };

        var toolCatalog = new List<ToolPackToolCatalogEntryModel> {
            new() {
                Name = " system_info ",
                Description = "System info"
            }
        };
        var packInfo = new ToolPackInfoModel {
            Pack = "system",
            Engine = "ComputerX",
            ToolCatalog = toolCatalog
        };

        return new[] {
            new GuardCase(
                ModelType: typeof(ToolRequestBindingResult<RequestModel>),
                PropertyName: nameof(ToolRequestBindingResult<RequestModel>.Hints),
                Model: binding,
                MutateSource: () => {
                    hints[0] = "changed";
                    hints.Add("new");
                },
                AssertSnapshot: static value => {
                    var list = Assert.IsAssignableFrom<IReadOnlyList<string>>(value);
                    Assert.Equal(new[] { "first hint" }, list);
                },
                AssertImmutable: static value => AssertReadOnlyList(value, addValue: "x", replaceValue: "y")),

            new GuardCase(
                ModelType: typeof(ToolTableViewRequest),
                PropertyName: nameof(ToolTableViewRequest.Columns),
                Model: tableRequest,
                MutateSource: () => {
                    requestColumns[0] = "mutated";
                    requestColumns.Add("memory_usage");
                },
                AssertSnapshot: static value => {
                    var list = Assert.IsAssignableFrom<IReadOnlyList<string>>(value);
                    Assert.Equal(new[] { "id", "name" }, list);
                },
                AssertImmutable: static value => AssertReadOnlyList(value, addValue: "x", replaceValue: "y")),

            new GuardCase(
                ModelType: typeof(ToolTableViewResult),
                PropertyName: nameof(ToolTableViewResult.Columns),
                Model: tableResultColumns,
                MutateSource: () => tableColumns.Add(new ToolColumn("name", "Name", "string")),
                AssertSnapshot: static value => {
                    var list = Assert.IsAssignableFrom<IReadOnlyList<ToolColumn>>(value);
                    var column = Assert.Single(list);
                    Assert.Equal("id", column.Key);
                },
                AssertImmutable: static value => AssertReadOnlyList(
                    value,
                    addValue: new ToolColumn("x", "X", "string"),
                    replaceValue: new ToolColumn("y", "Y", "string"))),

            new GuardCase(
                ModelType: typeof(ToolTableViewResult),
                PropertyName: nameof(ToolTableViewResult.PreviewRows),
                Model: tableResultPreview,
                MutateSource: () => {
                    previewRow[0] = "changed";
                    previewRows.Add(new[] { "beta" });
                },
                AssertSnapshot: static value => {
                    var rows = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<string>>>(value);
                    Assert.Single(rows);
                    Assert.Equal("alpha", rows[0][0]);
                },
                AssertImmutable: static value => {
                    AssertReadOnlyList(value, addValue: Array.Empty<string>(), replaceValue: new[] { "x" });
                    var rows = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<string>>>(value);
                    AssertReadOnlyList(rows[0], addValue: "z", replaceValue: "mutated");
                }),

            new GuardCase(
                ModelType: typeof(ToolChainContractModel),
                PropertyName: nameof(ToolChainContractModel.NextActions),
                Model: chain,
                MutateSource: () => {
                    nextActions[0] = new ToolNextActionModel {
                        Tool = "mutated_tool",
                        Reason = "mutated reason"
                    };
                    nextActions.Add(new ToolNextActionModel { Tool = "x", Reason = "y" });
                },
                AssertSnapshot: static value => {
                    var actions = Assert.IsAssignableFrom<IReadOnlyList<ToolNextActionModel>>(value);
                    var action = Assert.Single(actions);
                    Assert.Equal("ad_scope_discovery", action.Tool);
                    Assert.Equal("find boundaries", action.Reason);
                },
                AssertImmutable: static value => AssertReadOnlyList(
                    value,
                    addValue: new ToolNextActionModel { Tool = "x", Reason = "y" },
                    replaceValue: new ToolNextActionModel { Tool = "z", Reason = "r" })),

            new GuardCase(
                ModelType: typeof(ToolChainContractModel),
                PropertyName: nameof(ToolChainContractModel.Handoff),
                Model: chainHandoff,
                MutateSource: () => handoff["new"] = "value",
                AssertSnapshot: static value => {
                    var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(value);
                    Assert.Equal("scope", map["contract"]);
                    Assert.False(map.ContainsKey("new"));
                },
                AssertImmutable: static value => AssertReadOnlyDictionary(value)),

            new GuardCase(
                ModelType: typeof(ToolNextActionModel),
                PropertyName: nameof(ToolNextActionModel.SuggestedArguments),
                Model: nextAction,
                MutateSource: () => suggestedArguments["server"] = "dc01.contoso.com",
                AssertSnapshot: static value => {
                    var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(value);
                    Assert.Equal(@"C:\docs", map["path"]);
                    Assert.False(map.ContainsKey("server"));
                },
                AssertImmutable: static value => AssertReadOnlyDictionary(value)),

            new GuardCase(
                ModelType: typeof(ToolPackInfoModel),
                PropertyName: nameof(ToolPackInfoModel.ToolCatalog),
                Model: packInfo,
                MutateSource: () => toolCatalog.Add(new ToolPackToolCatalogEntryModel {
                    Name = "eventlog_live_query",
                    Description = "Added later"
                }),
                AssertSnapshot: static value => {
                    var list = Assert.IsAssignableFrom<IReadOnlyList<ToolPackToolCatalogEntryModel>>(value);
                    var entry = Assert.Single(list);
                    Assert.Equal("system_info", entry.Name);
                },
                AssertImmutable: static value => AssertReadOnlyList(
                    value,
                    addValue: new ToolPackToolCatalogEntryModel {
                        Name = "x",
                        Description = "x"
                    },
                    replaceValue: new ToolPackToolCatalogEntryModel {
                        Name = "y",
                        Description = "y"
                    }))
        };
    }

    private static void AssertReadOnlyList(object value, object addValue, object replaceValue) {
        var list = Assert.IsAssignableFrom<IList>(value);
        Assert.Throws<NotSupportedException>(() => list.Add(addValue));
        if (list.Count > 0) {
            Assert.Throws<NotSupportedException>(() => list[0] = replaceValue);
        }
    }

    private static void AssertReadOnlyDictionary(object value) {
        var dictionary = Assert.IsAssignableFrom<IDictionary>(value);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "1"));
        if (dictionary.Count > 0) {
            var firstKey = dictionary.Keys.Cast<object>().First();
            Assert.Throws<NotSupportedException>(() => dictionary[firstKey] = "updated");
        }
    }

    private sealed record GuardCase(
        Type ModelType,
        string PropertyName,
        object Model,
        Action MutateSource,
        Action<object> AssertSnapshot,
        Action<object> AssertImmutable);

    private sealed record RequestModel(string Name);
}
