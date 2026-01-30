using System;
using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class GitHubAppJwt {
    public static string Create(long appId, string pem) {
        var now = DateTimeOffset.UtcNow;
        var iat = now.AddSeconds(-30).ToUnixTimeSeconds();
        var exp = now.AddMinutes(9).ToUnixTimeSeconds();

        var headerJson = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        var payloadJson = $"{{\"iat\":{iat},\"exp\":{exp},\"iss\":{appId}}}";

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var unsignedToken = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsignedToken), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sig = Base64UrlEncode(signature);
        return $"{unsignedToken}.{sig}";
    }

    private static string Base64UrlEncode(byte[] bytes) {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
