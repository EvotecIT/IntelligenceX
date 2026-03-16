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
/// Performs governed Active Directory user lifecycle actions (dry-run by default).
/// </summary>
public sealed class AdUserLifecycleTool : ActiveDirectoryToolBase, ITool {
    private sealed record AttributeMutation(string Key, string Value);

    private sealed record UserLifecycleRequest(
        string Operation,
        string? Identity,
        string? SamAccountName,
        string? OrganizationalUnit,
        string? DomainName,
        string? CommonName,
        string? UserPrincipalName,
        string? GivenName,
        string? Surname,
        string? DisplayName,
        string? Mail,
        string? Description,
        string? Manager,
        string? InitialPassword,
        string? NewPassword,
        bool Apply,
        bool? Enabled,
        bool? MustChangePasswordAtLogon,
        IReadOnlyList<string> GroupsToAdd,
        IReadOnlyList<string> GroupsToRemove,
        IReadOnlyList<string> ClearAttributes,
        IReadOnlyList<AttributeMutation> ExtensionAttributes,
        IReadOnlyList<AttributeMutation> AdditionalAttributes);

    private sealed record UserLifecycleResult(
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
        string? UserPrincipalName,
        bool? Enabled,
        bool? MustChangePasswordAtLogon,
        IReadOnlyList<string> UpdatedAttributes,
        IReadOnlyList<string> ClearedAttributes,
        IReadOnlyList<string> GroupsAdded,
        IReadOnlyList<string> GroupsRemoved,
        bool PasswordReset,
        IReadOnlyList<AttributeMutation> ExtensionAttributes,
        IReadOnlyList<AttributeMutation> AdditionalAttributes,
        DateTime TimestampUtc);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_user_lifecycle",
        "Governed Active Directory user lifecycle actions for create/enable/disable/delete/reset_password/offboard. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("operation", ToolSchema.String("Lifecycle action to perform.").Enum("create", "enable", "disable", "delete", "reset_password", "offboard")),
                ("identity", ToolSchema.String("Existing user identity for enable/disable/delete/reset_password (DN, sAMAccountName, UPN, mail, or name).")),
                ("sam_account_name", ToolSchema.String("sAMAccountName for create operations.")),
                ("organizational_unit", ToolSchema.String("Target OU distinguished name for create operations.")),
                ("domain_name", ToolSchema.String("Optional domain DNS name for write operations.")),
                ("common_name", ToolSchema.String("Optional common name (CN) for create.")),
                ("user_principal_name", ToolSchema.String("Optional UPN for create.")),
                ("given_name", ToolSchema.String("Optional givenName for create.")),
                ("surname", ToolSchema.String("Optional sn/surname for create.")),
                ("display_name", ToolSchema.String("Optional displayName for create or offboard cleanup updates.")),
                ("mail", ToolSchema.String("Optional mail attribute for create or offboard cleanup updates.")),
                ("description", ToolSchema.String("Optional description for create or offboard cleanup updates.")),
                ("manager", ToolSchema.String("Optional manager DN for create.")),
                ("initial_password", ToolSchema.String("Optional initial password for create.")),
                ("new_password", ToolSchema.String("New password for reset_password or offboard operations.")),
                ("enabled", ToolSchema.Boolean("Optional enabled state for create. When omitted, AD defaults are preserved.")),
                ("must_change_password_at_logon", ToolSchema.Boolean("Optional password-change-at-next-logon flag for create/reset_password/offboard password resets.")),
                ("groups_to_add", ToolSchema.Array(
                    ToolSchema.String("Group identity to add the user to."),
                    "Optional groups to add during create or enable workflows.")),
                ("groups_to_remove", ToolSchema.Array(
                    ToolSchema.String("Group identity to remove the user from."),
                    "Optional groups to remove during disable or offboard workflows.")),
                ("clear_attributes", ToolSchema.Array(
                    ToolSchema.String("LDAP attribute name to clear."),
                    "Optional LDAP attributes to clear during offboard cleanup.")),
                ("extension_attributes", ToolSchema.Array(
                    ToolSchema.Object(
                            ("key", ToolSchema.String("Extension attribute number or name (for example 1 or extensionAttribute1).")),
                            ("value", ToolSchema.String("Extension attribute value.")))
                        .Required("key", "value")
                        .NoAdditionalProperties(),
                    "Optional extension attributes to set during create or offboard cleanup.")),
                ("additional_attributes", ToolSchema.Array(
                    ToolSchema.Object(
                            ("key", ToolSchema.String("LDAP attribute name.")),
                            ("value", ToolSchema.String("LDAP attribute value.")))
                        .Required("key", "value")
                        .NoAdditionalProperties(),
                    "Optional additional LDAP attributes to set during create or offboard cleanup.")),
                ("apply", ToolSchema.Boolean("When true, performs the lifecycle write. Otherwise returns a dry-run preview.")))
            .Required("operation")
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));

    /// <summary>
    /// Initializes a new instance of the <see cref="AdUserLifecycleTool"/> class.
    /// </summary>
    public AdUserLifecycleTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<UserLifecycleRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("operation", out var operation, out var operationError)) {
                return ToolRequestBindingResult<UserLifecycleRequest>.Failure(operationError);
            }

            if (!TryNormalizeOperation(operation, out var normalizedOperation)) {
                return ToolRequestBindingResult<UserLifecycleRequest>.Failure(
                    "operation must be one of create, enable, disable, delete, or reset_password.");
            }

            var request = new UserLifecycleRequest(
                Operation: normalizedOperation,
                Identity: reader.OptionalString("identity"),
                SamAccountName: reader.OptionalString("sam_account_name"),
                OrganizationalUnit: reader.OptionalString("organizational_unit"),
                DomainName: reader.OptionalString("domain_name"),
                CommonName: reader.OptionalString("common_name"),
                UserPrincipalName: reader.OptionalString("user_principal_name"),
                GivenName: reader.OptionalString("given_name"),
                Surname: reader.OptionalString("surname"),
                DisplayName: reader.OptionalString("display_name"),
                Mail: reader.OptionalString("mail"),
                Description: reader.OptionalString("description"),
                Manager: reader.OptionalString("manager"),
                InitialPassword: reader.OptionalString("initial_password"),
                NewPassword: reader.OptionalString("new_password"),
                Apply: reader.Boolean("apply"),
                Enabled: reader.OptionalBoolean("enabled"),
                MustChangePasswordAtLogon: reader.OptionalBoolean("must_change_password_at_logon"),
                GroupsToAdd: ReadTrimmedStrings(reader.Array("groups_to_add")),
                GroupsToRemove: ReadTrimmedStrings(reader.Array("groups_to_remove")),
                ClearAttributes: ReadTrimmedStrings(reader.Array("clear_attributes")),
                ExtensionAttributes: ReadAttributeMutations(reader.Array("extension_attributes")),
                AdditionalAttributes: ReadAttributeMutations(reader.Array("additional_attributes")));

            return ValidateRequest(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<UserLifecycleRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        if (!request.Apply) {
            return Task.FromResult(CreateSuccessResponse(CreateDryRunResult(request)));
        }

        try {
            var result = request.Operation switch {
                "create" => ExecuteCreate(request),
                "enable" => ExecuteEnable(request),
                "disable" => ExecuteDisable(request),
                "delete" => MapMutationResult(new DirectoryAccountHelper().DeleteUser(request.Identity!, request.DomainName), request),
                "reset_password" => MapMutationResult(
                    new DirectoryAccountHelper().ResetUserPassword(
                        request.Identity!,
                        request.NewPassword!,
                        request.DomainName,
                        changeAtNextLogon: request.MustChangePasswordAtLogon ?? false),
                    request),
                "offboard" => ExecuteOffboard(request),
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

    private static ToolRequestBindingResult<UserLifecycleRequest> ValidateRequest(UserLifecycleRequest request) {
        switch (request.Operation) {
            case "create":
                if (string.IsNullOrWhiteSpace(request.SamAccountName)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("sam_account_name is required for create.");
                }

                if (string.IsNullOrWhiteSpace(request.OrganizationalUnit)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("organizational_unit is required for create.");
                }

                if (!string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("new_password is not supported for create. Use initial_password for provisioning.");
                }

                if (request.GroupsToRemove.Count > 0) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("groups_to_remove is only supported for disable or offboard.");
                }

                if (request.ClearAttributes.Count > 0) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("clear_attributes is only supported for offboard.");
                }

                return ToolRequestBindingResult<UserLifecycleRequest>.Success(request);
            case "enable":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("identity is required for enable.");
                }

                if (HasProvisioningOnlyFields(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("create-only provisioning fields are not supported for enable.");
                }

                if (!string.IsNullOrWhiteSpace(request.NewPassword) || request.MustChangePasswordAtLogon.HasValue) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("Password reset fields are not supported for enable.");
                }

                if (request.GroupsToRemove.Count > 0) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("groups_to_remove is only supported for disable or offboard.");
                }

                if (HasAttributeCleanupPayload(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("Attribute cleanup fields are only supported for create or offboard.");
                }

                return ToolRequestBindingResult<UserLifecycleRequest>.Success(request);
            case "disable":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("identity is required for disable.");
                }

                if (HasProvisioningOnlyFields(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("create-only provisioning fields are not supported for disable.");
                }

                if (!string.IsNullOrWhiteSpace(request.NewPassword) || request.MustChangePasswordAtLogon.HasValue) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("Password reset fields are not supported for disable. Use offboard or reset_password.");
                }

                if (request.GroupsToAdd.Count > 0) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("groups_to_add is only supported for create or enable.");
                }

                if (HasAttributeCleanupPayload(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("Attribute cleanup fields are only supported for create or offboard.");
                }

                return ToolRequestBindingResult<UserLifecycleRequest>.Success(request);
            case "delete":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("identity is required for delete.");
                }

                if (HasProvisioningOnlyFields(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("create-only provisioning fields are not supported for delete.");
                }

                if (!string.IsNullOrWhiteSpace(request.NewPassword)
                    || request.MustChangePasswordAtLogon.HasValue
                    || request.GroupsToAdd.Count > 0
                    || request.GroupsToRemove.Count > 0
                    || HasAttributeCleanupPayload(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("delete does not support password, group membership, or attribute cleanup fields.");
                }

                return ToolRequestBindingResult<UserLifecycleRequest>.Success(request);
            case "reset_password":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("identity is required for reset_password.");
                }

                if (string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("new_password is required for reset_password.");
                }

                if (HasProvisioningOnlyFields(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("create-only provisioning fields are not supported for reset_password.");
                }

                if (request.GroupsToAdd.Count > 0 || request.GroupsToRemove.Count > 0 || HasAttributeCleanupPayload(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("reset_password does not support group membership or attribute cleanup fields.");
                }

                return ToolRequestBindingResult<UserLifecycleRequest>.Success(request);
            case "offboard":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("identity is required for offboard.");
                }

                if (HasProvisioningOnlyFields(request)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("create-only provisioning fields are not supported for offboard.");
                }

                if (request.GroupsToAdd.Count > 0) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("groups_to_add is only supported for create or enable.");
                }

                if (request.MustChangePasswordAtLogon.HasValue && string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<UserLifecycleRequest>.Failure("new_password is required when must_change_password_at_logon is set for offboard.");
                }

                return ToolRequestBindingResult<UserLifecycleRequest>.Success(request);
            default:
                return ToolRequestBindingResult<UserLifecycleRequest>.Failure(
                    "operation must be one of create, enable, disable, delete, reset_password, or offboard.");
        }
    }

    private static bool TryNormalizeOperation(string value, out string operation) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "create":
            case "enable":
            case "disable":
            case "delete":
            case "reset_password":
            case "offboard":
                operation = normalized;
                return true;
            default:
                operation = string.Empty;
                return false;
        }
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

    private static bool HasProvisioningOnlyFields(UserLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.SamAccountName)
               || !string.IsNullOrWhiteSpace(request.OrganizationalUnit)
               || !string.IsNullOrWhiteSpace(request.CommonName)
               || !string.IsNullOrWhiteSpace(request.UserPrincipalName)
               || !string.IsNullOrWhiteSpace(request.GivenName)
               || !string.IsNullOrWhiteSpace(request.Surname)
               || !string.IsNullOrWhiteSpace(request.Manager)
               || !string.IsNullOrWhiteSpace(request.InitialPassword)
               || request.Enabled.HasValue;
    }

    private static bool HasAttributeCleanupPayload(UserLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.DisplayName)
               || !string.IsNullOrWhiteSpace(request.Mail)
               || !string.IsNullOrWhiteSpace(request.Description)
               || request.ExtensionAttributes.Count > 0
               || request.AdditionalAttributes.Count > 0
               || request.ClearAttributes.Count > 0;
    }

    private UserLifecycleResult ExecuteCreate(UserLifecycleRequest request) {
        var createOptions = new DirectoryUserCreateOptions {
            CommonName = request.CommonName,
            UserPrincipalName = request.UserPrincipalName,
            GivenName = request.GivenName,
            Surname = request.Surname,
            DisplayName = request.DisplayName,
            Mail = request.Mail,
            Description = request.Description,
            Manager = request.Manager,
            InitialPassword = request.InitialPassword,
            Enabled = request.Enabled,
            MustChangePasswordAtLogon = request.MustChangePasswordAtLogon
        };

        for (var i = 0; i < request.ExtensionAttributes.Count; i++) {
            var entry = request.ExtensionAttributes[i];
            createOptions.ExtensionAttributes[entry.Key] = entry.Value;
        }

        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            createOptions.Attribute[entry.Key] = entry.Value;
        }

        var provisioningHelper = new ProvisioningHelper();
        using var createdEntry = provisioningHelper.CreateUser(request.SamAccountName!, request.OrganizationalUnit!, createOptions);
        var distinguishedName = createdEntry.GetDistinguishedName()
                                ?? BuildPredictedDistinguishedName(request.CommonName, request.SamAccountName!, request.OrganizationalUnit!);
        var domainName = string.IsNullOrWhiteSpace(request.DomainName)
            ? InferDomainNameFromDistinguishedName(distinguishedName)
            : request.DomainName!;

        var updatedAttributes = new HashSet<string>(createOptions.GetAttributeNames(), StringComparer.OrdinalIgnoreCase) {
            "sAMAccountName"
        };
        var messages = new List<string> { "User created." };
        IReadOnlyList<string> groupsAdded = Array.Empty<string>();

        if (request.GroupsToAdd.Count > 0) {
            var accountHelper = new DirectoryAccountHelper();
            var completedSteps = 1;
            try {
                groupsAdded = ApplyGroupMembershipChanges(
                    request.GroupsToAdd,
                    groupIdentity => accountHelper.AddGroupMember(groupIdentity, distinguishedName, domainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
            } catch (Exception ex) {
                throw new InvalidOperationException(
                    $"Create workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                    ex);
            }
        }

        return new UserLifecycleResult(
            Operation: "create",
            ObjectType: "user",
            Identity: request.SamAccountName!,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: true,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            UserPrincipalName: request.UserPrincipalName,
            Enabled: request.Enabled,
            MustChangePasswordAtLogon: request.MustChangePasswordAtLogon,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: Array.Empty<string>(),
            GroupsAdded: groupsAdded,
            GroupsRemoved: Array.Empty<string>(),
            PasswordReset: !string.IsNullOrWhiteSpace(request.InitialPassword),
            ExtensionAttributes: request.ExtensionAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private UserLifecycleResult ExecuteEnable(UserLifecycleRequest request) {
        var accountHelper = new DirectoryAccountHelper();
        var mutation = accountHelper.EnableUser(request.Identity!, request.DomainName);
        var updatedAttributes = new HashSet<string>(mutation.UpdatedAttributes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(mutation.Message)) {
            messages.Add(mutation.Message);
        }

        IReadOnlyList<string> groupsAdded = Array.Empty<string>();
        var changed = mutation.Changed;
        var completedSteps = mutation.Changed ? 1 : 0;

        if (request.GroupsToAdd.Count > 0) {
            try {
                groupsAdded = ApplyGroupMembershipChanges(
                    request.GroupsToAdd,
                    groupIdentity => accountHelper.AddGroupMember(groupIdentity, request.Identity!, request.DomainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
                changed |= groupsAdded.Count > 0;
            } catch (Exception ex) {
                throw new InvalidOperationException(
                    $"Enable workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                    ex);
            }
        }

        return new UserLifecycleResult(
            Operation: "enable",
            ObjectType: mutation.ObjectType,
            Identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: changed,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            UserPrincipalName: request.UserPrincipalName,
            Enabled: true,
            MustChangePasswordAtLogon: null,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: Array.Empty<string>(),
            GroupsAdded: groupsAdded,
            GroupsRemoved: Array.Empty<string>(),
            PasswordReset: false,
            ExtensionAttributes: request.ExtensionAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private UserLifecycleResult ExecuteDisable(UserLifecycleRequest request) {
        var accountHelper = new DirectoryAccountHelper();
        var mutation = accountHelper.DisableUser(request.Identity!, request.DomainName);
        var updatedAttributes = new HashSet<string>(mutation.UpdatedAttributes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(mutation.Message)) {
            messages.Add(mutation.Message);
        }

        IReadOnlyList<string> groupsRemoved = Array.Empty<string>();
        var changed = mutation.Changed;
        var completedSteps = mutation.Changed ? 1 : 0;

        if (request.GroupsToRemove.Count > 0) {
            try {
                groupsRemoved = ApplyGroupMembershipChanges(
                    request.GroupsToRemove,
                    groupIdentity => accountHelper.RemoveGroupMember(groupIdentity, request.Identity!, request.DomainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
                changed |= groupsRemoved.Count > 0;
            } catch (Exception ex) {
                throw new InvalidOperationException(
                    $"Disable workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                    ex);
            }
        }

        return new UserLifecycleResult(
            Operation: "disable",
            ObjectType: mutation.ObjectType,
            Identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: changed,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            UserPrincipalName: request.UserPrincipalName,
            Enabled: false,
            MustChangePasswordAtLogon: null,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: Array.Empty<string>(),
            GroupsAdded: Array.Empty<string>(),
            GroupsRemoved: groupsRemoved,
            PasswordReset: false,
            ExtensionAttributes: request.ExtensionAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private UserLifecycleResult ExecuteOffboard(UserLifecycleRequest request) {
        var accountHelper = new DirectoryAccountHelper();
        var objectHelper = new DirectoryObjectHelper();
        var messages = new List<string>();
        var updatedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clearedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> groupsRemoved = Array.Empty<string>();
        var changed = false;
        var completedSteps = 0;
        var domainName = request.DomainName ?? string.Empty;
        var distinguishedName = string.Empty;

        try {
            var disableResult = accountHelper.DisableUser(request.Identity!, request.DomainName);
            changed |= disableResult.Changed;
            completedSteps += disableResult.Changed ? 1 : 0;
            MergeUserMutation(disableResult, messages, updatedAttributes, clearedAttributes, ref distinguishedName, ref domainName);

            if (!string.IsNullOrWhiteSpace(request.NewPassword)) {
                var resetResult = accountHelper.ResetUserPassword(
                    request.Identity!,
                    request.NewPassword!,
                    request.DomainName,
                    changeAtNextLogon: request.MustChangePasswordAtLogon ?? false);
                changed |= resetResult.Changed;
                completedSteps += resetResult.Changed ? 1 : 0;
                MergeUserMutation(resetResult, messages, updatedAttributes, clearedAttributes, ref distinguishedName, ref domainName);
            }

            if (request.GroupsToRemove.Count > 0) {
                groupsRemoved = ApplyGroupMembershipChanges(
                    request.GroupsToRemove,
                    groupIdentity => accountHelper.RemoveGroupMember(groupIdentity, request.Identity!, request.DomainName),
                    messages,
                    updatedAttributes,
                    ref completedSteps);
                changed |= groupsRemoved.Count > 0;
            }

            var update = BuildAttributeCleanupUpdate(request);
            if (update is not null) {
                var setResult = objectHelper.SetUser(request.Identity!, update, request.DomainName);
                changed |= setResult.Changed;
                completedSteps += setResult.Changed ? 1 : 0;
                MergeUserMutation(setResult, messages, updatedAttributes, clearedAttributes, ref distinguishedName, ref domainName);
            }
        } catch (Exception ex) {
            throw new InvalidOperationException(
                $"Offboard workflow partially applied {completedSteps} change step(s) before failing: {ex.Message}",
                ex);
        }

        return new UserLifecycleResult(
            Operation: "offboard",
            ObjectType: "user",
            Identity: request.Identity!,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: changed,
            Apply: true,
            Message: string.Join(" ", messages.Where(static message => !string.IsNullOrWhiteSpace(message))),
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            UserPrincipalName: request.UserPrincipalName,
            Enabled: false,
            MustChangePasswordAtLogon: !string.IsNullOrWhiteSpace(request.NewPassword) ? request.MustChangePasswordAtLogon : null,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: clearedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            GroupsAdded: Array.Empty<string>(),
            GroupsRemoved: groupsRemoved,
            PasswordReset: !string.IsNullOrWhiteSpace(request.NewPassword),
            ExtensionAttributes: request.ExtensionAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private static UserLifecycleResult MapMutationResult(DirectoryMutationResult mutation, UserLifecycleRequest request) {
        ArgumentNullException.ThrowIfNull(mutation);

        return new UserLifecycleResult(
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
            UserPrincipalName: request.UserPrincipalName,
            Enabled: request.Operation switch {
                "enable" => true,
                "disable" => false,
                "offboard" => false,
                _ => request.Enabled
            },
            MustChangePasswordAtLogon: !string.IsNullOrWhiteSpace(request.NewPassword) ? request.MustChangePasswordAtLogon : null,
            UpdatedAttributes: mutation.UpdatedAttributes ?? Array.Empty<string>(),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            GroupsAdded: request.Operation == "create" || request.Operation == "enable"
                ? request.GroupsToAdd
                : Array.Empty<string>(),
            GroupsRemoved: request.Operation == "disable" || request.Operation == "offboard"
                ? request.GroupsToRemove
                : Array.Empty<string>(),
            PasswordReset: string.Equals(request.Operation, "reset_password", StringComparison.OrdinalIgnoreCase)
                           || (!string.IsNullOrWhiteSpace(request.NewPassword)
                               && string.Equals(request.Operation, "offboard", StringComparison.OrdinalIgnoreCase)),
            ExtensionAttributes: request.ExtensionAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private static UserLifecycleResult CreateDryRunResult(UserLifecycleRequest request) {
        var identity = request.Identity ?? request.SamAccountName ?? string.Empty;
        var distinguishedName = request.Operation == "create" && !string.IsNullOrWhiteSpace(request.OrganizationalUnit) && !string.IsNullOrWhiteSpace(identity)
            ? BuildPredictedDistinguishedName(request.CommonName, identity, request.OrganizationalUnit!)
            : string.Empty;
        var domainName = !string.IsNullOrWhiteSpace(request.DomainName)
            ? request.DomainName!
            : InferDomainNameFromDistinguishedName(distinguishedName);

        return new UserLifecycleResult(
            Operation: request.Operation,
            ObjectType: "user",
            Identity: identity,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: false,
            Apply: false,
            Message: "Dry-run only. Set apply=true to execute the lifecycle action.",
            OrganizationalUnit: request.OrganizationalUnit,
            SamAccountName: request.SamAccountName,
            UserPrincipalName: request.UserPrincipalName,
            Enabled: request.Operation switch {
                "enable" => true,
                "disable" => false,
                "offboard" => false,
                _ => request.Enabled
            },
            MustChangePasswordAtLogon: !string.IsNullOrWhiteSpace(request.NewPassword) ? request.MustChangePasswordAtLogon : null,
            UpdatedAttributes: BuildPlannedUpdatedAttributes(request),
            ClearedAttributes: request.ClearAttributes,
            GroupsAdded: request.GroupsToAdd,
            GroupsRemoved: request.GroupsToRemove,
            PasswordReset: string.Equals(request.Operation, "reset_password", StringComparison.OrdinalIgnoreCase)
                           || (!string.IsNullOrWhiteSpace(request.NewPassword)
                               && string.Equals(request.Operation, "offboard", StringComparison.OrdinalIgnoreCase)),
            ExtensionAttributes: request.ExtensionAttributes,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private static IReadOnlyList<string> BuildPlannedUpdatedAttributes(UserLifecycleRequest request) {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(request.Operation, "create", StringComparison.OrdinalIgnoreCase)) {
            if (!string.IsNullOrWhiteSpace(request.SamAccountName)) {
                attributes.Add("sAMAccountName");
            }

            AddIfPresent(attributes, "cn", request.CommonName);
            AddIfPresent(attributes, "userPrincipalName", request.UserPrincipalName);
            AddIfPresent(attributes, "givenName", request.GivenName);
            AddIfPresent(attributes, "sn", request.Surname);
            AddIfPresent(attributes, "displayName", request.DisplayName);
            AddIfPresent(attributes, "mail", request.Mail);
            AddIfPresent(attributes, "description", request.Description);
            AddIfPresent(attributes, "manager", request.Manager);
            if (!string.IsNullOrWhiteSpace(request.InitialPassword)) {
                attributes.Add("unicodePwd");
            }

            if (request.Enabled.HasValue) {
                attributes.Add("userAccountControl");
            }

            if (request.MustChangePasswordAtLogon.HasValue) {
                attributes.Add("pwdLastSet");
            }

            AddMutationKeys(attributes, request.ExtensionAttributes);
            AddMutationKeys(attributes, request.AdditionalAttributes);
        } else if (string.Equals(request.Operation, "reset_password", StringComparison.OrdinalIgnoreCase)) {
            attributes.Add("unicodePwd");
            if (request.MustChangePasswordAtLogon.HasValue) {
                attributes.Add("pwdLastSet");
            }
        } else if (string.Equals(request.Operation, "enable", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(request.Operation, "disable", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(request.Operation, "offboard", StringComparison.OrdinalIgnoreCase)) {
            attributes.Add("userAccountControl");
            if (!string.IsNullOrWhiteSpace(request.NewPassword)) {
                attributes.Add("unicodePwd");
            }

            if (request.MustChangePasswordAtLogon.HasValue) {
                attributes.Add("pwdLastSet");
            }

            AddIfPresent(attributes, "displayName", request.DisplayName);
            AddIfPresent(attributes, "mail", request.Mail);
            AddIfPresent(attributes, "description", request.Description);
            AddMutationKeys(attributes, request.ExtensionAttributes);
            AddMutationKeys(attributes, request.AdditionalAttributes);
        }

        if (request.GroupsToAdd.Count > 0 || request.GroupsToRemove.Count > 0) {
            attributes.Add("memberOf");
        }

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

    private static string BuildPredictedDistinguishedName(string? commonName, string samAccountName, string organizationalUnit) {
        var cn = string.IsNullOrWhiteSpace(commonName) ? samAccountName : commonName.Trim();
        return $"CN={cn},{organizationalUnit}";
    }

    private static IReadOnlyList<string> ApplyGroupMembershipChanges(
        IReadOnlyList<string> groups,
        Func<string, DirectoryMutationResult> applyChange,
        List<string> messages,
        HashSet<string> updatedAttributes,
        ref int completedSteps) {
        if (groups.Count == 0) {
            return Array.Empty<string>();
        }

        var changedGroups = new List<string>(groups.Count);
        for (var i = 0; i < groups.Count; i++) {
            var result = applyChange(groups[i]);
            if (!string.IsNullOrWhiteSpace(result.Message)) {
                messages.Add(result.Message);
            }

            if (!result.Changed) {
                continue;
            }

            completedSteps++;
            changedGroups.Add(groups[i]);
        }

        if (changedGroups.Count > 0) {
            updatedAttributes.Add("memberOf");
        }

        return changedGroups
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DirectoryObjectUpdate? BuildAttributeCleanupUpdate(UserLifecycleRequest request) {
        var update = new DirectoryObjectUpdate {
            DisplayName = request.DisplayName,
            Description = request.Description,
            Mail = request.Mail
        };

        for (var i = 0; i < request.ExtensionAttributes.Count; i++) {
            var entry = request.ExtensionAttributes[i];
            update.ExtensionAttributes[entry.Key] = entry.Value;
        }

        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            update.CustomAttributes[entry.Key] = entry.Value;
        }

        for (var i = 0; i < request.ClearAttributes.Count; i++) {
            update.ClearAttributes.Add(request.ClearAttributes[i]);
        }

        return update.HasChanges() ? update : null;
    }

    private static void MergeUserMutation(
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

    private static string CreateSuccessResponse(UserLifecycleResult result) {
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

        if (!string.IsNullOrWhiteSpace(result.OrganizationalUnit)) {
            facts.Add(("Organizational unit", result.OrganizationalUnit));
        }

        if (!string.IsNullOrWhiteSpace(result.Message)) {
            facts.Add(("Message", result.Message));
        }

        if (result.GroupsAdded.Count > 0) {
            facts.Add(("Groups added", string.Join(", ", result.GroupsAdded)));
        }

        if (result.GroupsRemoved.Count > 0) {
            facts.Add(("Groups removed", string.Join(", ", result.GroupsRemoved)));
        }

        if (result.PasswordReset) {
            facts.Add(("Password reset", "true"));
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: false)
            .Add("operation", result.Operation)
            .Add("object_type", result.ObjectType)
            .Add("write_candidate", true);
        if (!string.IsNullOrWhiteSpace(result.DomainName)) {
            meta.Add("domain_name", result.DomainName);
        }

        if (result.GroupsAdded.Count > 0) {
            meta.Add("groups_added_count", result.GroupsAdded.Count);
        }

        if (result.GroupsRemoved.Count > 0) {
            meta.Add("groups_removed_count", result.GroupsRemoved.Count);
        }

        if (result.PasswordReset) {
            meta.Add("password_reset", true);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: $"ad_user_{result.Operation}",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "AD user lifecycle");
    }
}
