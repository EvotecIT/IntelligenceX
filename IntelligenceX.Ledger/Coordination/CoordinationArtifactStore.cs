using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Service-owned immutable result files, separate from observation events and maintained project knowledge.
/// The coordination writer serializes admission and writes; callers never supply a filesystem path.</summary>
internal sealed class CoordinationArtifactStore(string root) {
    internal const string ArchiveDirectoryName = "archive";
    private const int MaximumContentBytes = 8192;
    private const long MaximumStoreBytes = 64 * 1024 * 1024;
    private const int MaximumStoreFiles = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal CoordinationArtifact Put(string requestId, long generation, string grantId, string content) =>
        Put(requestId, generation, grantId, content, null);

    internal CoordinationArtifact Put(string requestId, long generation, string grantId, string content, CoordinationState? state) {
        CoordinationValidation.Require(!string.IsNullOrWhiteSpace(content) && content.Length <= MaximumContentBytes,
            "invalid-result-content");
        byte[] bytes;
        try { bytes = Utf8.GetBytes(content); }
        catch (EncoderFallbackException) { throw new CoordinationConflictException("invalid-result-content"); }
        CoordinationValidation.Require(bytes.Length <= MaximumContentBytes, "result-content-too-large");
        var id = "result-" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(root);
        RejectLink(root);
        var assignmentName = AssignmentName(requestId, generation, grantId);
        var assignment = Find(assignmentName);
        if (assignment is not null) {
            CoordinationValidation.Require(new FileInfo(assignment).Length == 71 && File.ReadAllText(assignment, Utf8) == id,
                "result-content-identity-conflict");
            return Read(id) ?? throw new CoordinationConflictException("result-content-unavailable");
        }
        if (!HasCapacity(id, bytes.Length)) {
            if (state is not null) ArchiveCompleted(state);
            CoordinationValidation.Require(HasCapacity(id, bytes.Length), "result-store-capacity");
        }
        if (Find(id + ".txt") is not null) _ = Read(id);
        else WriteImmutable(Path.Combine(root, id + ".txt"), bytes);
        WriteImmutable(Path.Combine(root, assignmentName), Utf8.GetBytes(id));
        return new(id, content);
    }

    internal CoordinationArtifact? ReadForAssignment(string requestId, long generation, string grantId, string expectedRef) {
        if (!Directory.Exists(root)) return null;
        RejectLink(root);
        var assignment = Find(AssignmentName(requestId, generation, grantId));
        if (assignment is null) return null;
        CoordinationValidation.Require(new FileInfo(assignment).Length == 71, "result-content-unavailable");
        var bound = File.ReadAllText(assignment, Utf8);
        CoordinationValidation.Require(bound == expectedRef, "result-assignment-conflict");
        return Read(bound);
    }

