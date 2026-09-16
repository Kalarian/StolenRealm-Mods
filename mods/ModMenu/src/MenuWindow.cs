using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModMenu
{
    /// <summary>
    /// The in-game switchboard: one checkbox per plugin (MasterConfig.AllPlugins) plus "Verbose logging", Apply and
    /// Cancel. Built once at runtime from the game's own widgets so it looks native: the checkbox is a clone of the
    /// options-menu toggle (OptionsManager.instance.screenShakeToggle), the buttons are clones of the confirm dialog's
    /// (ConfirmWindow.Instance.ConfirmButton / CancelButton), fonts come from the confirm dialog's labels, and the
    /// panel lives in the same container as the game's windows (ReferenceLoader.Instance.StatManagerPlaceholder.parent)
    /// so it scales like them. A UIWindow component registers it with UIWindowManager: the game blocks its own input
    /// while it is open and Escape closes it. Apply writes stolenrealm.mods.cfg and bumps the shared reload token that
    /// every plugin's Update watches (MasterConfig.ReloadRequested), so all mods apply/remove their patches at once.
    /// </summary>
    internal static class MenuWindow
    {
        private const string MenuFlag = "stolenrealm.mods.menu";
        private const float PanelWidth = 620f, RowHeight = 34f, HeaderHeight = 100f, FooterHeight = 96f, ListHeight = 325f; // 2026-09-16: window 521 tall, the list takes all but the title block and the hint + buttons
        private const string SelfName = "ModMenu";      // never listed: the menu cannot switch itself off (edit stolenrealm.mods.cfg by hand)
        private static ScrollRect _scroll;

        private static GameObject _root;          // backdrop (full container) that also eats clicks
        private static GameObject _panel;
        private static UIWindow _uiWindow;
        private static bool _registered, _closing;
        private static readonly Dictionary<string, Toggle> _toggles = new Dictionary<string, Toggle>();
        private static Toggle _verbose;
        private static TextMeshProUGUI _fontTitle, _fontBody;

        private static ModMenuConfig Cfg => ModMenuPlugin.Cfg;
        private static bool IsOpen => _root != null && _root.activeSelf;

        // ---------- lifecycle ----------

        /// <summary>From the plugin's Update while patched.</summary>
        internal static void Tick()
        {
            try { if (AppDomain.CurrentDomain.GetData(MenuFlag) == null) AppDomain.CurrentDomain.SetData(MenuFlag, true); } catch { }
            if (Cfg == null || !Cfg.Enabled.Value) { if (IsOpen) Close(false); return; }
            if (Cfg.OpenKey.Value.IsDown())
            {
                if (IsOpen) { Close(false); return; }
                if (TypingInAnyTextField() || BindingControls()) return;
                Open();
            }
            else if (IsOpen && !_registered && Input.GetKeyDown(KeyCode.Escape)) Close(false);
        }

        internal static void Reset()
        {
            try { if (IsOpen) Close(false); } catch { }
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null; _panel = null; _uiWindow = null; _scroll = null; _toggles.Clear(); _verbose = null; _fontTitle = null; _fontBody = null;
            try { AppDomain.CurrentDomain.SetData(MenuFlag, null); } catch { }
        }

        private static void Open()
        {
            try
            {
                if (_root == null && !Build()) return;
                MasterConfig.Load();
                foreach (var kv in _toggles) { kv.Value.SetIsOnWithoutNotify(MasterConfig.Enabled(kv.Key)); }
                if (_verbose != null) _verbose.SetIsOnWithoutNotify(!MasterConfig.Verbose.HasValue || MasterConfig.Verbose.Value);
                _root.transform.SetAsLastSibling();
                _root.SetActive(true);
                if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
                _registered = false;
                if (_uiWindow != null && PauseMenu.instance != null && UIWindowManager.Instance != null) { _uiWindow.OpenWindow(); _registered = true; }
                if (Cfg.Verbose.Value) ModMenuPlugin.Log.LogInfo("Mod menu opened (" + (_registered ? "registered as a game window" : "plain overlay") + ")");
            }
            catch (Exception e) { ModMenuPlugin.Log.LogWarning("Mod menu open failed: " + e); }
        }

        private static void Close(bool fromGame)
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

        // ---------- apply ----------

        private static void Apply()
        {
            try
            {
                var states = new Dictionary<string, bool>();
                var turnedOn = new List<string>(); var turnedOff = new List<string>(); var restart = new List<string>();
                foreach (string name in MasterConfig.AllPlugins)
                {
                    Toggle t; bool on = _toggles.TryGetValue(name, out t) ? t.isOn : MasterConfig.Enabled(name); // no row (the menu itself): unchanged
                    states[name] = on;
                    bool was = MasterConfig.Enabled(name);
                    if (on != was) { (on ? turnedOn : turnedOff).Add(name); if (ModTable.Get(name).RestartRequired) restart.Add(ModTable.Get(name).Title); }
                }
                bool verbose = _verbose == null || _verbose.isOn;
                MasterConfig.Write(states, verbose);
                MasterConfig.RequestReload();
                ModMenuPlugin.Log.LogInfo("Mod menu: applied" + (turnedOn.Count > 0 ? " | on: " + string.Join(", ", turnedOn.ToArray()) : "") + (turnedOff.Count > 0 ? " | off: " + string.Join(", ", turnedOff.ToArray()) : "") + (turnedOn.Count + turnedOff.Count == 0 ? " (no mod changed; configs re-read)" : "") + " | verbose " + (verbose ? "on" : "off"));
                if (restart.Count > 0)
                {
                    try { ConfirmWindow.Instance.ShowPopupMessage("Stolen Realm Mods", "Restart the game for these changes to take effect:\n" + string.Join("\n", restart.ToArray())); } catch { }
                }
                if (Cfg.CloseOnApply.Value) Close(false);
            }
            catch (Exception e) { ModMenuPlugin.Log.LogWarning("Mod menu apply failed: " + e); }
        }

        // ---------- construction ----------

        private static bool Build()
        {
            Transform parent = null;
            try { if (ReferenceLoader.Instance != null && ReferenceLoader.Instance.StatManagerPlaceholder != null) parent = ReferenceLoader.Instance.StatManagerPlaceholder.parent; } catch { }
            try { if (parent == null && ConfirmWindow.Instance != null) parent = ConfirmWindow.Instance.transform.parent; } catch { }
            if (parent == null) { ModMenuPlugin.Log.LogWarning("Mod menu: no UI container found yet (open it once the game has loaded)"); return false; }
            try { if (ConfirmWindow.Instance != null) { _fontTitle = ConfirmWindow.Instance.Title; _fontBody = ConfirmWindow.Instance.Message; } } catch { }

            int rows = MasterConfig.AllPlugins.Length; // every plugin but this one, plus the verbose row
            float height = HeaderHeight + ListHeight + FooterHeight;

            _root = new GameObject("StolenRealmModMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one; rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
            Image backdrop = _root.GetComponent<Image>(); backdrop.color = new Color(0f, 0f, 0f, 0.55f); backdrop.raycastTarget = true;
            _uiWindow = _root.AddComponent<UIWindow>();
            _uiWindow.UIWindow_CanCancel = true; _uiWindow.UIWindow_CloseOnLoad = true;
            _uiWindow.OnCloseEvent = new UnityEngine.Events.UnityEvent();
            _uiWindow.OnCloseEvent.AddListener(() => Close(true)); // Escape handled by the game: hide the panel

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform prt = _panel.GetComponent<RectTransform>();
            prt.SetParent(rootRt, false);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, height); prt.anchoredPosition = Vector2.zero;
            Image panelImg = _panel.GetComponent<Image>();
            if (!CopyPanelLook(panelImg)) panelImg.color = new Color(0.10f, 0.08f, 0.07f, 0.97f);

            // title + subtitle
            TextMeshProUGUI title = MakeText(prt, "Title", "Stolen Realm Mods", _fontTitle, 20, new Color32(0xCB, 0xB3, 0x96, 0xFF), TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -48f), new Vector2(0f, -16f));
            TextMeshProUGUI sub = MakeText(prt, "Subtitle", "Tick the mods you want, then Apply. Changes take effect immediately; nothing is undone that a mod already did.", _fontBody, 11, new Color32(0x9A, 0xA5, 0xB1, 0xFF), TextAlignmentOptions.Center);
            Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -94f), new Vector2(-20f, -52f));

            // rows: a masked, scrolling list ListHeight tall
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            RectTransform vp = viewportGo.GetComponent<RectTransform>();
            vp.SetParent(prt, false);
            Place(vp, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -(HeaderHeight + ListHeight)), new Vector2(-24f, -HeaderHeight));
            Image vpImg = viewportGo.GetComponent<Image>(); vpImg.color = new Color(0f, 0f, 0f, 0f); vpImg.raycastTarget = true; // catches the mouse wheel
            var holder = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            holder.SetParent(vp, false);
            holder.anchorMin = new Vector2(0f, 1f); holder.anchorMax = new Vector2(1f, 1f); holder.pivot = new Vector2(0.5f, 1f);
            holder.anchoredPosition = Vector2.zero; holder.sizeDelta = new Vector2(-10f, rows * RowHeight); // leave room for the bar on the right
            var vlg = holder.GetComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true; vlg.childControlWidth = true; vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true; vlg.spacing = 0f;
            var fitter = holder.GetComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            _scroll = viewportGo.GetComponent<ScrollRect>();
            _scroll.content = holder; _scroll.viewport = vp; _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.inertia = true; _scroll.decelerationRate = 0.135f; _scroll.scrollSensitivity = 25f;
            _scroll.verticalScrollbar = MakeScrollbar(vp); _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            try { viewportGo.AddComponent<ScrollViewExtended>(); } catch { }
            _toggles.Clear();
            foreach (string name in MasterConfig.AllPlugins)
            {
                if (name == SelfName) continue;
                ModTable.Entry entry = ModTable.Get(name);
                bool installed = IsInstalled(name);
                string suffix = !installed ? "not installed" : entry.RestartRequired ? "restart required" : null;
                Toggle t = MakeRow(holder, entry.Title, Cfg.ShowDescriptions.Value ? entry.Description : null, suffix, installed);
                _toggles[name] = t;
            }
            _verbose = MakeRow(holder, "Verbose logging (all mods)", Cfg.ShowDescriptions.Value ? "Detailed lines in BepInEx\\LogOutput.log. Keep on while we are still testing." : null, null, true);

            // footer: hint + buttons
            TextMeshProUGUI hint = MakeText(prt, "Hint", "This window itself has no box: to turn the mod menu off, edit BepInEx\\config\\stolenrealm.mods.cfg by hand.", _fontBody, 11, new Color32(0x9A, 0xA5, 0xB1, 0xFF), TextAlignmentOptions.Center);
            Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 64f), new Vector2(-20f, 90f));
            Button apply = MakeButton(prt, "Apply", () => Apply());
            Button cancel = MakeButton(prt, "Cancel", () => Close(false));
            RectTransform art = apply.GetComponent<RectTransform>(), crt = cancel.GetComponent<RectTransform>();
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0f); art.pivot = new Vector2(0.5f, 0f); art.anchoredPosition = new Vector2(-110f, 16f);
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f); crt.pivot = new Vector2(0.5f, 0f); crt.anchoredPosition = new Vector2(110f, 16f);

            _root.SetActive(false);
            if (Cfg.Verbose.Value) ModMenuPlugin.Log.LogInfo("Mod menu built under " + parent.name + " (" + rows + " rows)");
            return true;
        }

        private static Scrollbar MakeScrollbar(RectTransform vp)
        {
            var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(vp, false);
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(6f, 0f); rt.anchoredPosition = new Vector2(0f, 0f);
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

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        /// <summary>Borrow the confirm dialog's panel sprite so the window matches the game's frames.</summary>
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
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align; tmp.enableWordWrapping = true; tmp.overflowMode = TextOverflowModes.Ellipsis; tmp.raycastTarget = false;
            return tmp;
        }

        private static Toggle MakeRow(RectTransform holder, string title, string description, string suffix, bool enabled)
        {
            var row = new GameObject("Row " + title, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(holder, false);
            row.GetComponent<LayoutElement>().preferredHeight = RowHeight; row.GetComponent<LayoutElement>().minHeight = RowHeight;
            Image rowBg = row.GetComponent<Image>(); rowBg.color = new Color(1f, 1f, 1f, 0.03f); rowBg.raycastTarget = true;
            RectTransform rrt = row.GetComponent<RectTransform>();

            Toggle toggle = MakeToggle(rrt);
            RectTransform trt = toggle.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(0f, 0.5f); trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(6f, 0f); trt.sizeDelta = new Vector2(24f, 24f);
            toggle.interactable = enabled;

            Color titleColor = enabled ? new Color32(0xE8, 0xDF, 0xD0, 0xFF) : new Color32(0x80, 0x80, 0x80, 0xFF);
            float right = suffix != null ? 150f : 14f;
            TextMeshProUGUI label = MakeText(rrt, "Label", title, _fontBody, 13, titleColor, TextAlignmentOptions.MidlineLeft);
            bool twoLines = !string.IsNullOrEmpty(description);
            if (twoLines)
            {
                // title on a fixed line at the top, description wrapping below; the row grows to fit (RowSizer)
                Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -22f), new Vector2(-right, -3f));
                TextMeshProUGUI desc = MakeText(rrt, "Description", description, _fontBody, 9.5f, new Color32(0x9A, 0xA5, 0xB1, 0xFF), TextAlignmentOptions.TopLeft);
                desc.overflowMode = TextOverflowModes.Overflow; desc.enableWordWrapping = true;
                Place(desc.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40f, 4f), new Vector2(-right, -22f));
                var sizer = row.AddComponent<RowSizer>(); sizer.Description = desc; sizer.Layout = row.GetComponent<LayoutElement>();
            }
            else Place(label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40f, 0f), new Vector2(-right, 0f));
            if (suffix != null)
            {
                TextMeshProUGUI s = MakeText(rrt, "Suffix", "(" + suffix + ")", _fontBody, 10, new Color32(0xE0, 0x8A, 0x3C, 0xFF), TextAlignmentOptions.MidlineRight);
                Place(s.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-146f, 0f), new Vector2(-8f, 0f));
            }
            // the whole row toggles, like a checkbox with its label
            var click = row.AddComponent<RowClick>(); click.Toggle = toggle; click.Title = title; click.Description = description;
            return toggle;
        }

        /// <summary>Clone the options menu's checkbox; fall back to a plain box if it is not there.</summary>
        private static Toggle MakeToggle(RectTransform parent)
        {
            try
            {
                Toggle src = OptionsManager.instance != null ? OptionsManager.instance.screenShakeToggle : null;
                if (src != null)
                {
                    GameObject go = UnityEngine.Object.Instantiate(src.gameObject, parent);
                    go.name = "Toggle"; go.SetActive(true);
                    foreach (Component c in go.GetComponentsInChildren<Component>(true))
                        if (c != null && c.GetType().Name.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0) UnityEngine.Object.Destroy(c);
                    foreach (TMP_Text txt in go.GetComponentsInChildren<TMP_Text>(true)) txt.gameObject.SetActive(false); // its own label, if any
                    foreach (Text txt in go.GetComponentsInChildren<Text>(true)) txt.gameObject.SetActive(false);
                    foreach (LayoutElement le in go.GetComponents<LayoutElement>()) UnityEngine.Object.Destroy(le);
                    Toggle t = go.GetComponent<Toggle>();
                    t.onValueChanged = new Toggle.ToggleEvent();
                    t.group = null;
                    return t;
                }
            }
            catch (Exception e) { if (Cfg.Verbose.Value) ModMenuPlugin.Log.LogInfo("Mod menu: options toggle not cloned (" + e.Message + "), using a plain box"); }
            var box = new GameObject("Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
            box.transform.SetParent(parent, false);
            Image bg = box.GetComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.15f);
            var mark = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            mark.transform.SetParent(box.transform, false);
            RectTransform mrt = mark.GetComponent<RectTransform>(); mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one; mrt.offsetMin = new Vector2(5f, 5f); mrt.offsetMax = new Vector2(-5f, -5f);
            mark.GetComponent<Image>().color = new Color32(0xCB, 0xB3, 0x96, 0xFF);
            Toggle tog = box.GetComponent<Toggle>();
            tog.targetGraphic = bg; tog.graphic = mark.GetComponent<Image>(); tog.transition = Selectable.Transition.ColorTint;
            return tog;
        }

        private static Button MakeButton(RectTransform parent, string label, Action onClick)
        {
            Button made = null;
            try
            {
                Button src = ConfirmWindow.Instance != null ? (label == "Cancel" ? ConfirmWindow.Instance.CancelButton : ConfirmWindow.Instance.ConfirmButton) : null;
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
            catch (Exception e) { if (Cfg.Verbose.Value) ModMenuPlugin.Log.LogInfo("Mod menu: dialog button not cloned (" + e.Message + "), using a plain button"); }
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

        // ---------- helpers ----------

        private static bool IsInstalled(string name)
        {
            try { return BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("stolenrealm." + name.ToLowerInvariant()); } catch { return true; }
        }

        private static bool BindingControls()
        {
            try { return OptionsManager.instance != null && OptionsManager.instance.CurrentlyBindingControls; } catch { return false; }
        }

        internal static bool TypingInAnyTextField()
        {
            try
            {
                if (MessageWindowManager.instance != null && MessageWindowManager.instance.TextInputIsFocused) return true;
                var es = EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null) return false;
                var f = go.GetComponent<InputField>(); if (f != null && f.isFocused) return true;
                var t = go.GetComponent<TMP_InputField>(); if (t != null && t.isFocused) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Grows a row to fit its wrapped description (TMP's preferredHeight follows the current width).</summary>
        public sealed class RowSizer : MonoBehaviour
        {
            public TextMeshProUGUI Description; public LayoutElement Layout;
            private void LateUpdate()
            {
                if (Description == null || Layout == null) return;
                float h = Mathf.Max(RowHeight, 22f + Description.preferredHeight + 8f);
                if (Mathf.Abs(Layout.preferredHeight - h) > 0.5f) { Layout.preferredHeight = h; Layout.minHeight = h; }
            }
        }

        /// <summary>Clicking anywhere on a row flips its checkbox; hovering shows the description as a game tooltip.</summary>
        public sealed class RowClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Toggle Toggle; public string Title; public string Description;
            public void OnPointerClick(PointerEventData eventData)
            {
                if (Toggle == null || !Toggle.interactable) return;
                if (eventData.pointerPress != null && eventData.pointerPress.GetComponentInParent<Toggle>() == Toggle) return; // the box itself already toggled
                Toggle.isOn = !Toggle.isOn;
            }
            public void OnPointerEnter(PointerEventData eventData)
            {
                try { if (!string.IsNullOrEmpty(Description) && GUIManager.instance != null && GUIManager.instance.tooltip != null) GUIManager.instance.tooltip.ShowUniversalTooltip(Title, "", Description); } catch { }
            }
            public void OnPointerExit(PointerEventData eventData)
            {
                try { if (GUIManager.instance != null && GUIManager.instance.tooltip != null) GUIManager.instance.tooltip.HideTooltip(); } catch { }
            }
        }
    }
}
