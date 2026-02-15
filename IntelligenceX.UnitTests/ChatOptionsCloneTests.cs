using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ChatOptionsCloneTests {
    [Fact]
    public void Clone_ShouldCopyAllPublicSettableProperties() {
        var options = new ChatOptions();

        var props = typeof(ChatOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static p => p.CanRead && p.CanWrite)
            .ToArray();

        foreach (var prop in props) {
            if (prop.Name == nameof(ChatOptions.Tools)) {
                var tools = new List<ToolDefinition> { new ToolDefinition("t1"), new ToolDefinition("t2") };
                prop.SetValue(options, tools);
                continue;
            }

            prop.SetValue(options, MakeSampleValue(prop.PropertyType, prop.Name));
        }

        var clone = options.Clone();

        foreach (var prop in props) {
            var originalValue = prop.GetValue(options);
            var clonedValue = prop.GetValue(clone);

            if (prop.Name == nameof(ChatOptions.Tools)) {
                Assert.NotNull(originalValue);
                Assert.NotNull(clonedValue);
                Assert.NotSame(originalValue, clonedValue);
                Assert.Equal(
                    ((IReadOnlyList<ToolDefinition>)originalValue!).Select(static t => t.Name).ToArray(),
                    ((IReadOnlyList<ToolDefinition>)clonedValue!).Select(static t => t.Name).ToArray());
                continue;
            }

            Assert.Equal(originalValue, clonedValue);
        }
    }

    [Fact]
    public void Clone_ShouldDefensivelyCopyToolsList() {
        var tools = new List<ToolDefinition> {
            new ToolDefinition("t1")
        };

        var options = new ChatOptions {
            Tools = tools
        };

        var clone = options.Clone();

        Assert.NotSame(options, clone);
        Assert.NotNull(clone.Tools);
        Assert.NotSame(options.Tools, clone.Tools);
        Assert.Single(clone.Tools!);

        // Mutating the original list should not affect the clone.
        tools.Add(new ToolDefinition("t2"));
        Assert.Single(clone.Tools!);
    }

    private static object? MakeSampleValue(Type propertyType, string propertyName) {
        var t = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (t == typeof(string)) {
            return $"{propertyName}-value";
        }

        if (t == typeof(bool)) {
            return Nullable.GetUnderlyingType(propertyType) is null ? true : (bool?)true;
        }

        if (t == typeof(double)) {
            return (double?)0.42;
        }

        if (t == typeof(long)) {
            return (long?)1234L;
        }

        if (t.IsEnum) {
            var values = Enum.GetValues(t);
            if (values.Length == 0) {
                throw new InvalidOperationException($"Enum {t.FullName} has no values.");
            }

            // Pick a deterministic non-default where possible.
            var value = values.Length > 1 ? values.GetValue(1)! : values.GetValue(0)!;
            return Nullable.GetUnderlyingType(propertyType) is null ? value : Activator.CreateInstance(propertyType, value);
        }

        if (t == typeof(ToolChoice)) {
            return ToolChoice.Custom("t1");
        }

        if (t == typeof(SandboxPolicy)) {
            return new SandboxPolicy("test-sandbox", networkAccess: true, writableRoots: new[] { "C:\\Temp" });
        }

        throw new NotSupportedException($"No sample value generator for property '{propertyName}' of type '{propertyType.FullName}'.");
    }
}
