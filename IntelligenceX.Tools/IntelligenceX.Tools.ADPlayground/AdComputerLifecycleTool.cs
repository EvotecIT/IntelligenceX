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
/// Performs governed Active Directory computer lifecycle actions (dry-run by default).
/// </summary>
public sealed class AdComputerLifecycleTool : ActiveDirectoryToolBase, ITool {
    private sealed record AttributeMutation(string Key, string Value);

    private sealed record ComputerLifecycleRequest(
        string Operation,
        string? Identity,
        string? SamAccountName,
        string? OrganizationalUnit,
        string? TargetOrganizationalUnit,
        string? DomainName,
        string? CommonName,
        string? NewCommonName,
        string? DnsHostName,
        string? Description,
        string? ManagedBy,
        string? Location,
        string? Office,
        string? OperatingSystem,
        string? OperatingSystemVersion,
        string? OperatingSystemServicePack,
        string? NewPassword,
        bool Apply,
        bool? Enabled,
        IReadOnlyList<string> ServicePrincipalNames,
        IReadOnlyList<string> ClearAttributes,
        IReadOnlyList<AttributeMutation> AdditionalAttributes);

    private sealed record ComputerLifecycleResult(
        string Operation,
        string ObjectType,
        string Identity,
        string DistinguishedName,
        string DomainName,
        bool Changed,
        bool Apply,
        string Message,
        string? OrganizationalUnit,
        string? TargetOrganizationalUnit,
        string? SamAccountName,
        string? ComputerName,
        bool? Enabled,
        IReadOnlyList<string> UpdatedAttributes,
        IReadOnlyList<string> ClearedAttributes,
        IReadOnlyList<string> ServicePrincipalNames,
        bool PasswordReset,
        IReadOnlyList<AttributeMutation> AdditionalAttributes,
        DateTime TimestampUtc);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_computer_lifecycle",
        "Governed Active Directory computer lifecycle actions for create/update/move/enable/disable/delete/reset_password. Dry-run by default; apply=true performs the write.",
        ToolSchema.Object(
                ("operation", ToolSchema.String("Lifecycle action to perform.").Enum("create", "update", "move", "enable", "disable", "delete", "reset_password")),
                ("identity", ToolSchema.String("Existing computer identity for update/move/enable/disable/delete/reset_password (DN, sAMAccountName, dNSHostName, or name).")),
                ("sam_account_name", ToolSchema.String("sAMAccountName for create operations.")),
                ("organizational_unit", ToolSchema.String("Target OU distinguished name for create operations.")),
                ("target_organizational_unit", ToolSchema.String("Target OU distinguished name for move operations. Use the current OU with new_common_name to perform a rename.")),
                ("domain_name", ToolSchema.String("Optional domain DNS name for write operations.")),
                ("common_name", ToolSchema.String("Optional common name (CN) for create operations.")),
                ("new_common_name", ToolSchema.String("Optional replacement common name for move/rename operations.")),
                ("dns_host_name", ToolSchema.String("Optional dNSHostName for create or update operations.")),
                ("description", ToolSchema.String("Optional description for create or update operations.")),
                ("managed_by", ToolSchema.String("Optional managedBy distinguished name for create or update operations.")),
                ("location", ToolSchema.String("Optional location attribute for create or update operations.")),
                ("office", ToolSchema.String("Optional office attribute for create or update operations.")),
                ("operating_system", ToolSchema.String("Optional operatingSystem for create or update operations.")),
                ("operating_system_version", ToolSchema.String("Optional operatingSystemVersion for create or update operations.")),
                ("operating_system_service_pack", ToolSchema.String("Optional operatingSystemServicePack for create or update operations.")),
                ("new_password", ToolSchema.String("New password for reset_password operations.")),
                ("enabled", ToolSchema.Boolean("Optional enabled state for create or update operations.")),
                ("service_principal_names", ToolSchema.Array(
                    ToolSchema.String("Service principal name entry."),
                    "Optional SPNs to set during create or update operations.")),
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
    /// Initializes a new instance of the <see cref="AdComputerLifecycleTool"/> class.
    /// </summary>
    public AdComputerLifecycleTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ComputerLifecycleRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("operation", out var operation, out var operationError)) {
                return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure(operationError);
            }

