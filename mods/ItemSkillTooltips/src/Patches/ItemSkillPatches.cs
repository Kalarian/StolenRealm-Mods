using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace ItemSkillTooltips.Patches
{
    /// <summary>
    /// An item tooltip only NAMES the skills an item grants ("Skills Granted" list from ItemInfo.GrantedSkills, or a
    /// {SKL=Name} token in the item's Special text). This shows the skill's own tooltip beside the item tooltip.
    ///
    /// The game already runs a second Tooltip instance ("Comp Tooltip") for the equipped-item comparison; we clone that
    /// object into our own panels, flag them HasStaticLayout (so the game's own positioning code leaves them alone) and
    /// place them ourselves on the free side of the item tooltip (after the comparison tooltip when it is up), the same
    /// way the game places the comparison tooltip: at the previous panel's compAnchorRight / compAnchorLeft, then pushed
    /// back inside the screen with GetGUIElementOffset.
    ///
    /// Tooltip.ShowSkillTooltip needs no ownership of the skill. It sizes the skill's numbers at the item's level only for
    /// an EQUIPPED item (SkillInfo.ObtainedByItem scans EquippedItems); while we render, a prefix makes ObtainedByItem
    /// return the hovered item so a bag/shop item shows what equipping it would give.
    /// </summary>
    internal static class ItemSkillPatches
    {
        private static readonly List<Tooltip> _panels = new List<Tooltip>();
        private static int _shown;
        private static Tooltip _host;
        private static Item _renderItem;
        private static readonly HashSet<SkillInfo> _renderSkills = new HashSet<SkillInfo>();
        private static readonly Regex SklToken = new Regex(@"\{SKL=([^}]+)\}", RegexOptions.IgnoreCase);

        private static ItemSkillTooltipsConfig Cfg => ItemSkillTooltipsPlugin.Cfg;

        internal static void Reset()
        {
            HideAll();
            foreach (Tooltip p in _panels) { try { if (p != null) UnityEngine.Object.Destroy(p.gameObject); } catch { } }
            _panels.Clear(); _renderItem = null; _renderSkills.Clear();
        }

        // ---------- what to show

        internal static List<SkillInfo> SkillsOf(ItemInfo info)
        {
            var list = new List<SkillInfo>();
            if (info == null || Cfg == null) return list;
            if (Cfg.ShowGrantedSkills.Value && info.GrantedSkills != null)
                foreach (SkillInfo s in info.GrantedSkills)
                    if (s != null && s.SkillType != SkillType.Basic && !list.Contains(s)) list.Add(s);
            if (Cfg.ShowNamedSkills.Value && !string.IsNullOrEmpty(info.OptionalDescription))
                foreach (Match m in SklToken.Matches(info.OptionalDescription))
                {
                    SkillInfo s = FindSkill(m.Groups[1].Value.Trim());
                    if (s != null && !list.Contains(s)) list.Add(s);
                }
            return list;
        }

        private static SkillInfo FindSkill(string name)
        {
            try
            {
                SkillInfo s = GUIManager.instance.tooltip.FindSkillByName(name);
                if (s != null) return s;
                if (Game.Instance != null && Game.Instance.Skills != null)
                    foreach (SkillInfo k in Game.Instance.Skills)
                        if (k != null && string.Equals(k.SkillName, name, StringComparison.OrdinalIgnoreCase)) return k;
            }
            catch { }
            return null;
        }

        // ---------- panels

        private static Tooltip Panel(int i, Tooltip host)
        {
            while (_panels.Count <= i) _panels.Add(null);
            if (_panels[i] != null) return _panels[i];
            Tooltip template = null;
            try { template = GUIManager.instance.tooltip.ComparisonTooltip; } catch { }
            if (template == null) template = host;
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = "ItemSkillTooltip" + (i + 1);
            Tooltip t = go.GetComponent<Tooltip>();
            if (t == null) { UnityEngine.Object.Destroy(go); return null; }
            t.HasStaticLayout = true;     // the game's Update/ShowTooltip skip their positioning; we place it
            go.SetActive(false);
            _panels[i] = t;
            return t;
        }

        internal static void Show(Tooltip host, Item item, ItemInfo info)
        {
            HideAll();
            if (Cfg == null || !Cfg.Enabled.Value || host == null || info == null) return;
            List<SkillInfo> skills = SkillsOf(info);
            if (skills.Count == 0) return;
            int max = Mathf.Clamp(Cfg.MaxPanels.Value, 1, 4);
            Character c = null;
            try { c = host.TooltipCharacter; } catch { }
            _host = host;
            _renderItem = Cfg.ItemLevelNumbers.Value ? item : null;
            _renderSkills.Clear(); foreach (SkillInfo s in skills) _renderSkills.Add(s);
            var names = new List<string>();
            try
            {
                for (int i = 0; i < skills.Count && i < max; i++)
                {
                    Tooltip panel = Panel(i, host);
                    if (panel == null) break;
                    try { panel.ShowSkillTooltip(skills[i], c, false, false, true); _shown = i + 1; names.Add(skills[i].SkillName); }
                    catch (Exception e) { ItemSkillTooltipsPlugin.Log.LogWarning("Skill tooltip for '" + skills[i].SkillName + "' failed: " + e.Message); break; }
                }
            }
            finally { _renderItem = null; _renderSkills.Clear(); }
            Position();
            if (Cfg.Verbose.Value && names.Count > 0)
                ItemSkillTooltipsPlugin.Log.LogInfo("Item '" + (item != null ? item.ItemName : info.ItemName) + "': skill tooltip(s) " + string.Join(", ", names.ToArray())
                    + (item != null && Cfg.ItemLevelNumbers.Value ? " at item level " + item.EffectiveLevel : "") + (skills.Count > names.Count ? " (+" + (skills.Count - names.Count) + " not shown)" : ""));
        }

        internal static void HideAll()
        {
            for (int i = 0; i < _panels.Count; i++) { try { if (_panels[i] != null && _panels[i].gameObject.activeSelf) _panels[i].HideTooltip(); } catch { } }
            _shown = 0; _host = null;
        }

        /// <summary>Place our panels beside the host (after the comparison tooltip when it is up), like the game places the comparison tooltip.</summary>
        internal static void Position()
        {
            if (_shown == 0 || _host == null) return;
            try
            {
                RectTransform mainRt = _host.GetComponent<RectTransform>();
                if (mainRt == null) return;
                bool right = mainRt.pivot.x == 0f;
                Tooltip prev = _host;
                try { Tooltip comp = _host.ComparisonTooltip; if (comp != null && comp.gameObject.activeSelf) prev = comp; } catch { }
                Vector3 hostPos = _host.transform.position;
                for (int i = 0; i < _shown; i++)
                {
                    Tooltip p = _panels[i];
                    if (p == null || !p.gameObject.activeSelf) continue;
                    RectTransform rt = p.GetComponent<RectTransform>();
                    Vector2 pv = new Vector2(right ? 0f : 1f, mainRt.pivot.y);
                    rt.pivot = pv; rt.anchorMin = pv; rt.anchorMax = pv;
                    Transform anchor = right ? prev.compAnchorRight : prev.compAnchorLeft;
                    float x;
                    if (anchor != null) x = anchor.position.x;
                    else
                    {
                        Vector3[] corners = new Vector3[4];
                        prev.GetComponent<RectTransform>().GetWorldCorners(corners);
                        x = right ? corners[2].x : corners[0].x;
                    }
                    rt.position = new Vector3(x, hostPos.y, hostPos.z);
                    rt.position += rt.GetGUIElementOffset();
                    prev = p;
                }
            }
            catch (Exception e) { if (Cfg != null && Cfg.Verbose.Value) ItemSkillTooltipsPlugin.Log.LogWarning("Position failed: " + e.Message); }
        }

        private static bool IsHostOrMain(Tooltip t)
        {
            if (t == null) return false;
            if (t == _host) return true;
            try { return GUIManager.instance != null && t == GUIManager.instance.tooltip; } catch { return false; }
        }

        // ---------- hooks

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowItemTooltip), new[] { typeof(Item), typeof(Transform), typeof(TooltipOffsetInfo), typeof(Character), typeof(bool), typeof(bool), typeof(ControlGlyphInfo?), typeof(string), typeof(Item) })]
        private static class ShowItem
        {
            private static void Postfix(Tooltip __instance, Item item)
            {
                try { if (__instance.gameObject.activeSelf) Show(__instance, item, item != null ? item.ItemInfo : null); }
                catch (Exception e) { ItemSkillTooltipsPlugin.Log.LogWarning("ShowItemTooltip hook failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowItemTooltip), new[] { typeof(ItemInfo), typeof(TooltipOffsetInfo) })]
        private static class ShowItemInfo
        {
            private static void Postfix(Tooltip __instance, ItemInfo itemInfo)
            {
                try { if (__instance.gameObject.activeSelf) Show(__instance, null, itemInfo); }
                catch (Exception e) { ItemSkillTooltipsPlugin.Log.LogWarning("ShowItemTooltip(ItemInfo) hook failed: " + e); }
            }
        }

        /// <summary>Any other tooltip taking over the main panel (skill bar, status, link...) ends our item preview.</summary>
        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowTooltip))]
        private static class ShowAny
        {
            private static void Prefix(Tooltip __instance)
            {
                if (_shown > 0 && IsHostOrMain(__instance)) HideAll();
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.HideTooltip))]
        private static class Hide
        {
            private static void Postfix(Tooltip __instance)
            {
                if (_shown > 0 && IsHostOrMain(__instance)) HideAll();
            }
        }

        [HarmonyPatch(typeof(Tooltip), "Update")]
        private static class Follow
        {
            private static void Postfix(Tooltip __instance)
            {
                if (_shown > 0 && __instance == _host) Position();
            }
        }

        /// <summary>While our panels render, the hovered item counts as the source of its skills, so the numbers are at the item's level.</summary>
        [HarmonyPatch(typeof(SkillInfo), nameof(SkillInfo.ObtainedByItem))]
        private static class ItemLevel
        {
            private static bool Prefix(SkillInfo __instance, ref Item __result)
            {
                if (_renderItem != null && _renderSkills.Contains(__instance)) { __result = _renderItem; return false; }
                return true;
            }
        }
    }
}
