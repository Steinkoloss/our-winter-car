using System.IO;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Steam;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// Host lobby on the main menu: hides load/resume controls until a guest connects,
    /// and shows a roster with Steam avatars and display names.
    /// </summary>
    public sealed class MainMenuHostGate : MonoBehaviour
    {
        private const string ContinuePath = "Interface/Buttons/ButtonContinue";
        private const string NewGamePath = "Interface/Buttons/ButtonNewgame";

        private const float AvatarSize = 40f;
        private const float RowHeight = 48f;
        private const float PanelWidth = 360f;

        private string _lastLevel = string.Empty;
        private GameObject? _continueButton;
        private GameObject? _newGameButton;
        private bool? _continueWasActive;
        private bool? _newGameWasActive;
        private bool _wasBlocking;

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

            var session = SessionManager.Instance;

            if (level != _lastLevel)
            {
                _lastLevel = level;
                ClearCachedButtons();
            }

            bool blocking = session != null
                && session.ShouldBlockHostMainMenuLoad
                && level == "MainMenu";

            if (blocking)
            {
                HideLoadButtons();
                _wasBlocking = true;
            }
            else if (_wasBlocking)
            {
                RestoreLoadButtons();
                _wasBlocking = false;
            }
        }

        private void OnGUI()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.ShowHostLobbyRoster) return;
            if (_lastLevel != "MainMenu") return;

            DrawLobbyPanel(session);
        }

        private void DrawLobbyPanel(SessionManager session)
        {
            int remoteCount = session.PlayerCount;
            int rowCount = 1 + remoteCount;
            bool waiting = session.ShouldBlockHostMainMenuLoad;

            float headerHeight = 28f;
            float footerHeight = waiting ? 52f : 28f;
            float panelHeight = headerHeight + rowCount * RowHeight + footerHeight + 16f;
            float x = (Screen.width - PanelWidth) / 2f;
            float y = Screen.height * 0.28f;

            GUILayout.BeginArea(new Rect(x, y, PanelWidth, panelHeight), GUI.skin.box);

            GUILayout.Label("<b>Co-op lobby</b>", RichLabel(TextAnchor.MiddleCenter));
            GUILayout.Space(4f);

            ulong localSteamId = LocalSteamId();
            DrawPlayerRow(localSteamId, session.LocalPlayerName, "Host (you)");

            foreach (var player in session.Players)
                DrawPlayerRow(player.SteamId, player.Name, null);

            GUILayout.Space(6f);
            if (waiting)
            {
                GUILayout.Label(
                    "<color=#FFCC88><b>Waiting for a friend to join</b></color>\n" +
                    "Steam → right-click you → <b>Join Game</b>\n" +
                    "<size=11><color=#AAAAAA>Single player: launch My Winter Car from Steam.</color></size>",
                    RichLabel(TextAnchor.MiddleCenter));
            }
            else
            {
                GUILayout.Label(
                    "<color=#88FF88><b>Ready — click Continue</b></color>",
                    RichLabel(TextAnchor.MiddleCenter));
            }

            GUILayout.EndArea();
        }

        private void DrawPlayerRow(ulong steamId, string name, string? badge)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));

            Rect avatarRect = GUILayoutUtility.GetRect(AvatarSize, AvatarSize, GUILayout.Width(AvatarSize));
            Texture2D? avatar = SteamAvatarCache.TryGet(steamId);
            if (avatar != null)
                GUI.DrawTexture(avatarRect, avatar, ScaleMode.ScaleToFit);
            else
                GUI.Box(avatarRect, string.Empty);

            GUILayout.Space(8f);

            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{EscapeRich(name)}</b>", RichLabel(TextAnchor.MiddleLeft));
            if (!string.IsNullOrEmpty(badge))
                GUILayout.Label($"<color=#AAAAAA>{badge}</color>", RichLabel(TextAnchor.MiddleLeft));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void HideLoadButtons()
        {
            EnsureButtonsCached();
            SetButtonHidden(_continueButton, ref _continueWasActive, true);
            SetButtonHidden(_newGameButton, ref _newGameWasActive, true);
        }

        private void RestoreLoadButtons()
        {
            EnsureButtonsCached();
            _continueWasActive = null;
            _newGameWasActive = null;

            if (_newGameButton != null)
                _newGameButton.SetActive(true);

            // Host gate hides before the game's Check Save FSM enables Continue; restoring
            // the captured wasActive flag left Continue stuck off even when savefile.txt exists.
            if (_continueButton != null)
                _continueButton.SetActive(HasSaveOnDisk());
        }

        private static bool HasSaveOnDisk()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "savefile.txt");
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureButtonsCached()
        {
            if (_continueButton == null)
                _continueButton = GameObject.Find(ContinuePath);

            if (_newGameButton == null)
                _newGameButton = GameObject.Find(NewGamePath);
        }

        private void ClearCachedButtons()
        {
            if (_wasBlocking)
                RestoreLoadButtons();

            _continueButton = null;
            _newGameButton = null;
            _continueWasActive = null;
            _newGameWasActive = null;
            _wasBlocking = false;
        }

        private static void SetButtonHidden(GameObject? button, ref bool? wasActive, bool hide)
        {
            if (button == null) return;

            if (hide)
            {
                if (!wasActive.HasValue)
                    wasActive = button.activeSelf;
                button.SetActive(false);
            }
            else if (wasActive.HasValue)
            {
                button.SetActive(wasActive.Value);
                wasActive = null;
            }
        }

        private static ulong LocalSteamId()
        {
#if STEAMWORKS
            return SteamBootstrap.LocalSteamId;
#else
            return 0UL;
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
