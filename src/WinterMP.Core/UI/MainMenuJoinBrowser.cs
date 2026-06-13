using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// Launcher "Join Game" path: a Join control on the main menu that lists Steam
    /// friends currently in My Winter Car and joins joinable hosts.
    /// </summary>
    public sealed class MainMenuJoinBrowser : MonoBehaviour
    {
        private const float AvatarSize = 36f;
        private const float RowHeight = 44f;
        private const float PanelWidth = 380f;
        private const float RefreshIntervalSeconds = 2.5f;

        private string _lastLevel = string.Empty;
        private bool _listOpen;
        private float _nextRefreshAt;
#if STEAMWORKS
        private readonly List<Steam.SteamFriendBrowser.FriendEntry> _friends =
            new List<Steam.SteamFriendBrowser.FriendEntry>();
#endif

        private void Update()
        {
            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (level != _lastLevel)
            {
                _lastLevel = level;
                _listOpen = false;
            }

            var session = SessionManager.Instance;
            if (session == null || !session.ShowJoinBrowseUI || level != "MainMenu") return;

#if STEAMWORKS
            if (_listOpen && Time.unscaledTime >= _nextRefreshAt)
            {
                _friends.Clear();
                _friends.AddRange(Steam.SteamFriendBrowser.ListFriendsInMwc());
                _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            }
#endif
        }

        private void OnGUI()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.ShowJoinBrowseUI) return;
            if (_lastLevel != "MainMenu") return;

            if (!_listOpen)
                DrawJoinButton();
            else
                DrawFriendList(session);
        }

        private void DrawJoinButton()
        {
            float buttonWidth = 200f;
            float buttonHeight = 44f;
            float x = (Screen.width - buttonWidth) / 2f;
            float y = Screen.height * 0.72f;

            if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), "Join"))
                OpenFriendList();
        }

        private void DrawFriendList(SessionManager session)
        {
#if STEAMWORKS
            int rowCount = _friends.Count;
            bool anyJoinable = false;
            for (int i = 0; i < _friends.Count; i++)
            {
                if (_friends[i].LobbyId != 0)
                {
                    anyJoinable = true;
                    break;
                }
            }

            float headerHeight = 30f;
            float emptyHeight = rowCount == 0 ? 48f : 0f;
            float footerHeight = 72f;
            float listHeight = rowCount * RowHeight;
            float panelHeight = headerHeight + emptyHeight + listHeight + footerHeight + 12f;
            float x = (Screen.width - PanelWidth) / 2f;
            float y = Screen.height * 0.22f;

            GUILayout.BeginArea(new Rect(x, y, PanelWidth, panelHeight), GUI.skin.box);

            GUILayout.Label("<b>Join a friend</b>", RichLabel(TextAnchor.MiddleCenter));
            GUILayout.Space(4f);

            if (rowCount == 0)
            {
                GUILayout.Label(
                    "<color=#AAAAAA>No friends in My Winter Car right now.</color>\n" +
                    "<size=11>Ask your host to use <b>HOST GAME</b> in the launcher.</size>",
                    RichLabel(TextAnchor.MiddleCenter));
            }
            else
            {
                foreach (var friend in _friends)
                    DrawFriendRow(session, friend);
            }

            GUILayout.Space(6f);

            if (!anyJoinable && rowCount > 0)
            {
                GUILayout.Label(
                    "<size=11><color=#AAAAAA>Hosts must launch via <b>HOST GAME</b> to appear as joinable.</color></size>",
                    RichLabel(TextAnchor.MiddleCenter));
                GUILayout.Space(4f);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Steam friends list", GUILayout.Height(28f)))
                Steam.SteamFriendBrowser.OpenSteamFriendsOverlay();
            if (GUILayout.Button("Close", GUILayout.Height(28f), GUILayout.Width(72f)))
                _listOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
#else
            DrawNoSteamPanel();
#endif
        }

#if STEAMWORKS
        private void DrawFriendRow(SessionManager session, Steam.SteamFriendBrowser.FriendEntry friend)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));

            Rect avatarRect = GUILayoutUtility.GetRect(AvatarSize, AvatarSize, GUILayout.Width(AvatarSize));
            Texture2D? avatar = Steam.SteamAvatarCache.TryGet(friend.SteamId);
            if (avatar != null)
                GUI.DrawTexture(avatarRect, avatar, ScaleMode.ScaleToFit);
            else
                GUI.Box(avatarRect, string.Empty);

            GUILayout.Space(8f);

            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{EscapeRich(friend.Name)}</b>", RichLabel(TextAnchor.MiddleLeft));
            GUILayout.Label(
                $"<color=#AAAAAA>{EscapeRich(friend.StatusHint)}</color>",
                RichLabel(TextAnchor.MiddleLeft));
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            GUI.enabled = friend.LobbyId != 0;
            if (GUILayout.Button("Join", GUILayout.Width(64f), GUILayout.Height(32f)))
                session.RequestJoinFromSteam(friend.LobbyId);
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }
#endif

        private void DrawNoSteamPanel()
        {
            float panelHeight = 120f;
            float x = (Screen.width - PanelWidth) / 2f;
            float y = Screen.height * 0.35f;

            GUILayout.BeginArea(new Rect(x, y, PanelWidth, panelHeight), GUI.skin.box);
            GUILayout.Label("<b>Join a friend</b>", RichLabel(TextAnchor.MiddleCenter));
            GUILayout.Space(8f);
            GUILayout.Label(
                "<color=#FFAAAA>Steam is not available in this build.</color>",
                RichLabel(TextAnchor.MiddleCenter));
            if (GUILayout.Button("Close", GUILayout.Height(28f)))
                _listOpen = false;
            GUILayout.EndArea();
        }

        private void OpenFriendList()
        {
            _listOpen = true;
            _nextRefreshAt = 0f;
#if STEAMWORKS
            Steam.SteamBootstrap.EnsureInitialized();
            Steam.SteamLobbyManager.EnsureCallbacksRegistered();
            _friends.Clear();
            _friends.AddRange(Steam.SteamFriendBrowser.ListFriendsInMwc());
            _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
#endif
        }

        private static string EscapeRich(string text)
        {
            if (string.IsNullOrEmpty(text)) return "?";
            return text.Replace("<", "‹").Replace(">", "›");
        }

        private static GUIStyle RichLabel(TextAnchor alignment)
        {
            return new GUIStyle(GUI.skin.label)
            {
                richText = true,
                alignment = alignment,
                wordWrap = true,
            };
        }
    }
}