            if (!TryNormalizeOperation(operation, out var normalizedOperation)) {
                return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure(
                    "operation must be one of create, update, enable, disable, delete, or reset_password.");
            }

            var request = new ComputerLifecycleRequest(
                Operation: normalizedOperation,
                Identity: reader.OptionalString("identity"),
                SamAccountName: reader.OptionalString("sam_account_name"),
                OrganizationalUnit: reader.OptionalString("organizational_unit"),
                TargetOrganizationalUnit: reader.OptionalString("target_organizational_unit"),
                DomainName: reader.OptionalString("domain_name"),
                CommonName: reader.OptionalString("common_name"),
                NewCommonName: reader.OptionalString("new_common_name"),
                DnsHostName: reader.OptionalString("dns_host_name"),
                Description: reader.OptionalString("description"),
                ManagedBy: reader.OptionalString("managed_by"),
                Location: reader.OptionalString("location"),
                Office: reader.OptionalString("office"),
                OperatingSystem: reader.OptionalString("operating_system"),
                OperatingSystemVersion: reader.OptionalString("operating_system_version"),
                OperatingSystemServicePack: reader.OptionalString("operating_system_service_pack"),
                NewPassword: reader.OptionalString("new_password"),
                Apply: reader.Boolean("apply"),
                Enabled: reader.OptionalBoolean("enabled"),
                ServicePrincipalNames: ReadTrimmedStrings(reader.Array("service_principal_names")),
                ClearAttributes: ReadTrimmedStrings(reader.Array("clear_attributes")),
                AdditionalAttributes: ReadAttributeMutations(reader.Array("additional_attributes")));

