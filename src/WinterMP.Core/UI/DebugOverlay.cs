using UnityEngine;
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

        private void OnGUI()
        {
            var session = SessionManager.Instance;
            if (session == null) return;

            HandleChatKeys(session);

            if (Input.GetKey(KeyCode.Tab))
                DrawPlayerList(session);

            DrawChat(session);
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
            const float width = 320f;
            float height = 106f + session.PlayerCount * 22f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), GUI.skin.box);

            GUILayout.Label($"<b>WinterMP</b> — {session.StatusText}", RichLabel());
            GUILayout.Label($"You: {session.LocalPlayerName} (id {session.LocalPlayerId})");

            foreach (var player in session.Players)
            {
                string ping = player.PingMs >= 0 ? $"{player.PingMs} ms" : "—";
                GUILayout.Label($"  {player.Name} (id {player.PlayerId})  {ping}");
            }

            var world = Sync.WorldSyncManager.Instance;
            if (world != null && (world.DoorCount > 0 || world.BoltCount > 0 || world.ItemCount > 0))
            {
                // Same id hash on both machines = identical sync catalogs.
                GUILayout.Label(
                    $"World: {world.DoorCount} doors, {world.BoltCount} bolts, " +
                    $"{world.ItemCount} items [{world.IdHash:X8}]");
            }

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
            var style = new GUIStyle(GUI.skin.label) { richText = true };
            return style;
        }
    }
}
