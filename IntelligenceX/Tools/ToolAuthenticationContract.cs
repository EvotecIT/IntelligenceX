using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares authentication requirements and argument conventions for a tool definition.
/// </summary>
public sealed class ToolAuthenticationContract {
    /// <summary>
    /// Default contract id for IX tool authentication metadata.
    /// </summary>
    public const string DefaultContractId = "ix.tool-auth.v1";

    /// <summary>
    /// True when tool exposes authentication behavior/requirements.
    /// </summary>
    public bool IsAuthenticationAware { get; set; }

    /// <summary>
    /// True when authentication is required for normal operation.
    /// </summary>
    public bool RequiresAuthentication { get; set; }

    /// <summary>
    /// Stable authentication contract identifier.
    /// </summary>
    public string AuthenticationContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// Strategy used for authentication source selection.
    /// </summary>
    public ToolAuthenticationMode Mode { get; set; } = ToolAuthenticationMode.None;

    /// <summary>
    /// Argument name used by <see cref="ToolAuthenticationMode.ProfileReference"/>.
    /// </summary>
    public string ProfileIdArgumentName { get; set; } = ToolAuthenticationArgumentNames.ProfileId;

    /// <summary>
    /// Argument name used by <see cref="ToolAuthenticationMode.RunAsReference"/>.
    /// </summary>
    public string RunAsProfileIdArgumentName { get; set; } = ToolAuthenticationArgumentNames.RunAsProfileId;

    /// <summary>
    /// True when tool should expose a preflight connectivity/auth probe flow.
    /// </summary>
    public bool SupportsConnectivityProbe { get; set; }

    /// <summary>
    /// Optional probe tool name to validate authentication before mutating operations.
    /// </summary>
    public string ProbeToolName { get; set; } = string.Empty;

    /// <summary>
    /// Argument name used when <see cref="SupportsConnectivityProbe"/> is enabled.
    /// </summary>
    public string ProbeIdArgumentName { get; set; } = ToolAuthenticationArgumentNames.ProbeId;

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsAuthenticationAware) {
            return;
        }

        if (string.IsNullOrWhiteSpace(AuthenticationContractId)) {
            throw new InvalidOperationException("AuthenticationContractId is required when IsAuthenticationAware is enabled.");
        }
        if (!Enum.IsDefined(typeof(ToolAuthenticationMode), Mode)) {
            throw new InvalidOperationException("Mode must be a supported ToolAuthenticationMode value when IsAuthenticationAware is enabled.");
        }

        if (RequiresAuthentication && Mode == ToolAuthenticationMode.None) {
            throw new InvalidOperationException(
                "Mode cannot be None when RequiresAuthentication is enabled.");
        }

        if (Mode == ToolAuthenticationMode.ProfileReference &&
            string.IsNullOrWhiteSpace(ProfileIdArgumentName)) {
            throw new InvalidOperationException(
                "ProfileIdArgumentName is required when Mode is ProfileReference.");
        }

        if (Mode == ToolAuthenticationMode.RunAsReference &&
            string.IsNullOrWhiteSpace(RunAsProfileIdArgumentName)) {
            throw new InvalidOperationException(
                "RunAsProfileIdArgumentName is required when Mode is RunAsReference.");
        }

        if (SupportsConnectivityProbe) {
            if (string.IsNullOrWhiteSpace(ProbeToolName)) {
                throw new InvalidOperationException(
                    "ProbeToolName is required when SupportsConnectivityProbe is enabled.");
            }

            if (string.IsNullOrWhiteSpace(ProbeIdArgumentName)) {
                throw new InvalidOperationException(
                    "ProbeIdArgumentName is required when SupportsConnectivityProbe is enabled.");
            }
        }
    }

    /// <summary>
    /// Returns schema argument names expected by this authentication mode.
    /// </summary>
    public IReadOnlyList<string> GetSchemaArgumentNames() {
        if (!IsAuthenticationAware) {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        switch (Mode) {
            case ToolAuthenticationMode.ProfileReference:
                arguments.Add(ProfileIdArgumentName.Trim());
                break;
            case ToolAuthenticationMode.RunAsReference:
                arguments.Add(RunAsProfileIdArgumentName.Trim());
                break;
        }

        if (SupportsConnectivityProbe) {
            arguments.Add(ProbeIdArgumentName.Trim());
        }

        return arguments.Count == 0
            ? Array.Empty<string>()
            : arguments.ToArray();
    }
}
