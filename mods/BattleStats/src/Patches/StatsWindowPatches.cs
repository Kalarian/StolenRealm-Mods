using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattleStats.Patches
{
    /// <summary>
    /// Adds rows to the game's post-battle Stats window (StatManager). The window is a fixed grid: BuildOutLabels
    /// instantiates one label row + one values row per stat (once, then reused for every later battle), and
    /// PopulateStats fills every values row from the list GetStatDisplay returns for each character, by row index.
    /// So we append our rows in a BuildOutLabels postfix and append the matching strings in a GetStatDisplay postfix,
    /// in the same order, using the window's own prefabs, sizes and colours. When the wanted rows change (config
    /// reload, mod switched on after the window was first built) a PopulateStats prefix forces the game to rebuild.
    /// Values come from the replicated dictionary (SharedStats), so clients see the host's numbers; if the host has
    /// no mod the rows show "n/a". Hovering a character's name at the top of a column shows their top damage sources.
    ///
    /// Layout (from a runtime dump of the prefab, game build 25240684): Content (full window) holds Title, the column
    /// headers (Character Name Holder, anchored to the centre, y +210), Stat Line Holder and Stat Value Holder (both
    /// stretched to the window with a vertical offset of -141 / -144, VerticalLayoutGroups with a 22 px row pitch; the
    /// line holder keeps an "End Separator" as its last child) and the Close button (bottom centre, 40..87 px from the
    /// bottom). There is no scroll view, so with the extra rows the table ran off the panel. We build one: a masked
    /// viewport between the headers and the Close button, a top-anchored scroll content inside it, and both holders
    /// moved into that content with their original offsets. Mouse wheel and drag scroll; a slim scrollbar sits on the
    /// right. Reset() puts the holders back where they were.
    /// </summary>
    internal static class StatsWindowPatches
    {
        private sealed class Row
        {
            public string Label; public bool Header; public bool SumHeader;   // SumHeader: the header shows the total of its section, like the game's Total Damage row
            public Func<Character, string> Value; public Func<Character, float> Num;   // Num: plain number rows (summed into the header)
            public int Bucket = -1;                                             // SharedStats bucket behind the cell's hover tooltip (-1 = none)
        }
        private static Row NumRow(string label, Func<Character, float> f, int bucket = -1) { return new Row { Label = label, Num = f, Value = c => N(f(c)), Bucket = bucket }; }
        private static List<Row> _rows;                 // rows currently built into the window
        private static string _builtSig = null;         // config signature the rows were built with
        private static readonly List<GameObject> _made = new List<GameObject>();

        // scroll view
        private const float ViewportTop = 141f;         // rows started this far below the window top in the vanilla layout
        private const float ViewportBottom = 100f;      // stay above the Close button (its top is 87 px from the bottom)
        private static GameObject _viewport;            // RectMask2D + ScrollRect
        private static RectTransform _content;          // top-anchored, height = rows
        private static ScrollRect _scroll;
        private static Transform _origParent; private static int _origLineIndex, _origValueIndex;
        private static Vector2 _origLinePos, _origValuePos;
        private static float _contentHeight = -1f;

        private static BattleStatsConfig Cfg => BattleStatsPlugin.Cfg;

        internal static void Reset()
        {
            try { if (StatManager.Instance != null) Unwrap(StatManager.Instance); } catch { }
            foreach (GameObject go in _made) { try { if (go != null) UnityEngine.Object.Destroy(go); } catch { } }
            _made.Clear(); _rows = null; _builtSig = null;
        }

        private static string Signature()
        {
            BattleStatsConfig c = Cfg;
            if (c == null || !c.Enabled.Value || !c.ShowInWindow.Value) return "";
            return (c.ShowBreakdown.Value ? "B" : "") + (c.ShowHits.Value ? "H" : "") + (c.ShowElements.Value ? "E" : "") + (c.ShowActivity.Value ? "A" : "");
        }

        private static string N(float v) { return v.ToString("N0", CultureInfo.InvariantCulture); }
        // every value the rows show goes through here: the live per-battle dictionary, or the run store while the window is in run mode
        private static float G(Character c, int key) { return RunStatsPatches.RunMode ? RunStatsPatches.Get(c, key) : SharedStats.Get(c, key); }
        private static float GameStat(Character c, BattleStat s) { return G(c, (int)s); }

        private static List<Row> BuildRows()
        {
            BattleStatsConfig cfg = Cfg;
            var rows = new List<Row>();
            if (cfg.ShowBreakdown.Value)
            {
                rows.Add(new Row { Label = "Damage Breakdown", Header = true, SumHeader = true, Bucket = SharedStats.B_All });
                rows.Add(NumRow("Direct Hits", c => G(c, SharedStats.Direct), SharedStats.B_Direct));
                rows.Add(NumRow("Over Time", c => G(c, SharedStats.Ticks), SharedStats.B_Ticks));
                rows.Add(NumRow("Ground Tiles", c => G(c, SharedStats.Tiles), SharedStats.B_Tiles));
                rows.Add(NumRow("Summons", c => GameStat(c, BattleStat.SummonDamageDealt), SharedStats.B_Summons));
                rows.Add(NumRow("Thorns", c => GameStat(c, BattleStat.DamageReturned), SharedStats.B_Thorns));
            }
            if (cfg.ShowHits.Value)
            {
                rows.Add(new Row { Label = "Hits", Header = true });
                rows.Add(new Row { Label = "Hits / Crits", Value = c => { float h = G(c, SharedStats.Hits), cr = G(c, SharedStats.Crits); return N(h) + " / " + N(cr) + (h > 0 ? " (" + (100f * cr / h).ToString("0") + "%)" : ""); } });
                rows.Add(new Row { Label = "Kills / Overkill", Value = c => N(G(c, SharedStats.Kills)) + " / " + N(G(c, SharedStats.Overkill)) });
                rows.Add(new Row { Label = "Biggest Hit", Value = c => { float b = G(c, SharedStats.BiggestHit); return b > 0 ? N(b) + " " + SharedStats.SourceName((int)G(c, SharedStats.BiggestHitSource)) : "-"; } });
                rows.Add(new Row { Label = "Best Skill", Value = c => { float d = G(c, SharedStats.BestSkillDamage); return d > 0 ? SharedStats.SourceName((int)G(c, SharedStats.BestSkillSource)) + " " + N(d) : "-"; } });
                rows.Add(new Row { Label = "Damage Per Turn", Value = c => { float t = G(c, SharedStats.Turns); float total = GameStat(c, BattleStat.DamageDealt) + GameStat(c, BattleStat.SummonDamageDealt) + GameStat(c, BattleStat.DamageReturned); return t > 0 ? N(total / t) : "-"; } });
            }
            if (cfg.ShowElements.Value)
            {
                rows.Add(new Row { Label = "Damage By Element", Header = true, SumHeader = true });
                foreach (DamageType t in new[] { DamageType.Physical, DamageType.Fire, DamageType.Cold, DamageType.Lightning, DamageType.Shadow, DamageType.Holy })
                {
                    DamageType tt = t;
                    rows.Add(NumRow(tt.ToString(), c => G(c, SharedStats.ElementBase + (int)tt), SharedStats.B_ElementBase + (int)tt));
                }
                // whatever part of Damage Dealt got no element: keeps the section total equal to Damage Dealt and flags tracking misses
                rows.Add(NumRow("Untyped", c =>
                {
                    float typed = 0f;
                    foreach (DamageType t in new[] { DamageType.Physical, DamageType.Fire, DamageType.Cold, DamageType.Lightning, DamageType.Shadow, DamageType.Holy }) typed += G(c, SharedStats.ElementBase + (int)t);
                    return Mathf.Max(0f, GameStat(c, BattleStat.DamageDealt) - typed);
                }, SharedStats.B_Untyped));
            }
            if (cfg.ShowActivity.Value)
            {
                rows.Add(new Row { Label = "Activity", Header = true });
                rows.Add(new Row { Label = "Casts (Free)", Value = c => N(G(c, SharedStats.Casts)) + " (" + N(G(c, SharedStats.FreeActions)) + ")" });
                rows.Add(new Row { Label = "Mana Spent", Value = c => N(G(c, SharedStats.ManaSpent)) });
                rows.Add(new Row { Label = "Hexes Moved", Value = c => N(G(c, SharedStats.HexesMoved)) });
                rows.Add(new Row { Label = "Taken From Ticks", Value = c => N(G(c, SharedStats.TakenFromTicks)) });
            }
            return rows;
        }

        /// <summary>True when the numbers being shown carry any of our keys (a host without the mod writes none): the live battle, or the run store in run mode.</summary>
        private static bool HostHasData()
        {
            return RunStatsPatches.RunMode ? RunStatsPatches.HasModKeys() : LiveHostHasData();
        }

        /// <summary>True when the host wrote any of our keys for anyone this battle.</summary>
        internal static bool LiveHostHasData()
        {
            try
            {
                Root root = NetworkingManager.Instance.NetworkManager.Root;
                if (root == null || root.BattleStats == null) return false;
                foreach (CharacterBattleStats cbs in root.BattleStats)
                {
                    if (cbs == null || cbs.BattleStats == null) continue;
                    foreach (var kv in cbs.BattleStats) if (kv.Key >= 1000) return true;
                }
            }
            catch { }
            return false;
        }

        // the window keeps its rows once built; rebuild when what we want to show has changed
        [HarmonyPatch(typeof(StatManager), nameof(StatManager.PopulateStats))]
        private static class StatManager_PopulateStats_Prefix
        {
            private static void Prefix(StatManager __instance, ref bool forceBuildLabels)
            {
                try
                {
                    if (!Application.isPlaying) return;
                    bool init = false; try { init = (bool)AccessTools.Field(typeof(StatManager), "init").GetValue(__instance); } catch { }
                    if (init && _builtSig != Signature()) forceBuildLabels = true;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(StatManager), nameof(StatManager.BuildOutLabels))]
        private static class StatManager_BuildOutLabels
        {
            private static void Postfix(StatManager __instance)
            {
                try
                {
                    _made.Clear(); // the game destroyed every row before rebuilding
                    _builtSig = Signature();
                    if (_builtSig.Length == 0 || !__instance.UseSections) { _rows = null; Unwrap(__instance); return; }
                    _rows = BuildRows();
                    foreach (Row r in _rows)
                    {
                        GameObject line = UnityEngine.Object.Instantiate(__instance.StatLinePrefab, __instance.StatLineHolder.transform);
                        var tmp = line.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                        string text;
                        if (r.Header)
                        {
                            tmp.fontSize = (int)__instance.SectionTitleSize;
                            text = "<color=#" + ColorUtility.ToHtmlStringRGB(__instance.SectionTitleColor) + ">" + r.Label + "</color>";
                        }
                        else
                        {
                            tmp.fontSize = (int)__instance.SubSectionSize;
                            text = "<color=#" + ColorUtility.ToHtmlStringRGB(__instance.SubSectionColor) + ">" + new string(' ', Math.Max(0, __instance.IndentAmount)) + r.Label + "</color>";
                        }
                        tmp.text = text;
                        line.transform.SetSiblingIndex(__instance.StatLineHolder.transform.childCount - 2);
                        GameObject values = UnityEngine.Object.Instantiate(__instance.StatValuesPrefab, __instance.StatValueHolder.transform);
                        _made.Add(line); _made.Add(values);
                    }
                    Wrap(__instance);
                    if (Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Stats window: added " + _rows.Count + " rows (" + _builtSig + "), scroll view " + (_viewport != null ? "on" : "OFF"));
                }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Stats window rows failed: " + e); _rows = null; }
            }
        }

        [HarmonyPatch(typeof(StatManager), nameof(StatManager.GetStatDisplay))]
        private static class StatManager_GetStatDisplay
        {
            private static void Postfix(StatManager __instance, Character character, ref List<string> __result)
            {
                try
                {
                    if (__result == null || !__instance.UseSections) return;
                    if (RunStatsPatches.RunMode && character != null) RewriteGameRows(__instance, character, __result);
                    if (_rows == null) return;
                    bool hostData = character == null || HostHasData();
                    string titleOpen = "<size=" + __instance.SectionTitleSize + "><color=#" + ColorUtility.ToHtmlStringRGB(__instance.SectionTitleColor) + ">";
                    string subOpen = "<size=" + __instance.SubSectionSize + "><color=#" + ColorUtility.ToHtmlStringRGB(__instance.SubSectionColor) + ">";
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        Row r = _rows[i];
                        if (r.Header)
                        {
                            string total = "";
                            if (r.SumHeader && character != null && hostData)
                            {
                                float sum = 0f;
                                for (int j = i + 1; j < _rows.Count && !_rows[j].Header; j++) { if (_rows[j].Num != null) { try { sum += _rows[j].Num(character); } catch { } } }
                                total = N(sum);
                            }
                            __result.Add(titleOpen + total + "</color></size>");
                            continue;
                        }
                        string v = "";
                        if (character != null)
                        {
                            if (!hostData) v = "n/a";
                            else { try { v = r.Value(character); } catch { v = "?"; } }
                        }
                        __result.Add(subOpen + v + "</color></size>");
                    }
                }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Stats window values failed: " + e); }
            }
        }

        /// <summary>Run mode: replace the game's own rows (built by GetStatDisplay from the live dictionary) with the same
        /// walk over the run store: per section a header holding the sum of the ceiled children, then the children unless
        /// the section hides them, same size/colour tags. Plain digits (float concatenation would print 1E+07 for a run).</summary>
        private static void RewriteGameRows(StatManager sm, Character character, List<string> result)
        {
            if (sm.StatSections == null) return;
            var d = RunStatsPatches.ViewFor(character);
            var rebuilt = new List<string>();
            string hexT = ColorUtility.ToHtmlStringRGB(sm.SectionTitleColor), hexS = ColorUtility.ToHtmlStringRGB(sm.SubSectionColor);
            foreach (StatSection sec in sm.StatSections)
            {
                float sum = 0f;
                var kids = new List<string>();
                if (sec.BattleStats != null)
                {
                    foreach (BattleStat bs in sec.BattleStats)
                    {
                        float v; d.TryGetValue((int)bs, out v);
                        v = Mathf.Ceil(v); sum += v;
                        if (!sec.HideChildren) kids.Add("<size=" + sm.SubSectionSize + "><color=#" + hexS + ">" + v.ToString("0", CultureInfo.InvariantCulture) + "</color></size>");
                    }
                }
                rebuilt.Add("<size=" + sm.SectionTitleSize + "><color=#" + hexT + ">" + sum.ToString("0", CultureInfo.InvariantCulture) + "</color></size>");
                rebuilt.AddRange(kids);
            }
            if (rebuilt.Count != result.Count) { RunStatsPatches.NoteRowMismatch(result.Count, rebuilt.Count); return; }
            result.Clear();
            result.AddRange(rebuilt);
        }

        // ---------- the scroll view ----------

        /// <summary>Move both row holders into a masked, scrolling area between the headers and the Close button.</summary>
        private static void Wrap(StatManager sm)
        {
            if (_viewport != null || sm == null || sm.StatLineHolder == null || sm.StatValueHolder == null) return;
            RectTransform line = sm.StatLineHolder.GetComponent<RectTransform>(), value = sm.StatValueHolder.GetComponent<RectTransform>();
            if (line == null || value == null || line.parent != value.parent) { BattleStatsPlugin.Log.LogWarning("Stats window: unexpected layout, no scroll view (rows may run off the panel)"); return; }
            RectTransform parent = line.parent as RectTransform;
            _origParent = parent; _origLineIndex = line.GetSiblingIndex(); _origValueIndex = value.GetSiblingIndex();
            _origLinePos = line.anchoredPosition; _origValuePos = value.anchoredPosition;

            // viewport: stretched to the panel, cut down to the row area
            _viewport = new GameObject("BattleStatsViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            RectTransform vp = _viewport.GetComponent<RectTransform>();
            vp.SetParent(parent, false);
            vp.SetSiblingIndex(Math.Min(_origLineIndex, _origValueIndex));
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.pivot = new Vector2(0.5f, 0.5f);
            vp.offsetMin = new Vector2(0f, ViewportBottom); vp.offsetMax = new Vector2(0f, -ViewportTop);
            Image bg = _viewport.GetComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0f); bg.raycastTarget = true; // catches the mouse wheel

            // content: top-anchored, height follows the rows (kept in step from Tick)
            var contentGo = new GameObject("BattleStatsScrollContent", typeof(RectTransform));
            _content = contentGo.GetComponent<RectTransform>();
            _content.SetParent(vp, false);
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero; _content.sizeDelta = new Vector2(0f, vp.rect.height);
            _contentHeight = -1f;

            // the holders keep stretching, now to the content; their vertical offset was relative to the panel's row start
            line.SetParent(_content, false); value.SetParent(_content, false);
            line.anchoredPosition = new Vector2(_origLinePos.x, 0f);
            value.anchoredPosition = new Vector2(_origValuePos.x, _origValuePos.y - _origLinePos.y); // values sit 3 px lower than the lines

            _scroll = _viewport.GetComponent<ScrollRect>();
            _scroll.content = _content; _scroll.viewport = vp;
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = true; _scroll.decelerationRate = 0.135f; _scroll.scrollSensitivity = 30f;
            _scroll.verticalScrollbar = MakeScrollbar(vp, sm);
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            try { _viewport.AddComponent<ScrollViewExtended>(); } catch { } // the game's gamepad-stick scrolling helper
            _scroll.verticalNormalizedPosition = 1f;
        }

        private static Scrollbar MakeScrollbar(RectTransform vp, StatManager sm)
        {
            var go = new GameObject("BattleStatsScrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(vp, false);
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(6f, 0f); rt.anchoredPosition = new Vector2(-6f, 0f);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            var area = new GameObject("Sliding Area", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(rt, false); area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = Vector2.zero; area.offsetMax = Vector2.zero;
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
            handle.SetParent(area, false); handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one; handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
            Color c = sm.SubSectionColor; handle.GetComponent<Image>().color = new Color(c.r, c.g, c.b, 0.55f);
            Scrollbar sb = go.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle; sb.targetGraphic = handle.GetComponent<Image>();
            sb.transition = Selectable.Transition.None;
            return sb;
        }

        /// <summary>Put the holders back into the panel exactly as the prefab had them and drop the scroll view.</summary>
        private static void Unwrap(StatManager sm)
        {
            if (_viewport == null) return;
            try
            {
                if (sm != null && sm.StatLineHolder != null && sm.StatValueHolder != null && _origParent != null)
                {
                    RectTransform line = sm.StatLineHolder.GetComponent<RectTransform>(), value = sm.StatValueHolder.GetComponent<RectTransform>();
                    line.SetParent(_origParent, false); value.SetParent(_origParent, false);
                    line.SetSiblingIndex(Math.Min(_origLineIndex, _origParent.childCount - 1));
                    value.SetSiblingIndex(Math.Min(_origValueIndex, _origParent.childCount - 1));
                    line.anchoredPosition = _origLinePos; value.anchoredPosition = _origValuePos;
                }
                UnityEngine.Object.Destroy(_viewport);
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Stats window unwrap: " + e.Message); }
            _viewport = null; _content = null; _scroll = null; _contentHeight = -1f;
        }

        /// <summary>Called from the plugin's Update while patched: keeps the scroll content as tall as the rows (the
        /// layout groups position rows top-down; the line holder's last child, the End Separator, marks the bottom).</summary>
        internal static void Tick()
        {
            if (_dumpCountdown > 0 && --_dumpCountdown == 0) DumpWindow(StatManager.Instance);
            if (_content == null || _scroll == null || !StatManager.IsNotNullAndIsActive) return;
            try
            {
                StatManager sm = StatManager.Instance;
                Transform lh = sm.StatLineHolder.transform;
                if (lh.childCount == 0) return;
                RectTransform end = lh.GetChild(lh.childCount - 1) as RectTransform;
                if (end == null) return;
                float h = -end.anchoredPosition.y + 40f;
                if (h < 10f) return;
                if (Mathf.Abs(h - _contentHeight) > 0.5f)
                {
                    _contentHeight = h;
                    _content.sizeDelta = new Vector2(0f, h);
                }
            }
            catch { }
        }

        // every time the window opens, start at the top
        [HarmonyPatch(typeof(StatManager), nameof(StatManager.OpenWindow))]
        private static class StatManager_OpenWindow
        {
            private static void Postfix(StatManager __instance)
            {
                try { if (_scroll != null) _scroll.verticalNormalizedPosition = 1f; } catch { }
                RunStatsPatches.ApplyTitle(__instance);
                if (Cfg != null && Cfg.DebugHooks.Value) _dumpCountdown = 3;
            }
        }

        // ---------- diagnostics: the window's structure, once per launch (DebugHooks only) ----------

        private static bool _dumped;
        private static int _dumpCountdown;

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts.ToArray());
        }

        /// <summary>Log the whole StatManager hierarchy: names, active flags, components and rect geometry (how the
        /// scroll view above was designed). Row children are summarised.</summary>
        private static void DumpWindow(StatManager sm)
        {
            if (_dumped || sm == null) return;
            _dumped = true;
            try
            {
                var known = new Dictionary<Transform, string>();
                if (sm.Content != null) known[sm.Content.transform] = "CONTENT";
                if (sm.StatCharacterNameHolder != null) known[sm.StatCharacterNameHolder.transform] = "NAME_HOLDER";
                if (sm.StatLineHolder != null) known[sm.StatLineHolder.transform] = "LINE_HOLDER";
                if (sm.StatValueHolder != null) known[sm.StatValueHolder.transform] = "VALUE_HOLDER";
                int lines = 0;
                BattleStatsPlugin.Log.LogInfo("Stats window dump: root " + PathOf(sm.transform));
                DumpNode(sm.transform, 0, known, ref lines);
                BattleStatsPlugin.Log.LogInfo("Stats window dump: end (" + lines + " nodes)");
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Stats window dump failed: " + e); }
        }

        /// <summary>Log any UI subtree once (used for the HUD button row with DebugHooks on).</summary>
        internal static void DumpHierarchy(Transform root, string tag)
        {
            if (root == null) return;
            try
            {
                int lines = 0;
                BattleStatsPlugin.Log.LogInfo(tag + " dump: root " + PathOf(root));
                DumpNode(root, 0, new Dictionary<Transform, string>(), ref lines);
                BattleStatsPlugin.Log.LogInfo(tag + " dump: end (" + lines + " nodes)");
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning(tag + " dump failed: " + e); }
        }

        private static void DumpNode(Transform t, int depth, Dictionary<Transform, string> known, ref int lines)
        {
            if (lines > 400) return;
            lines++;
            string tag; known.TryGetValue(t, out tag);
            var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray();
            string geo = "";
            RectTransform rt = t as RectTransform;
            if (rt != null)
                geo = " anchors=" + rt.anchorMin + "-" + rt.anchorMax + " pivot=" + rt.pivot + " pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta + " rect=" + rt.rect.size;
            string layout = "";
            var lg = t.GetComponent<LayoutGroup>();
            if (lg != null) layout += " padding=" + lg.padding.top + "/" + lg.padding.bottom + "/" + lg.padding.left + "/" + lg.padding.right;
            var hv = lg as HorizontalOrVerticalLayoutGroup;
            if (hv != null) layout += " spacing=" + hv.spacing + " ctrlH=" + hv.childControlHeight + " ctrlW=" + hv.childControlWidth + " forceH=" + hv.childForceExpandHeight + " forceW=" + hv.childForceExpandWidth;
            var le = t.GetComponent<LayoutElement>();
            if (le != null) layout += " layoutElement(min=" + le.minWidth + "x" + le.minHeight + " pref=" + le.preferredWidth + "x" + le.preferredHeight + " flex=" + le.flexibleWidth + "x" + le.flexibleHeight + ")";
            var csf = t.GetComponent<ContentSizeFitter>();
            if (csf != null) layout += " fitter(h=" + csf.horizontalFit + " v=" + csf.verticalFit + ")";
            var tmp = t.GetComponent<TextMeshProUGUI>();
            string text = tmp != null ? " text='" + (tmp.text ?? "").Replace("\n", "\\n") + "'" : "";
            if (text.Length > 60) text = text.Substring(0, 60) + "...'";
            BattleStatsPlugin.Log.LogInfo("  " + new string(' ', depth * 2) + (tag != null ? "[" + tag + "] " : "") + t.name + (t.gameObject.activeSelf ? "" : " (inactive)") + " {" + string.Join(",", comps) + "}" + geo + layout + text);
            bool isHolder = tag == "LINE_HOLDER" || tag == "VALUE_HOLDER" || tag == "NAME_HOLDER";
            for (int i = 0; i < t.childCount; i++)
            {
                if (isHolder && i >= 2 && i < t.childCount - 1) { if (i == 2) { lines++; BattleStatsPlugin.Log.LogInfo("  " + new string(' ', (depth + 1) * 2) + "... " + (t.childCount - 3) + " more children like the above ..."); } continue; }
                DumpNode(t.GetChild(i), depth + 1, known, ref lines);
            }
        }

        // ---------- hover on the character name: per-skill breakdown ----------

        [HarmonyPatch(typeof(StatManager), nameof(StatManager.PopulateStats))]
        private static class StatManager_PopulateStats_Postfix
        {
            private static void Postfix(StatManager __instance, List<Character> characters)
            {
                try
                {
                    RunStatsPatches.ApplyHistoryNames(__instance);   // a saved run names its own columns
                    if (Cfg == null || !Cfg.Enabled.Value || !Cfg.ShowInWindow.Value) return;
                    if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
                    if (!Cfg.TopSkillsTooltip.Value || __instance.StatCharacterNameHolder == null || characters == null) return;
                    Transform holder = __instance.StatCharacterNameHolder.transform;
                    for (int i = 0; i < holder.childCount; i++)
                    {
                        GameObject go = holder.GetChild(i).gameObject;
                        HeaderHover hh = go.GetComponent<HeaderHover>() ?? go.AddComponent<HeaderHover>();
                        hh.Character = i < characters.Count ? characters[i] : null;
                    }
                    if (Cfg.CellTooltips.Value) HookCells(__instance, characters);
                }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Stats window hover failed: " + e.Message); }
            }
        }

        /// <summary>Which bucket (and label) each value row carries: the game's rows by their BattleStat, ours by Row.Bucket.</summary>
        private static void HookCells(StatManager sm, List<Character> characters)
        {
            if (sm.StatValueHolder == null || !sm.UseSections) return;
            var labels = new List<string>(); var buckets = new List<int>();
            foreach (StatSection sec in sm.StatSections)
            {
                string title = Loc(sec.SectionName);
                labels.Add(title); buckets.Add(title.IndexOf("Total Damage", StringComparison.OrdinalIgnoreCase) == 0 && !title.Contains("Taken") && !title.Contains("Blocked") ? SharedStats.B_All : -1);
                if (sec.HideChildren || sec.BattleStats == null) continue;
                foreach (BattleStat bs in sec.BattleStats)
                {
                    labels.Add(Loc(GUIManager.SpaceOutString(bs.ToString())));
                    buckets.Add(bs == BattleStat.DamageDealt ? SharedStats.B_Dealt : bs == BattleStat.SummonDamageDealt ? SharedStats.B_Summons : bs == BattleStat.DamageReturned ? SharedStats.B_Thorns : -1);
                }
            }
            if (_rows != null) foreach (Row r in _rows) { labels.Add(r.Label); buckets.Add(r.Bucket); }
            Transform vh = sm.StatValueHolder.transform;
            for (int r = 0; r < vh.childCount && r < labels.Count; r++)
            {
                Transform row = vh.GetChild(r);
                for (int c = 0; c < row.childCount; c++)
                {
                    GameObject cell = row.GetChild(c).gameObject;
                    CellHover ch = cell.GetComponent<CellHover>() ?? cell.AddComponent<CellHover>();
                    ch.Character = c < characters.Count ? characters[c] : null;
                    ch.Bucket = buckets[r]; ch.Label = labels[r];
                    var tmp = cell.GetComponent<TextMeshProUGUI>();
                    if (tmp != null && buckets[r] >= 0) tmp.raycastTarget = true;
                }
            }
        }

        private static string Loc(string key) { try { return OptionsManager.Localize(key) ?? key; } catch { return key; } }

        /// <summary>Lives on each value cell; shows the abilities behind that number for that character.</summary>
        public sealed class CellHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Character Character; public int Bucket = -1; public string Label;

            public void OnPointerEnter(PointerEventData eventData)
            {
                try
                {
                    if (Bucket < 0 || Character == null || Cfg == null || !Cfg.Enabled.Value || !Cfg.CellTooltips.Value || GUIManager.instance == null || GUIManager.instance.tooltip == null) return;
                    if (!HostHasData()) return;
                    var lines = RunStatsPatches.RunMode ? SharedStats.Breakdown(RunStatsPatches.ViewFor(Character), Bucket) : SharedStats.Breakdown(Character, Bucket);
                    float total = 0f; foreach (var l in lines) total += l.Damage;
                    int max = Cfg.TopSkills.Value;
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < lines.Count && i < max; i++)
                    {
                        var l = lines[i];
                        sb.Append("<color=#CBB396>").Append(N(l.Damage)).Append("</color>  ").Append(l.Name)
                          .Append(" <color=#9AA5B1>(").Append(l.Hits).Append(l.Hits == 1 ? " hit" : " hits").Append(total > 0 ? ", " + (100f * l.Damage / total).ToString("0") + "%" : "").Append(")</color>\n");
                    }
                    if (lines.Count > max) sb.Append("<color=#9AA5B1>... and ").Append(lines.Count - max).Append(" more</color>");
                    string body = lines.Count == 0 ? "No damage in this section." : sb.ToString().TrimEnd('\n');
                    GUIManager.instance.tooltip.ShowUniversalTooltip(RunStatsPatches.DisplayName(Character), Label + ": " + N(total), body);
                }
                catch { }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                try { if (GUIManager.instance != null && GUIManager.instance.tooltip != null) GUIManager.instance.tooltip.HideTooltip(); } catch { }
            }
        }

        /// <summary>Lives on each column-name label; shows the top damage sources for that character.</summary>
        public sealed class HeaderHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Character Character;

            public void OnPointerEnter(PointerEventData eventData)
            {
                try
                {
                    if (Character == null || Cfg == null || !Cfg.Enabled.Value || !Cfg.TopSkillsTooltip.Value || GUIManager.instance == null || GUIManager.instance.tooltip == null) return;
                    var top = RunStatsPatches.RunMode ? SharedStats.TopList(k => RunStatsPatches.Get(Character, k)) : SharedStats.TopList(Character);
                    string body = top.Count == 0 ? "No damage recorded." : string.Join("\n", top.Select(t => "<color=#CBB396>" + N(t.Item2) + "</color>  " + t.Item1 + " <color=#9AA5B1>(" + t.Item3 + (t.Item3 == 1 ? " hit" : " hits") + ")</color>").ToArray());
                    float total = G(Character, (int)BattleStat.DamageDealt);
                    GUIManager.instance.tooltip.ShowUniversalTooltip(RunStatsPatches.DisplayName(Character), total > 0 ? N(total) + " damage - top sources" : "Top damage sources", body);
                }
                catch { }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                try { if (GUIManager.instance != null && GUIManager.instance.tooltip != null) GUIManager.instance.tooltip.HideTooltip(); } catch { }
            }
        }
    }
}
