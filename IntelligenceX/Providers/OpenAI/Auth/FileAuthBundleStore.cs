using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// File-based authentication bundle store with optional encryption.
/// </summary>
public sealed class FileAuthBundleStore : IAuthBundleStore {
    private readonly string _path;
    private readonly byte[]? _encryptionKey;

    /// <summary>
    /// Initializes a file-based auth bundle store.
    /// </summary>
    /// <param name="path">Optional override path.</param>
    /// <param name="encryptionKeyBase64">Optional base64 encryption key.</param>
    public FileAuthBundleStore(string? path = null, string? encryptionKeyBase64 = null) {
        _path = path ?? AuthPaths.ResolveAuthPath();
        _encryptionKey = ParseKey(encryptionKeyBase64 ?? Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY"));
        if (_encryptionKey is not null && !SupportsEncryption()) {
            throw new PlatformNotSupportedException("Encrypted auth store requires .NET 8 or later.");
        }
    }

    /// <summary>
    /// Retrieves a bundle for the specified provider and account.
    /// </summary>
    /// <param name="provider">Provider identifier.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) {
        var file = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
        if (file is null) {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(accountId)) {
            var key = BuildKey(provider, accountId);
            return file.Bundles.TryGetValue(key, out var bundle) ? bundle : null;
        }
        // Deterministic selection when multiple bundles exist for the same provider.
        // Prefer the bundle with the farthest expiry; fall back to stable AccountId ordering.
        AuthBundle? best = null;
        foreach (var entry in file.Bundles.Values) {
            if (!string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (best is null) {
                best = entry;
                continue;
            }
            var bestExpiry = best.ExpiresAt ?? DateTimeOffset.MinValue;
            var candidateExpiry = entry.ExpiresAt ?? DateTimeOffset.MinValue;
            if (candidateExpiry > bestExpiry) {
                best = entry;
                continue;
            }
            if (candidateExpiry < bestExpiry) {
                continue;
            }
            var bestAccount = best.AccountId ?? string.Empty;
            var candidateAccount = entry.AccountId ?? string.Empty;
            if (string.CompareOrdinal(candidateAccount, bestAccount) < 0) {
                best = entry;
            }
        }
        return best;
    }

    /// <summary>
    /// Lists all bundles for the specified provider.
    /// </summary>
    public async Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(provider)) {
            return Array.Empty<AuthBundle>();
        }

        var file = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
        if (file is null) {
            return Array.Empty<AuthBundle>();
        }

        return file.Bundles.Values
            .Where(entry => string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.AccountId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(entry => entry.ExpiresAt ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    /// <summary>
    /// Saves an authentication bundle to disk.
    /// </summary>
    /// <param name="bundle">Bundle to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) {
        var file = await ReadFileAsync(cancellationToken).ConfigureAwait(false)
                   ?? new AuthBundleFile(1, new Dictionary<string, AuthBundle>(StringComparer.OrdinalIgnoreCase));
        var key = BuildKey(bundle.Provider, bundle.AccountId);
        file.Bundles[key] = bundle;
        await WriteFileAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the auth store file if it exists.
    /// </summary>
    public void Delete() {
        if (File.Exists(_path)) {
            File.Delete(_path);
        }
    }

    private async Task<AuthBundleFile?> ReadFileAsync(CancellationToken cancellationToken) {
        if (!File.Exists(_path)) {
            return null;
        }
        var content = await ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            return null;
        }
        if (IsEncrypted(content)) {
            if (_encryptionKey is null) {
                throw new InvalidOperationException("Auth store is encrypted but INTELLIGENCEX_AUTH_KEY is not set.");
            }
            if (!SupportsEncryption()) {
                throw new PlatformNotSupportedException("Encrypted auth store requires .NET 8 or later.");
            }
            content = Decrypt(content, _encryptionKey);
        }
        return AuthBundleSerializer.DeserializeFile(content);
    }

    private async Task WriteFileAsync(AuthBundleFile file, CancellationToken cancellationToken) {
        var content = AuthBundleSerializer.SerializeFile(file);
        if (_encryptionKey is not null) {
            content = Encrypt(content, _encryptionKey);
        }
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        await WriteAllTextAsync(_path, content, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildKey(string provider, string? accountId) {
        if (string.IsNullOrWhiteSpace(accountId)) {
            return provider;
        }
        return $"{provider}:{accountId}";
    }

    private static byte[]? ParseKey(string? keyBase64) {
        if (string.IsNullOrWhiteSpace(keyBase64)) {
            return null;
        }
        byte[] bytes;
        try {
            bytes = Convert.FromBase64String(keyBase64);
        } catch (Exception ex) {
            throw new InvalidOperationException("INTELLIGENCEX_AUTH_KEY must be base64 and decode to 32 bytes.", ex);
        }
        if (bytes.Length != 32) {
            throw new InvalidOperationException("INTELLIGENCEX_AUTH_KEY must be base64 and decode to 32 bytes.");
        }
        return bytes;
    }

    private static bool IsEncrypted(string content) {
        return content.StartsWith("{\"encrypted\":", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.ReadAllText(path));
#else
        return File.ReadAllTextAsync(path, cancellationToken);
#endif
    }

    private static Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(path, content);
        return Task.CompletedTask;
#else
        return File.WriteAllTextAsync(path, content, cancellationToken);
#endif
    }

    private static string Encrypt(string plaintext, byte[] key) {
#if NET8_0_OR_GREATER
        using var aes = new AesGcm(key, 16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        var wrapper = new AuthStoreEnvelope {
            Encrypted = true,
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(cipher),
            Tag = Convert.ToBase64String(tag)
        };
        return wrapper.ToJson();
#else
        throw new PlatformNotSupportedException("Encrypted auth store requires .NET 8 or later.");
#endif
    }

    private static string Decrypt(string payload, byte[] key) {
#if NET8_0_OR_GREATER
        var envelope = AuthStoreEnvelope.FromJson(payload);
        if (envelope is null || !envelope.Encrypted) {
            return payload;
        }
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var cipher = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        using var aes = new AesGcm(key, 16);
        var plain = new byte[cipher.Length];
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
#else
        throw new PlatformNotSupportedException("Encrypted auth store requires .NET 8 or later.");
#endif
    }

    private static bool SupportsEncryption() {
#if NET8_0_OR_GREATER
        return true;
#else
        return false;
#endif
    }

    private sealed class AuthStoreEnvelope {
        public bool Encrypted { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;

        public string ToJson() {
            return $"{{\"encrypted\":true,\"nonce\":\"{Nonce}\",\"ciphertext\":\"{Ciphertext}\",\"tag\":\"{Tag}\"}}";
        }

        public static AuthStoreEnvelope? FromJson(string json) {
            var value = Json.JsonLite.Parse(json);
            var obj = value?.AsObject();
            if (obj is null) {
                return null;
            }
            return new AuthStoreEnvelope {
                Encrypted = obj.GetBoolean("encrypted"),
                Nonce = obj.GetString("nonce") ?? string.Empty,
                Ciphertext = obj.GetString("ciphertext") ?? string.Empty,
                Tag = obj.GetString("tag") ?? string.Empty
            };
        }
    }
}
