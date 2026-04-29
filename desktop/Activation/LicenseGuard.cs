using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Main activation gate. Call Configure() then IsActivated() at startup.
/// Mirrors Python license_guard.check_activation() and _verify_license_data() logic.
/// </summary>
public static class LicenseGuard
{
    private static string? _serverUrl;
    private static string? _productId;
    private static string? _publicKeyPem;
    private static bool    _allowInsecureHttp;

    /// <summary>
    /// Must be called before IsActivated() or ActivateOnlineAsync().
    /// Validates serverUrl: must be absolute URL; must use HTTPS for non-localhost
    /// unless allowInsecureHttp is true. Throws ArgumentException on invalid config.
    /// Slightly stricter than Python _get_activation_server(), which does not separately
    /// restrict localhost URLs to http/https schemes.
    /// </summary>
    public static void Configure(string serverUrl, string productId, string publicKeyPem, bool allowInsecureHttp = false)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("ServerUrl must not be empty.", nameof(serverUrl));
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("ProductId must not be empty.", nameof(productId));
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            throw new ArgumentException("PublicKeyPem must not be empty.", nameof(publicKeyPem));

        // Validate absolute URL with http or https scheme only
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("ServerUrl must be an absolute URL.", nameof(serverUrl));
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("ServerUrl must use http or https scheme.", nameof(serverUrl));

        // Enforce HTTPS for non-localhost (matches Python _get_activation_server)
        var host    = uri.Host.ToLowerInvariant();
        var isLocal = host is "localhost" or "127.0.0.1" or "::1";
        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        if (!isLocal && !isHttps && !allowInsecureHttp)
            throw new ArgumentException(
                "ServerUrl must use HTTPS for non-local endpoints. " +
                "Set AllowInsecureHttp=true in appsettings.json to override (not recommended).",
                nameof(serverUrl));

        // Validate PEM at startup — dedicated parser that throws on invalid PEM/wrong key type.
        // Cannot use VerifySignature() here — it swallows all exceptions and returns false.
        // Mirrors Python _load_public_key() which validates type and raises RuntimeError.
        ValidatePublicKeyPemOrThrow(publicKeyPem);

