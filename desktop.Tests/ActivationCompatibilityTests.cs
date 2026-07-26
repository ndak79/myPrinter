using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MyPrinter.Desktop;
using MyPrinter.Desktop.Activation;
using NSec.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace desktop.Tests;

public class ActivationCompatibilityTests
{
    private const string TestTransportKeyId = "test_transport_v1";
    private const string TestTransportPrivateKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA";
    private const string TestTransportPublicKey = "B6N8vBQgk8i3VdwbEOhstCY3StFqqFPtC9_AsrhtHHw";
    private const string TransportRequestType = "aiwa-activation-envelope-v1";
    private const string TransportResponseType = "aiwa-activation-response-v1";
    private const string TransportAlgorithm = "X25519-HKDF-SHA256-CHACHA20-POLY1305";
    private const string TransportDomain = "AIWA-ACTIVATION-ENVELOPE-V1";

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
    public async Task ActivateAsync_encrypts_activation_key_over_http_when_transport_key_is_configured()
    {
        using var server = new TestJsonServer(async request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            body.Should().NotContain("ABCD-EFGH-IJKL-MNOP");
            body.Should().NotContain(new string('c', 64));
            body.Should().NotContain("activation_key");

            var decrypted = DecryptClientEnvelope(body, "/activate", "prod_smartprinter", out var responseContext);
            decrypted.GetProperty("endpoint").GetString().Should().Be("/activate");
            decrypted.GetProperty("product_id").GetString().Should().Be("prod_smartprinter");
            decrypted.GetProperty("activation_key").GetString().Should().Be("ABCD-EFGH-IJKL-MNOP");
            decrypted.GetProperty("fingerprint").GetString().Should().Be(new string('c', 64));

            return JsonResponse.OkRaw(EncryptServerEnvelope(responseContext, new
            {
                status = "activated",
                license = "header.payload.signature",
                expires_at = 1893456000,
                heartbeat_required = true,
                heartbeat_grace_days = 7,
            }));
        });

        var activationClient = GetActivationType("MyPrinter.Desktop.Activation.ActivationClient");
        ConfigureTransportEncryption(activationClient, TestTransportKeyId, TestTransportPublicKey);

        try
        {
            var method = activationClient.GetMethod(
                "ActivateAsync",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(string), typeof(string), typeof(string), typeof(string)]);

