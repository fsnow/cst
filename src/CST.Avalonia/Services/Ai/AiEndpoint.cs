using System;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// Where an endpoint lives, and how long it is reasonable to wait for it. (#673)
    ///
    /// <para>One first-event timeout served two situations that have nothing in common. The ten-minute
    /// allowance was earned by a <b>local runner</b>: a model doing prompt evaluation over an injected passage
    /// on modest hardware can legitimately sit silent for minutes, and folding that into the ordinary idle
    /// window would abandon the fully-local path exactly when it is working hardest. That argument is sound —
    /// and it was then applied to every endpoint, including hosted ones, where it means a reader clicks a
    /// preset and the app is prepared to wait ten minutes before saying anything.</para>
    ///
    /// <para>The base URL already distinguishes the two, so the split costs nothing.</para>
    /// </summary>
    internal static class AiEndpoint
    {
        /// <summary>
        /// A hosted endpoint that produces nothing for this long is not thinking; it is queued behind someone
        /// else or gone. Chosen to be longer than any realistic time-to-first-token on a hosted model while
        /// still being a number a person will sit through.
        /// </summary>
        internal static readonly TimeSpan HostedFirstEventTimeout = TimeSpan.FromMinutes(2);

        /// <summary>True when the endpoint is on this machine or this network — loopback, a private range, or
        /// a <c>.local</c> name.</summary>
        internal static bool IsLocal(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;

            var host = uri.Host;
            if (host.Length == 0) return false;

            if (uri.IsLoopback) return true;
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

            // Private IPv4 ranges. A hosted model behind a LAN gateway is rare; a local runner on another
            // machine on the desk is not, and it deserves the same patience as one on this machine.
            //
            // Parsed as an address rather than matched as a prefix: "172." is private only in 16-31, and a
            // substring test gets 172.200.x wrong by reading two characters and finding "20".
            if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;

            var octets = ip.GetAddressBytes();
            return octets[0] switch
            {
                10 => true,
                172 => octets[1] is >= 16 and <= 31,
                192 => octets[1] == 168,
                _ => false,
            };
        }

        /// <summary>
        /// How long to allow before the FIRST byte. Local keeps the long allowance it earned; hosted gets an
        /// interactive ceiling.
        /// </summary>
        internal static TimeSpan FirstEventTimeoutFor(string? baseUrl) =>
            IsLocal(baseUrl) ? SseReader.DefaultFirstEventTimeout : HostedFirstEventTimeout;
    }
}
