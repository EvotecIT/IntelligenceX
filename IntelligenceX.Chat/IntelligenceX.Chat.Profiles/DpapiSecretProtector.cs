using System;
using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Chat.Profiles;

internal static class DpapiSecretProtector {
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IntelligenceX.Chat.ServiceProfile.ApiKey.v1");

    public static byte[] ProtectString(string value) {
        if (value is null) throw new ArgumentNullException(nameof(value));
        var plaintext = Encoding.UTF8.GetBytes(value);
        try {
            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        } finally {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string UnprotectString(byte[] protectedBytes) {
        if (protectedBytes is null) throw new ArgumentNullException(nameof(protectedBytes));
        var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try {
            return Encoding.UTF8.GetString(plaintext);
        } finally {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
