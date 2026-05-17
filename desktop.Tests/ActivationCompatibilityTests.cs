using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MyPrinter.Desktop;
using MyPrinter.Desktop.Activation;
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

            json.RootElement.GetProperty("product_id").GetString().Should().Be("prod_smartprinter");
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

        var task = (Task)method!.Invoke(null, [server.BaseUrl, new string('a', 64), "prod_smartprinter", "header.payload.signature"])!;
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

            json.RootElement.GetProperty("product_id").GetString().Should().Be("prod_smartprinter");
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

        var task = (Task)method!.Invoke(null, [server.BaseUrl, "ABCD-EFGH-IJKL-MNOP", new string('b', 64), "prod_smartprinter"])!;
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
        const string productId = "prod_smartprinter";
        const string issuer = "http://103.82.24.37";

        var key = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var rawPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var publicKeyPem = ToPem(rawPublicKey);
        var fpClaim = Sha256Hex($"{productId}:{fingerprint}");
        var token = CreateJws(key, issuer, productId, "dev_123", fpClaim, 1893456000, "prod_smartprinter_v2");

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

    [Fact]
    public void LicenseToken_materializes_signed_heartbeat_policy_from_v7_jws()
    {
        const string fingerprint = "1111111111111111111111111111111111111111111111111111111111111111";
        const string productId = "prod_smartprinter";
        const string issuer = "http://103.82.24.37";
        const string kid = "prod_smartprinter_v2";

        var key = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var fpClaim = Sha256Hex($"{productId}:{fingerprint}");
        var token = CreateJws(
            key,
            issuer,
            productId,
            "dev_heartbeat_policy",
            fpClaim,
            1893456000,
            kid,
            heartbeatRequired: false,
            heartbeatGraceDays: 0);

        var licenseTokenType = GetActivationType("MyPrinter.Desktop.Activation.LicenseToken");
        var fromSignedToken = licenseTokenType.GetMethod(
            "FromSignedToken",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)])!;

        var instance = fromSignedToken.Invoke(null, [token, fingerprint, productId]);
        instance.Should().NotBeNull();

        GetBool(instance!, "HeartbeatRequired").Should().BeFalse(
            "heartbeat_required is a signed policy claim and must not be inferred from an unsigned response");
        GetInt(instance!, "HeartbeatGraceDays").Should().Be(0);
    }

    [Fact]
    public void VerifySignatureWithKeyset_rejects_tampered_local_heartbeat_policy()
    {
        const string fingerprint = "2222222222222222222222222222222222222222222222222222222222222222";
        const string productId = "prod_smartprinter";
        const string issuer = "http://103.82.24.37";
        const string kid = "prod_smartprinter_v2";

        var key = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var rawPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var keysetJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                kid,
                key = Base64UrlEncode(rawPublicKey),
            },
        });
        var fpClaim = Sha256Hex($"{productId}:{fingerprint}");
        var token = CreateJws(
            key,
            issuer,
            productId,
            "dev_tamper_policy",
            fpClaim,
            1893456000,
            kid,
            heartbeatRequired: false,
            heartbeatGraceDays: 0);

        var licenseTokenType = GetActivationType("MyPrinter.Desktop.Activation.LicenseToken");
        var instance = licenseTokenType.GetMethod(
            "FromSignedToken",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)])!
            .Invoke(null, [token, fingerprint, productId])!;

        licenseTokenType.GetProperty("HeartbeatGraceDays")!.SetValue(instance, 30);

        var verifyWithKeyset = licenseTokenType.GetMethod(
            "VerifySignatureWithKeyset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)])!;

        ((bool)verifyWithKeyset.Invoke(instance, [keysetJson, productId, issuer])!).Should().BeFalse(
            "heartbeat policy is signed by the activation server and local tampering must invalidate the license");
    }

    [Fact]
    public void LicenseGuard_offline_parser_accepts_v7_compact_jws_license_file_content()
    {
        const string fingerprint = "3333333333333333333333333333333333333333333333333333333333333333";
        const string productId = "prod_smartprinter";
        const string issuer = "http://103.82.24.37";
        const string kid = "prod_smartprinter_v2";

        var key = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var fpClaim = Sha256Hex($"{productId}:{fingerprint}");
        var token = CreateJws(
            key,
            issuer,
            productId,
            "dev_offline_jws",
            fpClaim,
            1893456000,
            kid,
            heartbeatRequired: false,
            heartbeatGraceDays: 0);

        var licenseGuardType = GetActivationType("MyPrinter.Desktop.Activation.LicenseGuard");
        var parser = licenseGuardType.GetMethod(
            "ParseOfflineLicenseContent",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)]);

        parser.Should().NotBeNull("offline .lic files generated by ACTIVSYS are compact JWS token strings");

        var instance = parser!.Invoke(null, [$"  {token}\r\n", fingerprint, productId]);
        instance.Should().NotBeNull();
        GetString(instance!, "Token").Should().Be(token);
        GetString(instance!, "Fingerprint").Should().Be(fingerprint);
        GetString(instance!, "ProductId").Should().Be(productId);
        GetLong(instance!, "Expiry").Should().Be(1893456000);
        GetBool(instance!, "HeartbeatRequired").Should().BeFalse();
        GetInt(instance!, "HeartbeatGraceDays").Should().Be(0);
    }

    [Theory]
    [InlineData(1_700_000_000, 1_700_000_030, 1_700_000_030)]
    [InlineData(1_700_000_000, 1_700_000_120, 0)]
    [InlineData(0, 1_700_000_030, 1_700_000_030)]
    [InlineData(1_700_000_000, 0, 0)]
    public void NtpClient_trusts_https_time_and_never_raw_udp_ntp_alone(double ntpTime, double httpsTime, double expected)
    {
        var ntpClientType = GetActivationType("MyPrinter.Desktop.Activation.NtpClient");
        var selector = ntpClientType.GetMethod(
            "SelectTrustedTime",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(double), typeof(double)]);

        selector.Should().NotBeNull("trusted time selection must mirror ACTIVSYS: HTTPS is canonical, UDP NTP is only a cross-check");

        ((double)selector!.Invoke(null, [ntpTime, httpsTime])!).Should().Be(expected);
    }

    [Fact]
    public void LicenseToken_can_verify_v7_jws_against_matching_keyset_entry()
    {
        const string fingerprint = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        const string productId = "prod_smartprinter";
        const string issuer = "http://103.82.24.37";
        const string kid = "prod_smartprinter_v2";

        var key = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var rawPublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var keysetJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                kid,
                key = Base64UrlEncode(rawPublicKey),
            },
        });

        var fpClaim = Sha256Hex($"{productId}:{fingerprint}");
        var token = CreateJws(key, issuer, productId, "dev_456", fpClaim, 1893456000, kid);

        var licenseTokenType = GetActivationType("MyPrinter.Desktop.Activation.LicenseToken");
        var fromSignedToken = licenseTokenType.GetMethod(
            "FromSignedToken",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)])!;

        var instance = fromSignedToken.Invoke(null, [token, fingerprint, productId]);
        instance.Should().NotBeNull();

        var verifyWithKeyset = licenseTokenType.GetMethod(
            "VerifySignatureWithKeyset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)]);

        verifyWithKeyset.Should().NotBeNull("desktop client should verify JWS signatures against the shipped production keyset");
        ((bool)verifyWithKeyset!.Invoke(instance, [keysetJson, productId, issuer])!).Should().BeTrue();
    }

    [Fact]
    public void VerifySignatureWithKeyset_accepts_legacy_v2_signature_when_matching_key_is_not_first()
    {
        const string fingerprint = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        var firstKey = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });
        var secondKey = new Key(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving,
        });

        var token = CreateLegacyToken(
            secondKey,
            fingerprint,
            expiry: 1893456000);

        var keysetJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                kid = "prod_smartprinter_v1",
                key = Base64UrlEncode(firstKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            },
            new
            {
                kid = "prod_smartprinter_v2",
                key = Base64UrlEncode(secondKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            },
        });

        var verifyWithKeyset = token.GetType().GetMethod(
            "VerifySignatureWithKeyset",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string)]);

        verifyWithKeyset.Should().NotBeNull();
        ((bool)verifyWithKeyset!.Invoke(token, [keysetJson, "prod_smartprinter", "http://103.82.24.37"])!)
            .Should().BeTrue();
    }

    [Fact]
    public void LoadActivationConfig_reads_server_and_product_id_from_runtime_config()
    {
        var outputConfigPath = Path.Combine(AppContext.BaseDirectory, "smartprinter.appsettings.json");
        var original = File.Exists(outputConfigPath) ? File.ReadAllText(outputConfigPath) : null;
        File.WriteAllText(outputConfigPath, """
        {
          "Activation": {
            "ServerUrl": "http://103.82.24.37",
            "ProductId": "prod_smartprinter",
            "AllowInsecureHttp": true
          }
        }
        """);

        var method = typeof(ActivationForm).Assembly.GetType("MyPrinter.Desktop.Program", throwOnError: true)!
            .GetMethod("LoadActivationConfig", BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        try
        {
            var result = method!.Invoke(null, []);
            result.Should().NotBeNull();

            var serverUrl = (string?)result!.GetType().GetField("Item1", BindingFlags.Instance | BindingFlags.Public)?.GetValue(result);
            var productId = (string?)result.GetType().GetField("Item2", BindingFlags.Instance | BindingFlags.Public)?.GetValue(result);
            var allowInsecureHttp = (bool?)result.GetType().GetField("Item3", BindingFlags.Instance | BindingFlags.Public)?.GetValue(result);

            serverUrl.Should().Be("http://103.82.24.37");
            productId.Should().Be("prod_smartprinter");
            allowInsecureHttp.Should().BeTrue();
        }
        finally
        {
            if (original is null)
                File.Delete(outputConfigPath);
            else
                File.WriteAllText(outputConfigPath, original);
        }
    }

    [Fact]
    public void Committed_activation_config_matches_registered_smartprinter_endpoint()
    {
        var repoRoot = GetRepoRoot();
        var configPath = Path.Combine(repoRoot, "desktop", "smartprinter.appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var activation = doc.RootElement.GetProperty("Activation");

        activation.GetProperty("ServerUrl").GetString().Should().Be("http://103.82.24.37");
        activation.GetProperty("ProductId").GetString().Should().Be("prod_smartprinter");
        activation.GetProperty("AllowInsecureHttp").GetBoolean().Should().BeTrue(
            "the registered activation server currently uses HTTP, so the override must be explicit");
    }

    [Fact]
    public void Committed_smartprinter_keyset_matches_activation_system_rotation_set()
    {
        var repoRoot = GetRepoRoot();
        var keysetPath = Path.Combine(repoRoot, "desktop", "Activation", "license_keyset_prod_smartprinter.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(keysetPath));

        var keys = doc.RootElement.EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("kid").GetString()!,
                entry => entry.GetProperty("key").GetString()!);

        keys.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["prod_smartprinter_v1"] = "49otbCKjFpFXkaolcqRJ6YvYeCdtmQ6NJq7tuvt_bWg",
            ["prod_smartprinter_v2"] = "lPwBOiJYVdBEwFSX0ry378ejwuv9iBLCdcvWVujfkec",
        });
    }

    [Fact]
    public void Installer_definition_whitelists_runtime_files()
    {
        var repoRoot = GetRepoRoot();
        var issPath = Path.Combine(repoRoot, "installer", "myPrinter.iss");
        var script = File.ReadAllText(issPath);

        script.Should().NotContain("Source: \"{#PublishDir}\\*\"");
        script.Should().Contain("Source: \"{#PublishDir}\\MyPrinter.exe\"");
        script.Should().Contain("Source: \"{#PublishDir}\\smartprinter.appsettings.json\"");
        script.Should().Contain("Source: \"{#PublishDir}\\Activation\\license_keyset_prod_smartprinter.json\"");
        script.Should().Contain("Source: \"{#PublishDir}\\frontend\\*\"");
        script.Should().Contain("Excludes: \"*.backup,_fix_guide.js\"");
    }

    [Fact]
    public void Build_installer_script_requires_a_real_activation_server_url()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "build-installer.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("Installer config still contains placeholder Activation.ServerUrl");
        script.Should().Contain("Pass -ServerUrl with the real activation base URL.");
        script.Should().Contain("Non-HTTPS Activation.ServerUrl requires AllowInsecureHttp=true.");
    }

    [Fact]
    public void LicenseGuard_keeps_legacy_tokens_on_the_offline_path()
    {
        var repoRoot = GetRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "desktop", "Activation", "LicenseGuard.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("if (string.IsNullOrWhiteSpace(token.Token)) return false;");
        source.Should().Contain("var hasCompactToken = !string.IsNullOrWhiteSpace(token.Token);");
        source.Should().Contain("token.LastOnlineCheck = hasCompactToken ? effectiveTime : 0;");
        source.Should().Contain("if (hasCompactToken && token.HeartbeatRequired.GetValueOrDefault(true))");
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

    private static string CreateJws(
        Key key,
        string issuer,
        string audience,
        string subject,
        string fpClaim,
        long exp,
        string kid,
        bool heartbeatRequired = true,
        int heartbeatGraceDays = 7)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "EdDSA", kid });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            aud = audience,
            sub = subject,
            exp,
            fp = fpClaim,
            heartbeat_required = heartbeatRequired,
            heartbeat_grace_days = heartbeatGraceDays,
        });

        var headerB64 = Base64UrlEncode(header);
        var payloadB64 = Base64UrlEncode(payload);
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
        var signature = SignatureAlgorithm.Ed25519.Sign(key, signingInput);

        return $"{headerB64}.{payloadB64}.{Base64UrlEncode(signature)}";
    }

    private static object CreateLegacyToken(Key key, string fingerprint, long expiry)
    {
        var canonical = $"{{\"expiry\":{expiry},\"fingerprint\":\"{fingerprint}\",\"version\":2}}";
        var signature = SignatureAlgorithm.Ed25519.Sign(key, Encoding.UTF8.GetBytes(canonical));

        var licenseTokenType = GetActivationType("MyPrinter.Desktop.Activation.LicenseToken");
        var instance = Activator.CreateInstance(licenseTokenType)!;
        licenseTokenType.GetProperty("Fingerprint")!.SetValue(instance, fingerprint);
        licenseTokenType.GetProperty("Expiry")!.SetValue(instance, expiry);
        licenseTokenType.GetProperty("Version")!.SetValue(instance, 2);
        licenseTokenType.GetProperty("Signature")!.SetValue(instance, Base64UrlEncode(signature));
        return instance;
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

    private static string GetRepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

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
