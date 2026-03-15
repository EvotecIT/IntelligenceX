using System;
using System.Collections.Generic;
using System.DirectoryServices;
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
/// Performs governed Active Directory group lifecycle actions (dry-run by default).
/// </summary>
public sealed class AdGroupLifecycleTool : ActiveDirectoryToolBase, ITool {
    private sealed record AttributeMutation(string Key, string Value);

    private sealed record GroupLifecycleRequest(
        string Operation,
        string? Identity,
        string? SamAccountName,
        string? OrganizationalUnit,
        string? DomainName,
        string? CommonName,
        string? DisplayName,
        string? Description,
        string? Mail,
        string? ManagedBy,
        string? Notes,
        string? Scope,
        bool? SecurityEnabled,
        bool Apply,
        IReadOnlyList<string> MembersToAdd,
        IReadOnlyList<string> MembersToRemove,
        IReadOnlyList<string> ClearAttributes,
        IReadOnlyList<AttributeMutation> AdditionalAttributes);

    private sealed record GroupLifecycleResult(
        string Operation,
        string ObjectType,
        string Identity,
        string DistinguishedName,
        string DomainName,
        bool Changed,
        bool Apply,
        string Message,
        string? OrganizationalUnit,
        string? SamAccountName,
        string? Scope,
        bool? SecurityEnabled,
        IReadOnlyList<string> UpdatedAttributes,
        IReadOnlyList<string> ClearedAttributes,
        IReadOnlyList<string> MembersAdded,
        IReadOnlyList<string> MembersRemoved,
        IReadOnlyList<AttributeMutation> AdditionalAttributes,
        DateTime TimestampUtc);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_group_lifecycle",
        "Governed Active Directory group lifecycle actions for create/update/delete plus member add/remove flows. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("operation", ToolSchema.String("Lifecycle action to perform.").Enum("create", "update", "delete")),
                ("identity", ToolSchema.String("Existing group identity for update/delete operations (DN, sAMAccountName, mail, or name).")),
                ("sam_account_name", ToolSchema.String("sAMAccountName for create operations.")),
                ("organizational_unit", ToolSchema.String("Target OU distinguished name for create operations.")),
                ("domain_name", ToolSchema.String("Optional domain DNS name for write operations.")),
                ("common_name", ToolSchema.String("Optional common name (CN) for create operations.")),
                ("display_name", ToolSchema.String("Optional displayName for create or update operations.")),
                ("description", ToolSchema.String("Optional description for create or update operations.")),
                ("mail", ToolSchema.String("Optional mail attribute for create or update operations.")),
                ("managed_by", ToolSchema.String("Optional managedBy distinguished name for create or update operations.")),
                ("notes", ToolSchema.String("Optional info/notes attribute for create or update operations.")),
                ("scope", ToolSchema.String("Optional group scope for create operations.").Enum("domain_local", "global", "universal")),
                ("security_enabled", ToolSchema.Boolean("Optional security-enabled flag for create operations.")),
                ("members_to_add", ToolSchema.Array(
                    ToolSchema.String("Identity to add as a group member."),
                    "Optional members to add during create or update workflows.")),
                ("members_to_remove", ToolSchema.Array(
                    ToolSchema.String("Identity to remove from the group."),
                    "Optional members to remove during update workflows.")),
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
    /// Initializes a new instance of the <see cref="AdGroupLifecycleTool"/> class.
    /// </summary>
    public AdGroupLifecycleTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<GroupLifecycleRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("operation", out var operation, out var operationError)) {
                return ToolRequestBindingResult<GroupLifecycleRequest>.Failure(operationError);
            }

            if (!TryNormalizeOperation(operation, out var normalizedOperation)) {
                return ToolRequestBindingResult<GroupLifecycleRequest>.Failure(
                    "operation must be one of create, update, or delete.");
            }

            var request = new GroupLifecycleRequest(
                Operation: normalizedOperation,
                Identity: reader.OptionalString("identity"),
                SamAccountName: reader.OptionalString("sam_account_name"),
                OrganizationalUnit: reader.OptionalString("organizational_unit"),
                DomainName: reader.OptionalString("domain_name"),
                CommonName: reader.OptionalString("common_name"),
                DisplayName: reader.OptionalString("display_name"),
                Description: reader.OptionalString("description"),
                Mail: reader.OptionalString("mail"),
                ManagedBy: reader.OptionalString("managed_by"),
                Notes: reader.OptionalString("notes"),
                Scope: reader.OptionalString("scope"),
                SecurityEnabled: TryReadNullableBoolean(arguments, "security_enabled"),
                Apply: reader.Boolean("apply"),
                MembersToAdd: ReadTrimmedStrings(arguments?.GetArray("members_to_add")),
                MembersToRemove: ReadTrimmedStrings(arguments?.GetArray("members_to_remove")),
                ClearAttributes: ReadTrimmedStrings(arguments?.GetArray("clear_attributes")),
                AdditionalAttributes: ReadAttributeMutations(arguments?.GetArray("additional_attributes")));

            return ValidateRequest(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GroupLifecycleRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        if (!request.Apply) {
            return Task.FromResult(CreateSuccessResponse(CreateDryRunResult(request)));
        }

        try {
            var result = request.Operation switch {
                "create" => ExecuteCreate(request),
                "update" => ExecuteUpdate(request),
                "delete" => MapMutationResult(new DirectoryAccountHelper().DeleteGroup(request.Identity!, request.DomainName), request),
                _ => throw new InvalidOperationException($"Unsupported operation '{request.Operation}'.")
            };

            return Task.FromResult(CreateSuccessResponse(result));
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResultV2.Error("invalid_argument", ex.Message));
        } catch (NotSupportedException ex) {
            return Task.FromResult(ToolResultV2.Error("not_supported", ex.Message));
        } catch (DirectoryServicesCOMException ex) {
            return Task.FromResult(ToolResultV2.Error("directory_write_failed", ex.Message));
        } catch (InvalidOperationException ex) {
            return Task.FromResult(ToolResultV2.Error("directory_write_failed", ex.Message));
        } catch (Exception ex) {
            return Task.FromResult(ToolResultV2.Error("execution_failed", ex.Message));
        }
    }

    private static ToolRequestBindingResult<GroupLifecycleRequest> ValidateRequest(GroupLifecycleRequest request) {
        switch (request.Operation) {
            case "create":
                if (string.IsNullOrWhiteSpace(request.SamAccountName)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("sam_account_name is required for create.");
                }

                if (string.IsNullOrWhiteSpace(request.OrganizationalUnit)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("organizational_unit is required for create.");
                }

                if (request.MembersToRemove.Count > 0) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("members_to_remove is only supported for update.");
                }

                if (request.ClearAttributes.Count > 0) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("clear_attributes is only supported for update.");
                }

                if (!TryParseScope(request.Scope, out _)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("scope must be one of domain_local, global, or universal.");
                }

                return ToolRequestBindingResult<GroupLifecycleRequest>.Success(request);
            case "update":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("identity is required for update.");
                }

                if (HasCreateOnlyFields(request)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("create-only provisioning fields are not supported for update.");
                }

                if (!HasUpdatePayload(request)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("update requires at least one attribute, membership change, or clear_attributes entry.");
                }

                return ToolRequestBindingResult<GroupLifecycleRequest>.Success(request);
            case "delete":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("identity is required for delete.");
                }

                if (HasCreateOnlyFields(request) || HasUpdatePayload(request)) {
                    return ToolRequestBindingResult<GroupLifecycleRequest>.Failure("delete does not support create or update fields.");
                }

                return ToolRequestBindingResult<GroupLifecycleRequest>.Success(request);
            default:
                return ToolRequestBindingResult<GroupLifecycleRequest>.Failure(
                    "operation must be one of create, update, or delete.");
        }
    }

    private static bool TryNormalizeOperation(string value, out string operation) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "create":
            case "update":
            case "delete":
                operation = normalized;
                return true;
            default:
                operation = string.Empty;
                return false;
        }
    }

    private static bool? TryReadNullableBoolean(JsonObject? arguments, string key) {
        return arguments?.TryGetValue(key, out var value) == true && value is not null && value.Kind == JsonValueKind.Boolean
            ? value.AsBoolean()
            : null;
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

    private static bool HasCreateOnlyFields(GroupLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.SamAccountName)
               || !string.IsNullOrWhiteSpace(request.OrganizationalUnit)
               || !string.IsNullOrWhiteSpace(request.CommonName)
               || !string.IsNullOrWhiteSpace(request.Scope)
               || request.SecurityEnabled.HasValue;
    }

    private static bool HasUpdatePayload(GroupLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.DisplayName)
               || !string.IsNullOrWhiteSpace(request.Description)
               || !string.IsNullOrWhiteSpace(request.Mail)
               || !string.IsNullOrWhiteSpace(request.ManagedBy)
               || !string.IsNullOrWhiteSpace(request.Notes)
               || request.MembersToAdd.Count > 0
               || request.MembersToRemove.Count > 0
               || request.ClearAttributes.Count > 0
               || request.AdditionalAttributes.Count > 0;
    }

    private static bool TryParseScope(string? value, out GroupScope scope) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "":
            case "global":
                scope = GroupScope.Global;
                return true;
            case "domain_local":
                scope = GroupScope.DomainLocal;
                return true;
            case "universal":
                scope = GroupScope.Universal;
                return true;
            default:
                scope = GroupScope.Global;
                return false;
        }
    }

    private GroupLifecycleResult ExecuteCreate(GroupLifecycleRequest request) {
        _ = TryParseScope(request.Scope, out var scope);
        var options = new DirectoryGroupCreateOptions {
            CommonName = request.CommonName,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Mail = request.Mail,
            ManagedBy = request.ManagedBy,
            Notes = request.Notes,
            Scope = scope,
            SecurityEnabled = request.SecurityEnabled ?? true
        };

        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            options.Attribute[entry.Key] = entry.Value;
        }

        var provisioningHelper = new ProvisioningHelper();
        using var createdEntry = provisioningHelper.CreateGroup(request.SamAccountName!, request.OrganizationalUnit!, options);
        var distinguishedName = createdEntry.GetDistinguishedName()
                                ?? BuildPredictedDistinguishedName(request.CommonName, request.SamAccountName!, request.OrganizationalUnit!);
        var domainName = string.IsNullOrWhiteSpace(request.DomainName)
            ? InferDomainNameFromDistinguishedName(distinguishedName)
            : request.DomainName!;
        var updatedAttributes = new HashSet<string>(options.GetAttributeNames(), StringComparer.OrdinalIgnoreCase) {
            "sAMAccountName"
        };
        var messages = new List<string> { "Group created." };
        IReadOnlyList<string> membersAdded = Array.Empty<string>();

        if (request.MembersToAdd.Count > 0) {
            var accountHelper = new DirectoryAccountHelper();
            var completedSteps = 1;
            try {
                membersAdded = ApplyMembershipChanges(
                    request.MembersToAdd,
                    memberIdentity => accountHelper.AddGroupMember(distinguishedName, memberIdentity, domainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
            } catch (Exception ex) {
                throw new InvalidOperationException(
                    $"Create workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                    ex);
            }
        }

        return new GroupLifecycleResult(
            Operation: "create",
            ObjectType: "group",
            Identity: request.SamAccountName!,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: true,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            Scope: NormalizeScopeValue(scope),
            SecurityEnabled: request.SecurityEnabled ?? true,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: Array.Empty<string>(),
            MembersAdded: membersAdded,
            MembersRemoved: Array.Empty<string>(),
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private GroupLifecycleResult ExecuteUpdate(GroupLifecycleRequest request) {
        var messages = new List<string>();
        var updatedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clearedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        var completedSteps = 0;
        var domainName = request.DomainName ?? string.Empty;
        var distinguishedName = string.Empty;
        IReadOnlyList<string> membersAdded = Array.Empty<string>();
        IReadOnlyList<string> membersRemoved = Array.Empty<string>();

        try {
            var update = BuildUpdate(request);
            if (update is not null) {
                var setResult = new DirectoryObjectHelper().SetGroup(request.Identity!, update, request.DomainName);
                changed |= setResult.Changed;
                completedSteps += setResult.Changed ? 1 : 0;
                MergeMutation(setResult, messages, updatedAttributes, clearedAttributes, ref distinguishedName, ref domainName);
            }

            var accountHelper = new DirectoryAccountHelper();
            if (request.MembersToAdd.Count > 0) {
                membersAdded = ApplyMembershipChanges(
                    request.MembersToAdd,
                    memberIdentity => accountHelper.AddGroupMember(request.Identity!, memberIdentity, request.DomainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
                changed |= membersAdded.Count > 0;
            }

            if (request.MembersToRemove.Count > 0) {
                membersRemoved = ApplyMembershipChanges(
                    request.MembersToRemove,
                    memberIdentity => accountHelper.RemoveGroupMember(request.Identity!, memberIdentity, request.DomainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
                changed |= membersRemoved.Count > 0;
            }
        } catch (Exception ex) {
            throw new InvalidOperationException(
                $"Update workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                ex);
        }

        return new GroupLifecycleResult(
            Operation: "update",
            ObjectType: "group",
            Identity: request.Identity!,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: changed,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            Scope: null,
            SecurityEnabled: null,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: clearedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            MembersAdded: membersAdded,
            MembersRemoved: membersRemoved,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private static DirectoryObjectUpdate? BuildUpdate(GroupLifecycleRequest request) {
        var update = new DirectoryObjectUpdate {
            DisplayName = request.DisplayName,
            Description = request.Description,
            Mail = request.Mail,
            ManagedBy = request.ManagedBy
        };

        AddIfPresent(update.CustomAttributes, "info", request.Notes);
        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            update.CustomAttributes[entry.Key] = entry.Value;
        }

        for (var i = 0; i < request.ClearAttributes.Count; i++) {
            update.ClearAttributes.Add(request.ClearAttributes[i]);
        }

        return update.HasChanges() ? update : null;
    }

    private static void AddIfPresent(IDictionary<string, object?> attributes, string key, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            attributes[key] = value;
        }
    }

    private static GroupLifecycleResult MapMutationResult(DirectoryMutationResult mutation, GroupLifecycleRequest request) {
        ArgumentNullException.ThrowIfNull(mutation);

        return new GroupLifecycleResult(
            Operation: mutation.Operation,
            ObjectType: mutation.ObjectType,
            Identity: string.IsNullOrWhiteSpace(mutation.Identity)
                ? request.Identity ?? request.SamAccountName ?? string.Empty
                : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: mutation.Changed,
            Apply: true,
            Message: mutation.Message ?? string.Empty,
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            Scope: request.Scope,
            SecurityEnabled: request.SecurityEnabled,
            UpdatedAttributes: mutation.UpdatedAttributes ?? Array.Empty<string>(),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            MembersAdded: request.MembersToAdd,
            MembersRemoved: request.MembersToRemove,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private static GroupLifecycleResult CreateDryRunResult(GroupLifecycleRequest request) {
        var identity = request.Identity ?? request.SamAccountName ?? string.Empty;
        var distinguishedName = request.Operation == "create" && !string.IsNullOrWhiteSpace(request.OrganizationalUnit) && !string.IsNullOrWhiteSpace(identity)
            ? BuildPredictedDistinguishedName(request.CommonName, identity, request.OrganizationalUnit!)
            : string.Empty;
        var domainName = !string.IsNullOrWhiteSpace(request.DomainName)
            ? request.DomainName!
            : InferDomainNameFromDistinguishedName(distinguishedName);

        return new GroupLifecycleResult(
            Operation: request.Operation,
            ObjectType: "group",
            Identity: identity,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: false,
            Apply: false,
            Message: "Dry-run only. Set apply=true to execute the lifecycle action.",
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            Scope: request.Scope,
            SecurityEnabled: request.SecurityEnabled,
            UpdatedAttributes: BuildPlannedUpdatedAttributes(request),
            ClearedAttributes: request.ClearAttributes,
            MembersAdded: request.MembersToAdd,
            MembersRemoved: request.MembersToRemove,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private static IReadOnlyList<string> BuildPlannedUpdatedAttributes(GroupLifecycleRequest request) {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(request.Operation, "create", StringComparison.OrdinalIgnoreCase)) {
            if (!string.IsNullOrWhiteSpace(request.SamAccountName)) {
                attributes.Add("sAMAccountName");
            }

            AddIfPresent(attributes, "cn", request.CommonName);
            AddIfPresent(attributes, "displayName", request.DisplayName);
            AddIfPresent(attributes, "description", request.Description);
            AddIfPresent(attributes, "mail", request.Mail);
            AddIfPresent(attributes, "managedBy", request.ManagedBy);
            AddIfPresent(attributes, "info", request.Notes);
            attributes.Add("groupType");
        } else if (string.Equals(request.Operation, "update", StringComparison.OrdinalIgnoreCase)) {
            AddIfPresent(attributes, "displayName", request.DisplayName);
            AddIfPresent(attributes, "description", request.Description);
            AddIfPresent(attributes, "mail", request.Mail);
            AddIfPresent(attributes, "managedBy", request.ManagedBy);
            AddIfPresent(attributes, "info", request.Notes);
        }

        if (request.MembersToAdd.Count > 0 || request.MembersToRemove.Count > 0) {
            attributes.Add("member");
        }

        AddMutationKeys(attributes, request.AdditionalAttributes);
        return attributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
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

    private static IReadOnlyList<string> ApplyMembershipChanges(
        IReadOnlyList<string> members,
        Func<string, DirectoryMutationResult> applyChange,
        List<string> messages,
        HashSet<string> updatedAttributes,
        ref int completedSteps) {
        if (members.Count == 0) {
            return Array.Empty<string>();
        }

        var changedMembers = new List<string>(members.Count);
        for (var i = 0; i < members.Count; i++) {
            var result = applyChange(members[i]);
            if (!string.IsNullOrWhiteSpace(result.Message)) {
                messages.Add(result.Message);
            }

            if (!result.Changed) {
                continue;
            }

            completedSteps++;
            changedMembers.Add(members[i]);
        }

        if (changedMembers.Count > 0) {
            updatedAttributes.Add("member");
        }

        return changedMembers
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void MergeMutation(
        DirectoryMutationResult mutation,
        List<string> messages,
        HashSet<string> updatedAttributes,
        HashSet<string> clearedAttributes,
        ref string distinguishedName,
        ref string domainName) {
        if (!string.IsNullOrWhiteSpace(mutation.Message)) {
            messages.Add(mutation.Message);
        }

        if (!string.IsNullOrWhiteSpace(mutation.DistinguishedName)) {
            distinguishedName = mutation.DistinguishedName;
        }

        if (!string.IsNullOrWhiteSpace(mutation.DomainName)) {
            domainName = mutation.DomainName;
        }

        foreach (var attribute in mutation.UpdatedAttributes ?? Array.Empty<string>()) {
            if (!string.IsNullOrWhiteSpace(attribute)) {
                updatedAttributes.Add(attribute);
            }
        }

        foreach (var attribute in mutation.ClearedAttributes ?? Array.Empty<string>()) {
            if (!string.IsNullOrWhiteSpace(attribute)) {
                clearedAttributes.Add(attribute);
            }
        }
    }

    private static string NormalizeScopeValue(GroupScope scope) {
        return scope switch {
            GroupScope.DomainLocal => "domain_local",
            GroupScope.Universal => "universal",
            _ => "global"
        };
    }

    private static string BuildPredictedDistinguishedName(string? commonName, string samAccountName, string organizationalUnit) {
        var cn = string.IsNullOrWhiteSpace(commonName) ? samAccountName : commonName.Trim();
        return $"CN={cn},{organizationalUnit}";
    }

    private static string InferDomainNameFromDistinguishedName(string? distinguishedName) {
        if (string.IsNullOrWhiteSpace(distinguishedName)) {
            return string.Empty;
        }

        var components = distinguishedName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dcParts = new List<string>();
        for (var i = 0; i < components.Length; i++) {
            var part = components[i];
            if (!part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase) || part.Length <= 3) {
                continue;
            }

            dcParts.Add(part.Substring(3));
        }

        return dcParts.Count == 0 ? string.Empty : string.Join(".", dcParts);
    }

    private static string CreateSuccessResponse(GroupLifecycleResult result) {
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

        if (!string.IsNullOrWhiteSpace(result.Message)) {
            facts.Add(("Message", result.Message));
        }

        if (result.MembersAdded.Count > 0) {
            facts.Add(("Members added", string.Join(", ", result.MembersAdded)));
        }

        if (result.MembersRemoved.Count > 0) {
            facts.Add(("Members removed", string.Join(", ", result.MembersRemoved)));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("operation", result.Operation)
            .Add("object_type", result.ObjectType)
            .Add("write_candidate", true);
        if (!string.IsNullOrWhiteSpace(result.DomainName)) {
            meta.Add("domain_name", result.DomainName);
        }

        if (result.MembersAdded.Count > 0) {
            meta.Add("members_added_count", result.MembersAdded.Count);
        }

        if (result.MembersRemoved.Count > 0) {
            meta.Add("members_removed_count", result.MembersRemoved.Count);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: $"ad_group_{result.Operation}",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "AD group lifecycle");
    }
}
