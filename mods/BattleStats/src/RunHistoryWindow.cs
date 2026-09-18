using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattleStats
{
    /// <summary>
    /// The run browser: one clickable row per saved run (newest first), the run in progress at the top. Clicking a row
    /// opens the Stats window with that run's numbers. Built at runtime from the game's own widgets so it matches the
    /// rest of the UI (panel sprite and fonts borrowed from the confirm dialog, same container as the game's windows),
    /// and registered as a UIWindow so the game blocks its input and Escape closes it. Picking a run opens the Stats
    /// window and closes this list, so Escape from there goes straight back to the game.
    /// </summary>
    internal static class RunHistoryWindow
    {
        private const float PanelWidth = 720f, HeaderHeight = 86f, ListHeight = 360f, FooterHeight = 80f, RowHeight = 46f;

        private static GameObject _root, _panel;
        private static UIWindow _uiWindow;
        private static ScrollRect _scroll;
        private static RectTransform _rowHolder;
        private static TextMeshProUGUI _fontTitle, _fontBody, _subtitle;
        private static bool _registered, _closing;
        private static readonly List<GameObject> _rows = new List<GameObject>();

        private static BattleStatsConfig Cfg => BattleStatsPlugin.Cfg;
        internal static bool IsOpen => _root != null && _root.activeSelf;

        internal static void Tick()
        {
            if (Cfg == null) return;
            if (Cfg.RunHistoryKey.Value.IsDown() && !Patches.RunStatsPatches.TypingInAnyTextField() && !Patches.RunStatsPatches.BindingControls())
            {
                if (IsOpen) Close(false);
                else Open();
            }
            else if (IsOpen && !_registered && Input.GetKeyDown(KeyCode.Escape)) Close(false);
        }

        internal static void Reset()
        {
            try { if (IsOpen) Close(false); } catch { }
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null; _panel = null; _uiWindow = null; _scroll = null; _rowHolder = null; _subtitle = null; _rows.Clear();
        }

        internal static void Open()
        {
            try
            {
                if (_root == null && !Build()) return;
                Refresh();
                _root.transform.SetAsLastSibling();
                _root.SetActive(true);
                if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
                _registered = false;
                if (_uiWindow != null && UIWindowManager.Instance != null) { _uiWindow.OpenWindow(); _registered = true; }
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run history window failed to open: " + e); }
        }

        internal static void Close(bool fromGame)
        {
            if (_closing) return;
            _closing = true;
            try
            {
                if (_root != null) _root.SetActive(false);
                if (_registered && !fromGame && _uiWindow != null) { try { _uiWindow.CloseWindow(); } catch { } }
                _registered = false;
            }
            finally { _closing = false; }
        }

        // ---------- rows ----------

        private static void Refresh()
        {
            foreach (GameObject go in _rows) { try { if (go != null) UnityEngine.Object.Destroy(go); } catch { } }
            _rows.Clear();
            List<RunHistory.RunFile> runs = RunHistory.LoadAll();
            if (Cfg != null && Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Run history: " + runs.Count + " run file(s) in " + RunHistory.Folder);
            string current = RunStats.FilePath;
            foreach (RunHistory.RunFile r in runs)
                if (!string.IsNullOrEmpty(current) && string.Equals(r.Path, current, StringComparison.OrdinalIgnoreCase)) r.IsCurrent = true;
            // the run in progress belongs at the top whatever its timestamp says
            runs = runs.OrderByDescending(r => r.IsCurrent).ThenByDescending(r => r.StartedAt).ToList();
            if (_subtitle != null)
                _subtitle.text = runs.Count == 0
                    ? "No runs saved yet. Every battle you fight is added to the run you are on, and the run is written to disk after each fight."
                    : runs.Count + (runs.Count == 1 ? " run saved" : " runs saved") + ". Click one to open its stats page.";
            if (runs.Count == 0) return;
            foreach (RunHistory.RunFile r in runs) MakeRow(r);
        }

        private static void MakeRow(RunHistory.RunFile run)
        {
            var row = new GameObject("Run " + run.Started, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(Button));
            row.transform.SetParent(_rowHolder, false);
            var le = row.GetComponent<LayoutElement>(); le.preferredHeight = RowHeight; le.minHeight = RowHeight;
            Image bg = row.GetComponent<Image>();
            bg.color = run.IsCurrent ? new Color(0.80f, 0.70f, 0.45f, 0.10f) : new Color(1f, 1f, 1f, 0.03f);
            bg.raycastTarget = true;
            RectTransform rrt = row.GetComponent<RectTransform>();

            TextMeshProUGUI head = MakeText(rrt, "Head", run.Headline(), _fontBody, 13, new Color32(0xE8, 0xDF, 0xD0, 0xFF), TextAlignmentOptions.MidlineLeft);
            Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -25f), new Vector2(-12f, -3f));
            TextMeshProUGUI sub = MakeText(rrt, "Sub", run.SubLine(), _fontBody, 10, new Color32(0x9A, 0xA5, 0xB1, 0xFF), TextAlignmentOptions.MidlineLeft);
            Place(sub.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 4f), new Vector2(-12f, -25f));

            Button b = row.GetComponent<Button>();
            b.targetGraphic = bg;
            var colors = b.colors; colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f); colors.pressedColor = new Color(1f, 1f, 1f, 0.18f);
            colors.normalColor = Color.white; colors.selectedColor = colors.highlightedColor; b.colors = colors;
            b.onClick = new Button.ButtonClickedEvent();
            RunHistory.RunFile captured = run;
            b.onClick.AddListener(() => Patches.RunStatsPatches.OpenFromHistory(captured));
            _rows.Add(row);
        }

        // ---------- construction ----------

        private static bool Build()
        {
            Transform parent = null;
            try { if (ReferenceLoader.Instance != null && ReferenceLoader.Instance.StatManagerPlaceholder != null) parent = ReferenceLoader.Instance.StatManagerPlaceholder.parent; } catch { }
            try { if (parent == null && ConfirmWindow.Instance != null) parent = ConfirmWindow.Instance.transform.parent; } catch { }
            if (parent == null) { BattleStatsPlugin.Log.LogWarning("Run history: no UI container found yet (open it once the game has loaded)"); return false; }
            try { if (ConfirmWindow.Instance != null) { _fontTitle = ConfirmWindow.Instance.Title; _fontBody = ConfirmWindow.Instance.Message; } } catch { }

            float height = HeaderHeight + ListHeight + FooterHeight;
            _root = new GameObject("BattleStatsRunHistory", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one; rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
            Image backdrop = _root.GetComponent<Image>(); backdrop.color = new Color(0f, 0f, 0f, 0.55f); backdrop.raycastTarget = true;
            _uiWindow = _root.AddComponent<UIWindow>();
            _uiWindow.UIWindow_CanCancel = true; _uiWindow.UIWindow_CloseOnLoad = true;
            _uiWindow.OnCloseEvent = new UnityEngine.Events.UnityEvent();
            _uiWindow.OnCloseEvent.AddListener(() => Close(true));

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform prt = _panel.GetComponent<RectTransform>();
            prt.SetParent(rootRt, false);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, height); prt.anchoredPosition = Vector2.zero;
            Image panelImg = _panel.GetComponent<Image>();
            if (!CopyPanelLook(panelImg)) panelImg.color = new Color(0.10f, 0.08f, 0.07f, 0.97f);

            TextMeshProUGUI title = MakeText(prt, "Title", "Run History", _fontTitle, 20, new Color32(0xCB, 0xB3, 0x96, 0xFF), TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -32f), new Vector2(0f, 0f));
            _subtitle = MakeText(prt, "Subtitle", "", _fontBody, 11, new Color32(0x9A, 0xA5, 0xB1, 0xFF), TextAlignmentOptions.Center);
            Place(_subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -80f), new Vector2(-20f, -38f));

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            RectTransform vp = viewportGo.GetComponent<RectTransform>();
            vp.SetParent(prt, false);
            Place(vp, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -(HeaderHeight + ListHeight)), new Vector2(-24f, -HeaderHeight));
            Image vpImg = viewportGo.GetComponent<Image>(); vpImg.color = new Color(0f, 0f, 0f, 0f); vpImg.raycastTarget = true;
            _rowHolder = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            _rowHolder.SetParent(vp, false);
            _rowHolder.anchorMin = new Vector2(0f, 1f); _rowHolder.anchorMax = new Vector2(1f, 1f); _rowHolder.pivot = new Vector2(0.5f, 1f);
            _rowHolder.anchoredPosition = Vector2.zero; _rowHolder.sizeDelta = new Vector2(-10f, 0f);
            var vlg = _rowHolder.GetComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true; vlg.childControlWidth = true; vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true; vlg.spacing = 2f;
            var fitter = _rowHolder.GetComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            _scroll = viewportGo.GetComponent<ScrollRect>();
            _scroll.content = _rowHolder; _scroll.viewport = vp; _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.inertia = true; _scroll.decelerationRate = 0.135f; _scroll.scrollSensitivity = 25f;
            _scroll.verticalScrollbar = MakeScrollbar(vp); _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            try { viewportGo.AddComponent<ScrollViewExtended>(); } catch { }

            TextMeshProUGUI hint = MakeText(prt, "Hint", "Saved in BepInEx\\BattleStats\\runs as one .json per run.", _fontBody, 10, new Color32(0x9A, 0xA5, 0xB1, 0xFF), TextAlignmentOptions.Center);
            Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 60f), new Vector2(-20f, 78f));
            Button close = MakeButton(prt, "Close", () => Close(false));
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f); crt.pivot = new Vector2(0.5f, 0f); crt.anchoredPosition = new Vector2(0f, 14f);

            _root.SetActive(false);
            if (Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Run history window built under " + parent.name);
            return true;
        }

        // ---------- widget helpers (same approach as the mod menu) ----------

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        private static bool CopyPanelLook(Image target)
        {
            try
            {
                if (ConfirmWindow.Instance == null) return false;
                Image best = null;
                foreach (Image img in ConfirmWindow.Instance.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null || img.GetComponent<Button>() != null || img.GetComponentInParent<Button>() != null) continue;
                    RectTransform r = img.rectTransform;
                    if (best == null || r.rect.width * r.rect.height > best.rectTransform.rect.width * best.rectTransform.rect.height) best = img;
                }
                if (best == null) return false;
                target.sprite = best.sprite; target.type = best.type; target.material = best.material; target.color = best.color;
                target.pixelsPerUnitMultiplier = best.pixelsPerUnitMultiplier;
                return true;
            }
            catch { return false; }
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text, TextMeshProUGUI fontSource, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (fontSource != null) { tmp.font = fontSource.font; tmp.fontSharedMaterial = fontSource.fontSharedMaterial; }
            // TextMeshPro reads \r, \n and \t inside the text as escapes, so a Windows path would draw the rest of the
            // line back over its own start. Everything here is plain text: turn that parsing off.
            try { tmp.parseCtrlCharacters = false; } catch { }
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
            tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Ellipsis; tmp.raycastTarget = false;
            return tmp;
        }

        private static Scrollbar MakeScrollbar(RectTransform vp)
        {
            var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(vp, false);
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(6f, 0f); rt.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            var area = new GameObject("Sliding Area", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(rt, false); area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = Vector2.zero; area.offsetMax = Vector2.zero;
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
            handle.SetParent(area, false); handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one; handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
            handle.GetComponent<Image>().color = new Color32(0xCB, 0xB3, 0x96, 0x90);
            Scrollbar sb = go.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop; sb.handleRect = handle; sb.targetGraphic = handle.GetComponent<Image>(); sb.transition = Selectable.Transition.None;
            return sb;
        }

        private static Button MakeButton(RectTransform parent, string label, Action onClick)
        {
            Button made = null;
            try
            {
                Button src = ConfirmWindow.Instance != null ? ConfirmWindow.Instance.CancelButton : null;
                if (src != null)
                {
                    GameObject go = UnityEngine.Object.Instantiate(src.gameObject, parent);
                    go.name = "Button " + label; go.SetActive(true);
                    foreach (Component c in go.GetComponentsInChildren<Component>(true))
                        if (c != null && c.GetType().Name.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0) UnityEngine.Object.Destroy(c);
                    foreach (LayoutElement le in go.GetComponents<LayoutElement>()) UnityEngine.Object.Destroy(le);
                    made = go.GetComponent<Button>();
                    var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (tmp != null) tmp.text = label;
                    RectTransform rt = go.GetComponent<RectTransform>();
                    if (rt.sizeDelta.x < 120f) rt.sizeDelta = new Vector2(180f, Mathf.Max(36f, rt.sizeDelta.y));
                }
            }
            catch (Exception e) { if (Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Run history: dialog button not cloned (" + e.Message + "), using a plain button"); }
            if (made == null)
            {
                var go = new GameObject("Button " + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 40f);
                go.GetComponent<Image>().color = new Color(0.25f, 0.2f, 0.16f, 1f);
                made = go.GetComponent<Button>(); made.targetGraphic = go.GetComponent<Image>();
                TextMeshProUGUI tmp = MakeText(go.transform, "Text", label, _fontTitle, 16, new Color32(0xCB, 0xB3, 0x96, 0xFF), TextAlignmentOptions.Center);
                Place(tmp.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            made.onClick = new Button.ButtonClickedEvent();
            made.onClick.AddListener(() => onClick());
            made.interactable = true;
            return made;
        }
    }
}
