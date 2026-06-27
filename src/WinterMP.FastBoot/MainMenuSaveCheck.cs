using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Waits for the main-menu "Check Save" PlayMaker FSM before driving Continue.
    /// </summary>
    internal static class MainMenuSaveCheck
    {
        private const string ButtonsPath = "Interface/Buttons";
        private const string CheckSaveFsmName = "Check Save";
        private const string SaveValidState = "State 2";
        private const string SaveInvalidState = "State 3";

        private static GameObject? _buttonsRoot;
        private static PlayMakerFSM? _checkSaveFsm;

        public static void Reset()
        {
            _buttonsRoot = null;
            _checkSaveFsm = null;
        }

        public static bool IsSaveValid()
        {
            return TryGetState(out string? state) && state == SaveValidState;
        }

        public static bool AllowsContinueClick(float mainMenuSeenAt, float timeoutSeconds)
        {
            if (TryGetState(out string? state))
            {
                if (state == SaveInvalidState) return false;
                if (state == SaveValidState) return true;
            }

            return mainMenuSeenAt >= 0f
                   && Time.unscaledTime - mainMenuSeenAt >= timeoutSeconds;
        }

        private static bool TryGetState(out string? state)
        {
            state = null;
            if (!ResolveCheckSaveFsm()) return false;

            state = _checkSaveFsm!.ActiveStateName;
            return true;
        }

        private static bool ResolveCheckSaveFsm()
        {
            if (_checkSaveFsm != null) return true;

            if (_buttonsRoot == null)
                _buttonsRoot = GameObject.Find(ButtonsPath);
            if (_buttonsRoot == null) return false;

            foreach (var fsm in _buttonsRoot.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != CheckSaveFsmName) continue;
                _checkSaveFsm = fsm;
                return true;
            }

            return false;
        }
    }
}
