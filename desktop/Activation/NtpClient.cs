using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Trusted time lookup.
/// HTTPS time is canonical; UDP NTP is kept only as a consistency cross-check.
/// Returns Unix epoch as double, or 0 on failure/disagreement (caller treats 0 as "NTP unavailable").
/// </summary>
internal static class NtpClient
{
    private const string NtpServer           = "pool.ntp.org";
    private const int    NtpPort             = 123;
    private const long   NtpEpochOffset      = 2208988800L; // seconds between 1900 and 1970
    private const string HttpsTimeUrl        = "https://worldtimeapi.org/api/timezone/Etc/UTC";
    private const int    MaxClockSkewSeconds = 60;

    private static readonly HttpClient HttpsClient = new(
        new HttpClientHandler
            { AllowAutoRedirect = false, UseProxy = false, CheckCertificateRevocationList = true })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    public static double GetNtpTime()
        => SelectTrustedTime(GetNtpUdpTime(), GetHttpsTime());

    private static double SelectTrustedTime(double ntpTime, double httpsTime)
    {
        if (ntpTime > 0 && httpsTime > 0)
            return Math.Abs(ntpTime - httpsTime) <= MaxClockSkewSeconds ? httpsTime : 0;

        if (httpsTime > 0)
            return httpsTime;

        return 0;
    }

    private static double GetNtpUdpTime()
    {
        try
        {
            var packet = new byte[48];
            packet[0] = 0x1b; // LI=0, VN=3, Mode=3 (client)

            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 3_000;
            udp.Connect(NtpServer, NtpPort);
            udp.Send(packet, packet.Length);

            var remote   = new IPEndPoint(IPAddress.Any, 0);
            var response = udp.Receive(ref remote);

            if (response.Length >= 48)
            {
                uint seconds = (uint)response[40] << 24
                             | (uint)response[41] << 16
                             | (uint)response[42] << 8
                             | response[43];
                return (double)(seconds - NtpEpochOffset);
            }
        }
        catch { }

        return 0;
    }

    private static double GetHttpsTime()
    {
        try
        {
            using var response = HttpsClient.GetAsync(HttpsTimeUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return 0;

            using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("unixtime", out var unixTime)
                && unixTime.TryGetInt64(out var value)
                && value > 0)
            {
                return value;
            }
        }
        catch { }

        return 0;
    }
}
