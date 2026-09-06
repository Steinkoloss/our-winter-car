using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// M1 placeholder UI: TAB-held player list + session status, T-to-chat input.
    /// Replaced by proper UI later; IMGUI keeps us independent of the game's canvases.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private const int MaxVisibleChatLines = 8;

        private bool _chatOpen;
        private string _chatInput = string.Empty;

        // Built once: the version/protocol line never changes, but it was re-interpolated every frame.
        private static readonly string BadgeVersionLine =
            $"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} · protocol v{WinterMP.Net.ProtocolInfo.Version}";
        private static GUIStyle? _richLabel;

        private void OnGUI()
        {
            var session = SessionManager.Instance;
            if (session == null) return;

            HandleChatKeys(session);
            DrawSessionBadge(session);

            if (Input.GetKey(KeyCode.Tab))
                DrawPlayerList(session);

            DrawChat(session);
            if (!_chatOpen) Sync.WorldSyncManager.Instance?.DrawPartFitPrompt();
        }

        private static void DrawSessionBadge(SessionManager session)
        {
            if (session.State == SessionState.Idle) return;

            string color = session.State switch
            {
                SessionState.Connected => "#88FF88",
                SessionState.Hosting => "#88CCFF",
                SessionState.Connecting => "#FFDD88",
                SessionState.Failed => "#FF8888",
                _ => "#FFFFFF",
            };

            GUILayout.BeginArea(new Rect(Screen.width - 268f, 8f, 256f, 44f), GUI.skin.box);
            GUILayout.Label(
                $"<color={color}><b>{session.State}</b></color> — {session.StatusText}",
                RichLabel());
            GUILayout.Label(BadgeVersionLine, RichLabel());
            GUILayout.EndArea();
        }

        private void HandleChatKeys(SessionManager session)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (!_chatOpen && e.keyCode == KeyCode.T && session.State != SessionState.Idle)
            {
                _chatOpen = true;
                _chatInput = string.Empty;
                e.Use();
            }
            else if (_chatOpen && e.keyCode == KeyCode.Return)
            {
                if (!string.IsNullOrEmpty(_chatInput))
                    session.SendChat(_chatInput);
                _chatOpen = false;
                _chatInput = string.Empty;
                e.Use();
            }
            else if (_chatOpen && e.keyCode == KeyCode.Escape)
            {
                _chatOpen = false;
                e.Use();
            }
        }

        private void DrawPlayerList(SessionManager session)
        {
            const float width = 340f;
            float height = 128f + session.PlayerCount * 22f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), GUI.skin.box);

            GUILayout.Label($"<b>{MyPluginInfo.PLUGIN_NAME}</b> {MyPluginInfo.PLUGIN_VERSION} (proto v{WinterMP.Net.ProtocolInfo.Version})", RichLabel());
            GUILayout.Label(session.StatusText);
            GUILayout.Label($"You: {session.LocalPlayerName} (id {session.LocalPlayerId})");
            if (session.State == SessionState.Failed)
                GUILayout.Label("<color=#FFAAAA>F10 = host · Join Game = Steam friend</color>", RichLabel());

            foreach (var player in session.Players)
            {
                string ping = player.PingMs >= 0 ? $"{player.PingMs} ms" : "—";
                GUILayout.Label($"  {player.Name} (id {player.PlayerId})  {ping}");
            }

            var link = ConnectionQuality.Instance;
            string relay = link.UsingRelay switch
            {
                true => "relay",
                false => "direct",
                _ => "—",
            };
            int loss = link.LossPercent;
            string lossText = loss > 0 ? $"{loss}% loss" : "0% loss";
            if (link.SendFailures > 0)
                lossText += $", {link.SendFailures} send err";
            string pause = link.ShouldPauseOwnershipTransfers ? " · <color=#FFCC88>claims paused</color>" : string.Empty;
            GUILayout.Label($"Link: {link.TransportName} · {relay} · {lossText}{pause}", RichLabel());

            var traffic = NetTrafficMeter.Instance;
            if (traffic.SentBytesPerSec > 0 || traffic.RecvBytesPerSec > 0)
            {
                bool over = traffic.PerClientBytesPerSec > NetTrafficMeter.BudgetBytesPerSecond;
                string budget = over
                    ? $" · <color=#FF8888>over {NetTrafficMeter.BudgetBytesPerSecond / 1024} kB/s budget</color>"
                    : string.Empty;
                GUILayout.Label($"Net: {traffic.Summary()}{budget}", RichLabel());
            }

            var world = Sync.WorldSyncManager.Instance;
            if (world != null && (world.DoorCount > 0 || world.BuyCount > 0 || world.ItemCount > 0))
            {
                string catalog = SyncCatalog.Loaded
                    ? $" cat {SyncCatalog.Hash:X8}"
                    : " cat MISSING";
                GUILayout.Label(
                    $"World: {world.DoorCount} doors, {world.BuyCount} shops, {world.BoltCount} bolts, " +
                    $"{world.ItemCount} items [{world.IdHash:X8}]{catalog}");
                if (world.IsDisabled)
                    GUILayout.Label("<color=#FF8888>WorldSync DISABLED (see log)</color>", RichLabel());
            }

            if (WinterMPPlugin.DevKeysEnabled.Value && session.State != SessionState.Idle)
                GUILayout.Label("<color=#AAAAAA>F6 resync nearest · F7 dump sync log</color>", RichLabel());

            GUILayout.EndArea();
        }

        private void DrawChat(SessionManager session)
        {
            var log = session.ChatLog;
            int start = Mathf.Max(0, log.Count - MaxVisibleChatLines);
            int lines = log.Count - start;

            if (lines > 0 || _chatOpen)
            {
                float height = lines * 20f + (_chatOpen ? 28f : 0f) + 10f;
                GUILayout.BeginArea(new Rect(12f, Screen.height - height - 12f, 460f, height));

                for (int i = start; i < log.Count; i++)
                    GUILayout.Label(log[i]);

                if (_chatOpen)
                {
                    GUI.SetNextControlName("WinterMPChat");
                    _chatInput = GUILayout.TextField(_chatInput, 200, GUILayout.Width(440f));
                    GUI.FocusControl("WinterMPChat");
                }

                GUILayout.EndArea();
            }
        }

        private static GUIStyle RichLabel()
        {
            // Cache the rich-text style: it was allocating a fresh GUIStyle (a full copy of
            // GUI.skin.label) at every call site on every OnGUI pass (~2x/frame) all session.
            // Built lazily here because GUI.skin is only valid inside a GUI callback.
            if (_richLabel == null)
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            return _richLabel;
        }
    }
}