            return ValidateRequest(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ComputerLifecycleRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        if (!request.Apply) {
            return Task.FromResult(CreateSuccessResponse(CreateDryRunResult(request)));
        }

        try {
            var result = request.Operation switch {
                "create" => ExecuteCreate(request),
                "update" => ExecuteUpdate(request),
                "move" => ExecuteMove(request),
                "enable" => MapMutationResult(new DirectoryAccountHelper().EnableComputer(request.Identity!, request.DomainName), request),
                "disable" => MapMutationResult(new DirectoryAccountHelper().DisableComputer(request.Identity!, request.DomainName), request),
                "delete" => MapMutationResult(new DirectoryAccountHelper().DeleteComputer(request.Identity!, request.DomainName), request),
                "reset_password" => MapMutationResult(new DirectoryAccountHelper().ResetComputerPassword(request.Identity!, request.NewPassword!, request.DomainName), request),
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

    private static ToolRequestBindingResult<ComputerLifecycleRequest> ValidateRequest(ComputerLifecycleRequest request) {
        switch (request.Operation) {
            case "create":
                if (string.IsNullOrWhiteSpace(request.SamAccountName)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("sam_account_name is required for create.");
                }

                if (string.IsNullOrWhiteSpace(request.OrganizationalUnit)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("organizational_unit is required for create.");
                }

                if (!string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("new_password is only supported for reset_password.");
                }

                if (request.ClearAttributes.Count > 0) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("clear_attributes is only supported for update.");
                }

                if (!string.IsNullOrWhiteSpace(request.TargetOrganizationalUnit) || !string.IsNullOrWhiteSpace(request.NewCommonName)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("target_organizational_unit and new_common_name are only supported for move.");
                }

                return ToolRequestBindingResult<ComputerLifecycleRequest>.Success(request);
            case "update":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("identity is required for update.");
                }

                if (HasCreateOnlyFields(request)
                    || !string.IsNullOrWhiteSpace(request.TargetOrganizationalUnit)
                    || !string.IsNullOrWhiteSpace(request.NewCommonName)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("create-only provisioning fields are not supported for update.");
                }

                if (!string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("new_password is only supported for reset_password.");
                }

                if (!HasUpdatePayload(request)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("update requires at least one typed attribute, SPN, enabled state, custom attribute, or clear_attributes entry.");
                }

                return ToolRequestBindingResult<ComputerLifecycleRequest>.Success(request);
            case "move":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("identity is required for move.");
                }

                if (string.IsNullOrWhiteSpace(request.TargetOrganizationalUnit) && string.IsNullOrWhiteSpace(request.NewCommonName)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("move requires target_organizational_unit, new_common_name, or both.");
                }

                if (HasCreateOnlyFields(request)
                    || HasUpdatePayload(request)
                    || !string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("move only supports identity, target_organizational_unit, new_common_name, domain_name, and apply.");
                }

                return ToolRequestBindingResult<ComputerLifecycleRequest>.Success(request);
            case "enable":
            case "disable":
            case "delete":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure($"identity is required for {request.Operation}.");
                }

                if (HasCreateOnlyFields(request)
                    || HasUpdatePayload(request)
                    || !string.IsNullOrWhiteSpace(request.TargetOrganizationalUnit)
                    || !string.IsNullOrWhiteSpace(request.NewCommonName)
                    || !string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure($"{request.Operation} does not support create, update, or password-reset fields.");
                }

                return ToolRequestBindingResult<ComputerLifecycleRequest>.Success(request);
            case "reset_password":
                if (string.IsNullOrWhiteSpace(request.Identity)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("identity is required for reset_password.");
                }

                if (string.IsNullOrWhiteSpace(request.NewPassword)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("new_password is required for reset_password.");
                }

                if (HasCreateOnlyFields(request)
                    || HasUpdatePayload(request)
                    || !string.IsNullOrWhiteSpace(request.TargetOrganizationalUnit)
                    || !string.IsNullOrWhiteSpace(request.NewCommonName)) {
                    return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure("reset_password does not support create or update fields.");
                }

                return ToolRequestBindingResult<ComputerLifecycleRequest>.Success(request);
            default:
                return ToolRequestBindingResult<ComputerLifecycleRequest>.Failure(
                    "operation must be one of create, update, move, enable, disable, delete, or reset_password.");
        }
    }

    private static bool TryNormalizeOperation(string value, out string operation) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "create":
            case "update":
            case "move":
            case "enable":
            case "disable":
            case "delete":
            case "reset_password":
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

    private static bool HasCreateOnlyFields(ComputerLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.SamAccountName)
               || !string.IsNullOrWhiteSpace(request.OrganizationalUnit)
               || !string.IsNullOrWhiteSpace(request.CommonName);
    }

    private static bool HasUpdatePayload(ComputerLifecycleRequest request) {
        return !string.IsNullOrWhiteSpace(request.DnsHostName)
               || !string.IsNullOrWhiteSpace(request.Description)
               || !string.IsNullOrWhiteSpace(request.ManagedBy)
               || !string.IsNullOrWhiteSpace(request.Location)
               || !string.IsNullOrWhiteSpace(request.Office)
               || !string.IsNullOrWhiteSpace(request.OperatingSystem)
               || !string.IsNullOrWhiteSpace(request.OperatingSystemVersion)
               || !string.IsNullOrWhiteSpace(request.OperatingSystemServicePack)
               || request.Enabled.HasValue
               || request.ServicePrincipalNames.Count > 0
               || request.ClearAttributes.Count > 0
               || request.AdditionalAttributes.Count > 0;
    }

    private ComputerLifecycleResult ExecuteCreate(ComputerLifecycleRequest request) {
        var options = new DirectoryComputerCreateOptions {
            CommonName = request.CommonName,
            DnsHostName = request.DnsHostName,
            Description = request.Description,
            ManagedBy = request.ManagedBy,
            Location = request.Location,
            Office = request.Office,
            OperatingSystem = request.OperatingSystem,
            OperatingSystemVersion = request.OperatingSystemVersion,
            OperatingSystemServicePack = request.OperatingSystemServicePack,
            Enabled = request.Enabled
        };

        for (var i = 0; i < request.ServicePrincipalNames.Count; i++) {
            options.ServicePrincipalNames.Add(request.ServicePrincipalNames[i]);
        }

        for (var i = 0; i < request.AdditionalAttributes.Count; i++) {
            var entry = request.AdditionalAttributes[i];
            options.Attribute[entry.Key] = entry.Value;
        }

        var provisioningHelper = new ProvisioningHelper();
        using var createdEntry = provisioningHelper.CreateComputer(request.SamAccountName!, request.OrganizationalUnit!, options);
        var distinguishedName = createdEntry.GetDistinguishedName()
                                ?? BuildPredictedDistinguishedName(request.CommonName, request.SamAccountName!, request.OrganizationalUnit!);
        var domainName = string.IsNullOrWhiteSpace(request.DomainName)
            ? InferDomainNameFromDistinguishedName(distinguishedName)
            : request.DomainName!;
        var updatedAttributes = new HashSet<string>(options.GetAttributeNames(), StringComparer.OrdinalIgnoreCase) {
            "sAMAccountName"
        };

        return new ComputerLifecycleResult(
            Operation: "create",
            ObjectType: "computer",
            Identity: NormalizeComputerIdentity(request.SamAccountName!),
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: true,
            Apply: true,
            Message: "Computer account created.",
            OrganizationalUnit: request.OrganizationalUnit,
            TargetOrganizationalUnit: null,
            SamAccountName: NormalizeComputerIdentity(request.SamAccountName!),
            ComputerName: ResolveComputerName(
                identity: NormalizeComputerIdentity(request.SamAccountName!),
                distinguishedName: distinguishedName,
                samAccountName: request.SamAccountName,
                dnsHostName: request.DnsHostName,
                commonName: request.CommonName),
            Enabled: request.Enabled,
            UpdatedAttributes: updatedAttributes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ClearedAttributes: Array.Empty<string>(),
            ServicePrincipalNames: request.ServicePrincipalNames,
            PasswordReset: false,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private ComputerLifecycleResult ExecuteUpdate(ComputerLifecycleRequest request) {
        var update = BuildUpdate(request);
        var mutation = new DirectoryObjectHelper().SetComputer(request.Identity!, update, request.DomainName);
        return new ComputerLifecycleResult(
            Operation: "update",
            ObjectType: mutation.ObjectType,
            Identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: mutation.Changed,
            Apply: true,
            Message: mutation.Message ?? string.Empty,
            OrganizationalUnit: request.OrganizationalUnit,
            TargetOrganizationalUnit: null,
            SamAccountName: request.SamAccountName,
            ComputerName: ResolveComputerName(
                identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
                distinguishedName: mutation.DistinguishedName,
                samAccountName: request.SamAccountName,
                dnsHostName: request.DnsHostName,
                commonName: request.CommonName),
            Enabled: request.Enabled,
            UpdatedAttributes: mutation.UpdatedAttributes ?? Array.Empty<string>(),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            ServicePrincipalNames: request.ServicePrincipalNames,
            PasswordReset: false,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private ComputerLifecycleResult ExecuteMove(ComputerLifecycleRequest request) {
        var mutation = MoveComputer(
            request.Identity!,
            request.TargetOrganizationalUnit,
            request.NewCommonName,
            request.DomainName);

        return new ComputerLifecycleResult(
            Operation: mutation.Operation,
            ObjectType: mutation.ObjectType,
            Identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
            DistinguishedName: mutation.DistinguishedName ?? string.Empty,
            DomainName: mutation.DomainName ?? request.DomainName ?? string.Empty,
            Changed: mutation.Changed,
            Apply: true,
            Message: mutation.Message ?? string.Empty,
            OrganizationalUnit: null,
            TargetOrganizationalUnit: request.TargetOrganizationalUnit,
            SamAccountName: request.SamAccountName,
            ComputerName: ResolveComputerName(
                identity: string.IsNullOrWhiteSpace(mutation.Identity) ? request.Identity! : mutation.Identity,
                distinguishedName: mutation.DistinguishedName,
                samAccountName: request.SamAccountName,
                dnsHostName: request.DnsHostName,
                commonName: request.NewCommonName ?? request.CommonName),
            Enabled: null,
            UpdatedAttributes: mutation.UpdatedAttributes ?? Array.Empty<string>(),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            ServicePrincipalNames: request.ServicePrincipalNames,
            PasswordReset: false,
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private static DirectoryObjectUpdate BuildUpdate(ComputerLifecycleRequest request) {
        var update = new DirectoryObjectUpdate {
            DnsHostName = request.DnsHostName,
            Description = request.Description,
            ManagedBy = request.ManagedBy,
            Location = request.Location,
            Office = request.Office,
            Enabled = request.Enabled
        };

        AddIfPresent(update.CustomAttributes, "operatingSystem", request.OperatingSystem);
        AddIfPresent(update.CustomAttributes, "operatingSystemVersion", request.OperatingSystemVersion);
        AddIfPresent(update.CustomAttributes, "operatingSystemServicePack", request.OperatingSystemServicePack);

        if (request.ServicePrincipalNames.Count > 0) {
            update.CustomAttributes["servicePrincipalName"] = request.ServicePrincipalNames.ToArray();
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

    private static void AddIfPresent(IDictionary<string, object?> attributes, string key, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            attributes[key] = value;
        }
    }

    private static ComputerLifecycleResult MapMutationResult(DirectoryMutationResult mutation, ComputerLifecycleRequest request) {
        ArgumentNullException.ThrowIfNull(mutation);

        return new ComputerLifecycleResult(
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
            TargetOrganizationalUnit: request.TargetOrganizationalUnit,
            SamAccountName: request.SamAccountName,
            ComputerName: ResolveComputerName(
                identity: string.IsNullOrWhiteSpace(mutation.Identity)
                    ? request.Identity ?? request.SamAccountName ?? string.Empty
                    : mutation.Identity,
                distinguishedName: mutation.DistinguishedName,
                samAccountName: request.SamAccountName,
                dnsHostName: request.DnsHostName,
                commonName: request.NewCommonName ?? request.CommonName),
            Enabled: request.Operation switch {
                "enable" => true,
                "disable" => false,
                _ => request.Enabled
            },
            UpdatedAttributes: mutation.UpdatedAttributes ?? Array.Empty<string>(),
            ClearedAttributes: mutation.ClearedAttributes ?? Array.Empty<string>(),
            ServicePrincipalNames: request.ServicePrincipalNames,
            PasswordReset: string.Equals(request.Operation, "reset_password", StringComparison.OrdinalIgnoreCase),
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: mutation.TimestampUtc);
    }

    private static ComputerLifecycleResult CreateDryRunResult(ComputerLifecycleRequest request) {
        var identity = request.Identity ?? NormalizeComputerIdentity(request.SamAccountName ?? string.Empty);
        var distinguishedName = request.Operation switch {
            "create" when !string.IsNullOrWhiteSpace(request.OrganizationalUnit) && !string.IsNullOrWhiteSpace(identity)
                => BuildPredictedDistinguishedName(request.CommonName, identity, request.OrganizationalUnit!),
            "move" when !string.IsNullOrWhiteSpace(identity)
                => BuildPredictedMoveDistinguishedName(identity, request.TargetOrganizationalUnit, request.NewCommonName),
            _ => string.Empty
        };
        var domainName = !string.IsNullOrWhiteSpace(request.DomainName)
            ? request.DomainName!
            : InferDomainNameFromDistinguishedName(distinguishedName);

        return new ComputerLifecycleResult(
            Operation: request.Operation,
            ObjectType: "computer",
            Identity: identity,
            DistinguishedName: distinguishedName,
            DomainName: domainName,
            Changed: false,
            Apply: false,
            Message: "Dry-run only. Set apply=true to execute the lifecycle action.",
            OrganizationalUnit: request.OrganizationalUnit,
            TargetOrganizationalUnit: request.TargetOrganizationalUnit,
            SamAccountName: request.SamAccountName,
            ComputerName: ResolveComputerName(
                identity: identity,
                distinguishedName: distinguishedName,
                samAccountName: request.SamAccountName,
                dnsHostName: request.DnsHostName,
                commonName: request.NewCommonName ?? request.CommonName),
            Enabled: request.Operation switch {
                "enable" => true,
                "disable" => false,
                _ => request.Enabled
            },
            UpdatedAttributes: BuildPlannedUpdatedAttributes(request),
            ClearedAttributes: request.ClearAttributes,
            ServicePrincipalNames: request.ServicePrincipalNames,
            PasswordReset: string.Equals(request.Operation, "reset_password", StringComparison.OrdinalIgnoreCase),
            AdditionalAttributes: request.AdditionalAttributes,
            TimestampUtc: DateTime.UtcNow);
    }

    private static IReadOnlyList<string> BuildPlannedUpdatedAttributes(ComputerLifecycleRequest request) {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(request.Operation, "create", StringComparison.OrdinalIgnoreCase)) {
            if (!string.IsNullOrWhiteSpace(request.SamAccountName)) {
                attributes.Add("sAMAccountName");
            }

            AddIfPresent(attributes, "cn", request.CommonName);
            AddIfPresent(attributes, "dNSHostName", request.DnsHostName);
            AddIfPresent(attributes, "description", request.Description);
            AddIfPresent(attributes, "managedBy", request.ManagedBy);
            AddIfPresent(attributes, "location", request.Location);
            AddIfPresent(attributes, "physicalDeliveryOfficeName", request.Office);
            AddIfPresent(attributes, "operatingSystem", request.OperatingSystem);
            AddIfPresent(attributes, "operatingSystemVersion", request.OperatingSystemVersion);
            AddIfPresent(attributes, "operatingSystemServicePack", request.OperatingSystemServicePack);
            if (request.Enabled.HasValue) {
                attributes.Add("userAccountControl");
            }
        } else if (string.Equals(request.Operation, "update", StringComparison.OrdinalIgnoreCase)) {
            AddIfPresent(attributes, "dNSHostName", request.DnsHostName);
            AddIfPresent(attributes, "description", request.Description);
            AddIfPresent(attributes, "managedBy", request.ManagedBy);
            AddIfPresent(attributes, "location", request.Location);
            AddIfPresent(attributes, "physicalDeliveryOfficeName", request.Office);
            AddIfPresent(attributes, "operatingSystem", request.OperatingSystem);
            AddIfPresent(attributes, "operatingSystemVersion", request.OperatingSystemVersion);
            AddIfPresent(attributes, "operatingSystemServicePack", request.OperatingSystemServicePack);
            if (request.Enabled.HasValue) {
                attributes.Add("userAccountControl");
            }
        } else if (string.Equals(request.Operation, "move", StringComparison.OrdinalIgnoreCase)) {
            attributes.Add("distinguishedName");
            if (!string.IsNullOrWhiteSpace(request.NewCommonName)) {
                attributes.Add("cn");
                attributes.Add("name");
            }
        } else if (string.Equals(request.Operation, "enable", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(request.Operation, "disable", StringComparison.OrdinalIgnoreCase)) {
            attributes.Add("userAccountControl");
        } else if (string.Equals(request.Operation, "reset_password", StringComparison.OrdinalIgnoreCase)) {
            attributes.Add("unicodePwd");
        }

        if (request.ServicePrincipalNames.Count > 0) {
            attributes.Add("servicePrincipalName");
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

    private static string NormalizeComputerIdentity(string samAccountName) {
        if (string.IsNullOrWhiteSpace(samAccountName)) {
            return string.Empty;
        }

        var trimmed = samAccountName.Trim();
        return trimmed.EndsWith("$", StringComparison.Ordinal) ? trimmed : trimmed + "$";
    }

    private static string ResolveComputerName(
        string? identity,
        string? distinguishedName,
        string? samAccountName,
        string? dnsHostName,
        string? commonName) {
        if (!string.IsNullOrWhiteSpace(dnsHostName)) {
            return dnsHostName.Trim();
        }

        var normalizedSam = NormalizeComputerIdentity(samAccountName ?? string.Empty);
        var resolvedFromSam = TryResolveComputerLeafName(normalizedSam);
        if (!string.IsNullOrWhiteSpace(resolvedFromSam)) {
            return resolvedFromSam!;
        }

        var resolvedFromDistinguishedName = TryResolveComputerLeafName(distinguishedName);
        if (!string.IsNullOrWhiteSpace(resolvedFromDistinguishedName)) {
            return resolvedFromDistinguishedName!;
        }

        var resolvedFromIdentity = TryResolveComputerLeafName(identity);
        if (!string.IsNullOrWhiteSpace(resolvedFromIdentity)) {
            return resolvedFromIdentity!;
        }

        return string.IsNullOrWhiteSpace(commonName)
            ? string.Empty
            : commonName.Trim();
    }

    private static string BuildPredictedDistinguishedName(string? commonName, string samAccountName, string organizationalUnit) {
        var normalizedSam = NormalizeComputerIdentity(samAccountName);
        var defaultCn = normalizedSam.EndsWith("$", StringComparison.Ordinal)
            ? normalizedSam.Substring(0, normalizedSam.Length - 1)
            : normalizedSam;
        var cn = string.IsNullOrWhiteSpace(commonName) ? defaultCn : commonName.Trim();
        return $"CN={cn},{organizationalUnit}";
    }

    private static string BuildPredictedMoveDistinguishedName(string identity, string? targetOrganizationalUnit, string? newCommonName) {
        var normalizedIdentity = (identity ?? string.Empty).Trim();
        var currentDistinguishedName = DistinguishedNameHelper.LooksLikeDistinguishedName(normalizedIdentity)
            ? normalizedIdentity
            : null;

        var leafName = !string.IsNullOrWhiteSpace(newCommonName)
            ? newCommonName!.Trim()
            : TryResolveComputerLeafName(normalizedIdentity);
        var parent = !string.IsNullOrWhiteSpace(targetOrganizationalUnit)
            ? targetOrganizationalUnit!.Trim()
            : ExtractParentDistinguishedName(currentDistinguishedName);

        return string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(leafName)
            ? string.Empty
            : $"CN={leafName},{parent}";
    }

    private static string? TryResolveComputerLeafName(string? identity) {
        var normalized = (identity ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        if (normalized.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) {
            var separatorIndex = normalized.IndexOf(',');
            return separatorIndex > 3
                ? normalized.Substring(3, separatorIndex - 3).Trim()
                : normalized.Substring(3).Trim();
        }

        var slashIndex = normalized.IndexOf('\\');
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1) {
            normalized = normalized.Substring(slashIndex + 1).Trim();
        }

        return normalized.EndsWith("$", StringComparison.Ordinal)
            ? normalized.Substring(0, normalized.Length - 1)
            : normalized;
    }

    private static string? ExtractParentDistinguishedName(string? distinguishedName) {
        if (string.IsNullOrWhiteSpace(distinguishedName)) {
            return null;
        }

        var separatorIndex = distinguishedName.IndexOf(',');
        return separatorIndex >= 0 && separatorIndex < distinguishedName.Length - 1
            ? distinguishedName.Substring(separatorIndex + 1).Trim()
            : null;
    }

    private static DirectoryMutationResult MoveComputer(
        string identity,
        string? targetOrganizationalUnit,
        string? newCommonName,
        string? domainName) {
        var objectHelper = new DirectoryObjectHelper();
        var snapshot = objectHelper.GetComputer(identity, domainName, new[] { "cn", "distinguishedName" });
        var sourceDistinguishedName = snapshot.DistinguishedName;
        if (string.IsNullOrWhiteSpace(sourceDistinguishedName)) {
            throw new InvalidOperationException("Unable to resolve a distinguished name for the requested computer move.");
        }

        var currentParent = ExtractParentDistinguishedName(sourceDistinguishedName);
        var resolvedTargetParent = !string.IsNullOrWhiteSpace(targetOrganizationalUnit)
            ? targetOrganizationalUnit!.Trim()
            : currentParent;
        if (string.IsNullOrWhiteSpace(resolvedTargetParent)) {
            throw new InvalidOperationException("Unable to resolve the target organizational unit for the requested computer move.");
        }

        var currentLeafName = snapshot.Attributes.TryGetValue("cn", out var rawCn)
            ? ToolArgs.NormalizeOptional(rawCn?.ToString())
            : TryResolveComputerLeafName(identity);
        var resolvedLeafName = !string.IsNullOrWhiteSpace(newCommonName) ? newCommonName!.Trim() : currentLeafName;
        if (string.IsNullOrWhiteSpace(resolvedLeafName)) {
            throw new InvalidOperationException("Unable to resolve the computer common name for the requested move.");
        }

        var newRdn = string.Equals(currentLeafName, resolvedLeafName, StringComparison.OrdinalIgnoreCase)
            ? null
            : "CN=" + resolvedLeafName;
        var resolvedDomainName = !string.IsNullOrWhiteSpace(snapshot.DomainName)
            ? snapshot.DomainName
            : (domainName ?? InferDomainNameFromDistinguishedName(resolvedTargetParent));

        using var targetParent = new DirectoryEntry($"LDAP://{resolvedTargetParent}");
        using var entry = new DirectoryEntry($"LDAP://{sourceDistinguishedName}");
        if (string.IsNullOrWhiteSpace(newRdn)) {
            entry.MoveTo(targetParent);
        } else {
            entry.MoveTo(targetParent, newRdn);
        }

        targetParent.CommitChanges();
        entry.CommitChanges();

        var movedDistinguishedName = ToolArgs.NormalizeOptional(entry.Properties["distinguishedName"]?.Value?.ToString())
                                   ?? $"CN={resolvedLeafName},{resolvedTargetParent}";

        return new DirectoryMutationResult {
            Operation = "move",
            ObjectType = "computer",
            Identity = identity,
            DistinguishedName = movedDistinguishedName,
            DomainName = resolvedDomainName ?? string.Empty,
            Changed = !string.Equals(sourceDistinguishedName, movedDistinguishedName, StringComparison.OrdinalIgnoreCase),
            Message = string.IsNullOrWhiteSpace(newRdn) ? "Computer moved." : "Computer moved and renamed.",
            UpdatedAttributes = string.IsNullOrWhiteSpace(newRdn)
                ? new[] { "distinguishedName" }
                : new[] { "cn", "distinguishedName", "name" },
            TimestampUtc = DateTime.UtcNow
        };
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

    private static string CreateSuccessResponse(ComputerLifecycleResult result) {
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

        if (!string.IsNullOrWhiteSpace(result.TargetOrganizationalUnit)) {
            facts.Add(("Target organizational unit", result.TargetOrganizationalUnit));
        }

        if (!string.IsNullOrWhiteSpace(result.ComputerName)) {
            facts.Add(("Computer name", result.ComputerName));
        }

        if (!string.IsNullOrWhiteSpace(result.Message)) {
            facts.Add(("Message", result.Message));
        }

        if (result.ServicePrincipalNames.Count > 0) {
            facts.Add(("SPNs", string.Join(", ", result.ServicePrincipalNames)));
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

        if (!string.IsNullOrWhiteSpace(result.TargetOrganizationalUnit)) {
            meta.Add("target_organizational_unit", result.TargetOrganizationalUnit);
        }

        if (!string.IsNullOrWhiteSpace(result.ComputerName)) {
            meta.Add("computer_name", result.ComputerName);
        }

        if (result.ServicePrincipalNames.Count > 0) {
            meta.Add("service_principal_name_count", result.ServicePrincipalNames.Count);
        }

        if (result.PasswordReset) {
            meta.Add("password_reset", true);
        }

        return ToolResultV2.OkWriteActionModel(
            model: result,
            action: $"ad_computer_{result.Operation}",
            writeApplied: result.Apply,
            facts: facts,
            meta: meta,
            summaryTitle: "AD computer lifecycle");
    }
}
