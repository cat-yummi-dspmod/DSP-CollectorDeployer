using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CollectorDeployer
{
    [HarmonyPatch(typeof(UIFunctionPanel))]
    public static class UIFunctionPanelPatch
    {
        private const float Gap = 8f;
        private const int CollectorItemId = 2105;

        private static UIButton _button;
        private static RectTransform _rect;
        private static RectTransform _widthRect;
        private static Text[] _texts;
        private static int _labelLcid = -1;
        private static bool _createFailed;
        private static readonly Vector3[] Corners = new Vector3[4];

        [HarmonyPostfix]
        [HarmonyPatch("_OnUpdate")]
        public static void OnUpdate_Postfix(UIFunctionPanel __instance)
        {
            UIButton src = __instance.extractButton;
            if (src == null)
                return;

            if (_button == null)
            {
                if (_createFailed)
                    return;
                Create(__instance);
                if (_button == null)
                    return;
            }

            bool show = src.gameObject.activeSelf;
            if (_button.gameObject.activeSelf != show)
                _button.gameObject.SetActive(show);
            if (!show)
                return;

            RefreshLabel();
            Layout(__instance);
        }

        internal static void DestroyButton()
        {
            if (_button != null)
                Object.Destroy(_button.gameObject);
            _button = null;
        }

        private static void Create(UIFunctionPanel panel)
        {
            UIButton src = panel.extractButton;
            try
            {
                GameObject go = Object.Instantiate(src.gameObject, src.transform.parent, false);
                go.name = "collector-deployer-button";

                foreach (Localizer localizer in go.GetComponentsInChildren<Localizer>(true))
                    Object.Destroy(localizer);
                foreach (EventTrigger trigger in go.GetComponentsInChildren<EventTrigger>(true))
                    Object.Destroy(trigger);

                _button = go.GetComponent<UIButton>();
                _rect = go.GetComponent<RectTransform>();
                _button.eventReceiver = null;
                _button.highlighted = false;
                _button.tips.itemId = 0;
                if (_button.button != null)
                {
                    _button.button.onClick = new Button.ButtonClickedEvent();
                    _button.button.onClick.AddListener(CollectorDeploy.Request);
                }
                else
                {
                    _button.onClick += _ => CollectorDeploy.Request();
                }

                _widthRect = FindCounterpart(src.transform, panel.extractButtonTrans, go.transform) as RectTransform ?? _rect;

                if (panel.extractProgressImage != null
                    && FindCounterpart(src.transform, panel.extractProgressImage.transform, go.transform) is Transform progress
                    && progress.GetComponent<Image>() is Image progressImage)
                {
                    progressImage.fillAmount = 0f;
                    progressImage.enabled = false;
                }

                _texts = go.GetComponentsInChildren<Text>(true);
                SetIcon(go);
                _labelLcid = -1;
                RefreshLabel();

                CollectorDeployerPlugin.Log?.LogDebug("一键部署按钮已创建:\n" + DumpHierarchy(go.transform, 0));
            }
            catch (System.Exception e)
            {
                _createFailed = true;
                CollectorDeployerPlugin.Log?.LogError($"创建按钮失败: {e}");
                DestroyButton();
            }
        }

        private static void RefreshLabel()
        {
            int lcid = Localization.CurrentLanguageLCID;
            if (lcid == _labelLcid)
                return;
            _labelLcid = lcid;

            string label = Lang.ButtonLabel;
            if (_texts != null && _texts.Length > 0)
            {
                string[] lines = label.Split('\n');
                if (_texts.Length >= lines.Length && lines.Length > 1)
                {
                    for (int i = 0; i < _texts.Length; i++)
                        _texts[i].text = i < lines.Length ? lines[i] : "";
                }
                else
                {
                    _texts[0].text = label;
                    for (int i = 1; i < _texts.Length; i++)
                        _texts[i].text = "";
                }
            }

            _button.tips.tipTitle = Lang.TipTitle;
            _button.tips.tipText = Lang.TipText;
        }

        // The panel resizes the extract button every frame, so mirror its size and sit just to its left.
        private static void Layout(UIFunctionPanel panel)
        {
            RectTransform srcRoot = panel.extractButton.transform as RectTransform;
            RectTransform srcWidthRect = panel.extractButtonTrans != null ? panel.extractButtonTrans : srcRoot;
            if (srcRoot == null || srcWidthRect == null)
                return;

            _rect.sizeDelta = srcRoot.sizeDelta;
            if (_widthRect != _rect)
                _widthRect.sizeDelta = srcWidthRect.sizeDelta;

            Transform parent = srcRoot.parent;
            srcWidthRect.GetWorldCorners(Corners);
            float left = parent.InverseTransformPoint(Corners[0]).x;
            float right = parent.InverseTransformPoint(Corners[2]).x;
            float width = right - left;

            _rect.anchoredPosition = srcRoot.anchoredPosition - new Vector2(width + Gap, 0f);
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

            var path = new System.Collections.Generic.List<int>();
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