            var task = (Task)method!.Invoke(null, [server.BaseUrl, "ABCD-EFGH-IJKL-MNOP", new string('c', 64), "prod_smartprinter"])!;
            await task;

            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            GetString(result, "Error").Should().BeNull();
            GetString(result, "Token").Should().Be("header.payload.signature");
            GetNullableInt(result, "HeartbeatGraceDays").Should().Be(7);
        }
        finally
        {
            ConfigureTransportEncryption(activationClient, null, null);
        }
    }

    [Fact]
    public async Task HeartbeatAsync_encrypts_license_token_over_http_when_transport_key_is_configured()
    {
        using var server = new TestJsonServer(async request =>
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            body.Should().NotContain("header.payload.signature");
            body.Should().NotContain(new string('d', 64));
            body.Should().NotContain("license_token");

            var decrypted = DecryptClientEnvelope(body, "/heartbeat", "prod_smartprinter", out var responseContext);
            decrypted.GetProperty("endpoint").GetString().Should().Be("/heartbeat");
            decrypted.GetProperty("product_id").GetString().Should().Be("prod_smartprinter");
            decrypted.GetProperty("fingerprint").GetString().Should().Be(new string('d', 64));
            decrypted.GetProperty("license_token").GetString().Should().Be("header.payload.signature");

            return JsonResponse.OkRaw(EncryptServerEnvelope(responseContext, new
            {
                status = "ok",
                heartbeat_grace_days = 7,
            }));
        });

        var activationClient = GetActivationType("MyPrinter.Desktop.Activation.ActivationClient");
        ConfigureTransportEncryption(activationClient, TestTransportKeyId, TestTransportPublicKey);

        try
        {
            var method = activationClient.GetMethod(
                "HeartbeatAsync",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(string), typeof(string), typeof(string), typeof(string)]);

            var task = (Task)method!.Invoke(null, [server.BaseUrl, new string('d', 64), "prod_smartprinter", "header.payload.signature"])!;
            await task;

            var result = task.GetType().GetProperty("Result")!.GetValue(task);
            result.Should().NotBeNull();
            GetBool(result!, "Valid").Should().BeTrue();
            GetInt(result!, "HeartbeatGraceDays").Should().Be(7);
        }
        finally
        {
            ConfigureTransportEncryption(activationClient, null, null);
        }
    }

    [Fact]
    public async Task ActivateAsync_reports_activation_server_redirect_location_without_following_it()
    {
        using var server = new TestJsonServer(_ => Task.FromResult(JsonResponse.Redirect("https://103.82.24.37/activate")));

        var activationClient = GetActivationType("MyPrinter.Desktop.Activation.ActivationClient");
        var method = activationClient.GetMethod(
            "ActivateAsync",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string), typeof(string)]);

        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [server.BaseUrl, "ABCD-EFGH-IJKL-MNOP", new string('b', 64), "prod_smartprinter"])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        GetString(result, "Error").Should().Contain("https://103.82.24.37/activate");
        GetString(result, "Error").Should().Contain("fix the activation server HTTP/TLS proxy");
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

    [Fact]
    public void Trusted_time_window_only_exempts_signed_no_heartbeat_licenses()
    {
        var licenseGuardType = GetActivationType("MyPrinter.Desktop.Activation.LicenseGuard");
        var method = licenseGuardType.GetMethod(
            "IsOfflineTrustedTimeWindowExpired",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(double), typeof(double), typeof(bool), typeof(bool)]);

        method.Should().NotBeNull(
            "the seven-day trusted-time window must be a separate policy decision from signed license expiry");

        const double now = 1_700_000_000;
        var thirtyDaysAgo = now - 30 * 86400.0;

        ((bool)method!.Invoke(null, [now, thirtyDaysAgo, true, false])!).Should().BeFalse(
            "licenses signed with heartbeat_required=false must remain valid offline until their signed expiry");
        ((bool)method.Invoke(null, [now, thirtyDaysAgo, true, true])!).Should().BeTrue(
            "heartbeat-required licenses still need the anti-rollback trusted-time window");
        ((bool)method.Invoke(null, [now, thirtyDaysAgo, false, false])!).Should().BeTrue(
            "legacy/local heartbeat policy fields are not signed and must not disable anti-rollback checks");
    }

    [Fact]
    public void LicenseGuard_gates_trusted_time_window_by_signed_heartbeat_policy()
    {
        var repoRoot = GetRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "LicenseGuard.cs"));
        var verifyBody = ExtractMethodSource(source, "private static bool VerifyToken");

        verifyBody.Should().Contain(
            "IsOfflineTrustedTimeWindowExpired(now, lastTrusted, hasSignedHeartbeatPolicy, heartbeatRequired)",
            "a no-heartbeat key must not be invalidated only because trusted time was unavailable for seven days");
    }

    [Fact]
    public void FingerprintHelper_builds_candidates_for_each_valid_mac()
    {
        var fingerprintHelperType = GetActivationType("MyPrinter.Desktop.Activation.FingerprintHelper");
        var method = fingerprintHelperType.GetMethod(
            "BuildFingerprintCandidates",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(string[]), typeof(string[]), typeof(string[]), typeof(string[])]);

        method.Should().NotBeNull(
            "startup license recovery must tolerate Windows returning physical network adapters in a different order");

        var candidates = ((IEnumerable<string>)method!.Invoke(null,
        [
            new[] { "uuid-a" },
            new[] { "cpu-a" },
            new[] { "gpu-a" },
            new[] { "AA:AA:AA:AA:AA:AA", "BB:BB:BB:BB:BB:BB" },
        ])!).ToArray();

        candidates.Should().Contain(Sha256Hex("uuid-a|cpu-a|gpu-a|AA:AA:AA:AA:AA:AA"));
        candidates.Should().Contain(Sha256Hex("uuid-a|cpu-a|gpu-a|BB:BB:BB:BB:BB:BB"));
        candidates[0].Should().Be(Sha256Hex("uuid-a|cpu-a|gpu-a|AA:AA:AA:AA:AA:AA"),
            "the first candidate remains the legacy/current-order fingerprint used for new activations");
    }

    [Fact]
    public void LicenseGuard_startup_uses_machine_license_before_legacy_fingerprint_migration()
    {
        var repoRoot = GetRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "LicenseGuard.cs"));
        var isActivatedBody = ExtractMethodSource(source, "public static bool IsActivated");

        isActivatedBody.Should().Contain("var token = LicenseStorage.Load();");
        isActivatedBody.Should().Contain("!LicenseStorage.HasMachineLicense()");
        isActivatedBody.Should().Contain("LicenseStorage.MigrateLegacy(fingerprintCandidates)");
        isActivatedBody.Should().NotContain("fingerprintCandidates.Contains(token.Fingerprint");
        isActivatedBody.Should().Contain("VerifyToken(token, token.Fingerprint)",
            "the signed token fingerprint, not a transient current primary fingerprint, should drive verification and heartbeat");
    }

    [Fact]
    public void LicenseGuard_does_not_deactivate_when_verified_metadata_save_fails()
    {
        var repoRoot = GetRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "LicenseGuard.cs"));
        var verifyBody = ExtractMethodSource(source, "private static bool VerifyToken");

        verifyBody.Should().NotContain("if (!LicenseStorage.TrySave(token))\r\n            return false;");
        verifyBody.Should().Contain("LicenseStorage.TrySave(token);",
            "after a token is cryptographically valid, a transient metadata write failure must not reopen activation");
    }

    [Fact]
    public void LicenseGuard_uses_last_seen_as_effective_time_floor_for_expiry()
    {
        var licenseGuardType = GetActivationType("MyPrinter.Desktop.Activation.LicenseGuard");
        var selector = licenseGuardType.GetMethod(
            "SelectEffectiveLicenseTime",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(double), typeof(double), typeof(double)]);

        selector.Should().NotBeNull(
            "an already-seen post-expiry timestamp must prevent an expired license from becoming valid again after a local clock rollback");

        ((double)selector!.Invoke(null, [0d, 1_700_000_000d, 1_700_100_000d])!)
            .Should().Be(1_700_100_000d);
        ((double)selector.Invoke(null, [1_700_050_000d, 1_700_000_000d, 1_700_100_000d])!)
            .Should().Be(1_700_100_000d);
        ((double)selector.Invoke(null, [1_700_200_000d, 1_700_000_000d, 1_700_100_000d])!)
            .Should().Be(1_700_200_000d);
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
            "AllowInsecureHttp": true,
            "TransportKeyId": "actenc_prod_c094fac09065_v1",
            "TransportPublicKey": "OPTPpTm_-MhYRoRKCYyLTPZLxqQxYxZtnP49VcaIxjM"
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
            var transportKeyId = (string?)result.GetType().GetField("Item4", BindingFlags.Instance | BindingFlags.Public)?.GetValue(result);
            var transportPublicKey = (string?)result.GetType().GetField("Item5", BindingFlags.Instance | BindingFlags.Public)?.GetValue(result);

            serverUrl.Should().Be("http://103.82.24.37");
            productId.Should().Be("prod_smartprinter");
            allowInsecureHttp.Should().BeTrue();
            transportKeyId.Should().Be("actenc_prod_c094fac09065_v1");
            transportPublicKey.Should().Be("OPTPpTm_-MhYRoRKCYyLTPZLxqQxYxZtnP49VcaIxjM");
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
        activation.GetProperty("TransportKeyId").GetString().Should().Be("actenc_prod_c094fac09065_v1");
        activation.GetProperty("TransportPublicKey").GetString().Should().Be("OPTPpTm_-MhYRoRKCYyLTPZLxqQxYxZtnP49VcaIxjM");
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
        script.Should().Contain("#define KeysetFileName \"license_keyset_prod_smartprinter.json\"");
        script.Should().Contain("Source: \"{#PublishDir}\\Activation\\{#KeysetFileName}\"");
        script.Should().Contain("Source: \"{#PublishDir}\\frontend\\*\"");
        script.Should().Contain("Excludes: \"*.backup,_fix_guide.js,tests\\*\"");
        script.Should().Contain("Flags: nowait postinstall skipifsilent runascurrentuser");
        script.Should().Contain("SetupIconFile={#AppIconFile}");
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
        script.Should().Contain("Non-HTTPS Activation.ServerUrl requires Activation.TransportKeyId and Activation.TransportPublicKey.");
        script.Should().Contain("Published Activation.TransportPublicKey must decode to a 32-byte X25519 public key.");
        script.Should().Contain("Run-Tests");
        script.Should().Contain("Get-DotnetCli");
        script.Should().Contain("Validate-Keyset");
        script.Should().Contain("prod_smartprinter_v2");
        script.Should().Contain("$_.Name.EndsWith(\".backup\"");
        script.Should().Contain("$_.Name -eq \"_fix_guide.js\"");
        script.Should().Contain("frontend\\tests");
        script.Should().Contain("/DKeysetFileName=$keysetFileName");
        script.Should().Contain("/DAppIconFile=$IconPath");
    }

    [Fact]
    public void Publish_bat_delegates_to_hardened_installer_build()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "publish.bat");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("build-installer.ps1");
        script.Should().NotContain("-ExecutionPolicy Bypass");
        script.Should().Contain("-ServerUrl \"http://103.82.24.37\"");
        script.Should().Contain("-ProductId \"prod_smartprinter\"");
        script.Should().Contain("-TransportKeyId \"actenc_prod_c094fac09065_v1\"");
        script.Should().Contain("-TransportPublicKey \"OPTPpTm_-MhYRoRKCYyLTPZLxqQxYxZtnP49VcaIxjM\"");
        script.Should().Contain("-AllowInsecureHttp");
    }

    [Fact]
    public void Desktop_project_excludes_frontend_backup_artifacts_from_publish()
    {
        var repoRoot = GetRepoRoot();
        var csprojPath = Path.Combine(repoRoot, "desktop", "MyPrinter.Desktop.csproj");
        var project = File.ReadAllText(csprojPath);

        project.Should().Contain("Exclude=\"..\\frontend\\**\\*.backup;..\\frontend\\**\\_fix_guide.js;..\\frontend\\tests\\**\\*\"");
        project.Should().Contain("<ApplicationIcon>app.ico</ApplicationIcon>");
        File.Exists(Path.Combine(repoRoot, "desktop", "app.ico")).Should().BeTrue();
    }

    [Fact]
    public void LicenseGuard_keeps_offline_import_local_only()
    {
        var repoRoot = GetRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "desktop", "Activation", "LicenseGuard.cs");
        var source = File.ReadAllText(sourcePath);
        var offlineBody = ExtractMethodSource(source, "public static Task<bool> ActivateOfflineAsync");

        offlineBody.Should().NotContain("if (string.IsNullOrWhiteSpace(token.Token)) return false;");
        offlineBody.Should().NotContain("var hasCompactToken = !string.IsNullOrWhiteSpace(token.Token);");
        offlineBody.Should().NotContain("NtpClient.GetNtpTime()");
        offlineBody.Should().NotContain("ActivationClient.HeartbeatAsync");
        offlineBody.Should().Contain("token.LastOnlineCheck = 0;");
    }

    [Fact]
    public void Offline_license_import_does_not_require_internet()
    {
        var repoRoot = GetRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "LicenseGuard.cs"));
        var offlineBody = ExtractMethodSource(source, "public static Task<bool> ActivateOfflineAsync");
        offlineBody.Should().NotContain("NtpClient.GetNtpTime()");
        offlineBody.Should().NotContain("ActivationClient.HeartbeatAsync");
        offlineBody.Should().Contain("DateTimeOffset.UtcNow.ToUnixTimeSeconds()");

        var onlineStart = source.IndexOf("public static async Task<(bool Ok, string? Error)> ActivateOnlineAsync", StringComparison.Ordinal);
        var offlineStart = source.IndexOf("public static Task<bool> ActivateOfflineAsync", StringComparison.Ordinal);
        onlineStart.Should().BeGreaterThanOrEqualTo(0);
        var onlineBody = source[onlineStart..offlineStart];
        onlineBody.Should().Contain("NtpClient.GetNtpTime()");
        onlineBody.Should().Contain("ActivationClient.HeartbeatAsync");
    }

    [Fact]
    public void Activation_http_clients_check_tls_certificate_revocation()
    {
        var repoRoot = GetRepoRoot();
        var activationClient = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "ActivationClient.cs"));
        var ntpClient = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "NtpClient.cs"));

        activationClient.Should().Contain("CheckCertificateRevocationList = true");
        ntpClient.Should().Contain("CheckCertificateRevocationList = true");
    }

    [Fact]
    public void MainForm_disposes_owned_native_resources()
    {
        var repoRoot = GetRepoRoot();
        var designer = File.ReadAllText(Path.Combine(repoRoot, "desktop", "MainForm.Designer.cs"));
        var program = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Program.cs"));

        designer.Should().Contain("_webView?.Dispose();");
        designer.Should().Contain("_trayIcon.Visible = false;");
        designer.Should().Contain("_trayIcon?.Dispose();");
        designer.Should().Contain("foreach (var image in _ownedTrayImages)");
        designer.Should().Contain("_windowIcon?.Dispose();");
        designer.Should().Contain("_trayNotifyIcon?.Dispose();");
        program.Should().Contain("using var mainForm = new MainForm(startHidden);");
        program.Should().Contain("Application.Run(mainForm);");
    }

    [Fact]
    public void MainForm_releases_owned_gdi_resources()
    {
        var repoRoot = GetRepoRoot();
        var mainForm = File.ReadAllText(Path.Combine(repoRoot, "desktop", "MainForm.cs"));

        mainForm.Should().Contain("_ownedTrayImages.Add(bitmap);");
        mainForm.Should().Contain("DefaultDllImportSearchPaths(DllImportSearchPath.System32)");
        mainForm.Should().Contain("DestroyIcon(nativeIconHandle)");
        mainForm.Should().Contain("return (Icon)icon.Clone();");
    }

    [Fact]
    public void Tray_menu_exposes_windows_startup_toggle()
    {
        var repoRoot = GetRepoRoot();
        var mainForm = File.ReadAllText(Path.Combine(repoRoot, "desktop", "MainForm.cs"));

        mainForm.Should().Contain("\"Start with Windows\"");
        mainForm.Should().Contain("startupItem.CheckOnClick = true;");
        mainForm.Should().Contain("_windowsStartupService.IsEnabled()");
        mainForm.Should().Contain("_windowsStartupService.SetEnabledByUser(startupItem.Checked)");
        mainForm.Should().Contain("startupItem.Checked = !startupItem.Checked;");
    }

    [Fact]
    public void Hidden_startup_suppresses_initial_tray_balloon()
    {
        var repoRoot = GetRepoRoot();
        var mainForm = File.ReadAllText(Path.Combine(repoRoot, "desktop", "MainForm.cs"));

        mainForm.Should().Contain("private readonly bool _startHidden;");
        mainForm.Should().Contain("if (!_startHidden)");
        mainForm.Should().Contain("_trayIcon.ShowBalloonTip(2000);");
    }

    [Fact]
    public void Activation_http_requests_dispose_request_content()
    {
        var repoRoot = GetRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "ActivationClient.cs"));

        source.Should().Contain("using var content = new StringContent");
        source.Should().Contain("using var res  = await _httpActivate.PostAsync");
        source.Should().Contain("using var res  = await _httpHeartbeat.PostAsync");
    }

    [Fact]
    public void FingerprintHelper_uses_managed_wmi_without_powershell_process()
    {
        var repoRoot = GetRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "desktop", "Activation", "FingerprintHelper.cs"));

        source.Should().Contain("ManagementObjectSearcher");
        source.Should().NotContain("powershell.exe");
        source.Should().NotContain("ProcessStartInfo");
        source.Should().NotContain("System.Diagnostics");
    }

    [Fact]
    public void Frontend_entrypoint_uses_local_pdfjs_and_no_remote_runtime_dependencies()
    {
        var repoRoot = GetRepoRoot();
        var index = File.ReadAllText(Path.Combine(repoRoot, "frontend", "index.html"));

        index.Should().Contain("import * as pdfjsLib from '/lib/pdf.min.mjs'");
        index.Should().NotContain("https://cdnjs.cloudflare.com");
        index.Should().NotContain("fonts.googleapis.com");
        index.Should().NotContain("fonts.gstatic.com");
        File.Exists(Path.Combine(repoRoot, "frontend", "lib", "pdf.min.mjs")).Should().BeTrue();
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

    private static byte[] Base64UrlDecode(string value)
    {
        value.Should().NotContain("=");
        var padded = value.Replace('-', '+').Replace('_', '/');
        var padding = padded.Length % 4;
        padding.Should().NotBe(1);
        if (padding != 0)
            padded += new string('=', 4 - padding);
        return Convert.FromBase64String(padded);
    }

    private static void ConfigureTransportEncryption(Type activationClient, string? keyId, string? publicKey)
    {
        var method = activationClient.GetMethod(
            "ConfigureTransportEncryption",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(string)]);

        method.Should().NotBeNull("client must expose transport encryption configuration for HTTP activation");
        method!.Invoke(null, [keyId, publicKey]);
    }

    private static JsonElement DecryptClientEnvelope(
        string body,
        string expectedEndpoint,
        string expectedProductId,
        out TransportResponseContext responseContext)
    {
        using var envelopeDoc = JsonDocument.Parse(body);
        var envelope = envelopeDoc.RootElement;
        envelope.GetProperty("type").GetString().Should().Be(TransportRequestType);
        envelope.GetProperty("alg").GetString().Should().Be(TransportAlgorithm);
        envelope.GetProperty("kid").GetString().Should().Be(TestTransportKeyId);
        envelope.GetProperty("product_id").GetString().Should().Be(expectedProductId);

        var keyId = envelope.GetProperty("kid").GetString()!;
        var productId = envelope.GetProperty("product_id").GetString()!;
        var ts = envelope.GetProperty("ts").GetInt64();
        var nonceText = envelope.GetProperty("nonce").GetString()!;
        var nonce = Base64UrlDecode(nonceText);
        nonce.Should().HaveCount(12);

        var clientPublic = Base64UrlDecode(envelope.GetProperty("epk").GetString()!);
        clientPublic.Should().HaveCount(32);

        var serverPrivate = new X25519PrivateKeyParameters(Base64UrlDecode(TestTransportPrivateKey));
        var serverPublic = serverPrivate.GeneratePublicKey().GetEncoded();
        var shared = new byte[32];
        serverPrivate.GenerateSecret(new X25519PublicKeyParameters(clientPublic), shared, 0);
        var keys = DeriveTransportKeys(shared, keyId, clientPublic, serverPublic);

        var plaintext = Open(
            keys.RequestKey,
            nonce,
            BuildTransportAad("request", expectedEndpoint, keyId, productId, ts, nonceText),
            Base64UrlDecode(envelope.GetProperty("ct").GetString()!));

        using var payloadDoc = JsonDocument.Parse(plaintext);
        var payload = payloadDoc.RootElement.Clone();
        payload.GetProperty("product_id").GetString().Should().Be(productId);
        payload.GetProperty("ts").GetInt64().Should().Be(ts);
        payload.GetProperty("nonce").GetString().Should().Be(nonceText);

        responseContext = new TransportResponseContext(
            expectedEndpoint,
            keyId,
            productId,
            ts,
            keys.ResponseKey);
        return payload;
    }

    private static string EncryptServerEnvelope(TransportResponseContext context, object payload)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var nonceText = Base64UrlEncode(nonce);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        var ciphertext = Seal(
            context.ResponseKey,
            nonce,
            BuildTransportAad("response", context.Endpoint, context.KeyId, context.ProductId, context.Timestamp, nonceText),
            plaintext);

        return JsonSerializer.Serialize(new
        {
            type = TransportResponseType,
            alg = TransportAlgorithm,
            kid = context.KeyId,
            nonce = nonceText,
            ct = Base64UrlEncode(ciphertext),
        });
    }

    private static TransportKeys DeriveTransportKeys(
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
        hkdf.Init(new HkdfParameters(sharedSecret, Encoding.ASCII.GetBytes(TransportDomain), info));
        var okm = new byte[64];
        hkdf.GenerateBytes(okm, 0, okm.Length);

        var requestKey = new byte[32];
        var responseKey = new byte[32];
        Buffer.BlockCopy(okm, 0, requestKey, 0, 32);
        Buffer.BlockCopy(okm, 32, responseKey, 0, 32);
        return new TransportKeys(requestKey, responseKey);
    }

    private static byte[] BuildTransportAad(
        string direction,
        string endpoint,
        string keyId,
        string productId,
        long ts,
        string nonce)
        => Encoding.UTF8.GetBytes($"{TransportDomain}\n{direction}\n{endpoint}\n{keyId}\n{productId}\n{ts}\n{nonce}");

    private static byte[] Seal(byte[] key, byte[] nonce, byte[] aad, byte[] plaintext)
    {
        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
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
        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), 128, nonce, aad));
        var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
        var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
        length += cipher.DoFinal(output, length);
        if (length != output.Length)
            Array.Resize(ref output, length);
        return output;
    }

    private sealed record TransportKeys(byte[] RequestKey, byte[] ResponseKey);

    private sealed record TransportResponseContext(
        string Endpoint,
        string KeyId,
        string ProductId,
        long Timestamp,
        byte[] ResponseKey);

    private static string GetRepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ExtractMethodSource(string source, string methodDeclaration)
    {
        var start = source.IndexOf(methodDeclaration, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var braceStart = source.IndexOf('{', start);
        braceStart.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Could not extract method source for {methodDeclaration}.");
    }

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
                if (!string.IsNullOrWhiteSpace(response.Location))
                    context.Response.RedirectLocation = response.Location;
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

    private sealed record JsonResponse(HttpStatusCode StatusCode, string Body, string? Location = null)
    {
        public static JsonResponse Ok(object body) => new(HttpStatusCode.OK, JsonSerializer.Serialize(body));
        public static JsonResponse OkRaw(string body) => new(HttpStatusCode.OK, body);
        public static JsonResponse ErrorRaw(int statusCode, string body) => new((HttpStatusCode)statusCode, body);
        public static JsonResponse Redirect(string location) => new(HttpStatusCode.MovedPermanently, "", location);
    }
}
