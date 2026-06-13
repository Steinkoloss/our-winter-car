using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// Launcher "Join Game" path: native main-menu buttons listing Steam friends in MWC.
    /// </summary>
    public sealed class MainMenuJoinBrowser : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 2.5f;

        private string _lastLevel = string.Empty;
        private bool _listOpen;
        private float _nextRefreshAt;
        private Transform? _modRoot;
        private GameObject? _continueButton;
        private GameObject? _newGameButton;
        private bool? _continueWasActive;
        private bool? _newGameWasActive;
        private bool _hidVanillaButtons;
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
                TeardownUi();
            }

            var session = SessionManager.Instance;
            bool active = session != null && session.ShowJoinBrowseUI && level == "MainMenu";

            if (!active)
            {
                if (_hidVanillaButtons)
                    RestoreVanillaButtons();
                TeardownUi();
                return;
            }

            HideVanillaButtons();

#if STEAMWORKS
            if (_listOpen && Time.unscaledTime >= _nextRefreshAt)
            {
                RefreshFriends();
                RebuildButtons(session!);
                _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            }
#endif

            if (_modRoot == null)
                EnsureJoinButton(session!);
        }

        private void EnsureJoinButton(SessionManager session)
        {
            _modRoot = MainMenuUiFactory.EnsureModRoot();
            if (_modRoot == null) return;

            MainMenuUiFactory.CreateButton(
                _modRoot,
                "WinterMP_Join",
                "Join",
                MainMenuUiFactory.AnchorPosition(),
                OpenFriendList,
                clickEnabled: true);
        }

        private void OpenFriendList()
        {
            _listOpen = true;
            _nextRefreshAt = 0f;
#if STEAMWORKS
            Steam.SteamBootstrap.EnsureInitialized();
            Steam.SteamLobbyManager.EnsureCallbacksRegistered();
            RefreshFriends();
#endif
            var session = SessionManager.Instance;
            if (session != null)
                RebuildButtons(session);
        }

        private void CloseFriendList()
        {
            _listOpen = false;
            if (_modRoot != null)
                MainMenuUiFactory.DestroyChildren(_modRoot);

            var session = SessionManager.Instance;
            if (session != null)
                EnsureJoinButton(session);
        }

#if STEAMWORKS
        private void RefreshFriends()
        {
            _friends.Clear();
            _friends.AddRange(Steam.SteamFriendBrowser.ListFriendsInMwc());
        }

        private void RebuildButtons(SessionManager session)
        {
            if (_modRoot == null) return;

            MainMenuUiFactory.DestroyChildren(_modRoot);

            float rowStep = MainMenuUiFactory.RowStep();
            Vector3 anchor = MainMenuUiFactory.AnchorPosition();
            int row = 0;

            if (_friends.Count == 0)
            {
                MainMenuUiFactory.CreateButton(
                    _modRoot,
                    "WinterMP_NoFriends",
                    "No friends in MWC",
                    OffsetRow(anchor, rowStep, row++),
                    onClick: null,
                    clickEnabled: false);
            }
            else
            {
                foreach (var friend in _friends)
                {
                    string label = BuildFriendLabel(friend);
                    ulong lobbyId = friend.LobbyId;
                    MainMenuUiFactory.CreateButton(
                        _modRoot,
                        "WinterMP_Friend_" + friend.SteamId,
                        label,
                        OffsetRow(anchor, rowStep, row++),
                        () => session.RequestJoinFromSteam(lobbyId),
                        clickEnabled: lobbyId != 0);
                }
            }

            MainMenuUiFactory.CreateButton(
                _modRoot,
                "WinterMP_SteamFriends",
                "Steam friends list",
                OffsetRow(anchor, rowStep, row++),
                Steam.SteamFriendBrowser.OpenSteamFriendsOverlay,
                clickEnabled: true);

            MainMenuUiFactory.CreateButton(
                _modRoot,
                "WinterMP_Close",
                "Close",
                OffsetRow(anchor, rowStep, row++),
                CloseFriendList,
                clickEnabled: true);
        }

        private static string BuildFriendLabel(Steam.SteamFriendBrowser.FriendEntry friend)
        {
            if (friend.LobbyId != 0)
                return friend.Name + "  (hosting)";

            return friend.Name;
        }
#endif

        private static Vector3 OffsetRow(Vector3 anchor, float rowStep, int row)
        {
            return new Vector3(anchor.x, anchor.y - rowStep * row, anchor.z);
        }

        private void HideVanillaButtons()
        {
            EnsureVanillaButtonsCached();
            SetButtonHidden(_continueButton, ref _continueWasActive, true);
            SetButtonHidden(_newGameButton, ref _newGameWasActive, true);
            _hidVanillaButtons = true;
        }

        private void RestoreVanillaButtons()
        {
            EnsureVanillaButtonsCached();
            _continueWasActive = null;
            _newGameWasActive = null;

            if (_newGameButton != null)
                _newGameButton.SetActive(true);

            if (_continueButton != null)
                _continueButton.SetActive(HasSaveOnDisk());

            _hidVanillaButtons = false;
        }

        private void EnsureVanillaButtonsCached()
        {
            if (_continueButton == null)
                _continueButton = GameObject.Find(MainMenuUiFactory.ContinuePath);

            if (_newGameButton == null)
                _newGameButton = GameObject.Find(MainMenuUiFactory.NewGamePath);
        }

        private static bool HasSaveOnDisk()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "savefile.txt");
                return System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
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

        private void TeardownUi()
        {
            _listOpen = false;
            if (_modRoot != null)
            {
                MainMenuUiFactory.DestroyChildren(_modRoot);
                UnityEngine.Object.Destroy(_modRoot.gameObject);
                _modRoot = null;
            }
        }
    }
}
