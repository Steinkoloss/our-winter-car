using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.UI
{
    /// <summary>Guest prompt when the host wants to sleep / skip time.</summary>
    public sealed class SleepConsentPrompt : MonoBehaviour
    {
        public static SleepConsentPrompt? Instance { get; private set; }

        private SleepConsentRequest? _request;
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

        public void ShowRequest(SleepConsentRequest request)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            _request = request;
        }

        public void DismissRequest(byte requestId)
        {
            if (_request != null && _request.RequestId == requestId)
                _request = null;
        }

        public bool IsBlockingInput => _request != null;

        private void OnGUI()
        {
            if (_request == null) return;

            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            float width = 420f;
            float height = 200f;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.32f, width, height);

            EnsureStyles();
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);
            GUILayout.Space(14f);
            GUILayout.Label("Host wants to sleep", _titleStyle);
            GUILayout.Space(8f);
            GUILayout.Label(
                "Time will advance for everyone. Accept to sync with the host.",
                _bodyStyle);
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Accept", _buttonStyle, GUILayout.Height(36f)))
                Respond(true);
            GUILayout.Space(8f);
            if (GUILayout.Button("Decline", _buttonStyle, GUILayout.Height(36f)))
                Respond(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);
            GUILayout.EndArea();
        }

        private void Respond(bool accepted)
        {
            if (_request == null) return;

            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            session.SendPlayerProfileMessage(new SleepConsentResponse
            {
                RequestId = _request.RequestId,
                PlayerId = session.LocalPlayerId,
                Accepted = accepted,
            });

            _request = null;
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
