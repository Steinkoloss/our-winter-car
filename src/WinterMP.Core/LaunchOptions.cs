using System;

namespace WinterMP.Core
{
    public enum LaunchMode
    {
        None,
        Host,
        Join,
        /// <summary>Launcher "Join Game" — browse Steam friends in MWC on the main menu.</summary>
        JoinBrowse,
        /// <summary>Host a localhost UDP session (two-instance test mode, no Steam).</summary>
        HostLocal,
        /// <summary>Join a localhost UDP session (two-instance test mode, no Steam).</summary>
        JoinLocal,
    }

    /// <summary>
    /// Parsed command line. Entry paths:
    ///  - the launcher passes "-wintermp host", "-wintermp join" (browse friends), or
    ///    "-wintermp join &lt;lobbyId&gt;",
    ///  - Steam's "Join Game"/invite flow launches the game with "+connect_lobby &lt;lobbyId&gt;"
    ///    (driven by the rich presence "connect" key the host sets),
    ///  - the local two-instance test uses "-wintermp hostlocal [port]" and
    ///    "-wintermp joinlocal [address:port]" plus "-wintermp-playername &lt;name&gt;"
    ///    so the second instance is distinguishable.
    /// </summary>
    public sealed class LaunchOptions
    {
        public const int DefaultLocalPort = WinterMP.Net.Transport.UdpTransport.DefaultPort;

        public LaunchMode Mode { get; private set; } = LaunchMode.None;
        public ulong LobbyId { get; private set; }
        public int LocalPort { get; private set; } = DefaultLocalPort;
        public string LocalAddress { get; private set; } = "127.0.0.1";
        /// <summary>Optional display-name override; wins over config and Steam persona (test tooling).</summary>
        public string? PlayerName { get; private set; }
        /// <summary>Test tooling: auto open/close a door N seconds after world sync is ready. 0 = off.</summary>
        public float DoorTestDelaySeconds { get; private set; }

        /// <summary>Deprecated: auto-load is handled by WinterMP FastBoot. Flag is ignored.</summary>
        public bool AutoLoadSave { get; private set; }

        public static LaunchOptions FromCommandLine(string[] args)
        {
            var options = new LaunchOptions();

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "+connect_lobby", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                    && ulong.TryParse(args[i + 1], out ulong lobbyFromSteam))
                {
                    options.Mode = LaunchMode.Join;
                    options.LobbyId = lobbyFromSteam;
                    continue; // Steam invite wins over -wintermp, but keep scanning for the name override
                }

                if (string.Equals(args[i], "-wintermp-playername", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                {
                    options.PlayerName = args[i + 1];
                    continue;
                }

                if (string.Equals(args[i], "-wintermp-autoload", StringComparison.OrdinalIgnoreCase))
                {
                    options.AutoLoadSave = true;
                    continue;
                }

                if (string.Equals(args[i], "-wintermp-doortest", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                    && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float doorTestDelay))
                {
                    options.DoorTestDelaySeconds = doorTestDelay;
                    continue;
                }

                if (!string.Equals(args[i], "-wintermp", StringComparison.OrdinalIgnoreCase))
                    continue;

                string mode = i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : string.Empty;
                switch (mode)
                {
                    case "host":
                        if (options.Mode == LaunchMode.None) options.Mode = LaunchMode.Host;
                        break;

                    case "join":
                        if (options.Mode != LaunchMode.None) break;

                        if (i + 2 < args.Length && ulong.TryParse(args[i + 2], out ulong lobbyId))
                        {
                            options.Mode = LaunchMode.Join;
                            options.LobbyId = lobbyId;
                        }
                        else
                        {
                            options.Mode = LaunchMode.JoinBrowse;
                        }
                        break;

                    case "hostlocal":
                        if (options.Mode == LaunchMode.None)
                        {
                            options.Mode = LaunchMode.HostLocal;
                            if (i + 2 < args.Length && int.TryParse(args[i + 2], out int port))
                                options.LocalPort = port;
                        }
                        break;

                    case "joinlocal":
                        if (options.Mode == LaunchMode.None)
                        {
                            options.Mode = LaunchMode.JoinLocal;
                            if (i + 2 < args.Length)
                                options.ParseLocalEndpoint(args[i + 2]);
                        }
                        break;
                }
            }

            return options;
        }

        /// <summary>Accepts "port", "address:port" or "address". Silently keeps defaults on junk
        /// (the arg may simply be the next unrelated command-line switch).</summary>
        private void ParseLocalEndpoint(string text)
        {
            if (string.IsNullOrEmpty(text) || text.StartsWith("-", StringComparison.Ordinal)) return;

            if (int.TryParse(text, out int portOnly))
            {
                LocalPort = portOnly;
                return;
            }

            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int port))
            {
                LocalAddress = text.Substring(0, colon);
                LocalPort = port;
            }
            else
            {
                LocalAddress = text;
            }
        }
    }
}
