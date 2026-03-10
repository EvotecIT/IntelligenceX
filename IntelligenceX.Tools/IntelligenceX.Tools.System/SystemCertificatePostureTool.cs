using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Certificates;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns certificate-store posture for the local or remote Windows host.
/// </summary>
public sealed class SystemCertificatePostureTool : SystemToolBase, ITool {
    private sealed record CertificatePostureRequest(
        string? ComputerName,
        string Target,
        int RecentWindowDays,
        bool IncludeCertificates,
        int MaxCertificatesPerStore);

    private sealed record CertificateStoreResponse(
        CertificateStoreKind StoreKind,
        string StoreName,
        int CertificateCount,
        int SelfSignedCount,
        int RecentlyIssuedCount,
        int ExpiredCount,
        int NotYetValidCount,
        int SuspiciousCount,
        bool CertificatesTruncated,
        IReadOnlyList<CertificateInventoryItem> Certificates);

    private sealed record CertificatePostureResponse(
        string ComputerName,
        bool CollectedLocally,
        bool RemoteCollectionSupported,
        int RecentWindowDays,
        int TotalCertificates,
        int TotalSuspicious,
        IReadOnlyList<CertificateStoreResponse> Stores);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_certificate_posture",
        "Return machine certificate-store posture for the local or remote Windows host (ROOT/CA and related host stores), not LDAP/LDAPS service endpoint certificates.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("recent_window_days", ToolSchema.Integer("Recent issuance window in days. Default 30.")),
                ("include_certificates", ToolSchema.Boolean("When true, include capped per-store certificate rows. Default false.")),
                ("max_certificates_per_store", ToolSchema.Integer("Optional maximum certificate rows to keep per store when include_certificates=true (capped). Default 25.")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:machine_certificate_store", "scope:host_certificate_store" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemCertificatePostureTool"/> class.
    /// </summary>
    public SystemCertificatePostureTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<CertificatePostureRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var recentWindowDays = ToolArgs.GetCappedInt32(arguments, "recent_window_days", 30, 1, 3650);
            var includeCertificates = reader.Boolean("include_certificates", defaultValue: false);
            var maxCertificatesPerStore = ToolArgs.GetCappedInt32(arguments, "max_certificates_per_store", 25, 1, 250);
            return ToolRequestBindingResult<CertificatePostureRequest>.Success(new CertificatePostureRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                RecentWindowDays: recentWindowDays,
                IncludeCertificates: includeCertificates,
                MaxCertificatesPerStore: maxCertificatesPerStore));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<CertificatePostureRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_certificate_posture");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var posture = await CertificatePosture
                .GetAsync(request.ComputerName, request.RecentWindowDays, cancellationToken)
                .ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(posture.ComputerName) ? request.Target : posture.ComputerName;
            var stores = new CertificateStoreResponse[posture.Stores.Count];
            for (var i = 0; i < posture.Stores.Count; i++) {
                stores[i] = MapStore(posture.Stores[i], request.IncludeCertificates, request.MaxCertificatesPerStore);
            }

            var model = new CertificatePostureResponse(
                ComputerName: effectiveComputerName,
                CollectedLocally: posture.CollectedLocally,
                RemoteCollectionSupported: posture.RemoteCollectionSupported,
                RecentWindowDays: posture.RecentWindowDays,
                TotalCertificates: posture.TotalCertificates,
                TotalSuspicious: posture.TotalSuspicious,
                Stores: stores);

            return ToolResultV2.OkAutoTableResponse(
                arguments: context.Arguments,
                model: model,
                sourceRows: stores,
                viewRowsPath: "stores_view",
                title: "Certificate posture (preview)",
                maxTop: MaxViewTop,
                baseTruncated: false,
                scanned: posture.TotalCertificates,
                metaMutate: meta => {
                    AddComputerNameMeta(meta, effectiveComputerName);
                    meta.Add("recent_window_days", request.RecentWindowDays);
                    meta.Add("include_certificates", request.IncludeCertificates);
                    if (request.IncludeCertificates) {
                        meta.Add("max_certificates_per_store", request.MaxCertificatesPerStore);
                    }

                    AddReadOnlyPostureChainingMeta(
                        meta: meta,
                        currentTool: "system_certificate_posture",
                        targetComputer: effectiveComputerName,
                        isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                        scanned: posture.TotalCertificates,
                        truncated: false);
                });
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Certificate posture query failed.");
        }
    }

    private static CertificateStoreResponse MapStore(CertificateStoreInventory store, bool includeCertificates, int maxCertificatesPerStore) {
        IReadOnlyList<CertificateInventoryItem> certificates = Array.Empty<CertificateInventoryItem>();
        var truncated = false;
        if (includeCertificates) {
            certificates = CapRows(store.Certificates, maxCertificatesPerStore, out _, out truncated);
        }

        return new CertificateStoreResponse(
            StoreKind: store.StoreKind,
            StoreName: store.StoreName,
            CertificateCount: store.CertificateCount,
            SelfSignedCount: store.SelfSignedCount,
            RecentlyIssuedCount: store.RecentlyIssuedCount,
            ExpiredCount: store.ExpiredCount,
            NotYetValidCount: store.NotYetValidCount,
            SuspiciousCount: store.SuspiciousCount,
            CertificatesTruncated: truncated,
            Certificates: certificates);
    }
}
