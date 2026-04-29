using System;
using System.Net;
using System.Net.Sockets;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Raw UDP NTP query.
/// ⚠️ Intentional divergence from Python: Python's _get_ntp_time() cross-checks UDP NTP
/// against HTTPS time (worldtimeapi.org) and only trusts HTTPS-confirmed time. This C#
/// implementation uses raw UDP NTP only, which is weaker against time spoofing via
/// network interception. A network attacker who can block HTTPS and forge UDP NTP can
/// control effective_time. This is a known, accepted security tradeoff — document it here
/// so future maintainers understand the gap.
/// Returns Unix epoch as double, or 0 on failure (caller treats 0 as "NTP unavailable").
/// </summary>
internal static class NtpClient
{
    private const string NtpServer      = "pool.ntp.org";
    private const int    NtpPort        = 123;
    private const long   NtpEpochOffset = 2208988800L; // seconds between 1900 and 1970

    public static double GetNtpTime()
    {
        try
        {
            var packet = new byte[48];
            packet[0] = 0x1b; // LI=0, VN=3, Mode=3 (client) — matches Python exactly

            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 3_000; // 3s — matches Python timeout=3
            udp.Connect(NtpServer, NtpPort);
            udp.Send(packet, packet.Length);

            var remote   = new IPEndPoint(IPAddress.Any, 0);
            var response = udp.Receive(ref remote);

            if (response.Length >= 48)
            {
                // Transmit timestamp at bytes 40-43 (seconds since 1900)
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
}
