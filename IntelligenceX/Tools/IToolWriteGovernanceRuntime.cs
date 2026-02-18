namespace IntelligenceX.Tools;

/// <summary>
/// Runtime authorizer for write-capable tool execution.
/// </summary>
public interface IToolWriteGovernanceRuntime {
    /// <summary>
    /// Authorizes a write-intent tool call.
    /// </summary>
    /// <param name="request">Write authorization request.</param>
    /// <returns>Authorization result.</returns>
    ToolWriteGovernanceResult Authorize(ToolWriteGovernanceRequest request);
}
