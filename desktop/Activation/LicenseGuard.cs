using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Main activation gate. Call Configure() before IsActivated() or activation flows.
/// </summary>
public static class LicenseGuard
{
    private static string? _serverUrl;
    private static string? _productId;
    private static string? _publicKeysetJson;
    private static bool _allowInsecureHttp;

    public static void Configure(string serverUrl, string productId, string publicKeysetJson, bool allowInsecureHttp = false)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("ServerUrl must not be empty.", nameof(serverUrl));
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("ProductId must not be empty.", nameof(productId));
        if (string.IsNullOrWhiteSpace(publicKeysetJson))
            throw new ArgumentException("PublicKeysetJson must not be empty.", nameof(publicKeysetJson));

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("ServerUrl must be an absolute URL.", nameof(serverUrl));
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("ServerUrl must use http or https scheme.", nameof(serverUrl));

        var host = uri.Host.ToLowerInvariant();
        var isLocal = host is "localhost" or "127.0.0.1" or "::1";
        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        if (!isLocal && !isHttps && !allowInsecureHttp)
            throw new ArgumentException(
                "ServerUrl must use HTTPS for non-local endpoints. " +
                "Set AllowInsecureHttp=true in smartprinter.appsettings.json to override.",
                nameof(serverUrl));

        ValidatePublicKeysetOrThrow(publicKeysetJson);

