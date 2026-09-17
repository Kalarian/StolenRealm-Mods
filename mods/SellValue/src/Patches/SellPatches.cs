using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SellValue.Patches
{
    /// <summary>
    /// Every item tooltip gets its sell value (Item.SellPrice: 8% of the purchase price rounded to 5, or the item's fixed
    /// sell price, times the stack) as the last footer line, right-aligned.
    ///
    /// The game's Tooltip prefab has a FooterText line under the description that ShowItemTooltip fills from its
    /// footerText argument (ItemSlot passes "Not Enough Gold" / "Requirements Not Met" / "Owner: ..." there, most
    /// callers pass nothing). A prefix on ShowItemTooltip(Item, ...) appends our line to that argument, so the game lays
    /// the footer out itself; a TMP &lt;align="right"&gt; tag keeps only our line on the right while the game's own lines
    /// stay where they were. The comparison tooltip is filled by a nested Tooltip.ShowTooltip call with footerText null;
    /// while ShowItemTooltip runs, a prefix on ShowTooltip recognises that call (comparison instance, isItem, title ==
    /// the equipped item's name) and appends the equipped item's value the same way.
    /// </summary>
    internal static class SellPatches
    {
        private const string LabelColor = "CBB396";

        private static bool _inShowItem;
        private static Item _compCandidate;
        private static bool _dumped;

        private static SellValueConfig Cfg => SellValuePlugin.Cfg;

        internal static void Reset()
        {
            _inShowItem = false; _compCandidate = null; _dumped = false;
        }

        // ---------- the line

        internal static string Line(Item item)
        {
            if (item == null || item.ItemInfo == null) return null;
            float total;
            try { total = item.SellPrice; } catch { return null; }
            int stacks = 1;
            try { stacks = Math.Max(1, item.numStacks); } catch { }
            string text = Num(total);
            if (Cfg.PerUnit.Value && stacks > 1) text += " (" + Num(total / stacks) + " each)";
            string label = (Cfg.Label.Value ?? "").Trim();
            string body = (label.Length > 0 ? "<color=#" + LabelColor + ">" + label + ":</color> " : "") + text + " " + GoldIcon.Tag(15);
            return "<align=\"right\">" + body + "</align>";
        }

        private static string Num(float v)
        {
            return Mathf.RoundToInt(v).ToString();
        }

        private static string Append(string footer, string line)
        {
            if (string.IsNullOrEmpty(line)) return footer;
            if (string.IsNullOrEmpty(footer)) return line;
            return footer.TrimEnd('\n', '\r', ' ') + "\n" + line;
        }

        /// <summary>The item the game will show in the comparison tooltip (same rule as ShowItemTooltip's item2).</summary>
        private static Item CompItem(Item item, Character characterToCompare, bool showComp, Item compItemOverride)
        {
            try
            {
                if (compItemOverride != null) return compItemOverride;
                if (!showComp || item == null || item.ItemInfo == null || item.equipped) return null;
                Character c = characterToCompare ?? (GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null);
                if (c == null || c.EquippedItems == null) return null;
                return c.EquippedItems.FirstOrDefault(x => x != null && x.ItemInfo != null && x.ItemInfo.ItemType == item.ItemInfo.ItemType);
            }
            catch { return null; }
        }

        // ---------- hooks

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowItemTooltip), new[] { typeof(Item), typeof(Transform), typeof(TooltipOffsetInfo), typeof(Character), typeof(bool), typeof(bool), typeof(ControlGlyphInfo?), typeof(string), typeof(Item) })]
        private static class ShowItem
        {
            private static void Prefix(Item item, Character characterToCompare, bool showComp, Item compItemOverride, ref string footerText)
            {
                _inShowItem = false; _compCandidate = null;
                try
                {
                    if (Cfg == null || !Cfg.Enabled.Value || item == null) return;
                    string line = Line(item);
                    if (line == null) return;
                    footerText = Append(footerText, line);
                    _inShowItem = true;
                    if (Cfg.OnComparison.Value) _compCandidate = CompItem(item, characterToCompare, showComp, compItemOverride);
                    if (Cfg.Verbose.Value)
                        SellValuePlugin.Log.LogInfo("Item '" + item.ItemName + "' sells for " + Num(item.SellPrice)
                            + (_compCandidate != null ? "; equipped '" + _compCandidate.ItemName + "' sells for " + Num(_compCandidate.SellPrice) : ""));
                }
                catch (Exception e) { SellValuePlugin.Log.LogWarning("ShowItemTooltip hook failed: " + e); }
            }

            private static void Finalizer()
            {
                _inShowItem = false; _compCandidate = null;
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowTooltip))]
        private static class ShowAny
        {
            private static void Prefix(Tooltip __instance, string title, bool isItem, ref string footerText)
            {
                try
                {
                    if (!_inShowItem || _compCandidate == null || !isItem || __instance == null || !__instance.IsComparisonTooltip) return;
                    if (!string.Equals(title, _compCandidate.ItemName)) return;
                    footerText = Append(footerText, Line(_compCandidate));
                }
                catch (Exception e) { SellValuePlugin.Log.LogWarning("ShowTooltip hook failed: " + e); }
            }
        }

        // ---------- diagnostics: the tooltip prefab's layout, once per launch (DebugDump only)

        internal static void Tick()
        {
            if (_dumped || Cfg == null || !Cfg.DebugDump.Value) return;
            Tooltip t = null;
            try { if (GUIManager.instance != null) t = GUIManager.instance.tooltip; } catch { }
            if (t == null) return;
            _dumped = true;
            try
            {
                var known = new Dictionary<Transform, string>();
                if (t.FooterText != null) known[t.FooterText.transform] = "FOOTER";
                if (t.Description != null) known[t.Description.transform] = "DESCRIPTION";
                if (t.Title != null) known[t.Title.transform] = "TITLE";
                int lines = 0;
                SellValuePlugin.Log.LogInfo("Tooltip dump: root " + PathOf(t.transform) + " comparison=" + (t.ComparisonTooltip != null ? t.ComparisonTooltip.name : "null"));
                DumpNode(t.transform, 0, known, ref lines);
                SellValuePlugin.Log.LogInfo("Tooltip dump: end (" + lines + " nodes)");
                foreach (TMP_SpriteAsset sa in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
                {
                    if (sa == null || sa.name.IndexOf("Currency", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var names = new List<string>();
                    if (sa.spriteCharacterTable != null) foreach (var ch in sa.spriteCharacterTable) names.Add(ch.name + "#" + ch.glyphIndex);
                    SellValuePlugin.Log.LogInfo("Sprite asset '" + sa.name + "': " + string.Join(", ", names.ToArray()));
                }
                var def = TMP_Settings.defaultSpriteAsset;
                SellValuePlugin.Log.LogInfo("Default sprite asset: " + (def != null ? def.name : "null") + "; footer spriteAsset: " + (t.FooterText != null && t.FooterText.spriteAsset != null ? t.FooterText.spriteAsset.name : "null"));
            }
            catch (Exception e) { SellValuePlugin.Log.LogWarning("Tooltip dump failed: " + e); }
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts.ToArray());
        }

        private static void DumpNode(Transform t, int depth, Dictionary<Transform, string> known, ref int lines)
        {
            if (lines > 200) return;
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
            string text = "";
            if (tmp != null)
            {
                text = " tmp(align=" + tmp.alignment + " size=" + tmp.fontSize + " auto=" + tmp.enableAutoSizing + " wrap=" + tmp.textWrappingMode + " overflow=" + tmp.overflowMode + ") text='" + (tmp.text ?? "").Replace("\n", "\\n") + "'";
                if (text.Length > 140) text = text.Substring(0, 140) + "...'";
            }
            SellValuePlugin.Log.LogInfo("  " + new string(' ', depth * 2) + (tag != null ? "[" + tag + "] " : "") + t.name + (t.gameObject.activeSelf ? "" : " (inactive)") + " {" + string.Join(",", comps) + "}" + geo + layout + text);
            for (int i = 0; i < t.childCount; i++) DumpNode(t.GetChild(i), depth + 1, known, ref lines);
        }
    }
}
