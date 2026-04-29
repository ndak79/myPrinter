using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MyPrinter.Desktop;
using NSec.Cryptography;

namespace desktop.Tests;

public class ActivationCompatibilityTests
{
    [Fact]
    public async Task HeartbeatAsync_sends_product_id_and_license_token_and_accepts_v7_status_response()
    {
        using var server = new TestJsonServer(async request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var json = JsonDocument.Parse(body);

            json.RootElement.GetProperty("product_id").GetString().Should().Be("smartPrinter");
            json.RootElement.GetProperty("fingerprint").GetString().Should().Be(new string('a', 64));
            json.RootElement.GetProperty("license_token").GetString().Should().Be("header.payload.signature");

            return JsonResponse.Ok(new
            {
                status = "ok",
                heartbeat_grace_days = 7,
            });
        });

        var activationClient = GetActivationType("MyPrinter.Desktop.Activation.ActivationClient");
        var method = activationClient.GetMethod(
            "HeartbeatAsync",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string), typeof(string)]);

        method.Should().NotBeNull("HeartbeatAsync must accept serverUrl, fingerprint, productId, and licenseToken for v7 proof-of-possession");

        var task = (Task)method!.Invoke(null, [server.BaseUrl, new string('a', 64), "smartPrinter", "header.payload.signature"])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        result.Should().NotBeNull();

        GetBool(result!, "Valid").Should().BeTrue();
        GetInt(result!, "HeartbeatGraceDays").Should().Be(7);
    }

    [Fact]
    public async Task ActivateAsync_accepts_v7_activate_response_with_compact_jws_token()
    {
        using var server = new TestJsonServer(async request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            using var json = JsonDocument.Parse(body);

            json.RootElement.GetProperty("product_id").GetString().Should().Be("smartPrinter");
            json.RootElement.GetProperty("activation_key").GetString().Should().Be("ABCD-EFGH-IJKL-MNOP");
            json.RootElement.GetProperty("fingerprint").GetString().Should().Be(new string('b', 64));

            return JsonResponse.Ok(new
            {
                status = "activated",
                license = "header.payload.signature",
                expires_at = 1893456000,
                heartbeat_required = true,
                heartbeat_grace_days = 7,
            });
        });

        var activationClient = GetActivationType("MyPrinter.Desktop.Activation.ActivationClient");
        var method = activationClient.GetMethod(
            "ActivateAsync",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string), typeof(string)]);

        method.Should().NotBeNull("ActivateAsync must accept productId so desktop clients can target smartPrinter explicitly");

        var task = (Task)method!.Invoke(null, [server.BaseUrl, "ABCD-EFGH-IJKL-MNOP", new string('b', 64), "smartPrinter"])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        GetString(result, "Error").Should().BeNull();
        GetString(result, "Token").Should().Be("header.payload.signature");
        GetNullableInt(result, "HeartbeatGraceDays").Should().Be(7);
    }

    [Fact]
    public void LicenseToken_can_be_built_from_v7_jws_and_verify_signature_against_product_claims()
    {
        const string fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string productId = "smartPrinter";
        const string issuer = "https://license.smartprinter.test";

        var key = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var rawPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var publicKeyPem = ToPem(rawPublicKey);
        var fpClaim = Sha256Hex($"{productId}:{fingerprint}");
        var token = CreateJws(key, issuer, productId, "dev_123", fpClaim, 1893456000, "prod_default_v1");

        var licenseTokenType = GetActivationType("MyPrinter.Desktop.Activation.LicenseToken");
        var fromSignedToken = licenseTokenType.GetMethod(
            "FromSignedToken",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)]);

        fromSignedToken.Should().NotBeNull("desktop client must be able to materialize a local license object from the v7 compact JWS token");

        var instance = fromSignedToken!.Invoke(null, [token, fingerprint, productId]);
        instance.Should().NotBeNull();

        var verifySignature = licenseTokenType.GetMethod(
            "VerifySignature",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)]);

        verifySignature.Should().NotBeNull("license verification must validate JWS signature, aud, and iss for v7 tokens");

        ((bool)verifySignature!.Invoke(instance, [publicKeyPem, productId, issuer])!).Should().BeTrue();
        GetString(instance!, "Token").Should().Be(token);
        GetString(instance!, "ProductId").Should().Be(productId);
        GetLong(instance!, "Expiry").Should().Be(1893456000);
    }

    private static Type GetActivationType(string fullName)
        => typeof(ActivationForm).Assembly.GetType(fullName, throwOnError: true)!;

    private static string? GetString(object target, string propertyName)
        => (string?)target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

    private static bool GetBool(object target, string propertyName)
        => (bool)(target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ?? false);

    private static int GetInt(object target, string propertyName)
        => (int)(target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ?? 0);

    private static int? GetNullableInt(object target, string propertyName)
        => (int?)target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

    private static long GetLong(object target, string propertyName)
        => (long)(target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ?? 0L);

    private static string CreateJws(Key key, string issuer, string audience, string subject, string fpClaim, long exp, string kid)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "EdDSA", kid });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            aud = audience,
            sub = subject,
            exp,
            fp = fpClaim,
        });

        var headerB64 = Base64UrlEncode(header);
        var payloadB64 = Base64UrlEncode(payload);
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
        var signature = SignatureAlgorithm.Ed25519.Sign(key, signingInput);

        return $"{headerB64}.{payloadB64}.{Base64UrlEncode(signature)}";
    }

    private static string ToPem(byte[] rawPublicKey)
    {
        var spkiPrefix = new byte[]
        {
            0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00,
        };
        var der = spkiPrefix.Concat(rawPublicKey).ToArray();
        var base64 = Convert.ToBase64String(der);
        return $"-----BEGIN PUBLIC KEY-----\n{base64}\n-----END PUBLIC KEY-----";
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TestJsonServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerRequest, Task<JsonResponse>> _handler;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string BaseUrl { get; }

        public TestJsonServer(Func<HttpListenerRequest, Task<JsonResponse>> handler)
        {
            _handler = handler;
            var prefix = $"http://127.0.0.1:{GetFreePort()}/";
            BaseUrl = prefix.TrimEnd('/');
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _loop = Task.Run(ListenLoopAsync);
        }

        private async Task ListenLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException) when (_cts.IsCancellationRequested)
                {
                    break;
                }

                var response = await _handler(context.Request);
                var bytes = Encoding.UTF8.GetBytes(response.Body);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            try { _loop.GetAwaiter().GetResult(); } catch { }
            _cts.Dispose();
        }

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed record JsonResponse(HttpStatusCode StatusCode, string Body)
    {
        public static JsonResponse Ok(object body) => new(HttpStatusCode.OK, JsonSerializer.Serialize(body));
    }
}
