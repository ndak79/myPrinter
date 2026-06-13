using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace MyPrinter.Desktop.Activation;

internal sealed class ActivationTransportRequest
{
    public JsonObject Body { get; }
    internal ActivationTransportClientContext Context { get; }

    public ActivationTransportRequest(JsonObject body, ActivationTransportClientContext context)
    {
        Body = body;
        Context = context;
    }
}

internal sealed class ActivationTransportClientContext
{
    public string Endpoint { get; }
    public string KeyId { get; }
    public string ProductId { get; }
    public long Timestamp { get; }
    public byte[] ResponseKey { get; }

    public ActivationTransportClientContext(
        string endpoint,
        string keyId,
        string productId,
        long timestamp,
        byte[] responseKey)
    {
        Endpoint = endpoint;
        KeyId = keyId;
        ProductId = productId;
        Timestamp = timestamp;
        ResponseKey = responseKey;
    }
}

internal static class ActivationTransportEnvelope
{
    public const string RequestType = "aiwa-activation-envelope-v1";
    public const string ResponseType = "aiwa-activation-response-v1";
    public const string Algorithm = "X25519-HKDF-SHA256-CHACHA20-POLY1305";

    private static readonly byte[] HkdfSalt = Encoding.ASCII.GetBytes("AIWA-ACTIVATION-ENVELOPE-V1");
    private static readonly SecureRandom Random = new();

    public static ActivationTransportRequest EncryptRequest(
        string endpoint,
        string productId,
        JsonObject payload,
        string keyId,
        string serverPublicKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint must not be empty.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product id must not be empty.", nameof(productId));
        ArgumentNullException.ThrowIfNull(payload);

        var serverPublicBytes = Base64UrlDecode(serverPublicKey);
        if (serverPublicBytes.Length != 32)
            throw new InvalidOperationException("Activation transport public key must be 32 bytes.");

        var serverPublic = new X25519PublicKeyParameters(serverPublicBytes);
        var clientPrivate = new X25519PrivateKeyParameters(Random);
        var clientPublicBytes = clientPrivate.GeneratePublicKey().GetEncoded();

        var sharedSecret = new byte[32];
        clientPrivate.GenerateSecret(serverPublic, sharedSecret, 0);
        var keys = DeriveKeys(sharedSecret, keyId, clientPublicBytes, serverPublicBytes);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = RandomBytes(12);
        var nonceText = Base64UrlEncode(nonce);
        var plaintext = JsonNode.Parse(payload.ToJsonString())!.AsObject();
        plaintext["endpoint"] = endpoint;
        plaintext["product_id"] = productId;
        plaintext["ts"] = ts;
        plaintext["nonce"] = nonceText;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext.ToJsonString());
        var ciphertext = Seal(
            keys.RequestKey,
            nonce,
            BuildAad("request", endpoint, keyId, productId, ts, nonceText),
            plaintextBytes);

        var body = new JsonObject
        {
            ["type"] = RequestType,
            ["alg"] = Algorithm,
            ["kid"] = keyId,
            ["product_id"] = productId,
            ["epk"] = Base64UrlEncode(clientPublicBytes),
            ["nonce"] = nonceText,
            ["ts"] = ts,
            ["ct"] = Base64UrlEncode(ciphertext),
        };

        return new ActivationTransportRequest(
            body,
            new ActivationTransportClientContext(endpoint, keyId, productId, ts, keys.ResponseKey));
    }

    public static string DecryptResponse(string json, ActivationTransportRequest request)
    {
        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement;

        if (!string.Equals(ReadString(envelope, "type"), ResponseType, StringComparison.Ordinal))
            throw new InvalidOperationException("Activation server did not return an encrypted transport response.");
        if (!string.Equals(ReadString(envelope, "alg"), Algorithm, StringComparison.Ordinal))
            throw new InvalidOperationException("Activation transport response uses an unsupported algorithm.");
        if (!string.Equals(ReadString(envelope, "kid"), request.Context.KeyId, StringComparison.Ordinal))
            throw new InvalidOperationException("Activation transport response key id mismatch.");

        var nonceText = ReadString(envelope, "nonce");
        var plaintext = Open(
            request.Context.ResponseKey,
            Base64UrlDecode(nonceText),
            BuildAad(
                "response",
                request.Context.Endpoint,
                request.Context.KeyId,
                request.Context.ProductId,
                request.Context.Timestamp,
                nonceText),
            Base64UrlDecode(ReadString(envelope, "ct")));
        return Encoding.UTF8.GetString(plaintext);
    }

    private static TransportKeys DeriveKeys(
        byte[] sharedSecret,
        string keyId,
        byte[] clientPublic,
        byte[] serverPublic)
    {
        var infoPrefix = Encoding.ASCII.GetBytes($"AIWA transport {keyId}\n");
        var info = new byte[infoPrefix.Length + clientPublic.Length + 1 + serverPublic.Length];
        Buffer.BlockCopy(infoPrefix, 0, info, 0, infoPrefix.Length);
        Buffer.BlockCopy(clientPublic, 0, info, infoPrefix.Length, clientPublic.Length);
        info[infoPrefix.Length + clientPublic.Length] = (byte)'\n';
        Buffer.BlockCopy(serverPublic, 0, info, infoPrefix.Length + clientPublic.Length + 1, serverPublic.Length);

        var hkdf = new HkdfBytesGenerator(new Sha256Digest());
        hkdf.Init(new HkdfParameters(sharedSecret, HkdfSalt, info));
        var okm = new byte[64];
        hkdf.GenerateBytes(okm, 0, okm.Length);

        var requestKey = new byte[32];
        var responseKey = new byte[32];
        Buffer.BlockCopy(okm, 0, requestKey, 0, 32);
        Buffer.BlockCopy(okm, 32, responseKey, 0, 32);
        return new TransportKeys(requestKey, responseKey);
    }

    private static byte[] Seal(byte[] key, byte[] nonce, byte[] aad, byte[] plaintext)
    {
        var cipher = new ChaCha20Poly1305();
        cipher.Init(true, new AeadParameters(new KeyParameter(key), 128, nonce, aad));
        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var length = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        length += cipher.DoFinal(output, length);
        if (length != output.Length)
            Array.Resize(ref output, length);
        return output;
    }

    private static byte[] Open(byte[] key, byte[] nonce, byte[] aad, byte[] ciphertext)
    {
        var cipher = new ChaCha20Poly1305();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), 128, nonce, aad));
        var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
        var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
        length += cipher.DoFinal(output, length);
        if (length != output.Length)
            Array.Resize(ref output, length);
        return output;
    }

    internal static byte[] BuildAad(
        string direction,
        string endpoint,
        string keyId,
        string productId,
        long ts,
        string nonce)
        => Encoding.UTF8.GetBytes($"AIWA-ACTIVATION-ENVELOPE-V1\n{direction}\n{endpoint}\n{keyId}\n{productId}\n{ts}\n{nonce}");

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Activation transport field '{name}' must not be empty.");

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Activation transport field '{name}' must not be empty.");
        return text.Trim();
    }

    private static byte[] RandomBytes(int count)
    {
        var value = new byte[count];
        Random.NextBytes(value);
        return value;
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('='))
            throw new FormatException("Activation transport base64url values must be unpadded.");

        var padded = value.Replace('-', '+').Replace('_', '/');
        var padding = padded.Length % 4;
        if (padding == 1)
            throw new FormatException("Invalid activation transport base64url length.");
        if (padding != 0)
            padded += new string('=', 4 - padding);
        return Convert.FromBase64String(padded);
    }

    private sealed record TransportKeys(byte[] RequestKey, byte[] ResponseKey);
}
