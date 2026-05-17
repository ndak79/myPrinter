using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace MyPrinter.Desktop.Activation;

internal sealed class LicenseToken
{
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("expiry")] public long Expiry { get; set; }
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("product_id")] public string ProductId { get; set; } = "";

    [JsonPropertyName("_last_seen")] public double LastSeen { get; set; }
    [JsonPropertyName("_last_online_check")] public double LastOnlineCheck { get; set; }
    [JsonPropertyName("_heartbeat_required")] public bool? HeartbeatRequired { get; set; }
    [JsonPropertyName("_heartbeat_grace_days")] public int HeartbeatGraceDays { get; set; } = 30;
    [JsonPropertyName("_last_trusted_time")] public double LastTrustedTime { get; set; }

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

        var heartbeatRequired = true;
        if (root.TryGetProperty("heartbeat_required", out var heartbeatRequiredProp))
        {
            if (heartbeatRequiredProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("Signed token payload heartbeat_required claim must be a boolean.", nameof(token));
            heartbeatRequired = heartbeatRequiredProp.GetBoolean();
        }

        var heartbeatGraceDays = 30;
        if (root.TryGetProperty("heartbeat_grace_days", out var heartbeatGraceDaysProp))
        {
            if (!heartbeatGraceDaysProp.TryGetInt32(out heartbeatGraceDays) || heartbeatGraceDays is < 0 or > 365)
                throw new ArgumentException("Signed token payload heartbeat_grace_days claim must be an integer from 0 to 365.", nameof(token));
        }

        return new LicenseToken
        {
            Fingerprint = fingerprint,
            ProductId = productId,
            Token = token,
            Expiry = expiryValue,
            Version = 3,
            HeartbeatRequired = heartbeatRequired,
            HeartbeatGraceDays = heartbeatGraceDays,
        };
    }

    public bool VerifySignature(string publicKeyPem)
        => VerifySignature(publicKeyPem, ProductId, null);

    public bool VerifySignature(string publicKeyPem, string expectedProductId, string? expectedIssuer)
    {
        if (!string.IsNullOrWhiteSpace(Token))
            return VerifyJwsSignature(ExtractRawPublicKey(publicKeyPem), expectedProductId, expectedIssuer);

        return VerifyLegacySignature(ExtractRawPublicKey(publicKeyPem));
    }

    public bool VerifySignatureWithKeyset(string keysetJson, string expectedProductId, string? expectedIssuer)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Token))
            {
                var parts = Token.Split('.');
                if (parts.Length != 3)
                    return false;

                using var headerDoc = JsonDocument.Parse(Base64UrlDecode(parts[0]));
                var header = headerDoc.RootElement;
                if (!header.TryGetProperty("kid", out var kid) || string.IsNullOrWhiteSpace(kid.GetString()))
                    return false;

                var rawKey = FindKeyByKid(keysetJson, kid.GetString()!);
                return VerifyJwsSignature(rawKey, expectedProductId, expectedIssuer);
            }

            foreach (var rawKey in GetAllKeys(keysetJson))
            {
                if (VerifyLegacySignature(rawKey))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool IsSupportedFormat()
        => !string.IsNullOrWhiteSpace(Token) || Version == 2;

    public static byte[] DecodeKeysetEntry(string encodedKey)
    {
        var rawKey = Base64UrlDecode(encodedKey);
        if (rawKey.Length != 32)
            throw new ArgumentException("Keyset entries must decode to a 32-byte Ed25519 public key.", nameof(encodedKey));

        var algo = SignatureAlgorithm.Ed25519;
        _ = PublicKey.Import(algo, rawKey, KeyBlobFormat.RawPublicKey);
        return rawKey;
    }

    private bool VerifyLegacySignature(byte[] rawKey)
    {
        try
        {
            if (Version != 2)
                return false;

            var canonical = $"{{\"expiry\":{Expiry},\"fingerprint\":\"{Fingerprint}\",\"version\":2}}";
            var message = Encoding.UTF8.GetBytes(canonical);

            var padded = Signature.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var sigBytes = Convert.FromBase64String(padded);

            var algo = SignatureAlgorithm.Ed25519;
            var pubKey = PublicKey.Import(algo, rawKey, KeyBlobFormat.RawPublicKey);
            return algo.Verify(pubKey, message, sigBytes);
        }
        catch
        {
            return false;
        }
    }

    private bool VerifyJwsSignature(byte[] rawKey, string expectedProductId, string? expectedIssuer)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Token))
                return false;

            var parts = Token.Split('.');
            if (parts.Length != 3)
                return false;

            var headerBytes = Base64UrlDecode(parts[0]);
            var payloadBytes = Base64UrlDecode(parts[1]);
            var signatureBytes = Base64UrlDecode(parts[2]);

            using var headerDoc = JsonDocument.Parse(headerBytes);
            using var payloadDoc = JsonDocument.Parse(payloadBytes);
            var header = headerDoc.RootElement;
            var payload = payloadDoc.RootElement;

            if (!header.TryGetProperty("alg", out var alg) || alg.GetString() != "EdDSA")
                return false;
            if (!payload.TryGetProperty("aud", out var aud) || aud.GetString() != expectedProductId)
                return false;
            if (!payload.TryGetProperty("fp", out var fp) || fp.GetString() != ComputeFpClaim(expectedProductId, Fingerprint))
                return false;
            if (!payload.TryGetProperty("exp", out var exp) || exp.GetInt64() != Expiry)
                return false;
            if (!string.IsNullOrWhiteSpace(expectedIssuer) &&
                (!payload.TryGetProperty("iss", out var iss) || iss.GetString() != expectedIssuer))
                return false;
            if (payload.TryGetProperty("heartbeat_required", out var heartbeatRequired))
            {
                if (heartbeatRequired.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;

                var signedHeartbeatRequired = heartbeatRequired.GetBoolean();
                if (HeartbeatRequired.HasValue && HeartbeatRequired.Value != signedHeartbeatRequired)
                    return false;
                HeartbeatRequired = signedHeartbeatRequired;
            }
            else if (!HeartbeatRequired.HasValue)
            {
                HeartbeatRequired = true;
            }

            if (payload.TryGetProperty("heartbeat_grace_days", out var heartbeatGraceDays))
            {
                if (!heartbeatGraceDays.TryGetInt32(out var signedHeartbeatGraceDays) || signedHeartbeatGraceDays is < 0 or > 365)
                    return false;
                if (HeartbeatGraceDays != signedHeartbeatGraceDays)
                    return false;
            }

            var message = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var algoType = SignatureAlgorithm.Ed25519;
            var pubKey = PublicKey.Import(algoType, rawKey, KeyBlobFormat.RawPublicKey);
            return algoType.Verify(pubKey, message, signatureBytes);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] FindKeyByKid(string keysetJson, string kid)
    {
        using var doc = JsonDocument.Parse(keysetJson);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("kid", out var candidateKid) || candidateKid.GetString() != kid)
                continue;
            if (!entry.TryGetProperty("key", out var key))
                break;

            return DecodeKeysetEntry(key.GetString() ?? "");
        }

        throw new ArgumentException($"Keyset does not contain kid '{kid}'.", nameof(kid));
    }

    private static byte[][] GetAllKeys(string keysetJson)
    {
        var keys = new List<byte[]>();
        using var doc = JsonDocument.Parse(keysetJson);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("key", out var key))
                keys.Add(DecodeKeysetEntry(key.GetString() ?? ""));
        }

        if (keys.Count == 0)
            throw new ArgumentException("Keyset JSON does not contain any keys.", nameof(keysetJson));

        return keys.ToArray();
    }

    private static byte[] ExtractRawPublicKey(string publicKeyPem)
    {
        var pem = publicKeyPem.Trim();
        var b64 = pem
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();
        var der = Convert.FromBase64String(b64);

        ReadOnlySpan<byte> ed25519SpkiPrefix =
        [
            0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
        ];

        if (der.Length != 44)
            throw new ArgumentException("Ed25519 SPKI is not the expected length.", nameof(publicKeyPem));
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
    public bool FingerprintMatches(string fp) => Fingerprint == fp;
}
