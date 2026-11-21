using System.Security.Cryptography;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>
/// Images that asks and results carry, such as screenshots for a design review. Each file is named by the SHA-256 of
/// its bytes, so an upload is idempotent and a reader can check what it got. Only PNG, JPEG, WebP and GIF are kept,
/// recognised by their first bytes rather than by what the caller claims. Images older than the retention period make
/// room for new ones; the store never grows past its bounds.
/// </summary>
internal sealed class CoordinationImageStore(string root, long maximumBytes = 2L * 1024 * 1024 * 1024, int maximumFiles = 4096) {
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    internal CoordinationImage Put(byte[] bytes, DateTimeOffset now) {
        CoordinationValidation.Require(bytes.Length is > 0 and <= CoordinationImage.MaximumBytes, "image-too-large");
        var mediaType = CoordinationImage.Sniff(bytes);
        CoordinationValidation.Require(mediaType is not null, "image-type-unsupported");
        var id = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Directory.CreateDirectory(root);
        RejectLink(root);
        var file = Path.Combine(root, id + CoordinationImage.Extension(mediaType!));
        if (File.Exists(file)) {
            RejectLink(file);
            // Touching it keeps a picture that is still being used from being pruned as old.
            File.SetLastWriteTimeUtc(file, now.UtcDateTime);
            return new(id, mediaType!, bytes.Length);
        }
        MakeRoom(bytes.Length, now);
        var temporary = Path.Combine(root, ".partial-" + Guid.NewGuid().ToString("N"));
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, file, overwrite: false); }
            catch (IOException) when (File.Exists(file)) { }
            // Retention is judged on the service clock, the same one the next upload is checked against.
            File.SetLastWriteTimeUtc(file, now.UtcDateTime);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(id, mediaType!, bytes.Length);
    }

    /// <summary>The stored file and its media type, or null when the store has no such image.</summary>
    internal (string Path, string MediaType)? Find(string id) {
        if (!CoordinationImage.ValidId(id) || !Directory.Exists(root)) return null;
        RejectLink(root);
        foreach (var mediaType in new[] { "image/png", "image/jpeg", "image/webp", "image/gif" }) {
            var file = Path.Combine(root, id + CoordinationImage.Extension(mediaType));
            if (!File.Exists(file)) continue;
            RejectLink(file);
            return (file, mediaType);
        }
        return null;
    }

    internal bool Exists(string id) => Find(id) is not null;

    // Removes images past retention, oldest first, until the new one fits; refuses when even that is not enough.
    private void MakeRoom(long incoming, DateTimeOffset now) {
        var files = new DirectoryInfo(root).EnumerateFiles().Where(f => !f.Name.StartsWith(".partial-", StringComparison.Ordinal))
            .OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);
        while ((total + incoming > maximumBytes || files.Count + 1 > maximumFiles) && files.Count > 0 &&
               now.UtcDateTime - files[0].LastWriteTimeUtc > Retention) {
            total -= files[0].Length;
            try { files[0].Delete(); } catch (IOException) { }
            files.RemoveAt(0);
        }
        CoordinationValidation.Require(total + incoming <= maximumBytes && files.Count + 1 <= maximumFiles, "image-store-capacity");
    }

    private static void RejectLink(string path) => CoordinationValidation.Require(
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "image-storage-link");
}
