using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Registry for tools available to the model.
/// </summary>
public sealed class ToolRegistry {
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runtime authorizer used for write-intent tool calls.
    /// </summary>
    public IToolWriteGovernanceRuntime? WriteGovernanceRuntime { get; set; }

    /// <summary>
    /// Append-only audit sink used to persist write authorization events.
    /// </summary>
    public IToolWriteAuditSink? WriteAuditSink { get; set; }

    /// <summary>
    /// When true, write-intent calls are rejected if no <see cref="WriteGovernanceRuntime"/> is configured.
    /// </summary>
    public bool RequireWriteGovernanceRuntime { get; set; } = true;

    /// <summary>
    /// When true, write-intent calls are rejected when no <see cref="WriteAuditSink"/> is configured.
    /// </summary>
    public bool RequireWriteAuditSinkForWriteOperations { get; set; }

    /// <summary>
    /// Registers a tool.
    /// </summary>
    /// <param name="tool">Tool instance.</param>
    public void Register(ITool tool) {
        Register(tool, replaceExisting: false);
    }

    /// <summary>
    /// Registers a tool with optional replacement.
    /// </summary>
    /// <param name="tool">Tool instance.</param>
    /// <param name="replaceExisting">Replace an existing tool with the same name.</param>
    public void Register(ITool tool, bool replaceExisting) {
        if (tool is null) {
            throw new ArgumentNullException(nameof(tool));
        }

        var definition = tool.Definition;
        if (replaceExisting) {
            RemoveCanonicalEntries(definition.CanonicalName);
        }

        var registeredTool = CreateRegisteredTool(tool, definition);
        RegisterEntry(registeredTool, definition, replaceExisting);
        foreach (var alias in definition.Aliases) {
            var aliasDefinition = definition.CreateAliasDefinition(alias.Name, alias.Description, alias.Tags);
            RegisterEntry(registeredTool, aliasDefinition, replaceExisting);
        }
    }

    /// <summary>
    /// Gets a tool by name.
    /// </summary>
    public bool TryGet(string name, out ITool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>
    /// Gets a registered tool definition by name.
    /// </summary>
    public bool TryGetDefinition(string name, out ToolDefinition definition) => _definitions.TryGetValue(name, out definition!);

    /// <summary>
    /// Registers an alias for an already-registered tool.
    /// </summary>
    /// <param name="aliasName">Alias name.</param>
    /// <param name="targetToolName">Existing canonical or alias tool name to map to.</param>
    /// <param name="description">Optional alias-specific description override.</param>
    /// <param name="tags">Optional alias tags merged with canonical tags.</param>
    /// <param name="replaceExisting">Replace an existing registration that uses <paramref name="aliasName"/>.</param>
    public void RegisterAlias(
        string aliasName,
        string targetToolName,
        string? description = null,
        IReadOnlyList<string>? tags = null,
        bool replaceExisting = false) {
        if (string.IsNullOrWhiteSpace(aliasName)) {
            throw new ArgumentException("Alias name cannot be empty.", nameof(aliasName));
        }
        if (string.IsNullOrWhiteSpace(targetToolName)) {
            throw new ArgumentException("Target tool name cannot be empty.", nameof(targetToolName));
        }

        if (!_tools.TryGetValue(targetToolName, out var tool) || !_definitions.TryGetValue(targetToolName, out var targetDefinition)) {
            throw new InvalidOperationException($"Tool '{targetToolName}' is not registered.");
        }

        var aliasDefinition = targetDefinition.CreateAliasDefinition(aliasName, description, tags);
        RegisterEntry(tool, aliasDefinition, replaceExisting);
    }

    /// <summary>
    /// Returns tool definitions for the registry.
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetDefinitions()
        => _definitions.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void RegisterEntry(ITool tool, ToolDefinition definition, bool replaceExisting) {
        if (!replaceExisting && _tools.ContainsKey(definition.Name)) {
            throw new InvalidOperationException($"Tool '{definition.Name}' is already registered.");
        }

        ValidateWriteGovernanceContract(definition);

        _tools[definition.Name] = tool;
        _definitions[definition.Name] = definition;
    }

    private ITool CreateRegisteredTool(ITool tool, ToolDefinition definition) {
        if (tool is RegistryToolWrapper) {
            return tool;
        }

        var contract = definition.WriteGovernance;
        if (contract is null || !contract.IsWriteCapable) {
            return tool;
        }

        return new RegistryToolWrapper(tool, definition, this);
    }

    private void RemoveCanonicalEntries(string canonicalName) {
        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return;
        }

        var toRemove = _definitions
            .Where(static kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Where(kv => string.Equals(kv.Value.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var key in toRemove) {
            _definitions.Remove(key);
            _tools.Remove(key);
        }
    }

    private static void ValidateWriteGovernanceContract(ToolDefinition definition) {
        ToolWriteGovernanceContract? contract = definition.WriteGovernance;
        if (contract is null || !contract.IsWriteCapable) {
            return;
        }

        contract.Validate();
        if (!contract.RequiresGovernanceAuthorization) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' is write-capable and must require governance authorization.");
        }
        if (string.IsNullOrWhiteSpace(contract.GovernanceContractId)) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' is write-capable and must declare GovernanceContractId.");
        }
    }

