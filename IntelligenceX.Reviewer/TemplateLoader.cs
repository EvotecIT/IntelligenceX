using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace IntelligenceX.Reviewer;

internal static class TemplateLoader {
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Load(string name) {
        return Cache.GetOrAdd(name, LoadInternal);
    }

    private static string LoadInternal(string name) {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Templates.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) {
            throw new InvalidOperationException($"Template resource not found: {resourceName}");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
