using System;
using System.Net;
using System.Net.Sockets;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Loopback port checks for the stable-config feature (#275): pre-check a FIXED port before Kestrel binds
    /// (so we can warn the user instead of failing the API start), and pick a fresh available port for the
    /// "rotate port" action.
    /// </summary>
    internal static class PortAvailability
    {
        /// <summary>True if a loopback listener can bind <paramref name="port"/> right now. Port 0 (ephemeral)
        /// is always available. There is an inherent bind-check-then-bind race, but it turns "silent Kestrel
        /// throw" into "clear warning" for the common in-use case.</summary>
        public static bool IsAvailable(int port)
        {
            if (port <= 0) return true;
            if (port > 65535) return false;
            TcpListener? listener = null;
            try { listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); return true; }
            catch (SocketException) { return false; }
            finally { listener?.Stop(); }
        }

        /// <summary>A random currently-available loopback port below the OS ephemeral range (so it won't clash
        /// with an OS-assigned one), or 0 (ephemeral) if none is found.</summary>
        public static int PickAvailable()
        {
            for (int i = 0; i < 50; i++)
            {
                int p = Random.Shared.Next(1025, 49151);
                if (IsAvailable(p)) return p;
            }
            return 0;
        }
    }
}
