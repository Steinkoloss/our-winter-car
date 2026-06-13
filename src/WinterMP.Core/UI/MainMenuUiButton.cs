using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// Click handler for cloned main-menu buttons. Forwards hover/press to the game's
    /// SetSize PlayMaker FSM so the button looks native.
    /// </summary>
    internal sealed class MainMenuUiButton : MonoBehaviour
    {
        private PlayMakerFSM? _setSizeFsm;
        private Action? _onClick;
        private bool _clickEnabled = true;

        public void Configure(Action onClick, bool clickEnabled)
        {
            _onClick = onClick;
            _clickEnabled = clickEnabled;
            ResolveSetSizeFsm();
        }

        public void SetClickEnabled(bool enabled)
        {
            _clickEnabled = enabled;
        }

        private void OnMouseEnter()
        {
            _setSizeFsm?.SendEvent("OVER");
        }

        private void OnMouseExit()
        {
            _setSizeFsm?.SendEvent("UP");
        }

        private void OnMouseDown()
        {
            if (!_clickEnabled) return;

            _setSizeFsm?.SendEvent("DOWN");
            _onClick?.Invoke();
        }

        private void ResolveSetSizeFsm()
        {
            if (_setSizeFsm != null) return;

            foreach (var fsm in GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "SetSize") continue;
                _setSizeFsm = fsm;
                return;
            }
        }
    }
}