        _serverUrl         = serverUrl.TrimEnd('/');
        _productId         = productId.Trim();
        _publicKeyPem      = publicKeyPem;
        _allowInsecureHttp = allowInsecureHttp;
    }

    /// <summary>
    /// Returns the 64-char hex hardware fingerprint for this device.
    /// Delegates to FingerprintHelper which caches after first call.
    /// Throws InvalidOperationException if too few hardware components can be read.
    /// </summary>
    public static string GetFingerprint()
        => FingerprintHelper.GetFingerprint();

    /// <summary>
    /// Main gate: verify existing license or return false (caller shows activation dialog).
    /// Throws InvalidOperationException if Configure() has not been called.
    /// ⚠️ MUST only be called before Application.Run() — this method blocks for up to ~68s
    /// (WMI + NTP + heartbeat + grace-retry). Safe at startup because no SynchronizationContext
    /// exists yet. Calling from UI thread after Application.Run() will freeze the UI.
    /// </summary>
    public static bool IsActivated()
    {
        EnsureConfigured();
        var fp    = GetFingerprint();
        var token = LicenseStorage.Load(fp);
        if (token == null) return false;
        return VerifyToken(token, fp);
    }

    /// <summary>
    /// Activate online: POST /activate, verify signature, sync heartbeat metadata, save license.dat.
    /// Returns (success, errorMessage).
    /// Throws InvalidOperationException if Configure() has not been called.
    /// </summary>
    public static async Task<(bool Ok, string? Error)> ActivateOnlineAsync(string activationKey)
    {
        EnsureConfigured();
        var fp = GetFingerprint();

        var result = await ActivationClient.ActivateAsync(_serverUrl!, activationKey.Trim(), fp, _productId!);
        if (result.Error != null)
            return (false, result.Error);
        if (string.IsNullOrWhiteSpace(result.Token))
            return (false, "Server returned no license token.");

        LicenseToken token;
        try
        {
            token = LicenseToken.FromSignedToken(result.Token!, fp, _productId!);
        }
        catch (Exception ex)
        {
            return (false, $"Server returned an invalid signed license token: {ex.Message}");
        }

        if (!token.IsSupportedFormat())
            return (false, "Unsupported license format.");
        if (!token.FingerprintMatches(fp))
            return (false, "License fingerprint mismatch — this key may be bound to another device.");
        if (!token.VerifySignature(_publicKeyPem!, _productId!, _serverUrl!))
            return (false, "License signature invalid.");

        // Use NTP for expiry check (same as startup path)
        double ntpTime      = NtpClient.GetNtpTime();
        double now          = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double effectiveTime = ntpTime > 0 ? ntpTime : now;
        if (token.IsExpired(effectiveTime))
            return (false, "License is already expired.");

        // Sync HeartbeatGraceDays from server — matches Python's post-activation heartbeat.
        // ⚠️ Grace policy drift risk: if this heartbeat fails (returns null), token.HeartbeatGraceDays
        // remains at its default (30). That default is FABRICATED — it does not represent the server's
        // configured value, and is wrong for any server-configured custom value, especially 0 ("no
        // heartbeat required"). Consequence: a custom heartbeat_days=0 server config will not be
        // applied until the NEXT successful heartbeat at startup. This is a known limitation —
        // the server does not embed heartbeat policy in the /activate response.
        if (result.HeartbeatGraceDays.HasValue)
            token.HeartbeatGraceDays = result.HeartbeatGraceDays.Value;

        var hb = await ActivationClient.HeartbeatAsync(_serverUrl!, fp, _productId!, token.Token);
        if (hb != null)
        {
            if (hb.PermanentlyInvalid || hb.Revoked)
                return (false, hb.Error ?? "Key was revoked on the server.");
            if (!hb.Valid)
                return (false, hb.Error ?? "Activation rejected by server (device not recognized).");
            if (hb.HeartbeatGraceDays.HasValue)
                token.HeartbeatGraceDays = hb.HeartbeatGraceDays.Value;
        }
        // If hb == null (server unreachable): activation still succeeds but grace policy may be stale.
        // The next successful heartbeat at startup will correct token.HeartbeatGraceDays.

        token.LastSeen        = effectiveTime;
        token.LastOnlineCheck = effectiveTime;
        token.LastTrustedTime = effectiveTime; // Seed trusted-time anchor on activation — matches Python
        if (!LicenseStorage.TrySave(token))
            return (false, "Failed to save license file. Check disk permissions.");
        return (true, null);
    }

    /// <summary>
    /// Activate offline: import a .lic file generated by admin via dashboard.
    /// Uses NTP for expiry check. Heartbeats server when reachable to reject revoked devices.
    /// Async so callers can await without blocking WinForms; NTP and heartbeat are the long-running
    /// awaited calls. File I/O, JSON parse, fingerprinting, and signature verification run
    /// synchronously before the first await — callers should invoke on a background thread if
    /// these synchronous steps must not touch the UI thread (e.g. wrap in Task.Run at call site).
    /// Throws InvalidOperationException if Configure() not called.
    /// </summary>
    public static async Task<bool> ActivateOfflineAsync(string licFilePath)
    {
        EnsureConfigured();
        try
        {
            var json  = File.ReadAllText(licFilePath);
            var token = JsonSerializer.Deserialize<LicenseToken>(json);
            if (token == null) return false;
            if (!token.IsSupportedFormat()) return false;

            var fp = GetFingerprint();
            if (!token.FingerprintMatches(fp))         return false;
            if (!token.VerifySignature(_publicKeyPem!, _productId!, _serverUrl!)) return false;

            // Use NTP for time — same as startup path
            double ntpTime       = NtpClient.GetNtpTime();
            double now           = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double effectiveTime = ntpTime > 0 ? ntpTime : now;
            if (token.IsExpired(effectiveTime))         return false;

            token.LastSeen        = effectiveTime;
            // Set LastOnlineCheck at activation time so the grace period starts from activation.
            // Matches Python _verify_license_data(): sets _last_online_check on successful heartbeat
            // AND seeds it on first verification when missing (if last_online == 0). This is not
            // an intentional divergence — both implementations seed the anchor on first verification.
            token.LastOnlineCheck = effectiveTime;
            token.LastTrustedTime = effectiveTime; // Seed trusted-time anchor — matches Python

            // Heartbeat when server reachable — reject revoked/deleted devices even offline
            // Mirrors Python activate_offline → _verify_license_data → _heartbeat_check
            var hb = await ActivationClient.HeartbeatAsync(_serverUrl!, fp, _productId!, token.Token);
            if (hb != null && (hb.PermanentlyInvalid || hb.Revoked || !hb.Valid))
                return false;
            if (hb != null)
            {
                if (hb.HeartbeatGraceDays.HasValue)
                    token.HeartbeatGraceDays = hb.HeartbeatGraceDays.Value;
                token.LastOnlineCheck    = effectiveTime;
            }

            if (!LicenseStorage.TrySave(token))
                return false; // Fail if persistence fails
            return true;
        }
        catch { return false; }
    }

    // -----------------------------------------------------------------------
    // Internal: full verification — mirrors Python _verify_license_data()
    // -----------------------------------------------------------------------
    private static bool VerifyToken(LicenseToken token, string fp)
    {
        if (!token.IsSupportedFormat())     return false;
        if (!token.FingerprintMatches(fp))  return false;
        if (string.IsNullOrWhiteSpace(token.Token)) return false;
        if (!token.VerifySignature(_publicKeyPem!, _productId!, _serverUrl!)) return false;

        // Time: NTP first, local fallback.
        // ⚠️ Intentional divergence: Python cross-checks UDP NTP against HTTPS (worldtimeapi.org)
        // and only trusts HTTPS-confirmed time. C# uses raw UDP NTP, which is weaker against
        // time-spoofing attacks. See NtpClient XML doc for the security tradeoff.
        double ntpTime       = NtpClient.GetNtpTime();
        double now           = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double effectiveTime = ntpTime > 0 ? ntpTime : now;

        // Offline trusted-time window — matches Python _MAX_OFFLINE_WINDOW_DAYS = 7
        // If no trusted time (NTP or heartbeat) for > 7 days, fail closed to prevent
        // frozen-clock + blocked-network expiry bypass.
        const int MaxOfflineWindowDays = 7;
        double lastTrusted = token.LastTrustedTime;
        if (ntpTime > 0)
        {
            // Got NTP time — update trusted-time anchor
            token.LastTrustedTime = ntpTime;
        }
        else if (lastTrusted <= 0)
        {
            // First offline run — seed anchor from local clock (matches Python lines 473-475:
            // data["_last_trusted_time"] = now on first no-NTP verification).
            // Without this, LastTrustedTime stays 0 indefinitely and the 7-day check never fires.
            token.LastTrustedTime = now;
        }
        else if ((now - lastTrusted) > MaxOfflineWindowDays * 86400.0)
        {
            // No trusted time for > 7 days — fail closed (matches Python)
            return false;
        }

        // Clock rollback detection — matches Python logic
        double lastSeen       = token.LastSeen;
        bool   clockRolledBack = lastSeen > 0 && now < lastSeen - 86400;
        if (clockRolledBack && ntpTime <= 0)
        {
            ShowClockWarning();
            return false;
        }

        // Expiry check
        if (effectiveTime > token.Expiry) return false;

        // Update last_seen
        token.LastSeen = Math.Max(effectiveTime, lastSeen);

        // Heartbeat (startup check) — matches Python step 4
        var hb = HeartbeatSync(fp, token.Token);
        if (hb != null)
        {
            if (hb.PermanentlyInvalid || hb.Revoked || !hb.Valid)
            {
                LicenseStorage.Delete();
                return false;
            }
            token.HeartbeatGraceDays  = hb.HeartbeatGraceDays ?? token.HeartbeatGraceDays;
            token.LastOnlineCheck     = effectiveTime;
            token.LastTrustedTime     = effectiveTime; // heartbeat success = trusted time anchor
        }

        // Grace period enforcement — matches Python (graceDays == 0 is NOT skipped;
        // 0 means "reconnect immediately once any positive time has elapsed")
        double lastOnline = token.LastOnlineCheck;
        int    graceDays  = token.HeartbeatGraceDays;
        if (hb == null && lastOnline > 0 && (effectiveTime - lastOnline) > graceDays * 86400.0)
        {
            // Grace expired and server unreachable — block
            var hb2 = HeartbeatSync(fp, token.Token);
            if (hb2 == null) return false;
            if (hb2.PermanentlyInvalid || hb2.Revoked || !hb2.Valid) { LicenseStorage.Delete(); return false; }
            if (hb2.HeartbeatGraceDays.HasValue)
                token.HeartbeatGraceDays = hb2.HeartbeatGraceDays.Value;
            token.LastOnlineCheck    = effectiveTime;
            token.LastTrustedTime    = effectiveTime; // grace-retry success = trusted time anchor
        }

        // Seed LastOnlineCheck on first verification even without a successful heartbeat —
        // matches Python _verify_license_data() which does: if last_online == 0: data["_last_online_check"] = effective_time.
        if (lastOnline <= 0) token.LastOnlineCheck = effectiveTime;

        // Save updated metadata — fail if persistence fails to prevent silent state loss
        if (!LicenseStorage.TrySave(token))
            return false;
        return true;
    }

    private static void EnsureConfigured()
    {
        if (_serverUrl == null || _productId == null || _publicKeyPem == null)
            throw new InvalidOperationException(
                "LicenseGuard.Configure() must be called before using LicenseGuard.");
    }

    /// <summary>
    /// Validates that publicKeyPem is a well-formed Ed25519 SubjectPublicKeyInfo PEM.
    /// Throws ArgumentException on invalid PEM, wrong encoding, or wrong key type.
    /// Mirrors Python _load_public_key() which raises RuntimeError on invalid/wrong type.
    /// </summary>
    private static void ValidatePublicKeyPemOrThrow(string publicKeyPem)
    {
        try
        {
            var pem = publicKeyPem.Trim();

            // Require PEM armor — matches Python load_pem_public_key() which rejects non-PEM input
            if (!pem.Contains("-----BEGIN PUBLIC KEY-----") || !pem.Contains("-----END PUBLIC KEY-----"))
                throw new ArgumentException("PublicKeyPem must be a PEM-encoded PUBLIC KEY (with -----BEGIN/END PUBLIC KEY----- markers).", nameof(publicKeyPem));
            var b64 = pem
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----",   "")
                .Replace("\r", "").Replace("\n", "").Trim();
            var der = Convert.FromBase64String(b64);  // throws FormatException on bad base64

            // Ed25519 SPKI DER must be exactly 44 bytes with the fixed 12-byte OID prefix
            ReadOnlySpan<byte> ed25519Prefix = [
                0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
            ];
            if (der.Length != 44)
                throw new ArgumentException($"Ed25519 public key SPKI DER must be 44 bytes; got {der.Length}.");
            if (!((ReadOnlySpan<byte>)der[..12]).SequenceEqual(ed25519Prefix))
                throw new ArgumentException("DER prefix does not match Ed25519 SubjectPublicKeyInfo OID.");

            // Import the raw key to confirm NSec accepts it
            var rawKey = der[12..];
            var algo   = NSec.Cryptography.SignatureAlgorithm.Ed25519;
            var _      = NSec.Cryptography.PublicKey.Import(algo, rawKey, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"PublicKeyPem is not a valid Ed25519 PEM: {ex.Message}",
                nameof(publicKeyPem),
                ex);
        }
    }

    /// <summary>
    /// Blocking heartbeat call — runs on thread-pool to avoid sync-context issues.
    /// Returns null if server unreachable.
    /// </summary>
    private static HeartbeatResponse? HeartbeatSync(string fp, string licenseToken)
    {
        try
        {
            // Task.Run ensures no captured SynchronizationContext — safe in all callers
            return Task.Run(() => ActivationClient.HeartbeatAsync(_serverUrl!, fp, _productId!, licenseToken))
                       .GetAwaiter().GetResult();
        }
        catch { return null; }
    }

    private static void ShowClockWarning()
    {
        MessageBox.Show(
            "Phát hiện đồng hồ hệ thống bị sai lệch nghiêm trọng.\n\n" +
            "Nguyên nhân có thể:\n" +
            "• Pin CMOS trên mainboard đã hết\n" +
            "• Đồng hồ hệ thống bị chỉnh sai\n\n" +
            "Cách khắc phục:\n" +
            "1. Kết nối Internet để đồng bộ thời gian tự động\n" +
            "2. Settings → Date & Time → Set time automatically: ON\n\n" +
            "Sau khi sửa, khởi động lại phần mềm.",
            "smartPrinter — Lỗi thời gian hệ thống",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