        _serverUrl = serverUrl.TrimEnd('/');
        _productId = productId.Trim();
        _publicKeysetJson = publicKeysetJson;
        _allowInsecureHttp = allowInsecureHttp;
    }

    public static string GetFingerprint()
        => FingerprintHelper.GetFingerprint();

    public static bool IsActivated()
    {
        EnsureConfigured();
        var fp = GetFingerprint();
        var token = LicenseStorage.Load(fp);
        if (token == null)
            return false;

        return VerifyToken(token, fp);
    }

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
            return (false, "License fingerprint mismatch.");
        if (!token.VerifySignatureWithKeyset(_publicKeysetJson!, _productId!, _serverUrl!))
            return (false, "License signature invalid.");

        var ntpTime = NtpClient.GetNtpTime();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var effectiveTime = ntpTime > 0 ? ntpTime : now;
        if (token.IsExpired(effectiveTime))
            return (false, "License is already expired.");

        if (result.HeartbeatGraceDays.HasValue)
            token.HeartbeatGraceDays = result.HeartbeatGraceDays.Value;

        var hb = await ActivationClient.HeartbeatAsync(_serverUrl!, fp, _productId!, token.Token);
        if (hb != null)
        {
            if (hb.PermanentlyInvalid || hb.Revoked)
                return (false, hb.Error ?? "Key was revoked on the server.");
            if (!hb.Valid)
                return (false, hb.Error ?? "Activation rejected by server.");
            if (hb.HeartbeatGraceDays.HasValue)
                token.HeartbeatGraceDays = hb.HeartbeatGraceDays.Value;
        }

        token.LastSeen = effectiveTime;
        token.LastOnlineCheck = effectiveTime;
        token.LastTrustedTime = effectiveTime;
        if (!LicenseStorage.TrySave(token))
            return (false, "Failed to save license file. Check disk permissions.");

        return (true, null);
    }

    public static async Task<bool> ActivateOfflineAsync(string licFilePath)
    {
        EnsureConfigured();
        try
        {
            var json = File.ReadAllText(licFilePath);
            var token = JsonSerializer.Deserialize<LicenseToken>(json);
            if (token == null)
                return false;
            if (!token.IsSupportedFormat())
                return false;

            var fp = GetFingerprint();
            if (!token.FingerprintMatches(fp))
                return false;
            if (!token.VerifySignatureWithKeyset(_publicKeysetJson!, _productId!, _serverUrl!))
                return false;

            var ntpTime = NtpClient.GetNtpTime();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var effectiveTime = ntpTime > 0 ? ntpTime : now;
            if (token.IsExpired(effectiveTime))
                return false;

            var hasCompactToken = !string.IsNullOrWhiteSpace(token.Token);
            token.LastSeen = effectiveTime;
            token.LastOnlineCheck = hasCompactToken ? effectiveTime : 0;
            token.LastTrustedTime = effectiveTime;

            // Legacy offline licenses do not include the compact server token needed by /heartbeat.
            // In that case we only persist the locally verified token.
            if (hasCompactToken)
            {
                var hb = await ActivationClient.HeartbeatAsync(_serverUrl!, fp, _productId!, token.Token);
                if (hb != null && (hb.PermanentlyInvalid || hb.Revoked || !hb.Valid))
                    return false;
                if (hb != null)
                {
                    if (hb.HeartbeatGraceDays.HasValue)
                        token.HeartbeatGraceDays = hb.HeartbeatGraceDays.Value;
                    token.LastOnlineCheck = effectiveTime;
                }
            }

            if (!LicenseStorage.TrySave(token))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyToken(LicenseToken token, string fp)
    {
        if (!token.IsSupportedFormat())
            return false;
        if (!token.FingerprintMatches(fp))
            return false;
        if (!token.VerifySignatureWithKeyset(_publicKeysetJson!, _productId!, _serverUrl!))
            return false;

        var ntpTime = NtpClient.GetNtpTime();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var effectiveTime = ntpTime > 0 ? ntpTime : now;

        const int MaxOfflineWindowDays = 7;
        var lastTrusted = token.LastTrustedTime;
        if (ntpTime > 0)
        {
            token.LastTrustedTime = ntpTime;
        }
        else if (lastTrusted <= 0)
        {
            token.LastTrustedTime = now;
        }
        else if ((now - lastTrusted) > MaxOfflineWindowDays * 86400.0)
        {
            return false;
        }

        var lastSeen = token.LastSeen;
        var clockRolledBack = lastSeen > 0 && now < lastSeen - 86400;
        if (clockRolledBack && ntpTime <= 0)
        {
            ShowClockWarning();
            return false;
        }

        if (effectiveTime > token.Expiry)
            return false;

        token.LastSeen = Math.Max(effectiveTime, lastSeen);

        var hasCompactToken = !string.IsNullOrWhiteSpace(token.Token);
        if (hasCompactToken)
        {
            var hb = HeartbeatSync(fp, token.Token);
            if (hb != null)
            {
                if (hb.PermanentlyInvalid || hb.Revoked || !hb.Valid)
                {
                    LicenseStorage.Delete();
                    return false;
                }

                token.HeartbeatGraceDays = hb.HeartbeatGraceDays ?? token.HeartbeatGraceDays;
                token.LastOnlineCheck = effectiveTime;
                token.LastTrustedTime = effectiveTime;
            }

            var lastOnline = token.LastOnlineCheck;
            var graceDays = token.HeartbeatGraceDays;
            if (hb == null && lastOnline > 0 && (effectiveTime - lastOnline) > graceDays * 86400.0)
            {
                var hb2 = HeartbeatSync(fp, token.Token);
                if (hb2 == null)
                    return false;
                if (hb2.PermanentlyInvalid || hb2.Revoked || !hb2.Valid)
                {
                    LicenseStorage.Delete();
                    return false;
                }

                if (hb2.HeartbeatGraceDays.HasValue)
                    token.HeartbeatGraceDays = hb2.HeartbeatGraceDays.Value;
                token.LastOnlineCheck = effectiveTime;
                token.LastTrustedTime = effectiveTime;
            }

            if (lastOnline <= 0)
                token.LastOnlineCheck = effectiveTime;
        }

        if (!LicenseStorage.TrySave(token))
            return false;

        return true;
    }

    private static void EnsureConfigured()
    {
        if (_serverUrl == null || _productId == null || _publicKeysetJson == null)
            throw new InvalidOperationException(
                "LicenseGuard.Configure() must be called before using LicenseGuard.");
    }

    private static void ValidatePublicKeysetOrThrow(string publicKeysetJson)
    {
        try
        {
            using var parsed = JsonDocument.Parse(publicKeysetJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Array || parsed.RootElement.GetArrayLength() == 0)
                throw new ArgumentException("Public keyset JSON must be a non-empty array.", nameof(publicKeysetJson));

            foreach (var entry in parsed.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("kid", out var kid) || string.IsNullOrWhiteSpace(kid.GetString()))
                    throw new ArgumentException("Each keyset entry must contain a non-empty 'kid'.", nameof(publicKeysetJson));
                if (!entry.TryGetProperty("key", out var key) || string.IsNullOrWhiteSpace(key.GetString()))
                    throw new ArgumentException("Each keyset entry must contain a non-empty 'key'.", nameof(publicKeysetJson));

                _ = LicenseToken.DecodeKeysetEntry(key.GetString()!);
            }
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"PublicKeysetJson is not a valid keyset: {ex.Message}",
                nameof(publicKeysetJson),
                ex);
        }
    }

    private static HeartbeatResponse? HeartbeatSync(string fp, string licenseToken)
    {
        try
        {
            return Task.Run(() => ActivationClient.HeartbeatAsync(_serverUrl!, fp, _productId!, licenseToken))
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static void ShowClockWarning()
    {
        MessageBox.Show(
            "Phat hien dong ho he thong bi sai lech nghiem trong.\n\n" +
            "Nguyen nhan co the:\n" +
            "- Pin CMOS tren mainboard da het\n" +
            "- Dong ho he thong bi chinh sai\n\n" +
            "Cach khac phuc:\n" +
            "1. Ket noi Internet de dong bo thoi gian tu dong\n" +
            "2. Settings > Date & Time > Set time automatically: ON\n\n" +
            "Sau khi sua, khoi dong lai phan mem.",
            "smartPrinter - Loi thoi gian he thong",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
