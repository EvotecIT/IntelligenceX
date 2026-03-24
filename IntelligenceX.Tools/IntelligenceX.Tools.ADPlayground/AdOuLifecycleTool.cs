using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Performs governed Active Directory organizational-unit lifecycle actions (dry-run by default).
/// </summary>
public sealed class AdOuLifecycleTool : ActiveDirectoryToolBase, ITool {
    private const int GpOptionsBlockInheritanceFlag = 1;

    private sealed record AttributeMutation(string Key, string Value);

    private sealed record OuLifecycleRequest(
        string Operation,
        string? Identity,
        string? Name,
        string? ParentDistinguishedName,
        string? TargetParentDistinguishedName,
        string? DomainName,
        string? NewName,
        string? Description,
        string? DisplayName,
        string? ManagedBy,
        bool? ProtectFromAccidentalDeletion,
        bool? BlockInheritance,
        bool Recursive,
        bool Apply,
        IReadOnlyList<string> ClearAttributes,
        IReadOnlyList<AttributeMutation> AdditionalAttributes);

    private sealed record OuLifecycleResult(
        string Operation,
        string ObjectType,
        string Identity,
        string DistinguishedName,
        string DomainName,
        bool Changed,
        bool Apply,
        string Message,
        string? Name,
        string? ParentDistinguishedName,
        string? TargetParentDistinguishedName,
        string? NewName,
        string? Description,
        string? DisplayName,
        string? ManagedBy,
        bool? ProtectFromAccidentalDeletion,
        bool? BlockInheritance,
        bool Recursive,
        IReadOnlyList<string> UpdatedAttributes,
        IReadOnlyList<string> ClearedAttributes,
        IReadOnlyList<AttributeMutation> AdditionalAttributes,
        DateTime TimestampUtc);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ou_lifecycle",
        "Governed Active Directory organizational unit lifecycle actions for create/update/move/delete and protection changes. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("operation", ToolSchema.String("Lifecycle action to perform.").Enum("create", "update", "move", "delete")),
                ("identity", ToolSchema.String("Existing OU identity for update/move/delete operations (DN, ou, or name).")),
                ("name", ToolSchema.String("OU name for create operations.")),
                ("parent_distinguished_name", ToolSchema.String("Parent DN where the new OU should be created.")),
                ("target_parent_distinguished_name", ToolSchema.String("Target parent DN for move operations. Use the current parent with new_name to perform a rename.")),
                ("domain_name", ToolSchema.String("Optional domain DNS name for update/move/delete operations.")),
                ("new_name", ToolSchema.String("Optional new OU name for move/rename operations.")),
                ("description", ToolSchema.String("Optional description for create or update operations.")),
                ("display_name", ToolSchema.String("Optional displayName for create or update operations.")),
                ("managed_by", ToolSchema.String("Optional managedBy distinguished name for create or update operations.")),
                ("protect_from_accidental_deletion", ToolSchema.Boolean("Optional protectedFromAccidentalDeletion value for create or update operations.")),
                ("block_inheritance", ToolSchema.Boolean("Optional block-inheritance toggle for create or update operations (maps to gPOptions bit 1).")),
                ("recursive", ToolSchema.Boolean("When true, delete removes child objects recursively. Only valid for delete.")),
                ("clear_attributes", ToolSchema.Array(
                    ToolSchema.String("LDAP attribute name to clear."),
                    "Optional LDAP attributes to clear during update operations.")),
                ("additional_attributes", ToolSchema.Array(
                    ToolSchema.Object(
                            ("key", ToolSchema.String("LDAP attribute name.")),
                            ("value", ToolSchema.String("LDAP attribute value.")))
                        .Required("key", "value")
                        .NoAdditionalProperties(),
                    "Optional additional LDAP attributes to set during create or update operations.")),
                ("apply", ToolSchema.Boolean("When true, performs the lifecycle write. Otherwise returns a dry-run preview.")))
            .Required("operation")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="AdOuLifecycleTool"/> class.
    /// </summary>
    public AdOuLifecycleTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<OuLifecycleRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("operation", out var operation, out var operationError)) {
                return ToolRequestBindingResult<OuLifecycleRequest>.Failure(operationError);
            }

            if (!TryNormalizeOperation(operation, out var normalizedOperation)) {
                return ToolRequestBindingResult<OuLifecycleRequest>.Failure(
                    "operation must be one of create, update, move, or delete.");
            }

            var request = new OuLifecycleRequest(
                Operation: normalizedOperation,
                Identity: reader.OptionalString("identity"),
                Name: reader.OptionalString("name"),
                ParentDistinguishedName: reader.OptionalString("parent_distinguished_name"),
                TargetParentDistinguishedName: reader.OptionalString("target_parent_distinguished_name"),
                DomainName: reader.OptionalString("domain_name"),
                NewName: reader.OptionalString("new_name"),
                Description: reader.OptionalString("description"),
                DisplayName: reader.OptionalString("display_name"),
                ManagedBy: reader.OptionalString("managed_by"),
                ProtectFromAccidentalDeletion: reader.OptionalBoolean("protect_from_accidental_deletion"),
                BlockInheritance: reader.OptionalBoolean("block_inheritance"),
                Recursive: reader.Boolean("recursive"),
                Apply: reader.Boolean("apply"),
                ClearAttributes: ReadTrimmedStrings(reader.Array("clear_attributes")),
                AdditionalAttributes: ReadAttributeMutations(reader.Array("additional_attributes")));

            return ValidateRequest(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<OuLifecycleRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        if (!request.Apply) {
            return Task.FromResult(CreateSuccessResponse(CreateDryRunResult(request)));
        }

        try {
            var result = request.Operation switch {
                "create" => ExecuteCreate(request),
                "update" => ExecuteUpdate(request),
                "move" => MapMutationResult(
                    new DirectoryOrganizationalUnitHelper().MoveOrganizationalUnit(
                        request.Identity!,
                        request.TargetParentDistinguishedName!,
                        request.NewName,
                        request.DomainName),
                    request),
                "delete" => MapMutationResult(
                    new DirectoryOrganizationalUnitHelper().DeleteOrganizationalUnit(
                        request.Identity!,
                        request.DomainName,
                        request.Recursive),
                    request),
                _ => throw new InvalidOperationException($"Unsupported operation '{request.Operation}'.")
            };

            return Task.FromResult(CreateSuccessResponse(result));
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResultV2.Error("invalid_argument", ex.Message));
        } catch (NotSupportedException ex) {
            return Task.FromResult(ToolResultV2.Error("not_supported", ex.Message));
        } catch (Exception ex) when (IsDirectoryWriteFailure(ex)) {
            return Task.FromResult(ToolResultV2.Error("directory_write_failed", ex.Message));
        } catch (Exception ex) {
            return Task.FromResult(ToolResultV2.Error("execution_failed", ex.Message));
        }
    }

    private static ToolRequestBindingResult<OuLifecycleRequest> ValidateRequest(OuLifecycleRequest request) {
        switch (request.Operation) {
            case "create":
                if (string.IsNullOrWhiteSpace(request.Name)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("name is required for create.");
                }

                if (string.IsNullOrWhiteSpace(request.ParentDistinguishedName)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("parent_distinguished_name is required for create.");
                }

                if (!string.IsNullOrWhiteSpace(request.Identity)
                    || !string.IsNullOrWhiteSpace(request.TargetParentDistinguishedName)
                    || !string.IsNullOrWhiteSpace(request.NewName)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("create does not support identity, target_parent_distinguished_name, or new_name.");
                }

                if (request.Recursive) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("recursive is only supported for delete.");
                }

                if (request.ClearAttributes.Count > 0) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("clear_attributes is only supported for update.");
                }

                return ToolRequestBindingResult<OuLifecycleRequest>.Success(request);
            case "update":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("identity is required for update.");
                }

                if (!string.IsNullOrWhiteSpace(request.Name)
                    || !string.IsNullOrWhiteSpace(request.ParentDistinguishedName)
                    || !string.IsNullOrWhiteSpace(request.TargetParentDistinguishedName)
                    || !string.IsNullOrWhiteSpace(request.NewName)
                    || request.Recursive) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("update does not support create, move, or delete-only fields.");
                }

                if (!HasUpdatePayload(request)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure(
                        "update requires at least one typed attribute, protection toggle, block-inheritance toggle, clear_attributes entry, or additional_attributes entry.");
                }

                return ToolRequestBindingResult<OuLifecycleRequest>.Success(request);
            case "move":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("identity is required for move.");
                }

                if (string.IsNullOrWhiteSpace(request.TargetParentDistinguishedName)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("target_parent_distinguished_name is required for move.");
                }

                if (!string.IsNullOrWhiteSpace(request.Name)
                    || !string.IsNullOrWhiteSpace(request.ParentDistinguishedName)
                    || HasUpdatePayload(request)
                    || request.Recursive) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("move only supports identity, target_parent_distinguished_name, optional new_name, domain_name, and apply.");
                }

                return ToolRequestBindingResult<OuLifecycleRequest>.Success(request);
            case "delete":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("identity is required for delete.");
                }

                if (!string.IsNullOrWhiteSpace(request.Name)
                    || !string.IsNullOrWhiteSpace(request.ParentDistinguishedName)
                    || !string.IsNullOrWhiteSpace(request.TargetParentDistinguishedName)
                    || !string.IsNullOrWhiteSpace(request.NewName)
                    || HasUpdatePayload(request)) {
                    return ToolRequestBindingResult<OuLifecycleRequest>.Failure("delete only supports identity, domain_name, recursive, and apply.");
                }

                return ToolRequestBindingResult<OuLifecycleRequest>.Success(request);
            default:
                return ToolRequestBindingResult<OuLifecycleRequest>.Failure(
                    "operation must be one of create, update, move, or delete.");
        }
    }

    private static bool TryNormalizeOperation(string value, out string operation) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "create":
            case "update":
            case "move":
            case "delete":
                operation = normalized;
                return true;
            default:
                operation = string.Empty;
                return false;
        }
    }

    private static IReadOnlyList<string> ReadTrimmedStrings(JsonArray? array) {
        if (array is null || array.Count == 0) {
            return Array.Empty<string>();
        }

        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < array.Count; i++) {
            if (array[i].Kind != JsonValueKind.String) {
                continue;
            }

            var value = array[i].AsString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) {
                items.Add(value);
            }
        }

        return items.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<AttributeMutation> ReadAttributeMutations(JsonArray? array) {
        if (array is null || array.Count == 0) {
            return Array.Empty<AttributeMutation>();
        }

        var items = new List<AttributeMutation>(array.Count);
        for (var i = 0; i < array.Count; i++) {
            if (array[i].Kind != JsonValueKind.Object) {
                continue;
            }

            var obj = array[i].AsObject();
            var key = ToolArgs.GetOptionalTrimmed(obj, "key");
            var value = ToolArgs.GetOptionalTrimmed(obj, "value");
            if (string.IsNullOrWhiteSpace(key) || value is null) {
                continue;
            }

            items.Add(new AttributeMutation(key, value));
        }

        return items;
    }

    private static bool HasUpdatePayload(OuLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.Description)
               || !string.IsNullOrWhiteSpace(request.DisplayName)
               || !string.IsNullOrWhiteSpace(request.ManagedBy)
               || request.ProtectFromAccidentalDeletion.HasValue
               || request.BlockInheritance.HasValue
               || request.ClearAttributes.Count > 0
               || request.AdditionalAttributes.Count > 0;
    }

    private OuLifecycleResult ExecuteCreate(OuLifecycleRequest request) {
        var helper = new DirectoryOrganizationalUnitHelper();
        var objectHelper = new DirectoryObjectHelper();
        var mutation = helper.CreateOrganizationalUnit(
            request.Name!,
            request.ParentDistinguishedName!,
            request.Description);
        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(mutation.Message)) {
            messages.Add(mutation.Message);
        }

        var updatedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ou"
        };
        AddIfPresent(updatedAttributes, "description", request.Description);

        var distinguishedName = mutation.DistinguishedName ?? BuildPredictedDistinguishedName(request.Name!, request.ParentDistinguishedName!);
        var domainName = mutation.DomainName ?? InferDomainNameFromDistinguishedName(distinguishedName);
        var completedSteps = mutation.Changed ? 1 : 0;
        var changed = mutation.Changed;

        var update = BuildCreateSupplementalUpdate(request);
        if (update is not null) {
            try {
                var setResult = objectHelper.SetOrganizationalUnit(distinguishedName, update, domainName);
                changed |= setResult.Changed;
                completedSteps += setResult.Changed ? 1 : 0;
                MergeMutation(setResult, messages, updatedAttributes, distinguishedName, domainName, out distinguishedName, out domainName);
            } catch (Exception ex) {
                throw new InvalidOperationException(
                    $"Create workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                    ex);
            }
        }

        return new OuLifecycleResult(
            Operation: "create",
            ObjectType: "organizational_unit",
            Identity: distinguishedName,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: changed,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            Name: request.Name,
            ParentDistinguishedName: request.ParentDistinguishedName,
            TargetParentDistinguishedName: null,
            NewName: null,
            Description: request.Description,
            DisplayName: request.DisplayName,
            ManagedBy: request.ManagedBy,
            ProtectFromAccidentalDeletion: request.ProtectFromAccidentalDeletion,
            BlockInheritance: request.BlockInheritance,
            Recursive: false,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: Array.Empty<string>(),
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private OuLifecycleResult ExecuteUpdate(OuLifecycleRequest request) {
        var objectHelper = new DirectoryObjectHelper();
        var currentGpOptions = request.BlockInheritance.HasValue
            ? ReadCurrentGpOptions(objectHelper, request.Identity!, request.DomainName)
            : 0;
        var update = BuildUpdate(request, currentGpOptions);
        var mutation = objectHelper.SetOrganizationalUnit(request.Identity!, update, request.DomainName);

        return new OuLifecycleResult(
            Operation: "update",
            ObjectType: "organizational_unit",
            Identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: mutation.Changed,
            Apply: true,
            Message: mutation.Message ?? string.Empty,
            Name: request.Name,
            ParentDistinguishedName: request.ParentDistinguishedName,
            TargetParentDistinguishedName: null,
            NewName: null,
            Description: request.Description,
            DisplayName: request.DisplayName,
            ManagedBy: request.ManagedBy,
            ProtectFromAccidentalDeletion: request.ProtectFromAccidentalDeletion,
            BlockInheritance: request.BlockInheritance,
            Recursive: false,
            UpdatedAttributes: mutation.UpdatedAttributes ?? Array.Empty<string>(),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private static DirectoryObjectUpdate? BuildCreateSupplementalUpdate(OuLifecycleRequest request) {
        var update = new DirectoryObjectUpdate {
            DisplayName = request.DisplayName,
            ManagedBy = request.ManagedBy
        };

        if (request.ProtectFromAccidentalDeletion.HasValue) {
            update.CustomAttributes["protectedFromAccidentalDeletion"] = request.ProtectFromAccidentalDeletion.Value;
        }

        if (request.BlockInheritance.HasValue) {
            update.CustomAttributes["gPOptions"] = request.BlockInheritance.Value ? GpOptionsBlockInheritanceFlag : 0;
        }

        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            update.CustomAttributes[entry.Key] = entry.Value;
        }

        return update.HasChanges() ? update : null;
    }

    private static DirectoryObjectUpdate BuildUpdate(OuLifecycleRequest request, int currentGpOptions) {
        var update = new DirectoryObjectUpdate {
            Description = request.Description,
            DisplayName = request.DisplayName,
            ManagedBy = request.ManagedBy
        };

        if (request.ProtectFromAccidentalDeletion.HasValue) {
            update.CustomAttributes["protectedFromAccidentalDeletion"] = request.ProtectFromAccidentalDeletion.Value;
        }

        if (request.BlockInheritance.HasValue) {
            update.CustomAttributes["gPOptions"] = request.BlockInheritance.Value
                ? currentGpOptions | GpOptionsBlockInheritanceFlag
                : currentGpOptions & ~GpOptionsBlockInheritanceFlag;
        }

        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            update.CustomAttributes[entry.Key] = entry.Value;
        }

        for (var i = 0; i < request.ClearAttributes.Count; i++) {
            update.ClearAttributes.Add(request.ClearAttributes[i]);
        }

        return update;
    }

    private static int ReadCurrentGpOptions(DirectoryObjectHelper objectHelper, string identity, string? domainName) {
        var snapshot = objectHelper.GetOrganizationalUnit(identity, domainName, new[] { "gPOptions" });
        if (snapshot.Attributes.TryGetValue("gPOptions", out var rawValue) && TryReadIntValue(rawValue, out var value)) {
            return value;
        }

        return 0;
    }

    private static bool TryReadIntValue(object? value, out int result) {
        switch (value) {
            case null:
                result = 0;
                return false;
            case int intValue:
                result = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                result = (int)longValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case string stringValue when int.TryParse(stringValue, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static OuLifecycleResult MapMutationResult(DirectoryMutationResult mutation, OuLifecycleRequest request) {
        ArgumentNullException.ThrowIfNull(mutation);

        return new OuLifecycleResult(
            Operation: mutation.Operation,
            ObjectType: "organizational_unit",
            Identity: string.IsNullOrWhiteSpace(mutation.Identity)
                ? request.Identity ?? request.Name ?? string.Empty
                : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: mutation.Changed,
            Apply: true,
            Message: mutation.Message ?? string.Empty,
            Name: request.Name,
            ParentDistinguishedName: request.ParentDistinguishedName,
            TargetParentDistinguishedName: request.TargetParentDistinguishedName,
            NewName: request.NewName,
            Description: request.Description,
            DisplayName: request.DisplayName,
            ManagedBy: request.ManagedBy,
            ProtectFromAccidentalDeletion: request.ProtectFromAccidentalDeletion,
            BlockInheritance: request.BlockInheritance,
            Recursive: request.Recursive,
            UpdatedAttributes: mutation.UpdatedAttributes ?? BuildPlannedUpdatedAttributes(request),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private static OuLifecycleResult CreateDryRunResult(OuLifecycleRequest request) {
        var distinguishedName = request.Operation switch {
            "create" when !string.IsNullOrWhiteSpace(request.Name) && !string.IsNullOrWhiteSpace(request.ParentDistinguishedName)
                => BuildPredictedDistinguishedName(request.Name!, request.ParentDistinguishedName!),
            "move" when !string.IsNullOrWhiteSpace(request.TargetParentDistinguishedName)
                => BuildPredictedDistinguishedName(
                    !string.IsNullOrWhiteSpace(request.NewName) ? request.NewName! : ExtractOuLeafName(request.Identity),
                    request.TargetParentDistinguishedName!),
            _ => string.Empty
        };
        var domainName = !string.IsNullOrWhiteSpace(request.DomainName)
            ? request.DomainName!
            : InferDomainNameFromDistinguishedName(distinguishedName);

        return new OuLifecycleResult(
            Operation: request.Operation,
            ObjectType: "organizational_unit",
            Identity: request.Identity ?? request.Name ?? string.Empty,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: false,
            Apply: false,
            Message: "Dry-run only. Set apply=true to execute the lifecycle action.",
            Name: request.Name,
            ParentDistinguishedName: request.ParentDistinguishedName,
            TargetParentDistinguishedName: request.TargetParentDistinguishedName,
            NewName: request.NewName,
            Description: request.Description,
            DisplayName: request.DisplayName,
            ManagedBy: request.ManagedBy,
            ProtectFromAccidentalDeletion: request.ProtectFromAccidentalDeletion,
            BlockInheritance: request.BlockInheritance,
            Recursive: request.Recursive,
            UpdatedAttributes: BuildPlannedUpdatedAttributes(request),
            ClearedAttributes: request.ClearAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private static IReadOnlyList<string> BuildPlannedUpdatedAttributes(OuLifecycleRequest request) {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        switch (request.Operation) {
            case "create":
                attributes.Add("ou");
                AddIfPresent(attributes, "description", request.Description);
                AddIfPresent(attributes, "displayName", request.DisplayName);
                AddIfPresent(attributes, "managedBy", request.ManagedBy);
                if (request.ProtectFromAccidentalDeletion.HasValue) {
                    attributes.Add("protectedFromAccidentalDeletion");
                }

                if (request.BlockInheritance.HasValue) {
                    attributes.Add("gPOptions");
                }

                AddMutationKeys(attributes, request.AdditionalAttributes);
                break;
            case "update":
                AddIfPresent(attributes, "description", request.Description);
                AddIfPresent(attributes, "displayName", request.DisplayName);
                AddIfPresent(attributes, "managedBy", request.ManagedBy);
                if (request.ProtectFromAccidentalDeletion.HasValue) {
                    attributes.Add("protectedFromAccidentalDeletion");
                }

                if (request.BlockInheritance.HasValue) {
                    attributes.Add("gPOptions");
                }

                AddMutationKeys(attributes, request.AdditionalAttributes);
                break;
            case "move":
                attributes.Add("distinguishedName");
                if (!string.IsNullOrWhiteSpace(request.NewName)) {
                    attributes.Add("ou");
                    attributes.Add("name");
                }
                break;
        }

        return attributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void MergeMutation(
        DirectoryMutationResult mutation,
        List<string> messages,
        HashSet<string> updatedAttributes,
        string distinguishedName,
        string domainName,
        out string mergedDistinguishedName,
        out string mergedDomainName) {
        mergedDistinguishedName = distinguishedName;
        mergedDomainName = domainName;

        if (!string.IsNullOrWhiteSpace(mutation.Message)) {
            messages.Add(mutation.Message);
        }

        if (!string.IsNullOrWhiteSpace(mutation.DistinguishedName)) {
            mergedDistinguishedName = mutation.DistinguishedName;
        }

        if (!string.IsNullOrWhiteSpace(mutation.DomainName)) {
            mergedDomainName = mutation.DomainName;
        }

        foreach (var attribute in mutation.UpdatedAttributes ?? Array.Empty<string>()) {
            if (!string.IsNullOrWhiteSpace(attribute)) {
                updatedAttributes.Add(attribute);
            }
        }
    }

    private static void AddMutationKeys(HashSet<string> attributes, IReadOnlyList<AttributeMutation> entries) {
        for (var i = 0; i < entries.Count; i++) {
            var key = (entries[i].Key ?? string.Empty).Trim();
            if (key.Length > 0) {
                attributes.Add(key);
            }
        }
    }

    private static void AddIfPresent(HashSet<string> attributes, string key, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            attributes.Add(key);
        }
    }

    private static string BuildPredictedDistinguishedName(string name, string parentDistinguishedName) {
        return DistinguishedNameHelper.BuildChildDistinguishedName("OU", name, parentDistinguishedName);
    }

    private static string ExtractOuLeafName(string? identity) {
        if (string.IsNullOrWhiteSpace(identity)) {
            return "organizational-unit";
        }

        var trimmed = identity.Trim();
        if (!DistinguishedNameHelper.LooksLikeDistinguishedName(trimmed)) {
            return trimmed;
        }

        var lastRdnValue = DistinguishedNameHelper.GetLastRdnValue(trimmed);
        return string.IsNullOrWhiteSpace(lastRdnValue) ? trimmed : lastRdnValue;
    }

    private static string InferDomainNameFromDistinguishedName(string? distinguishedName) {
        return DistinguishedNameHelper.GetDomainCanonicalName(distinguishedName);
    }

    private static string CreateSuccessResponse(OuLifecycleResult result) {
        var facts = new List<(string Key, string Value)> {
            ("Operation", result.Operation),
            ("Identity", result.Identity),
            ("Changed", result.Changed ? "true" : "false")
        };
        if (!string.IsNullOrWhiteSpace(result.DomainName)) {
            facts.Add(("Domain", result.DomainName));
        }

        if (!string.IsNullOrWhiteSpace(result.DistinguishedName)) {
            facts.Add(("Distinguished name", result.DistinguishedName));
        }

        if (!string.IsNullOrWhiteSpace(result.ParentDistinguishedName)) {
            facts.Add(("Parent DN", result.ParentDistinguishedName));
        }

        if (!string.IsNullOrWhiteSpace(result.TargetParentDistinguishedName)) {
            facts.Add(("Target parent DN", result.TargetParentDistinguishedName));
        }

        if (!string.IsNullOrWhiteSpace(result.NewName)) {
            facts.Add(("New name", result.NewName));
        }

        if (!string.IsNullOrWhiteSpace(result.Message)) {
            facts.Add(("Message", result.Message));
        }

        if (result.ProtectFromAccidentalDeletion.HasValue) {
            facts.Add(("Protected from accidental deletion", result.ProtectFromAccidentalDeletion.Value ? "true" : "false"));
        }

        if (result.BlockInheritance.HasValue) {
            facts.Add(("Block inheritance", result.BlockInheritance.Value ? "true" : "false"));
        }

        if (result.Recursive) {
            facts.Add(("Recursive", "true"));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("operation", result.Operation)
            .Add("object_type", result.ObjectType)
            .Add("write_candidate", true);
        if (!string.IsNullOrWhiteSpace(result.DomainName)) {
            meta.Add("domain_name", result.DomainName);
        }

        if (result.Recursive) {
            meta.Add("recursive", true);
        }

        if (result.ProtectFromAccidentalDeletion.HasValue) {
            meta.Add("protect_from_accidental_deletion", result.ProtectFromAccidentalDeletion.Value);
        }

        if (result.BlockInheritance.HasValue) {
            meta.Add("block_inheritance", result.BlockInheritance.Value);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: $"ad_ou_{result.Operation}",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "AD OU lifecycle");
    }
}
