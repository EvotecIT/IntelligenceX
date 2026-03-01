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
    private readonly object _writeOperationReplaySync = new();
    private readonly Dictionary<string, string> _writeOperationReplayOutputs = new(StringComparer.Ordinal);
    private readonly Queue<string> _writeOperationReplayOrder = new();
    private readonly Dictionary<string, Task<string>> _writeOperationReplayInFlight = new(StringComparer.Ordinal);
    private const int MaxWriteOperationReplayEntries = 512;

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
    /// Runtime mode for write-governance enforcement.
    /// </summary>
    public ToolWriteGovernanceMode WriteGovernanceMode { get; set; } = ToolWriteGovernanceMode.Enforced;

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

        var definition = ToolSelectionMetadata.Enrich(tool.Definition, tool.GetType());
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
        ValidateAuthenticationContract(definition);
        ValidateRoutingContract(definition);

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

        ValidateWriteGovernanceSchemaMetadata(definition);
    }

    private static void ValidateWriteGovernanceSchemaMetadata(ToolDefinition definition) {
        JsonObject? properties = definition.Parameters?.GetObject("properties");
        if (properties is null) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' is write-capable and must define an object schema with properties.");
        }

        List<string> missingArguments = new();
        for (var i = 0; i < ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments.Count; i++) {
            string argumentName = ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments[i];
            if (properties.GetObject(argumentName) is null) {
                missingArguments.Add(argumentName);
            }
        }

        if (missingArguments.Count == 0) {
            return;
        }

        throw new InvalidOperationException(
            $"Tool '{definition.Name}' is write-capable and must expose canonical write governance metadata arguments in schema properties: {string.Join(", ", missingArguments)}. " +
            "Use ToolSchemaExtensions.WithWriteGovernanceMetadata().");
    }

    private static void ValidateAuthenticationContract(ToolDefinition definition) {
        ToolAuthenticationContract? contract = definition.Authentication;
        if (contract is null || !contract.IsAuthenticationAware) {
            return;
        }

        contract.Validate();
        if (string.IsNullOrWhiteSpace(contract.AuthenticationContractId)) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' is authentication-aware and must declare AuthenticationContractId.");
        }

        IReadOnlyList<string> expectedArgumentNames = contract.GetSchemaArgumentNames();
        if (expectedArgumentNames.Count == 0) {
            return;
        }

        JsonObject? properties = definition.Parameters?.GetObject("properties");
        if (properties is null) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' is authentication-aware and must define an object schema with properties.");
        }

        List<string> missingArguments = new();
        for (var i = 0; i < expectedArgumentNames.Count; i++) {
            string argumentName = expectedArgumentNames[i];
            if (string.IsNullOrWhiteSpace(argumentName)) {
                continue;
            }

            if (properties.GetObject(argumentName) is null) {
                missingArguments.Add(argumentName.Trim());
            }
        }

        if (missingArguments.Count == 0) {
            return;
        }

        throw new InvalidOperationException(
            $"Tool '{definition.Name}' is authentication-aware and must expose authentication argument(s) in schema properties: {string.Join(", ", missingArguments)}.");
    }

    private void ValidateRoutingContract(ToolDefinition definition) {
        ToolRoutingContract? contract = definition.Routing;
        if (contract is null) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' must declare a routing contract.");
        }

        if (!contract.IsRoutingAware) {
            throw new InvalidOperationException(
                $"Tool '{definition.Name}' cannot opt out of routing metadata (IsRoutingAware=false).");
        }

        contract.Validate();
        ValidateDomainIntentActionCatalogConsistency(definition, contract);
    }

    private void ValidateDomainIntentActionCatalogConsistency(ToolDefinition definition, ToolRoutingContract contract) {
        var family = (contract.DomainIntentFamily ?? string.Empty).Trim();
        var actionId = (contract.DomainIntentActionId ?? string.Empty).Trim();
        if (family.Length == 0 || actionId.Length == 0) {
            return;
        }

        foreach (var existingDefinition in _definitions.Values) {
            if (existingDefinition is null) {
                continue;
            }

            var existingContract = existingDefinition.Routing;
            if (existingContract is null || !existingContract.IsRoutingAware) {
                continue;
            }

            var existingFamily = (existingContract.DomainIntentFamily ?? string.Empty).Trim();
            if (!string.Equals(existingFamily, family, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var existingActionId = (existingContract.DomainIntentActionId ?? string.Empty).Trim();
            if (existingActionId.Length == 0
                || string.Equals(existingActionId, actionId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            throw new InvalidOperationException(
                $"Tool '{definition.Name}' declares DomainIntentActionId '{actionId}' for family '{family}', " +
                $"but existing tool '{existingDefinition.Name}' already declares '{existingActionId}'.");
        }
    }

    private Task<string> ExecuteWriteOperationWithReplayAsync(string operationReplayKey, Func<Task<string>> executeAsync) {
        if (string.IsNullOrWhiteSpace(operationReplayKey)) {
            throw new ArgumentException("Operation replay key cannot be empty.", nameof(operationReplayKey));
        }
        if (executeAsync is null) {
            throw new ArgumentNullException(nameof(executeAsync));
        }

        Task<string> task;
        lock (_writeOperationReplaySync) {
            if (_writeOperationReplayOutputs.TryGetValue(operationReplayKey, out var replayedOutput)) {
                return Task.FromResult(replayedOutput);
            }

            if (_writeOperationReplayInFlight.TryGetValue(operationReplayKey, out var existingInFlightTask)) {
                return existingInFlightTask;
            }

            task = ExecuteWriteOperationCoreAsync(operationReplayKey, executeAsync);
            _writeOperationReplayInFlight[operationReplayKey] = task;
        }

        return task;
    }

    private async Task<string> ExecuteWriteOperationCoreAsync(string operationReplayKey, Func<Task<string>> executeAsync) {
        try {
            var output = await executeAsync().ConfigureAwait(false);
            lock (_writeOperationReplaySync) {
                _writeOperationReplayOutputs[operationReplayKey] = output ?? string.Empty;
                _writeOperationReplayOrder.Enqueue(operationReplayKey);
                while (_writeOperationReplayOutputs.Count > MaxWriteOperationReplayEntries && _writeOperationReplayOrder.Count > 0) {
                    var oldestKey = _writeOperationReplayOrder.Dequeue();
                    _writeOperationReplayOutputs.Remove(oldestKey);
                }
            }

            return output ?? string.Empty;
        } finally {
            lock (_writeOperationReplaySync) {
                _writeOperationReplayInFlight.Remove(operationReplayKey);
            }
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
                if (string.IsNullOrWhiteSpace(request.OperationId)) {
                    return CreateDeniedGovernanceOutput(
                        request,
                        errorCode: ToolWriteGovernanceErrorCodes.WriteOperationIdRequired,
                        error: $"Tool '{_definition.Name}' requires idempotency metadata via '{ToolWriteGovernanceArgumentNames.OperationId}'.",
                        defaultError: $"Tool '{_definition.Name}' requires idempotency metadata via '{ToolWriteGovernanceArgumentNames.OperationId}'.",
                        hints: new[] {
                            $"Provide {ToolWriteGovernanceArgumentNames.OperationId} with a stable per-operation value.",
                            "Reuse the same operation id to safely retry without duplicating side effects."
                        },
                        missingRequirements: new[] { ToolWriteGovernanceArgumentNames.OperationId });
                }

                var operationReplayKey = BuildWriteOperationReplayKey(request.CanonicalToolName, request.OperationId);
                var canBypassWriteGovernanceInYolo =
                    _owner.WriteGovernanceMode == ToolWriteGovernanceMode.Yolo
                    && !_owner.RequireWriteGovernanceRuntime
                    && !_owner.RequireWriteAuditSinkForWriteOperations;
                if (canBypassWriteGovernanceInYolo) {
                    return await _owner.ExecuteWriteOperationWithReplayAsync(
                            operationReplayKey,
                            () => _inner.InvokeAsync(arguments, cancellationToken))
                        .ConfigureAwait(false);
                }

                if (contract.RequireExplicitConfirmation && !contract.HasExplicitConfirmation(arguments)) {
                    return CreateDeniedGovernanceOutput(
                        request,
                        errorCode: ToolWriteGovernanceErrorCodes.WriteConfirmationRequired,
                        error: $"Tool '{_definition.Name}' requires explicit write confirmation via '{contract.ConfirmationArgumentName}=true'.",
                        defaultError: $"Tool '{_definition.Name}' requires explicit write confirmation via '{contract.ConfirmationArgumentName}=true'.",
                        hints: new[] {
                            $"Set {contract.ConfirmationArgumentName}=true to confirm write intent.",
                            "Provide governance metadata for immutable audit and rollback tracking."
                        },
                        missingRequirements: new[] { contract.ConfirmationArgumentName });
                }

                if (contract.RequiresGovernanceAuthorization) {
                    if (_owner.RequireWriteAuditSinkForWriteOperations && _owner.WriteAuditSink is null) {
                        return CreateDeniedGovernanceOutput(
                            request,
                            errorCode: ToolWriteGovernanceErrorCodes.WriteAuditSinkRequired,
                            error: $"Tool '{_definition.Name}' requires a configured write audit sink for write operations.",
                            defaultError: $"Tool '{_definition.Name}' requires a configured write audit sink for write operations.",
                            hints: new[] {
                                "Configure ToolRegistry.WriteAuditSink.",
                                $"Required contract: {contract.GovernanceContractId}."
                            },
                            missingRequirements: new[] { "write_audit_sink" },
                            appendAuditRecord: false);
                    }

                    if (_owner.WriteGovernanceRuntime is null) {
                        if (_owner.RequireWriteGovernanceRuntime) {
                            return CreateDeniedGovernanceOutput(
                                request,
                                errorCode: ToolWriteGovernanceErrorCodes.WriteGovernanceRuntimeRequired,
                                error: $"Tool '{_definition.Name}' requires a configured write governance runtime.",
                                defaultError: $"Tool '{_definition.Name}' requires a configured write governance runtime.",
                                hints: new[] {
                                    "Configure ToolRegistry.WriteGovernanceRuntime.",
                                    $"Required contract: {contract.GovernanceContractId}."
                                },
                                missingRequirements: new[] { "write_governance_runtime" });
                        }
                    } else {
                        ToolWriteGovernanceResult authorization = _owner.WriteGovernanceRuntime.Authorize(request);
                        ToolWriteGovernanceResult normalizedAuthorization = NormalizeDeniedAuthorizationResult(authorization);
                        string? appendFailureOutput = TryCreateAuditAppendFailureOutput(request, normalizedAuthorization);
                        if (appendFailureOutput is not null) {
                            return appendFailureOutput;
                        }

                        if (!normalizedAuthorization.IsAuthorized) {
                            return CreateGovernanceErrorOutput(
                                normalizedAuthorization,
                                ToolWriteGovernanceErrorCodes.WriteGovernanceDenied,
                                $"Write authorization denied for tool '{_definition.Name}'.");
                        }
                    }
                }

                return await _owner.ExecuteWriteOperationWithReplayAsync(
                        operationReplayKey,
                        () => _inner.InvokeAsync(arguments, cancellationToken))
                    .ConfigureAwait(false);
            }

            return await _inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        private string CreateDeniedGovernanceOutput(
            ToolWriteGovernanceRequest request,
            string errorCode,
            string error,
            string defaultError,
            IReadOnlyList<string>? hints = null,
            IReadOnlyList<string>? missingRequirements = null,
            bool appendAuditRecord = true,
            bool isTransient = false) {
            ToolWriteGovernanceResult denied = CreateDeniedResult(
                request: request,
                errorCode: errorCode,
                error: error,
                hints: hints,
                missingRequirements: missingRequirements,
                isTransient: isTransient);

            if (appendAuditRecord) {
                string? appendFailureOutput = TryCreateAuditAppendFailureOutput(request, denied);
                if (appendFailureOutput is not null) {
                    return appendFailureOutput;
                }
            }

            return CreateGovernanceErrorOutput(denied, errorCode, defaultError);
        }

        private ToolWriteGovernanceResult CreateDeniedResult(
            ToolWriteGovernanceRequest request,
            string errorCode,
            string error,
            IReadOnlyList<string>? hints = null,
            IReadOnlyList<string>? missingRequirements = null,
            bool isTransient = false) {
            return new ToolWriteGovernanceResult {
                IsAuthorized = false,
                ErrorCode = errorCode ?? string.Empty,
                Error = error ?? string.Empty,
                Hints = hints ?? Array.Empty<string>(),
                MissingRequirements = missingRequirements ?? Array.Empty<string>(),
                IsTransient = isTransient,
                OperationId = request.OperationId,
                ExecutionId = request.ExecutionId,
                AuditCorrelationId = request.AuditCorrelationId
            };
        }

        private string? TryCreateAuditAppendFailureOutput(
            ToolWriteGovernanceRequest request,
            ToolWriteGovernanceResult authorization) {
            ToolWriteGovernanceResult? appendFailure = AppendWriteAuditRecord(request, authorization);
            if (appendFailure is null) {
                return null;
            }

            return CreateGovernanceErrorOutput(
                appendFailure,
                ToolWriteGovernanceErrorCodes.WriteAuditAppendFailed,
                $"Write governance audit append failed for tool '{_definition.Name}'.");
        }

        private ToolWriteGovernanceRequest CreateGovernanceRequest(
            JsonObject? arguments,
            ToolWriteGovernanceContract contract) {
            string operationIdArgumentName = ToolWriteGovernanceArgumentNames.OperationId;
            string executionIdArgumentName = ToolWriteGovernanceArgumentNames.ExecutionId;
            string actorIdArgumentName = ToolWriteGovernanceArgumentNames.ActorId;
            string changeReasonArgumentName = ToolWriteGovernanceArgumentNames.ChangeReason;
            string rollbackPlanIdArgumentName = ToolWriteGovernanceArgumentNames.RollbackPlanId;
            string rollbackProviderIdArgumentName = ToolWriteGovernanceArgumentNames.RollbackProviderId;
            string auditCorrelationIdArgumentName = ToolWriteGovernanceArgumentNames.AuditCorrelationId;

            if (_owner.WriteGovernanceRuntime is ToolWriteGovernanceStrictRuntime strictRuntime) {
                operationIdArgumentName = NormalizeArgumentName(
                    strictRuntime.OperationIdArgumentName,
                    ToolWriteGovernanceArgumentNames.OperationId);
                executionIdArgumentName = NormalizeArgumentName(
                    strictRuntime.ExecutionIdArgumentName,
                    ToolWriteGovernanceArgumentNames.ExecutionId);
                actorIdArgumentName = NormalizeArgumentName(
                    strictRuntime.ActorIdArgumentName,
                    ToolWriteGovernanceArgumentNames.ActorId);
                changeReasonArgumentName = NormalizeArgumentName(
                    strictRuntime.ChangeReasonArgumentName,
                    ToolWriteGovernanceArgumentNames.ChangeReason);
                rollbackPlanIdArgumentName = NormalizeArgumentName(
                    strictRuntime.RollbackPlanIdArgumentName,
                    ToolWriteGovernanceArgumentNames.RollbackPlanId);
                rollbackProviderIdArgumentName = NormalizeArgumentName(
                    strictRuntime.RollbackProviderIdArgumentName,
                    ToolWriteGovernanceArgumentNames.RollbackProviderId);
                auditCorrelationIdArgumentName = NormalizeArgumentName(
                    strictRuntime.AuditCorrelationIdArgumentName,
                    ToolWriteGovernanceArgumentNames.AuditCorrelationId);
            }

            string operationId = ReadArgumentWithFallback(
                arguments,
                ToolWriteGovernanceArgumentNames.OperationId,
                operationIdArgumentName);
            string executionId = ReadArgumentWithFallback(
                arguments,
                ToolWriteGovernanceArgumentNames.ExecutionId,
                executionIdArgumentName);
            string auditCorrelationId = ReadArgumentWithFallback(
                arguments,
                ToolWriteGovernanceArgumentNames.AuditCorrelationId,
                auditCorrelationIdArgumentName);
            if (string.IsNullOrWhiteSpace(auditCorrelationId)) {
                auditCorrelationId = string.IsNullOrWhiteSpace(operationId) ? executionId : operationId;
            }

            return new ToolWriteGovernanceRequest {
                ToolName = _definition.Name,
                CanonicalToolName = _definition.CanonicalName,
                GovernanceContractId = contract.GovernanceContractId,
                Arguments = arguments,
                ConfirmationArgumentName = contract.ConfirmationArgumentName,
                OperationId = operationId,
                ExecutionId = executionId,
                ActorId = ReadArgumentWithFallback(
                    arguments,
                    ToolWriteGovernanceArgumentNames.ActorId,
                    actorIdArgumentName),
                ChangeReason = ReadArgumentWithFallback(
                    arguments,
                    ToolWriteGovernanceArgumentNames.ChangeReason,
                    changeReasonArgumentName),
                RollbackPlanId = ReadArgumentWithFallback(
                    arguments,
                    ToolWriteGovernanceArgumentNames.RollbackPlanId,
                    rollbackPlanIdArgumentName),
                RollbackProviderId = ReadArgumentWithFallback(
                    arguments,
                    ToolWriteGovernanceArgumentNames.RollbackProviderId,
                    rollbackProviderIdArgumentName),
                AuditCorrelationId = auditCorrelationId
            };
        }

        private ToolWriteGovernanceResult? AppendWriteAuditRecord(
            ToolWriteGovernanceRequest request,
            ToolWriteGovernanceResult authorization) {
            IToolWriteAuditSink? sink = _owner.WriteAuditSink;
            if (sink is null) {
                return null;
            }

            string executionId = string.IsNullOrWhiteSpace(authorization.ExecutionId)
                ? request.ExecutionId
                : authorization.ExecutionId;
            string auditCorrelationId = string.IsNullOrWhiteSpace(authorization.AuditCorrelationId)
                ? request.AuditCorrelationId
                : authorization.AuditCorrelationId;
            if (string.IsNullOrWhiteSpace(auditCorrelationId)) {
                auditCorrelationId = string.IsNullOrWhiteSpace(request.OperationId)
                    ? executionId
                    : request.OperationId;
            }

            ToolWriteAuditRecord record = new() {
                TimestampUtc = DateTimeOffset.UtcNow,
                ToolName = _definition.Name,
                CanonicalToolName = _definition.CanonicalName,
                GovernanceContractId = request.GovernanceContractId,
                IsAuthorized = authorization.IsAuthorized,
                ErrorCode = authorization.ErrorCode,
                Error = authorization.Error,
                OperationId = request.OperationId,
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

            try {
                sink.Append(record);
                return null;
            } catch (Exception ex) {
                return new ToolWriteGovernanceResult {
                    IsAuthorized = false,
                    ErrorCode = ToolWriteGovernanceErrorCodes.WriteAuditAppendFailed,
                    Error = $"Write governance audit append failed for tool '{_definition.Name}'. {ex.Message}",
                    Hints = new[] {
                        "Ensure ToolRegistry.WriteAuditSink is available and append-only.",
                        "Retry when audit sink health is restored."
                    },
                    IsTransient = true,
                    OperationId = request.OperationId,
                    ExecutionId = executionId,
                    AuditCorrelationId = auditCorrelationId,
                    ImmutableAuditProviderId = authorization.ImmutableAuditProviderId,
                    RollbackProviderId = string.IsNullOrWhiteSpace(authorization.RollbackProviderId)
                        ? request.RollbackProviderId
                        : authorization.RollbackProviderId
                };
            }
        }

        private static string ReadArgument(JsonObject? arguments, string argumentName) {
            if (arguments is null || string.IsNullOrWhiteSpace(argumentName)) {
                return string.Empty;
            }

            string? value = arguments.GetString(argumentName);
            return value?.Trim() ?? string.Empty;
        }

        private static string ReadArgumentWithFallback(
            JsonObject? arguments,
            string primaryArgumentName,
            string fallbackArgumentName) {
            string primaryValue = ReadArgument(arguments, primaryArgumentName);
            if (!string.IsNullOrWhiteSpace(primaryValue)) {
                return primaryValue;
            }

            if (string.Equals(primaryArgumentName, fallbackArgumentName, StringComparison.Ordinal)) {
                return primaryValue;
            }

            return ReadArgument(arguments, fallbackArgumentName);
        }

        private static string NormalizeArgumentName(string candidate, string fallback) {
            if (string.IsNullOrWhiteSpace(candidate)) {
                return fallback;
            }

            return candidate.Trim();
        }

        private static string BuildWriteOperationReplayKey(string canonicalToolName, string operationId) {
            var canonical = (canonicalToolName ?? string.Empty).Trim();
            var opId = (operationId ?? string.Empty).Trim();
            return canonical + "|" + opId;
        }

        private ToolWriteGovernanceResult NormalizeDeniedAuthorizationResult(ToolWriteGovernanceResult authorization) {
            if (authorization.IsAuthorized) {
                return authorization;
            }

            if (!string.IsNullOrWhiteSpace(authorization.ErrorCode) && !string.IsNullOrWhiteSpace(authorization.Error)) {
                return authorization;
            }

            return new ToolWriteGovernanceResult {
                IsAuthorized = false,
                ErrorCode = string.IsNullOrWhiteSpace(authorization.ErrorCode)
                    ? ToolWriteGovernanceErrorCodes.WriteGovernanceDenied
                    : authorization.ErrorCode,
                Error = string.IsNullOrWhiteSpace(authorization.Error)
                    ? $"Write authorization denied for tool '{_definition.Name}'."
                    : authorization.Error,
                MissingRequirements = authorization.MissingRequirements,
                Hints = authorization.Hints,
                IsTransient = authorization.IsTransient,
                OperationId = authorization.OperationId,
                ExecutionId = authorization.ExecutionId,
                AuditCorrelationId = authorization.AuditCorrelationId,
                ImmutableAuditProviderId = authorization.ImmutableAuditProviderId,
                RollbackProviderId = authorization.RollbackProviderId
            };
        }

        private static string CreateGovernanceErrorOutput(
            ToolWriteGovernanceResult result,
            string defaultErrorCode,
            string defaultError) {
            string errorCode = string.IsNullOrWhiteSpace(result.ErrorCode)
                ? defaultErrorCode
                : result.ErrorCode;
            string error = string.IsNullOrWhiteSpace(result.Error)
                ? defaultError
                : result.Error;
            return ToolOutputEnvelope.Error(
                errorCode: errorCode,
                error: error,
                hints: result.Hints,
                isTransient: result.IsTransient);
        }
    }
}