    private sealed class RegistryToolWrapper : ITool {
        private readonly ITool _inner;
        private readonly ToolDefinition _definition;
        private readonly ToolRegistry _owner;

        public RegistryToolWrapper(ITool inner, ToolDefinition definition, ToolRegistry owner) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public ToolDefinition Definition => _definition;

        public async Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            ToolWriteGovernanceContract? contract = _definition.WriteGovernance;
            if (contract is not null &&
                contract.IsWriteCapable &&
                contract.IsWriteRequested(arguments)) {
                ToolWriteGovernanceRequest request = CreateGovernanceRequest(arguments, contract);

                if (contract.RequireExplicitConfirmation && !contract.HasExplicitConfirmation(arguments)) {
                    ToolWriteGovernanceResult denied = new() {
                        IsAuthorized = false,
                        ErrorCode = "write_confirmation_required",
                        Error = $"Tool '{_definition.Name}' requires explicit write confirmation via '{contract.ConfirmationArgumentName}=true'.",
                        Hints = new[] {
                            $"Set {contract.ConfirmationArgumentName}=true to confirm write intent.",
                            "Provide governance metadata for immutable audit and rollback tracking."
                        },
                        IsTransient = false,
                        ExecutionId = request.ExecutionId,
                        AuditCorrelationId = request.AuditCorrelationId
                    };
                    AppendWriteAuditRecord(request, denied);
                    return ToolOutputEnvelope.Error(
                        errorCode: "write_confirmation_required",
                        error: $"Tool '{_definition.Name}' requires explicit write confirmation via '{contract.ConfirmationArgumentName}=true'.",
                        hints: new[] {
                            $"Set {contract.ConfirmationArgumentName}=true to confirm write intent.",
                            "Provide governance metadata for immutable audit and rollback tracking."
                        },
                        isTransient: false);
                }

                if (contract.RequiresGovernanceAuthorization) {
                    if (_owner.RequireWriteAuditSinkForWriteOperations && _owner.WriteAuditSink is null) {
                        return ToolOutputEnvelope.Error(
                            errorCode: "write_audit_sink_required",
                            error: $"Tool '{_definition.Name}' requires a configured write audit sink for write operations.",
                            hints: new[] {
                                "Configure ToolRegistry.WriteAuditSink.",
                                $"Required contract: {contract.GovernanceContractId}."
                            },
                            isTransient: false);
                    }

                    if (_owner.WriteGovernanceRuntime is null) {
                        if (_owner.RequireWriteGovernanceRuntime) {
                            ToolWriteGovernanceResult denied = new() {
                                IsAuthorized = false,
                                ErrorCode = "write_governance_runtime_required",
                                Error = $"Tool '{_definition.Name}' requires a configured write governance runtime.",
                                Hints = new[] {
                                    "Configure ToolRegistry.WriteGovernanceRuntime.",
                                    $"Required contract: {contract.GovernanceContractId}."
                                },
                                IsTransient = false,
                                ExecutionId = request.ExecutionId,
                                AuditCorrelationId = request.AuditCorrelationId
                            };
                            AppendWriteAuditRecord(request, denied);
                            return ToolOutputEnvelope.Error(
                                errorCode: "write_governance_runtime_required",
                                error: $"Tool '{_definition.Name}' requires a configured write governance runtime.",
                                hints: new[] {
                                    "Configure ToolRegistry.WriteGovernanceRuntime.",
                                    $"Required contract: {contract.GovernanceContractId}."
                                },
                                isTransient: false);
                        }
                    } else {
                        ToolWriteGovernanceResult authorization = _owner.WriteGovernanceRuntime.Authorize(request);
                        AppendWriteAuditRecord(request, authorization);

                        if (!authorization.IsAuthorized) {
                            string errorCode = string.IsNullOrWhiteSpace(authorization.ErrorCode)
                                ? "write_governance_denied"
                                : authorization.ErrorCode;
                            string error = string.IsNullOrWhiteSpace(authorization.Error)
                                ? $"Write authorization denied for tool '{_definition.Name}'."
                                : authorization.Error;
                            return ToolOutputEnvelope.Error(
                                errorCode: errorCode,
                                error: error,
                                hints: authorization.Hints,
                                isTransient: authorization.IsTransient);
                        }
                    }
                }
            }

