using System;
using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.OpenAI.Auth;

internal static class OAuthPkce {
    public static string CreateCodeVerifier() {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string CreateCodeChallenge(string verifier) {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] data) {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
