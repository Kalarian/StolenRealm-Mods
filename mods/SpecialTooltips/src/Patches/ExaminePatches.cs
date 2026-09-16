using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SpecialTooltips.Patches
{
    /// <summary>
    /// The Examine window lists a character's SpecialEffect tags as plain lines. The window's own Update already
    /// looks for TMP links in that text (specialLinkList) but nothing ever creates them. We rewrite the text with
    /// one link per entry, pad specialLinkList with empty entries so the game's Update stays index-safe (it only
    /// checks .skill / .actionStatus, both null), and show our own tooltip from an Update postfix.
    /// </summary>
    internal static class ExaminePatches
    {
        private static readonly List<SpecialEffect> _effects = new List<SpecialEffect>();
        private static Character _character;
        private static int _shown = -1;
        private static bool _loggedConditions;

        [HarmonyPatch(typeof(ExamineWindow), nameof(ExamineWindow.ShowCharacterExamine))]
        private static class ExamineWindow_ShowCharacterExamine
        {
            private static void Postfix(ExamineWindow __instance, Character character)
            {
                try
                {
                    Rebuild(__instance, character);
                }
                catch (Exception e)
                {
                    SpecialTooltipsPlugin.Log.LogWarning("Special list rebuild failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(ExamineWindow), "Update")]
        private static class ExamineWindow_Update
        {
            private static void Postfix(ExamineWindow __instance)
            {
                try
                {
                    Hover(__instance);
                }
                catch (Exception e)
                {
                    if (SpecialTooltipsPlugin.Cfg.Verbose.Value) SpecialTooltipsPlugin.Log.LogWarning("Special hover failed: " + e);
                }
            }
        }

        private static void Rebuild(ExamineWindow win, Character character)
        {
            _effects.Clear();
            _character = null;
            _shown = -1;
            SpecialTooltipsConfig cfg = SpecialTooltipsPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || character == null || win.SpecialText == null) return;
            if (!win.SpecialSection.activeSelf) return;

            LogConditionsOnce();

            List<SpecialEffect> list = character.CalculatedSpecialEffects;
            if (list == null || list.Count == 0) return;
            _effects.AddRange(list.Distinct());
            _character = character;

            win.specialLinkList.Clear();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _effects.Count; i++)
            {
                win.specialLinkList.Add(new TextLinkData()); // keeps the game's Update index-safe; skill/actionStatus stay null
                string label = OptionsManager.LocalizeEnum(_effects[i], character.CharacterInfo.LanguageGender);
                if (cfg.UnderlineLinks.Value) label = "<u>" + label + "</u>";
                sb.Append("<link=").Append(i).Append('>').Append(label).Append("</link>");
                if (i < _effects.Count - 1) sb.Append('\n');
            }
            win.SpecialText.text = sb.ToString();
        }

        private static void Hover(ExamineWindow win)
        {
            if (_character == null || _effects.Count == 0 || win.SpecialText == null) return;
            if (!win.gameObject.activeInHierarchy) { _shown = -1; return; }
            Tooltip tooltip = GUIManager.instance != null ? GUIManager.instance.tooltip : null;
            if (tooltip == null) return;

            int idx = TMP_TextUtilities.FindIntersectingLink(win.SpecialText, VirtualInput.mousePosition, null);
            if (idx < 0 || idx >= _effects.Count)
            {
                _shown = -1; // the game's own Update hides the tooltip when no link is hovered
                return;
            }
            if (idx == _shown && tooltip.gameObject.activeSelf) return;

            SpecialEffect effect = _effects[idx];
            string title = OptionsManager.LocalizeEnum(effect, _character.CharacterInfo.LanguageGender);
            string body = SpecialTooltipsPlugin.Desc.Get(effect, _character);
            tooltip.ShowUniversalTooltip(title, "", body);
            _shown = idx;
            if (SpecialTooltipsPlugin.Cfg.Verbose.Value)
                SpecialTooltipsPlugin.Log.LogInfo("Special tooltip " + effect + " on " + _character.LocalizedCharacterName);
        }

        private static void LogConditionsOnce()
        {
            if (_loggedConditions || !SpecialTooltipsPlugin.Cfg.Verbose.Value) return;
            _loggedConditions = true;
            try
            {
                var conds = Game.Instance != null ? Game.Instance.SpecialEffectConditions : null;
                if (conds == null) return;
                foreach (SpecialEffectCondition c in conds)
                    SpecialTooltipsPlugin.Log.LogInfo("Conditional special: " + c.SpecialEffect + " when [" + c.Condition + "]");
            }
            catch (Exception e)
            {
                SpecialTooltipsPlugin.Log.LogWarning("Could not read SpecialEffectConditions: " + e.Message);
            }
        }
    }
}
