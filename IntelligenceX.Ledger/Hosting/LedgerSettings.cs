using System.Net;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>Machine-local configuration, loaded explicitly rather than from a repository file.</summary>
public sealed record LedgerSettings {
    public required string DataDirectory { get; init; }
    public required string ReadToken { get; init; }
    public required string WriteToken { get; init; }
    public string? LanAddress { get; init; }
    public int Port { get; init; } = 7348;
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
    public Dictionary<string, string[]> ExpectedReporters { get; init; } = [];
    public RuleSettings Rules { get; init; } = new();
    public string? ProjectEvidenceReporterId { get; init; }

    public static LedgerSettings Load(string file) {
        var settings = JsonSerializer.Deserialize<LedgerSettings>(File.ReadAllText(file), LedgerJson.Options)
            ?? throw new InvalidDataException("Missing settings.");
        if (!Path.IsPathFullyQualified(settings.DataDirectory) || settings.ReadToken.Length < 32 ||
            settings.WriteToken.Length < 32 || settings.ReadToken == settings.WriteToken ||
            settings.Port is < 1024 or > 65535 || settings.ExpectedReporters.Count == 0)
            throw new InvalidDataException("Invalid ledger settings.");
        var reporters = settings.ExpectedReporters.Values.SelectMany(x => x).ToArray();
        if (settings.ExpectedReporters.Values.Any(x => x.Length == 0) || reporters.Distinct(StringComparer.Ordinal).Count() != reporters.Length)
            throw new InvalidDataException("Expected reporter IDs must be globally unique and nonempty.");
        if (settings.ProjectEvidenceReporterId is not null && !reporters.Contains(settings.ProjectEvidenceReporterId))
            throw new InvalidDataException("Project evidence requires an enrolled reporter.");
        if (settings.LanAddress is not null) {
            var ip = IPAddress.Parse(settings.LanAddress);
            if (!IsPrivateLan(ip) || IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) ||
                !System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(n =>
                    n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(ip)))
                throw new InvalidDataException("LAN address must identify a local interface.");
            if (settings.CertificatePath is null) throw new InvalidDataException("LAN access requires TLS.");
        }
        foreach (var interval in new[] { settings.Rules.RecentActivity, settings.Rules.AdmissionGrace,
            settings.Rules.InterruptionGrace, settings.Rules.NoProgressAfter, settings.Rules.ReporterTimeout, settings.Rules.ReviewWait,
            settings.Rules.EvidenceMaxAge })
            if (interval <= TimeSpan.Zero) throw new InvalidDataException("Rule thresholds must be positive.");
        if (settings.Rules.RecentActivity >= settings.Rules.NoProgressAfter) throw new InvalidDataException("Invalid activity windows.");
        return settings;
    }

    private static bool IsPrivateLan(IPAddress address) {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4) return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254;
        return address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
    }
}