    private static string AssignmentName(string requestId, long generation, string grantId) =>
        "assignment-" + Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(JsonSerializer.Serialize(
            new[] { requestId, generation.ToString(System.Globalization.CultureInfo.InvariantCulture), grantId })))) + ".txt";

    private string? Find(string name) {
        var active = Path.Combine(root, name);
        if (Path.Exists(active)) {
            RejectLink(active);
            return active;
        }
        var archive = Path.Combine(root, ArchiveDirectoryName);
        if (!Path.Exists(archive)) return null;
        RejectLink(archive);
        var archived = Path.Combine(archive, name);
        if (!Path.Exists(archived)) return null;
        RejectLink(archived);
        return archived;
    }

    private bool HasCapacity(string id, int contentBytes) {
        var retained = Directory.EnumerateFiles(root).Take(MaximumStoreFiles + 1).Select(path => new FileInfo(path)).ToArray();
        var newContent = Find(id + ".txt") is null;
        return retained.Length + (newContent ? 2 : 1) <= MaximumStoreFiles &&
            retained.Sum(info => info.Length) + 71 + (newContent ? contentBytes : 0) <= MaximumStoreBytes;
    }

    // Moves are independent of journal commits. Every intermediate layout resolves through both tiers,
    // so shared content and a crash between content and binding moves preserve the same immutable replay.
    private void ArchiveCompleted(CoordinationState state) {
        foreach (var work in state.Work.Values.Where(work => CanArchive(work, state))) {
            var assignment = Path.Combine(root, AssignmentName(work.Request.Id, work.Generation, work.GrantId!));
            var content = Path.Combine(root, work.ResultRef + ".txt");
            if (!Path.Exists(assignment) && !Path.Exists(content)) continue;
            CoordinationValidation.Require(ReadForAssignment(work.Request.Id, work.Generation, work.GrantId!, work.ResultRef!) is not null,
                "result-content-unavailable");
            var archive = Path.Combine(root, ArchiveDirectoryName);
            Directory.CreateDirectory(archive);
            RejectLink(archive);
            MoveToArchive(content, archive);
            MoveToArchive(assignment, archive);
        }
    }

    private static bool CanArchive(CoordinationWork work, CoordinationState state) {
        if (work.State is not ("succeeded" or "failed" or "cancelled") || work.Request.ReturnTo is null ||
            work.GrantId is null || work.ResultRef is null ||
            !state.ResultOutbox.TryGetValue(work.Request.Id, out var result) || result.RequestId != work.Request.Id ||
            result.Generation != work.Generation || result.ResultRef != work.ResultRef || result.ConsumedAtUtc is null ||
            result.Candidate != work.Request.Checkpoint || result.ReturnTo != work.Request.ReturnTo) return false;
        if (!state.Bindings.TryGetValue(work.Request.Id, out var binding)) return NeverCreated(work, state);
        if (binding.RequestId != work.Request.Id ||
            binding.Generation != work.Generation || binding.GrantId != work.GrantId || binding.WorkerId != work.Assignee ||
            binding.MachineId != work.Machine || !CoordinationEngine.SameCandidate(binding.Candidate, work.Request.Checkpoint) ||
            !state.Workers.TryGetValue(binding.WorkerId, out var worker)) return false;
        if (worker.TemporaryWorker)
            return state.Retirements.TryGetValue(work.Request.Id, out var retirement) && retirement.Binding == binding &&
                retirement.ResultRef == work.ResultRef && retirement.CompletedAtUtc is not null &&
                retirement.WaitingOn is null && retirement.Evidence.ContainsKey("verified");
        return CoordinationEngine.ExplicitlyReusesSession(work.Request, worker) &&
            !state.Retirements.ContainsKey(work.Request.Id);
    }

    private static bool NeverCreated(CoordinationWork work, CoordinationState state) =>
        work.State == "cancelled" && work.Request.Kind is "help" or "review" && work.Request.WorkerId is null &&
        work.Provider is not null && work.Assignee is null && work.ExecutionId is null &&
        !state.Retirements.ContainsKey(work.Request.Id) &&
        state.Reconciliations.TryGetValue(work.Request.Id, out var exclusion) &&
        exclusion.RequestId == work.Request.Id && exclusion.Generation == work.Generation &&
        exclusion.GrantId == work.GrantId && exclusion.OriginalBootId == work.BootId &&
        exclusion.ResultRef == work.ResultRef && exclusion.Disposition == "never-created" &&
        exclusion.NativeExclusionVerified && exclusion.Binding is null && exclusion.ExecutionId is null;

    private static void MoveToArchive(string source, string archive) {
        if (!Path.Exists(source)) return;
        RejectLink(source);
        var destination = Path.Combine(archive, Path.GetFileName(source));
        if (Path.Exists(destination)) {
            RejectLink(destination);
            CoordinationValidation.Require(new FileInfo(source).Length == new FileInfo(destination).Length &&
                File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(destination)), "result-content-conflict");
            return;
        }
        File.Move(source, destination, overwrite: false);
    }

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
        var file = Find(id + ".txt");
        if (file is null) return null;
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
