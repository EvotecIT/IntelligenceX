using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Provides typed construction of service sidecar launch arguments.
/// </summary>
internal static class ServiceLaunchArguments {
    /// <summary>
    /// Builds service-sidecar arguments for the configured pipe and lifecycle mode.
    /// </summary>
    /// <param name="pipeName">Named-pipe identifier.</param>
    /// <param name="detachedServiceMode">Whether service is detached from app lifetime.</param>
    /// <param name="parentProcessId">Parent process id used for exit-on-disconnect mode.</param>
    /// <returns>Ordered argument vector.</returns>
    public static IReadOnlyList<string> Build(string pipeName, bool detachedServiceMode, int parentProcessId) {
        var normalizedPipe = (pipeName ?? string.Empty).Trim();
        if (normalizedPipe.Length == 0) {
            throw new ArgumentException("Pipe name cannot be empty.", nameof(pipeName));
        }

        var args = new List<string> {
            "--pipe",
            normalizedPipe
        };

        if (!detachedServiceMode) {
            args.Add("--exit-on-disconnect");
            args.Add("--parent-pid");
            args.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return args;
    }
}
