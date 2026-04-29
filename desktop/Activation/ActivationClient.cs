using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyPrinter.Desktop.Activation;

internal sealed record ActivateResponse(
    string? Token,
    string? Error,
    bool HeartbeatRequired,
    int? HeartbeatGraceDays,
    long? ExpiresAt);

internal sealed record HeartbeatResponse(
    bool Valid,
    bool Revoked,
    bool PermanentlyInvalid,
    int? HeartbeatGraceDays,
    string? Error);

internal static class ActivationClient
{
    // Separate timeouts to match Python: 30s for activate, 10s for heartbeat
    // ⚠️ AllowAutoRedirect = false — matches Python follow_redirects=False.
    // Default HttpClient follows redirects, which can leak activation traffic to unintended hosts.
    // ⚠️ UseProxy = false — matches Python trust_env=False (R9-M1).
    // Without this, HttpClient inherits ambient proxy/CA environment variables (HTTPS_PROXY, etc.).
    // Activation keys and fingerprints MUST NOT be routed through unintended proxy infrastructure.
    // If a proxy is required, configure it explicitly; do NOT rely on machine/environment defaults.
    private static readonly HttpClient _httpActivate  = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly HttpClient _httpHeartbeat = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// POST /activate — request body: { "activation_key": "...", "fingerprint": "..." }
    /// Server reads data.get('activation_key', '') — field name is NOT "key".
    /// </summary>
    public static async Task<ActivateResponse> ActivateAsync(
        string serverUrl, string activationKey, string fingerprint, string productId)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                activation_key = activationKey,
                fingerprint,
                product_id = productId,
            });
            var res  = await _httpActivate.PostAsync(
                $"{serverUrl.TrimEnd('/')}/activate",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                var err = TryGetString(json, "error") ?? $"HTTP {(int)res.StatusCode}";
                return new ActivateResponse(null, err, false, null, null);
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("license", out var lic))
                return new ActivateResponse(null, "Server response missing 'license' field", false, null, null);

            if (lic.ValueKind is not JsonValueKind.String)
                return new ActivateResponse(null, "Server response 'license' must be a compact token string", false, null, null);

            var token = lic.GetString();
            if (string.IsNullOrWhiteSpace(token) || !token.Contains('.'))
                return new ActivateResponse(null, "Server returned an invalid compact license token", false, null, null);

            return new ActivateResponse(
                token,
                null,
                doc.RootElement.TryGetProperty("heartbeat_required", out var heartbeatRequired) && heartbeatRequired.GetBoolean(),
                doc.RootElement.TryGetProperty("heartbeat_grace_days", out var heartbeatGraceDays) ? heartbeatGraceDays.GetInt32() : null,
                doc.RootElement.TryGetProperty("expires_at", out var expiresAt) ? expiresAt.GetInt64() : null);
        }
        catch (Exception ex)
        {
            return new ActivateResponse(null, ex.Message, false, null, null);
        }
    }

    /// <summary>
    /// POST /heartbeat — request body: { "fingerprint": "..." }
    /// Response: { "valid": bool, "revoked": bool, "heartbeat_grace_days": int }
    /// ⚠️ "heartbeat_grace_days" is ALWAYS present in every response.
    ///    - Unknown fingerprint: server returns exactly 30 (hardcoded).
    ///    - Known device: server returns stored heartbeat_grace_days from DB (0–365, default 30).
    /// Returns null if server unreachable (caller treats null as offline).
    /// </summary>
    public static async Task<HeartbeatResponse?> HeartbeatAsync(string serverUrl, string fingerprint, string productId, string licenseToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                fingerprint,
                product_id = productId,
                license_token = licenseToken,
            });
            var res  = await _httpHeartbeat.PostAsync(
                $"{serverUrl.TrimEnd('/')}/heartbeat",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var json = await res.Content.ReadAsStringAsync();

            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var err = TryGetString(json, "error") ?? "License rejected by server";
                return new HeartbeatResponse(
                    Valid: false,
                    Revoked: string.Equals(err, "Device not authorized", StringComparison.OrdinalIgnoreCase),
                    PermanentlyInvalid: true,
                    HeartbeatGraceDays: TryGetInt(json, "heartbeat_grace_days"),
                    Error: err);
            }

            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var valid = root.TryGetProperty("status", out var status) && status.GetString() == "ok"
                     || root.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();

            return new HeartbeatResponse(
                Valid: valid,
                Revoked: root.TryGetProperty("revoked", out var revoked) && revoked.GetBoolean(),
                PermanentlyInvalid: false,
                HeartbeatGraceDays: root.TryGetProperty("heartbeat_grace_days", out var g) ? g.GetInt32() : null,
                Error: TryGetString(json, "error"));
        }
        catch { return null; }
    }

    private static string? TryGetString(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }

    private static int? TryGetInt(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var val) ? val.GetInt32() : null;
        }
        catch { return null; }
    }
}
