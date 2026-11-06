using System.Text.Json;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Explicit local read credentials shared by CLI, MCP, tray and tile clients.</summary>
public sealed record LedgerClientSettings {
    public required string Endpoint { get; init; }
    public required string ReadToken { get; init; }
    public string? CertificateSha256 { get; init; }

    /// <summary>Loads a bounded machine-local file and validates its transport and credentials.</summary>
    public static LedgerClientSettings Load(string path) {
        if (new FileInfo(path).Length > 65_536) throw new InvalidDataException("Ledger client settings exceed their bound.");
        var settings = JsonSerializer.Deserialize<LedgerClientSettings>(File.ReadAllText(path), LedgerReadJson.Default.LedgerClientSettings)
            ?? throw new InvalidDataException("Ledger client settings are missing.");
        settings.Validate();
        return settings;
    }

    /// <summary>Permits HTTPS or loopback HTTP and rejects ambiguous endpoint components.</summary>
    public void Validate() {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
            ReadToken is null || ReadToken.Length < 32 || ReadToken.Any(char.IsWhiteSpace) ||
            CertificateSha256 is not null && (CertificateSha256.Length != 64 || !CertificateSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid Ledger read settings.");
    }
}
