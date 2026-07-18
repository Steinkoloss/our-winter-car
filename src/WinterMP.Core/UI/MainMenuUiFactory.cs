using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.Core.UI
{
    /// <summary>Clones native Interface/Buttons menu controls for mod UI.</summary>
    internal static class MainMenuUiFactory
    {
        public const string ButtonsPath = "Interface/Buttons";
        public const string ContinuePath = "Interface/Buttons/ButtonContinue";
        public const string NewGamePath = "Interface/Buttons/ButtonNewgame";
        private const string TemplatePath = NewGamePath;
        private const string ModRootName = "WinterMP_JoinBrowser";

        private static Vector3? _continuePos;
        private static Vector3? _newGamePos;
        private static float? _rowStep;

        public static Transform? EnsureModRoot()
        {
            var parent = GameObject.Find(ButtonsPath);
            if (parent == null) return null;

            var existing = parent.transform.Find(ModRootName);
            if (existing != null) return existing;

            var root = new GameObject(ModRootName);
            root.transform.parent = parent.transform;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root.transform;
        }

        public static void ResetLayoutCache()
        {
            _continuePos = null;
            _newGamePos = null;
            _rowStep = null;
        }

        public static float RowStep()
        {
            if (_rowStep.HasValue) return _rowStep.Value;

            EnsureLayoutCache();
            return _rowStep ?? 0.08f;
        }

        public static Vector3 RowPosition(int row)
        {
            EnsureLayoutCache();

            Vector3 continuePos = _continuePos ?? Vector3.zero;
            Vector3 newGamePos = _newGamePos ?? continuePos;
            float rowStep = _rowStep ?? 0.08f;

            if (row <= 0) return continuePos;
            if (row == 1) return newGamePos;
            return new Vector3(newGamePos.x, newGamePos.y - rowStep * (row - 1), newGamePos.z);
        }

        public static GameObject? SyncButton(
            Transform parent,
            string objectName,
            string label,
            int row,
            Action? onClick,
            bool clickEnabled)
        {
            var button = parent.Find(objectName)?.gameObject;
            if (button == null)
            {
                button = CreateButton(parent, objectName, label, row, onClick, clickEnabled);
                return button;
            }

            SetButtonLabel(button, label);
            ConfigureClick(button, onClick, clickEnabled);
            button.transform.localPosition = RowPosition(row);
            if (!button.activeSelf)
                button.SetActive(true);
            return button;
        }

        public static GameObject? CreateButton(
            Transform parent,
            string objectName,
            string label,
            int row,
            Action? onClick,
            bool clickEnabled)
        {
            var template = GameObject.Find(TemplatePath);
            if (template == null) return null;

            var clone = UnityEngine.Object.Instantiate(template);
            clone.name = objectName;
            clone.transform.parent = parent;
            clone.transform.localPosition = RowPosition(row);
            clone.transform.localRotation = template.transform.localRotation;
            clone.transform.localScale = template.transform.localScale;

            StripActionFsms(clone);
            SetButtonLabel(clone, label);
            ConfigureClick(clone, onClick, clickEnabled);
            clone.SetActive(true);
            return clone;
        }

        public static void RemoveExcept(Transform parent, HashSet<string> keepNames)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (keepNames.Contains(child.name)) continue;
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public static void DestroyChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
        }

        public static void HideVanillaSiblings(bool hide, IList<HiddenMenuButton> hidden)
        {
            var buttons = GameObject.Find(ButtonsPath);
            if (buttons == null) return;

            if (hide)
            {
                hidden.Clear();
                foreach (Transform child in buttons.transform)
                {
                    if (child.name == ModRootName) continue;

                    var go = child.gameObject;
                    hidden.Add(new HiddenMenuButton(go, go.activeSelf));
                    go.SetActive(false);
                }
                return;
            }

            for (int i = 0; i < hidden.Count; i++)
            {
                var entry = hidden[i];
                if (entry.Button != null)
                    entry.Button.SetActive(entry.WasActive);
            }

            hidden.Clear();
        }

        private static void EnsureLayoutCache()
        {
            if (_continuePos.HasValue && _newGamePos.HasValue && _rowStep.HasValue) return;

            var continueBtn = GameObject.Find(ContinuePath);
            var newGameBtn = GameObject.Find(NewGamePath);

            _continuePos = continueBtn != null ? continueBtn.transform.localPosition : Vector3.zero;
            _newGamePos = newGameBtn != null ? newGameBtn.transform.localPosition : _continuePos.Value;
            _rowStep = Mathf.Abs(_continuePos.Value.y - _newGamePos.Value.y);
            if (_rowStep.Value <= 0.0001f)
                _rowStep = 0.08f;
        }

        private static void ConfigureClick(GameObject button, Action? onClick, bool clickEnabled)
        {
            var click = button.GetComponent<MainMenuUiButton>();
            if (click == null)
                click = button.AddComponent<MainMenuUiButton>();

            click.Configure(onClick ?? delegate { }, clickEnabled);
        }

        private static void StripActionFsms(GameObject button)
        {
            foreach (var fsm in button.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm.FsmName == "SetSize") continue;
                fsm.enabled = false;
            }
        }

        private static void SetButtonLabel(GameObject button, string label)
        {
            foreach (var text in button.GetComponentsInChildren<TextMesh>(true))
                text.text = label;
        }

        internal struct HiddenMenuButton
        {
            public readonly GameObject? Button;
            public readonly bool WasActive;

            public HiddenMenuButton(GameObject button, bool wasActive)
            {
                Button = button;
                WasActive = wasActive;
            }
        }
    }
}
