using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Matches Python license_guard token format (version 2).
/// Canonical payload for Ed25519 verify: sorted-key JSON, no whitespace.
/// </summary>
internal sealed class LicenseToken
{
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("expiry")]      public long   Expiry      { get; set; }
    [JsonPropertyName("signature")]   public string Signature   { get; set; } = "";
    [JsonPropertyName("version")]     public int    Version     { get; set; }
    [JsonPropertyName("token")]       public string Token       { get; set; } = "";
    [JsonPropertyName("product_id")]  public string ProductId   { get; set; } = "";

    // Runtime-only metadata (stored in license.dat — not part of signature)
    [JsonPropertyName("_last_seen")]              public double LastSeen           { get; set; }
    [JsonPropertyName("_last_online_check")]      public double LastOnlineCheck    { get; set; }
    [JsonPropertyName("_heartbeat_grace_days")]   public int    HeartbeatGraceDays { get; set; } = 30;
    /// <summary>
    /// Last time we obtained a trusted time value (NTP or heartbeat).
    /// ⚠️ Python persists _last_trusted_time and enforces _MAX_OFFLINE_WINDOW_DAYS = 7:
    /// if no trusted time in 7 days, fail closed regardless of local clock.
    /// C# mirrors this with LastTrustedTime + the 7-day check in VerifyToken.
    /// </summary>
    [JsonPropertyName("_last_trusted_time")]      public double LastTrustedTime    { get; set; }

    /// <summary>
    /// Verify Ed25519 signature against the embedded public key PEM.
    /// Canonical payload MUST match Python _canonical_license_payload() exactly:
    ///   {"expiry":<int>,"fingerprint":"<hex>","version":2}  (keys sorted, no spaces)
    /// </summary>
    public static LicenseToken FromSignedToken(string token, string fingerprint, string productId)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new ArgumentException("Signed token must be a compact JWS with 3 parts.", nameof(token));

        var payloadBytes = Base64UrlDecode(parts[1]);
        using var payloadDoc = JsonDocument.Parse(payloadBytes);
        var root = payloadDoc.RootElement;

        if (!root.TryGetProperty("exp", out var expiry) || !expiry.TryGetInt64(out var expiryValue))
            throw new ArgumentException("Signed token payload is missing a valid exp claim.", nameof(token));

        return new LicenseToken
        {
            Fingerprint = fingerprint,
            ProductId = productId,
            Token = token,
            Expiry = expiryValue,
            Version = 3,
        };
    }

    public bool VerifySignature(string publicKeyPem)
        => VerifySignature(publicKeyPem, ProductId, null);

    public bool VerifySignature(string publicKeyPem, string expectedProductId, string? expectedIssuer)
    {
        if (!string.IsNullOrWhiteSpace(Token))
            return VerifyJwsSignature(publicKeyPem, expectedProductId, expectedIssuer);

        return VerifyLegacySignature(publicKeyPem);
    }

    public bool IsSupportedFormat()
        => !string.IsNullOrWhiteSpace(Token) || Version == 2;

    private bool VerifyLegacySignature(string publicKeyPem)
    {
        try
        {
            if (Version != 2) return false;

            // Build canonical payload — MUST match Python json.dumps(sort_keys=True, separators=(',',':'))
            var canonical = $"{{\"expiry\":{Expiry},\"fingerprint\":\"{Fingerprint}\",\"version\":2}}";
            var message   = Encoding.UTF8.GetBytes(canonical);

            // Base64url → base64 → bytes (pad to multiple of 4)
            var padded = Signature.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var sigBytes = Convert.FromBase64String(padded);

            // Parse PEM: strip headers, base64-decode to DER (SubjectPublicKeyInfo)
            var pem = publicKeyPem.Trim();
            var b64 = pem
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----",   "")
                .Replace("\r", "").Replace("\n", "").Trim();
            var der = Convert.FromBase64String(b64);

            // Validate Ed25519 SPKI OID prefix: 30 2A 30 05 06 03 2B 65 70 03 21 00 (12 bytes)
            // This is the fixed DER encoding for "SubjectPublicKeyInfo { Ed25519 }".
            ReadOnlySpan<byte> ed25519SpkiPrefix = [
                0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
            ];
            if (der.Length != 44) return false; // Ed25519 SPKI is always exactly 44 bytes
            if (!((ReadOnlySpan<byte>)der[..12]).SequenceEqual(ed25519SpkiPrefix)) return false;

            // Extract raw 32-byte public key (bytes 12..43)
            var rawKey = der[12..];

            var algo   = SignatureAlgorithm.Ed25519;
            var pubKey = PublicKey.Import(algo, rawKey, KeyBlobFormat.RawPublicKey);
            return algo.Verify(pubKey, message, sigBytes);
        }
        catch
        {
            return false;
        }
    }

    private bool VerifyJwsSignature(string publicKeyPem, string expectedProductId, string? expectedIssuer)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Token)) return false;

            var parts = Token.Split('.');
            if (parts.Length != 3) return false;

            var headerBytes = Base64UrlDecode(parts[0]);
            var payloadBytes = Base64UrlDecode(parts[1]);
            var signatureBytes = Base64UrlDecode(parts[2]);

            using var headerDoc = JsonDocument.Parse(headerBytes);
            using var payloadDoc = JsonDocument.Parse(payloadBytes);
            var header = headerDoc.RootElement;
            var payload = payloadDoc.RootElement;

            if (!header.TryGetProperty("alg", out var alg) || alg.GetString() != "EdDSA") return false;
            if (!payload.TryGetProperty("aud", out var aud) || aud.GetString() != expectedProductId) return false;
            if (!payload.TryGetProperty("fp", out var fp) || fp.GetString() != ComputeFpClaim(expectedProductId, Fingerprint)) return false;
            if (!payload.TryGetProperty("exp", out var exp) || exp.GetInt64() != Expiry) return false;
            if (!string.IsNullOrWhiteSpace(expectedIssuer)
                && (!payload.TryGetProperty("iss", out var iss) || iss.GetString() != expectedIssuer)) return false;

            var message = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var rawKey = ExtractRawPublicKey(publicKeyPem);
            var algo = SignatureAlgorithm.Ed25519;
            var pubKey = PublicKey.Import(algo, rawKey, KeyBlobFormat.RawPublicKey);
            return algo.Verify(pubKey, message, signatureBytes);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ExtractRawPublicKey(string publicKeyPem)
    {
        var pem = publicKeyPem.Trim();
        var b64 = pem
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----",   "")
            .Replace("\r", "").Replace("\n", "").Trim();
        var der = Convert.FromBase64String(b64);

        ReadOnlySpan<byte> ed25519SpkiPrefix = [
            0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
        ];
        if (der.Length != 44) throw new ArgumentException("Ed25519 SPKI is not the expected length.", nameof(publicKeyPem));
        if (!((ReadOnlySpan<byte>)der[..12]).SequenceEqual(ed25519SpkiPrefix))
            throw new ArgumentException("Public key is not an Ed25519 SubjectPublicKeyInfo PEM.", nameof(publicKeyPem));

        return der[12..];
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static string ComputeFpClaim(string productId, string fingerprint)
    {
        var bytes = Encoding.UTF8.GetBytes($"{productId}:{fingerprint}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public bool IsExpired(double effectiveTime) => effectiveTime > Expiry;
    public bool FingerprintMatches(string fp)   => Fingerprint == fp;
}
