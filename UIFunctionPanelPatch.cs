using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CollectorDeployer
{
    [HarmonyPatch(typeof(UIFunctionPanel))]
    public static class UIFunctionPanelPatch
    {
        private const float Gap = 8f;
        private const int CollectorItemId = 2105;

        private sealed class DeployButton
        {
            public UIButton button;
            public RectTransform rect;
            public RectTransform widthRect;
            public Text[] texts;
            public int labelLcid = -1;
            public Func<string> label;
            public Func<string> tipTitle;
            public Func<string> tipText;
        }

        private static DeployButton _local;
        private static DeployButton _galaxy;
        private static bool _createFailed;
        private static readonly Vector3[] Corners = new Vector3[4];

        [HarmonyPostfix]
        [HarmonyPatch("_OnUpdate")]
        [YummiHarness.Guard("2a1ab2fb257cca98")]
        public static void OnUpdate_Postfix(UIFunctionPanel __instance)
        {
            UIButton src = __instance.extractButton;
            if (src == null)
                return;

            if (_local == null || _galaxy == null)
            {
                if (_createFailed)
                    return;
                CreateButtons(__instance);
                if (_local == null || _galaxy == null)
                    return;
            }

            bool show = src.gameObject.activeSelf;
            bool showGalaxy = show && CollectorDeployerPlugin.EnableGalaxyDeploy.Value;
            SetActive(_local, show);
            SetActive(_galaxy, showGalaxy);
            if (!show)
                return;

            float width = SyncSizeAndGetWidth(__instance, _local);
            RefreshLabel(_local);
            int slot = 1;
            if (showGalaxy)
            {
                SyncSizeAndGetWidth(__instance, _galaxy);
                RefreshLabel(_galaxy);
                Place(__instance, _galaxy, width, slot++);
            }
            Place(__instance, _local, width, slot);
        }

        internal static void DestroyButtons()
        {
            if (_local?.button != null)
                Object.Destroy(_local.button.gameObject);
            if (_galaxy?.button != null)
                Object.Destroy(_galaxy.button.gameObject);
            _local = null;
            _galaxy = null;
        }

        private static void CreateButtons(UIFunctionPanel panel)
        {
            try
            {
                _local = CreateButton(panel, "collector-deployer-button", CollectorDeploy.RequestLocal,
                    () => Lang.ButtonLabel, () => Lang.TipTitle, () => Lang.TipText);
                _galaxy = CreateButton(panel, "collector-deployer-galaxy-button", CollectorDeploy.RequestGalaxy,
                    () => Lang.GalaxyButtonLabel, () => Lang.GalaxyTipTitle, () => Lang.GalaxyTipText);
                CollectorDeployerPlugin.Log?.LogDebug("一键部署按钮已创建:\n" + DumpHierarchy(_local.button.transform, 0));
            }
            catch (Exception e)
            {
                _createFailed = true;
                CollectorDeployerPlugin.Log?.LogError($"创建按钮失败: {e}");
                DestroyButtons();
            }
        }

        private static DeployButton CreateButton(UIFunctionPanel panel, string name, Action onClick,
            Func<string> label, Func<string> tipTitle, Func<string> tipText)
        {
            UIButton src = panel.extractButton;
            GameObject go = Object.Instantiate(src.gameObject, src.transform.parent, false);
            go.name = name;

            foreach (Localizer localizer in go.GetComponentsInChildren<Localizer>(true))
                Object.Destroy(localizer);
            foreach (EventTrigger trigger in go.GetComponentsInChildren<EventTrigger>(true))
                Object.Destroy(trigger);

            var result = new DeployButton
            {
                button = go.GetComponent<UIButton>(),
                rect = go.GetComponent<RectTransform>(),
                label = label,
                tipTitle = tipTitle,
                tipText = tipText,
            };

            UIButton button = result.button;
            button.eventReceiver = null;
            button.highlighted = false;
            button.tips.itemId = 0;
            if (button.button != null)
            {
                button.button.onClick = new Button.ButtonClickedEvent();
                button.button.onClick.AddListener(() => onClick());
            }
            else
            {
                button.onClick += _ => onClick();
            }

            result.widthRect = FindCounterpart(src.transform, panel.extractButtonTrans, go.transform) as RectTransform ?? result.rect;

            if (panel.extractProgressImage != null
                && FindCounterpart(src.transform, panel.extractProgressImage.transform, go.transform) is Transform progress
                && progress.GetComponent<Image>() is Image progressImage)
            {
                progressImage.fillAmount = 0f;
                progressImage.enabled = false;
            }

            result.texts = go.GetComponentsInChildren<Text>(true);
            SetIcon(go);
            go.SetActive(false);
            return result;
        }

        private static void SetActive(DeployButton b, bool active)
        {
            if (b.button.gameObject.activeSelf != active)
                b.button.gameObject.SetActive(active);
        }

        private static void RefreshLabel(DeployButton b)
        {
            int lcid = Localization.CurrentLanguageLCID;
            if (lcid == b.labelLcid)
                return;
            b.labelLcid = lcid;

            string label = b.label();
            Text[] texts = b.texts;
            if (texts != null && texts.Length > 0)
            {
                string[] lines = label.Split('\n');
                if (texts.Length >= lines.Length && lines.Length > 1)
                {
                    for (int i = 0; i < texts.Length; i++)
                        texts[i].text = i < lines.Length ? lines[i] : "";
                }
                else
                {
                    texts[0].text = label;
                    for (int i = 1; i < texts.Length; i++)
                        texts[i].text = "";
                }
            }

            b.button.tips.tipTitle = b.tipTitle();
            b.button.tips.tipText = b.tipText();
        }

        // The panel resizes the extract button every frame, so mirror its size; returns its width in the parent's space.
        private static float SyncSizeAndGetWidth(UIFunctionPanel panel, DeployButton b)
        {
            RectTransform srcRoot = (RectTransform)panel.extractButton.transform;
            RectTransform srcWidthRect = panel.extractButtonTrans != null ? panel.extractButtonTrans : srcRoot;

            b.rect.sizeDelta = srcRoot.sizeDelta;
            if (b.widthRect != b.rect)
                b.widthRect.sizeDelta = srcWidthRect.sizeDelta;

            Transform parent = srcRoot.parent;
            srcWidthRect.GetWorldCorners(Corners);
            return parent.InverseTransformPoint(Corners[2]).x - parent.InverseTransformPoint(Corners[0]).x;
        }

        // slot 1 is directly left of the extract button, slot 2 left of that.
        private static void Place(UIFunctionPanel panel, DeployButton b, float width, int slot)
        {
            RectTransform srcRoot = (RectTransform)panel.extractButton.transform;
            b.rect.anchoredPosition = srcRoot.anchoredPosition - new Vector2((width + Gap) * slot, 0f);
        }

        private static void SetIcon(GameObject go)
        {
            ItemProto item = LDB.items.Select(CollectorItemId);
            if (item?.iconSprite == null)
                return;
            foreach (Image image in go.GetComponentsInChildren<Image>(true))
            {
                if (image.name.ToLowerInvariant().Contains("icon"))
                    image.sprite = item.iconSprite;
            }
        }

        private static Transform FindCounterpart(Transform srcRoot, Transform srcTarget, Transform cloneRoot)
        {
            if (srcTarget == null)
                return null;
            if (srcTarget == srcRoot)
                return cloneRoot;
            if (!srcTarget.IsChildOf(srcRoot))
                return null;

            var path = new List<int>();
            for (Transform t = srcTarget; t != srcRoot; t = t.parent)
                path.Add(t.GetSiblingIndex());

            Transform current = cloneRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= current.childCount)
                    return null;
                current = current.GetChild(path[i]);
            }
            return current;
        }

        private static string DumpHierarchy(Transform t, int depth)
        {
            var sb = new StringBuilder();
            sb.Append(' ', depth * 2).Append(t.name);
            foreach (Component c in t.GetComponents<Component>())
                sb.Append(" [").Append(c.GetType().Name).Append(']');
            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++)
                sb.Append(DumpHierarchy(t.GetChild(i), depth + 1));
            return sb.ToString();
        }
    }
}
