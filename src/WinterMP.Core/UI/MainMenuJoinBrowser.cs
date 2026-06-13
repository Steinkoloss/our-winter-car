using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// Launcher "Join Game" path: native main-menu buttons listing Steam friends in MWC.
    /// </summary>
    public sealed class MainMenuJoinBrowser : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 5f;
        private const string JoinButtonName = "WinterMP_Join";
        private const string NoFriendsButtonName = "WinterMP_NoFriends";
        private const string SteamFriendsButtonName = "WinterMP_SteamFriends";
        private const string CloseButtonName = "WinterMP_Close";

        private string _lastLevel = string.Empty;
        private bool _listOpen;
        private float _nextRefreshAt;
        private Transform? _modRoot;
        private GameObject? _continueButton;
        private GameObject? _newGameButton;
        private bool? _continueWasActive;
        private bool? _newGameWasActive;
        private bool _hidVanillaButtons;
        private string _friendsSignature = string.Empty;
        private readonly List<MainMenuUiFactory.HiddenMenuButton> _hiddenSiblings =
            new List<MainMenuUiFactory.HiddenMenuButton>();
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
                MainMenuUiFactory.ResetLayoutCache();
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

            if (_modRoot == null)
            {
                EnsureJoinButton();
                return;
            }

#if STEAMWORKS
            if (_listOpen && Time.unscaledTime >= _nextRefreshAt)
            {
                RefreshFriends();
                if (FriendsSignatureChanged())
                    SyncFriendList(session!);
                _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            }
#endif
        }

        private void EnsureJoinButton()
        {
            _modRoot = MainMenuUiFactory.EnsureModRoot();
            if (_modRoot == null) return;

            MainMenuUiFactory.SyncButton(
                _modRoot,
                JoinButtonName,
                "Join",
                row: 0,
                OpenFriendList,
                clickEnabled: true);
        }

        private void OpenFriendList()
        {
            _listOpen = true;
            _friendsSignature = string.Empty;
            _nextRefreshAt = 0f;
            MainMenuUiFactory.HideVanillaSiblings(hide: true, _hiddenSiblings);

#if STEAMWORKS
            Steam.SteamBootstrap.EnsureInitialized();
            Steam.SteamLobbyManager.EnsureCallbacksRegistered();
            RefreshFriends();
#endif

            var session = SessionManager.Instance;
            if (session != null)
                SyncFriendList(session);
        }

        private void CloseFriendList()
        {
            _listOpen = false;
            _friendsSignature = string.Empty;
            MainMenuUiFactory.HideVanillaSiblings(hide: false, _hiddenSiblings);

            if (_modRoot != null)
                MainMenuUiFactory.DestroyChildren(_modRoot);

            EnsureJoinButton();
        }

#if STEAMWORKS
        private void RefreshFriends()
        {
            _friends.Clear();
            _friends.AddRange(Steam.SteamFriendBrowser.ListFriendsInMwc());
        }

        private bool FriendsSignatureChanged()
        {
            var sb = new StringBuilder(_friends.Count * 24);
            foreach (var friend in _friends)
            {
                sb.Append(friend.SteamId);
                sb.Append(':');
                sb.Append(friend.LobbyId);
                sb.Append(';');
            }

            string signature = sb.ToString();
            if (signature == _friendsSignature)
                return false;

            _friendsSignature = signature;
            return true;
        }

        private void SyncFriendList(SessionManager session)
        {
            if (_modRoot == null) return;

            var keep = new HashSet<string>();
            int row = 0;

            if (_friends.Count == 0)
            {
                keep.Add(NoFriendsButtonName);
                MainMenuUiFactory.SyncButton(
                    _modRoot,
                    NoFriendsButtonName,
                    "No friends in MWC",
                    row++,
                    onClick: null,
                    clickEnabled: false);
            }
            else
            {
                foreach (var friend in _friends)
                {
                    string key = "WinterMP_Friend_" + friend.SteamId;
                    keep.Add(key);

                    string label = friend.LobbyId != 0
                        ? friend.Name + "  (hosting)"
                        : friend.Name;
                    ulong lobbyId = friend.LobbyId;

                    MainMenuUiFactory.SyncButton(
                        _modRoot,
                        key,
                        label,
                        row++,
                        () => session.RequestJoinFromSteam(lobbyId),
                        clickEnabled: lobbyId != 0);
                }
            }

            keep.Add(SteamFriendsButtonName);
            MainMenuUiFactory.SyncButton(
                _modRoot,
                SteamFriendsButtonName,
                "Steam friends list",
                row++,
                Steam.SteamFriendBrowser.OpenSteamFriendsOverlay,
                clickEnabled: true);

            keep.Add(CloseButtonName);
            MainMenuUiFactory.SyncButton(
                _modRoot,
                CloseButtonName,
                "Close",
                row++,
                CloseFriendList,
                clickEnabled: true);

            MainMenuUiFactory.RemoveExcept(_modRoot, keep);
        }
#endif

        private void HideVanillaButtons()
        {
            EnsureVanillaButtonsCached();
            SetButtonHidden(_continueButton, ref _continueWasActive, true);
            SetButtonHidden(_newGameButton, ref _newGameWasActive, true);
            _hidVanillaButtons = true;
        }

        private void RestoreVanillaButtons()
        {
            MainMenuUiFactory.HideVanillaSiblings(hide: false, _hiddenSiblings);

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
            _friendsSignature = string.Empty;
            MainMenuUiFactory.HideVanillaSiblings(hide: false, _hiddenSiblings);

            if (_modRoot != null)
            {
                MainMenuUiFactory.DestroyChildren(_modRoot);
                UnityEngine.Object.Destroy(_modRoot.gameObject);
                _modRoot = null;
            }
        }
    }
}
