using System;
using HutongGames.PlayMaker;
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

        public static float RowStep()
        {
            var continueBtn = GameObject.Find(ContinuePath);
            var newGameBtn = GameObject.Find(NewGamePath);
            if (continueBtn == null || newGameBtn == null) return 0.08f;

            return Mathf.Abs(continueBtn.transform.localPosition.y - newGameBtn.transform.localPosition.y);
        }

        public static Vector3 AnchorPosition()
        {
            var continueBtn = GameObject.Find(ContinuePath);
            if (continueBtn != null) return continueBtn.transform.localPosition;

            var newGameBtn = GameObject.Find(NewGamePath);
            if (newGameBtn != null) return newGameBtn.transform.localPosition;

            return Vector3.zero;
        }

        public static GameObject? CreateButton(
            Transform parent,
            string objectName,
            string label,
            Vector3 localPosition,
            Action? onClick,
            bool clickEnabled)
        {
            var template = GameObject.Find(TemplatePath);
            if (template == null) return null;

            var existing = parent.Find(objectName);
            if (existing != null)
            {
                var existingButton = existing.gameObject;
                SetButtonLabel(existingButton, label);
                ConfigureClick(existingButton, onClick, clickEnabled);
                existingButton.transform.localPosition = localPosition;
                existingButton.SetActive(true);
                return existingButton;
            }

            var clone = UnityEngine.Object.Instantiate(template);
            clone.name = objectName;
            clone.transform.parent = parent;
            clone.transform.localPosition = localPosition;
            clone.transform.localRotation = template.transform.localRotation;
            clone.transform.localScale = template.transform.localScale;

            StripActionFsms(clone);
            SetButtonLabel(clone, label);
            ConfigureClick(clone, onClick, clickEnabled);
            clone.SetActive(true);
            return clone;
        }

        public static void DestroyChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
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
            foreach (var fsm in button.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName == "SetSize") continue;
                fsm.enabled = false;
            }
        }

        private static void SetButtonLabel(GameObject button, string label)
        {
            foreach (var text in button.GetComponentsInChildren<TextMesh>(true))
                text.text = label;

            foreach (var fsm in button.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Text") continue;

                foreach (var variable in fsm.FsmVariables.StringVariables)
                    variable.Value = label;
            }
        }
    }
}