            return await _inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        private ToolWriteGovernanceRequest CreateGovernanceRequest(
            JsonObject? arguments,
            ToolWriteGovernanceContract contract) {
            string executionId = ReadArgument(arguments, ToolWriteGovernanceArgumentNames.ExecutionId);
            string auditCorrelationId = ReadArgument(arguments, ToolWriteGovernanceArgumentNames.AuditCorrelationId);
            if (string.IsNullOrWhiteSpace(auditCorrelationId)) {
                auditCorrelationId = executionId;
            }

            return new ToolWriteGovernanceRequest {
                ToolName = _definition.Name,
                CanonicalToolName = _definition.CanonicalName,
                GovernanceContractId = contract.GovernanceContractId,
                Arguments = arguments,
                ConfirmationArgumentName = contract.ConfirmationArgumentName,
                ExecutionId = executionId,
                ActorId = ReadArgument(arguments, ToolWriteGovernanceArgumentNames.ActorId),
                ChangeReason = ReadArgument(arguments, ToolWriteGovernanceArgumentNames.ChangeReason),
                RollbackPlanId = ReadArgument(arguments, ToolWriteGovernanceArgumentNames.RollbackPlanId),
                RollbackProviderId = ReadArgument(arguments, ToolWriteGovernanceArgumentNames.RollbackProviderId),
                AuditCorrelationId = auditCorrelationId
            };
        }

        private void AppendWriteAuditRecord(
            ToolWriteGovernanceRequest request,
            ToolWriteGovernanceResult authorization) {
            IToolWriteAuditSink? sink = _owner.WriteAuditSink;
            if (sink is null) {
                return;
            }

            string executionId = string.IsNullOrWhiteSpace(authorization.ExecutionId)
                ? request.ExecutionId
                : authorization.ExecutionId;
            string auditCorrelationId = string.IsNullOrWhiteSpace(authorization.AuditCorrelationId)
                ? request.AuditCorrelationId
                : authorization.AuditCorrelationId;
            if (string.IsNullOrWhiteSpace(auditCorrelationId)) {
                auditCorrelationId = executionId;
            }

            ToolWriteAuditRecord record = new() {
                TimestampUtc = DateTimeOffset.UtcNow,
                ToolName = _definition.Name,
                CanonicalToolName = _definition.CanonicalName,
                GovernanceContractId = request.GovernanceContractId,
                IsAuthorized = authorization.IsAuthorized,
                ErrorCode = authorization.ErrorCode,
                Error = authorization.Error,
                ExecutionId = executionId,
                AuditCorrelationId = auditCorrelationId,
                ActorId = request.ActorId,
                ChangeReason = request.ChangeReason,
                RollbackPlanId = request.RollbackPlanId,
                ImmutableAuditProviderId = authorization.ImmutableAuditProviderId,
                RollbackProviderId = string.IsNullOrWhiteSpace(authorization.RollbackProviderId)
                    ? request.RollbackProviderId
                    : authorization.RollbackProviderId
            };
            sink.Append(record);
        }

        private static string ReadArgument(JsonObject? arguments, string argumentName) {
            if (arguments is null || string.IsNullOrWhiteSpace(argumentName)) {
                return string.Empty;
            }

            string? value = arguments.GetString(argumentName);
            return value?.Trim() ?? string.Empty;
        }
    }
}
