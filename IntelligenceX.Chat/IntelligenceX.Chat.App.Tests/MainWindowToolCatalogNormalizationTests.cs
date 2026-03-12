using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards canonical pack-id normalization when the desktop app ingests tool catalog payloads.
/// </summary>
public sealed class MainWindowToolCatalogNormalizationTests {
    private static readonly MethodInfo UpdateToolCatalogMethod = typeof(MainWindow).GetMethod(
        "UpdateToolCatalog",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("UpdateToolCatalog not found.");

    private static readonly FieldInfo ToolDescriptionsField = typeof(MainWindow).GetField(
        "_toolDescriptions",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolDescriptions field not found.");

    private static readonly FieldInfo ToolDisplayNamesField = typeof(MainWindow).GetField(
        "_toolDisplayNames",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolDisplayNames field not found.");

    private static readonly FieldInfo ToolCategoriesField = typeof(MainWindow).GetField(
        "_toolCategories",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCategories field not found.");

    private static readonly FieldInfo ToolTagsField = typeof(MainWindow).GetField(
        "_toolTags",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolTags field not found.");

    private static readonly FieldInfo ToolPackIdsField = typeof(MainWindow).GetField(
        "_toolPackIds",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolPackIds field not found.");

    private static readonly FieldInfo ToolPackNamesField = typeof(MainWindow).GetField(
        "_toolPackNames",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolPackNames field not found.");

    private static readonly FieldInfo ToolParametersField = typeof(MainWindow).GetField(
        "_toolParameters",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolParameters field not found.");

    private static readonly FieldInfo ToolWriteCapabilitiesField = typeof(MainWindow).GetField(
        "_toolWriteCapabilities",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolWriteCapabilities field not found.");

    private static readonly FieldInfo ToolStatesField = typeof(MainWindow).GetField(
        "_toolStates",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolStates field not found.");

    private static readonly FieldInfo ToolRoutingConfidenceField = typeof(MainWindow).GetField(
        "_toolRoutingConfidence",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingConfidence field not found.");

    private static readonly FieldInfo ToolRoutingReasonField = typeof(MainWindow).GetField(
        "_toolRoutingReason",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingReason field not found.");

    private static readonly FieldInfo ToolRoutingScoreField = typeof(MainWindow).GetField(
        "_toolRoutingScore",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingScore field not found.");

    /// <summary>
    /// Ensures tool catalog ingest stores canonical shared Chat pack ids instead of raw alias labels.
    /// </summary>
    [Fact]
    public void UpdateToolCatalog_NormalizesPackIds_ToCanonicalContractIds() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "ad_search_users",
                Description = "Searches users.",
                PackId = "ADPlayground",
                PackName = "Active Directory",
                Category = "directory"
            },
            new ToolDefinitionDto {
                Name = "computer_inventory",
                Description = "Reads computer inventory.",
                PackId = "ComputerX",
                PackName = "System",
                Category = "system"
            },
            new ToolDefinitionDto {
                Name = "fs_scan",
                Description = "Scans filesystems.",
                PackId = "fs",
                PackName = "Filesystem",
                Category = "files"
            }
        };

        InvokeUpdateToolCatalog(window, tools);

        var packIds = Assert.IsType<Dictionary<string, string>>(ToolPackIdsField.GetValue(window));
        Assert.Equal("active_directory", packIds["ad_search_users"]);
        Assert.Equal("system", packIds["computer_inventory"]);
        Assert.Equal("filesystem", packIds["fs_scan"]);
    }

    private static MainWindow CreateWindow() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        SetField(ToolDescriptionsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolDisplayNamesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolCategoriesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolTagsField, window, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolPackIdsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolPackNamesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolParametersField, window, new Dictionary<string, ToolParameterDto[]>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolWriteCapabilitiesField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolStatesField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolRoutingConfidenceField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolRoutingReasonField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolRoutingScoreField, window, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
        return window;
    }

    private static void InvokeUpdateToolCatalog(MainWindow window, ToolDefinitionDto[] tools) {
        try {
            UpdateToolCatalogMethod.Invoke(window, new object?[] { tools, null, null, null });
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static void SetField(FieldInfo field, MainWindow window, object value) {
        field.SetValue(window, value);
    }
}
