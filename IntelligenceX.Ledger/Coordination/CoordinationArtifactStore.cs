using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Service-owned immutable result files, separate from observation events and maintained project knowledge.
/// The coordination writer serializes admission and writes; callers never supply a filesystem path.</summary>
internal sealed class CoordinationArtifactStore(string root) {
    private const int MaximumContentBytes = 8192;
    private const long MaximumStoreBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal CoordinationArtifact Put(string requestId, long generation, string grantId, string content) {
        CoordinationValidation.Require(!string.IsNullOrWhiteSpace(content) && content.Length <= MaximumContentBytes,
            "invalid-result-content");
        byte[] bytes;
        try { bytes = Utf8.GetBytes(content); }
        catch (EncoderFallbackException) { throw new CoordinationConflictException("invalid-result-content"); }
        CoordinationValidation.Require(bytes.Length <= MaximumContentBytes, "result-content-too-large");
        var id = "result-" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(root);
        RejectLink(root);
        var assignment = AssignmentPath(requestId, generation, grantId);
        if (File.Exists(assignment)) {
            RejectLink(assignment);
            CoordinationValidation.Require(new FileInfo(assignment).Length == 71 && File.ReadAllText(assignment, Utf8) == id,
                "result-content-identity-conflict");
            return Read(id) ?? throw new CoordinationConflictException("result-content-unavailable");
        }
        var file = Path.Combine(root, id + ".txt");
        var retained = Directory.EnumerateFiles(root).Take(4097).Select(path => new FileInfo(path)).ToArray();
        CoordinationValidation.Require(retained.Length < 4096 &&
            retained.Sum(info => info.Length) + bytes.Length <= MaximumStoreBytes, "result-store-capacity");
        if (File.Exists(file)) _ = Read(id);
        else WriteImmutable(file, bytes);
        WriteImmutable(assignment, Utf8.GetBytes(id));
        return new(id, content);
    }

    internal CoordinationArtifact? ReadForAssignment(string requestId, long generation, string grantId, string expectedRef) {
        if (!Directory.Exists(root)) return null;
        RejectLink(root);
        var assignment = AssignmentPath(requestId, generation, grantId);
        if (!File.Exists(assignment)) return null;
        RejectLink(assignment);
        CoordinationValidation.Require(new FileInfo(assignment).Length == 71, "result-content-unavailable");
        var bound = File.ReadAllText(assignment, Utf8);
        CoordinationValidation.Require(bound == expectedRef, "result-assignment-conflict");
        return Read(bound);
    }

    private string AssignmentPath(string requestId, long generation, string grantId) => Path.Combine(root,
        "assignment-" + Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(JsonSerializer.Serialize(
            new[] { requestId, generation.ToString(System.Globalization.CultureInfo.InvariantCulture), grantId })))) + ".txt");

    private void WriteImmutable(string file, byte[] bytes) {
        var temporary = Path.Combine(root, ".partial-" + Guid.NewGuid().ToString("N"));
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, file, overwrite: false);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private CoordinationArtifact? Read(string id) {
        if (id.Length != 71 || !id.StartsWith("result-", StringComparison.Ordinal) ||
            !id.AsSpan(7).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
        if (!Directory.Exists(root)) return null;
        RejectLink(root);
        var file = Path.Combine(root, id + ".txt");
        if (!File.Exists(file)) return null;
        RejectLink(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        CoordinationValidation.Require(stream.Length is > 0 and <= MaximumContentBytes, "result-content-unavailable");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        CoordinationValidation.Require(id == "result-" + Convert.ToHexStringLower(SHA256.HashData(bytes)), "result-content-conflict");
        return new(id, Utf8.GetString(bytes));
    }

    private static void RejectLink(string path) => CoordinationValidation.Require(
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "result-storage-link");
}
