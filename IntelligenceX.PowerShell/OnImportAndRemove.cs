using System;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// Handles module import and cleanup events for the PowerShell module.
/// </summary>
public class OnModuleImportAndRemove : IModuleAssemblyInitializer, IModuleAssemblyCleanup {
    /// <summary>
    /// Called when the module is imported.
    /// </summary>
    public void OnImport() {
        if (IsNetFramework()) {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }
    }

    /// <summary>
    /// Called when the module is removed.
    /// </summary>
    /// <param name="module">The module being removed.</param>
    public void OnRemove(PSModuleInfo module) {
        if (IsNetFramework()) {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
        }
    }

    private static Assembly? ResolveAssembly(object? sender, ResolveEventArgs args) {
        var libDirectory = Path.GetDirectoryName(typeof(OnModuleImportAndRemove).Assembly.Location);
        if (string.IsNullOrEmpty(libDirectory)) {
            return null;
        }

        var directoriesToSearch = new List<string> { libDirectory };
        if (Directory.Exists(libDirectory)) {
            directoriesToSearch.AddRange(Directory.GetDirectories(libDirectory, "*", SearchOption.AllDirectories));
        }

        var requestedAssemblyName = new AssemblyName(args.Name).Name + ".dll";
        foreach (var directory in directoriesToSearch) {
            var assemblyPath = Path.Combine(directory, requestedAssemblyName);
            if (File.Exists(assemblyPath)) {
                try {
                    return Assembly.LoadFrom(assemblyPath);
                } catch {
                    // Ignore load failures.
                }
            }
        }

        return null;
    }

    private static bool IsNetFramework() {
        return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(
            ".NET Framework", StringComparison.OrdinalIgnoreCase);
    }
}
