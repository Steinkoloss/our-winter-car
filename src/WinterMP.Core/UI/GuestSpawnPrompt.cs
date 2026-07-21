using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net.Messages;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// Guest-only: returning guests pick spawn at the host or last saved pose;
    /// first-time joiners snap to the host immediately.
    /// </summary>
    public sealed class GuestSpawnPrompt : MonoBehaviour
    {
        public static GuestSpawnPrompt? Instance { get; private set; }

        private GuestSpawn? _offer;
        private GUIStyle? _titleStyle;
        private GUIStyle? _bodyStyle;
        private GUIStyle? _buttonStyle;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ShowOffer(GuestSpawn offer)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            var relocator = PlayerSyncManager.Instance?.GuestRelocator;
            if (relocator == null) return;

            _offer = offer;

            if (!offer.HasLastPosition)
            {
                relocator.ApplyImmediate(offer.HostPosition, offer.HostRotation);
                _offer = null;
            }
        }

        private void ApplySavedNeeds(GuestSpawn offer)
        {
            if (!offer.HasSavedNeeds) return;

            var needsSync = PlayerSyncManager.Instance?.NeedsSync;
            if (needsSync == null) return;

            needsSync.ApplySnapshot(new Session.GuestProfileStore.NeedsSnapshot
            {
                Hunger = offer.Hunger,
                Fatigue = offer.Fatigue,
                Thirst = offer.Thirst,
                Urine = offer.Urine,
                BodyTemp = offer.BodyTemp,
                Stress = offer.Stress,
                Drunk = offer.Drunk,
                Dirtiness = offer.Dirtiness,
                HasDirtiness = offer.HasSavedDirtiness,
                PlayerAlco = offer.PlayerAlco,
                HasAlco = offer.HasSavedAlco,
                Valid = true,
            });
        }

        public bool IsBlockingInput =>
            _offer != null && _offer.HasLastPosition;

        private void OnGUI()
        {
            if (_offer == null || !_offer.HasLastPosition) return;

            var relocator = PlayerSyncManager.Instance?.GuestRelocator;
            if (relocator == null) return;

            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            float width = 420f;
            float height = 210f;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.38f, width, height);

            EnsureStyles();
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);
            GUILayout.Space(14f);
            GUILayout.Label("Where do you want to spawn?", _titleStyle);
            GUILayout.Space(8f);
            GUILayout.Label(
                "Your save loaded somewhere else — pick a starting point for this session.",
                _bodyStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Spawn with host", _buttonStyle, GUILayout.Height(36f)))
                ChooseHost();

            GUILayout.Space(6f);

            string lastLabel = "Spawn at last position  (" +
                _offer.LastPosition.X.ToString("0") + ", " +
                _offer.LastPosition.Z.ToString("0") + ")";
            if (GUILayout.Button(lastLabel, _buttonStyle, GUILayout.Height(36f)))
                ChooseLast();

            GUILayout.Space(12f);
            GUILayout.EndArea();
        }

        private void ChooseHost()
        {
            if (_offer == null) return;
            var relocator = PlayerSyncManager.Instance?.GuestRelocator;
            if (relocator == null) return;

            relocator.ApplyImmediate(_offer.HostPosition, _offer.HostRotation);
            WinterMPPlugin.Log.LogInfo("PlayerSync: guest chose spawn with host.");
            _offer = null;
        }

        private void ChooseLast()
        {
            if (_offer == null) return;
            var relocator = PlayerSyncManager.Instance?.GuestRelocator;
            if (relocator == null) return;

            relocator.ApplyImmediate(_offer.LastPosition, _offer.LastRotation);
            ApplySavedNeeds(_offer);
            WinterMPPlugin.Log.LogInfo("PlayerSync: guest chose last saved position.");
            _offer = null;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            _titleStyle.normal.textColor = Color.white;

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 13,
                wordWrap = true,
            };
            _bodyStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}
