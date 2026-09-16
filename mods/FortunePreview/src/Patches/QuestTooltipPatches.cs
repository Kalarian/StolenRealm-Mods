using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace FortunePreview.Patches
{
    /// <summary>
    /// Tooltip.ShowQuestNodeTooltip builds the quest description and calls Tooltip.ShowTooltip(title, subtitle, icon,
    /// description, ...). A prefix on the former computes the fortune list for the hovered quest and parks it; a prefix
    /// on the latter appends it to the description while it is parked. The postfix clears it either way.
    /// The last hovered node (and the glyph/footer args it was shown with) are remembered so the tooltip can be re-shown
    /// when the detail key is pressed or released.
    /// </summary>
    internal static class QuestTooltipPatches
    {
        private static string _pending;
        internal static void SetPending(string text) { _pending = text; }
        internal static void Reset() { _pending = null; _lastNode = null; _reshowing = false; }
        private static QuestNode _lastNode;
        private static ControlGlyphInfo? _lastGlyphs;
        private static string _lastFooter;
        private static bool _lastDetailed;
        private static bool _reshowing;

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowQuestNodeTooltip))]
        private static class Tooltip_ShowQuestNodeTooltip
        {
            private static void Prefix(QuestNode questNode, ControlGlyphInfo? controlGlyphInfo, string footerText)
            {
                _pending = null;
                _inQuestTooltip = true;
                try
                {
                    FortunePreviewConfig cfg = FortunePreviewPlugin.Cfg;
                    if (cfg == null || !cfg.QuestTooltip.Value || questNode == null) return;
                    if (questNode.questNodeType == QuestNodeType.Town || !questNode.CanNavigateTo) { _lastNode = null; return; }
                    QuestInstance quest = questNode.CurrentQuestInstance;
                    if (quest == null) { _lastNode = null; return; }
                    _lastNode = questNode; _lastGlyphs = controlGlyphInfo; _lastFooter = footerText;
                    _lastDetailed = cfg.DetailKey.Value.IsPressed();
                    _pending = _lastDetailed ? BuildDetailed(quest, cfg) : BuildCompact(quest, cfg);
                }
                catch (Exception e)
                {
                    _pending = null;
                    if (FortunePreviewPlugin.Cfg != null && FortunePreviewPlugin.Cfg.Verbose.Value) FortunePreviewPlugin.Log.LogWarning("Quest fortune list failed: " + e);
                }
            }

            private static void Postfix()
            {
                _pending = null;
                _inQuestTooltip = false;
            }
        }

        private static bool _inQuestTooltip;

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowTooltip))]
        private static class Tooltip_ShowTooltip
        {
            private static void Prefix(ref string description)
            {
                // Any tooltip that is not the quest tooltip (a button, an icon, an item) replaces it: forget the quest so the
                // detail key does not bring the quest tooltip back on top of it.
                if (!_inQuestTooltip && !_reshowing) _lastNode = null;
                if (string.IsNullOrEmpty(_pending)) return;
                description = (description ?? "") + _pending;
                _pending = null;
            }
        }

        /// <summary>Called from the plugin's Update: re-show the quest tooltip when the detail key changes state.</summary>
        internal static void Tick()
        {
            FortunePreviewConfig cfg = FortunePreviewPlugin.Cfg;
            if (cfg == null || _lastNode == null || _reshowing) return;
            Tooltip tooltip = GUIManager.instance != null ? GUIManager.instance.tooltip : null;
            if (tooltip == null || !tooltip.gameObject.activeSelf) { _lastNode = null; return; }
            bool held = cfg.DetailKey.Value.IsPressed();
            if (held == _lastDetailed) return;
            try
            {
                _reshowing = true;
                tooltip.ShowQuestNodeTooltip(_lastNode, _lastGlyphs, _lastFooter); // prefix re-reads the key state
            }
            catch (Exception e)
            {
                if (cfg.Verbose.Value) FortunePreviewPlugin.Log.LogWarning("Re-show failed: " + e);
            }
            finally { _reshowing = false; }
        }

        // ---------- rendering ----------

        private sealed class Row
        {
            public FortuneHit Hit;
            public bool Owned;
            public float OwnedLevel;
            public string Color;
        }

        private static List<Row> Rows(QuestInstance quest, FortunePreviewConfig cfg, out int hidden)
        {
            hidden = 0;
            var owned = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            Character me = null;
            try { me = GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null; } catch { }
            if (me != null && me.FortuneData != null)
                foreach (FortuneSaveData f in me.FortuneData) if (f != null && f.Guid != null) owned[f.Guid] = f.Level;
            float grantLevel = Mathf.Min(30, quest.QuestLevel);

            var rows = new List<Row>();
            foreach (FortuneHit h in FortuneResolver.ForQuest(quest))
            {
                float have;
                bool isOwned = owned.TryGetValue(h.Fortune.Guid.ToString(), out have);
                if (cfg.HideOwned.Value && isOwned && have >= grantLevel) { hidden++; continue; }
                string color = "FFFFFF";
                try { color = ColorUtility.ToHtmlStringRGB(GlobalSettingsManager.instance.globalSettings.GetItemQualityColor(h.Fortune.Rarity)); } catch { }
                rows.Add(new Row { Hit = h, Owned = isOwned, OwnedLevel = have, Color = color });
            }
            return rows;
        }

        private static string Header(QuestInstance quest, string hint)
        {
            return "\n\n<color=#CBB396>" + OptionsManager.Localize("Fortunes") + "</color> <color=#9AA5B1>(L"
                + Mathf.Min(30, quest.QuestLevel).ToString(CultureInfo.InvariantCulture) + " on this island" + hint + ")</color>";
        }

        private static string OwnedTag(Row r)
        {
            return " <color=#8FBF7F>(L" + r.OwnedLevel.ToString("0", CultureInfo.InvariantCulture) + ")</color>";
        }

        private static string BuildCompact(QuestInstance quest, FortunePreviewConfig cfg)
        {
            int hidden;
            List<Row> rows = Rows(quest, cfg, out hidden);
            string keyName = cfg.DetailKey.Value.MainKey.ToString().Replace("Left", "L").Replace("Right", "R");
            var sb = new StringBuilder(Header(quest, ", hold " + keyName + " for details"));
            if (rows.Count == 0)
            {
                sb.Append('\n').Append(OptionsManager.Localize("None"));
                if (hidden > 0) sb.Append(" <color=#9AA5B1>(").Append(hidden).Append(" already owned)</color>");
                return sb.ToString();
            }
            sb.Append("<size=").Append(cfg.CompactSizePercent.Value).Append("%>");
            int filtered = 0, ownedDropped = 0, listed = 0;
            // rows are sorted by rarity desc, then name
            for (int q = (int)ItemQuality.Mythic; q >= 0; q--)
            {
                var names = new List<string>();
                string color = "FFFFFF";
                foreach (Row r in rows)
                {
                    if ((int)r.Hit.Fortune.Rarity != q) continue;
                    if (r.Hit.Fortune.Rarity < cfg.MinRarity.Value) { filtered++; continue; }
                    if (cfg.CompactHideAllOwned.Value && r.Owned) { ownedDropped++; continue; } // compact = only what you still need
                    color = r.Color;
                    string n = OptionsManager.Localize(r.Hit.Fortune.StatusName);
                    if (cfg.MarkOwned.Value && r.Owned) n += OwnedTag(r);
                    names.Add(n);
                }
                if (names.Count == 0) continue;
                listed += names.Count;
                sb.Append("\n<color=#").Append(color).Append('>').Append(OptionsManager.LocalizeEnum((ItemQuality)q)).Append(":</color> ")
                  .Append(string.Join(", ", names.ToArray()));
            }
            if (listed == 0) sb.Append('\n').Append(OptionsManager.Localize("None"));
            if (filtered > 0) sb.Append("\n<color=#9AA5B1>+").Append(filtered).Append(" below ").Append(cfg.MinRarity.Value).Append("</color>");
            int ownedTotal = hidden + ownedDropped;
            if (ownedTotal > 0) sb.Append("\n<color=#9AA5B1>").Append(ownedTotal).Append(" already owned</color>");
            sb.Append("</size>");
            return sb.ToString();
        }

        private static string BuildDetailed(QuestInstance quest, FortunePreviewConfig cfg)
        {
            int hidden;
            List<Row> rows = Rows(quest, cfg, out hidden);
            var sb = new StringBuilder(Header(quest, ""));
            if (rows.Count == 0)
            {
                sb.Append('\n').Append(OptionsManager.Localize("None"));
                return sb.ToString();
            }
            int max = cfg.DetailMaxListed.Value;
            for (int i = 0; i < rows.Count && i < max; i++)
            {
                Row r = rows[i];
                sb.Append("\n<color=#").Append(r.Color).Append('>').Append(OptionsManager.Localize(r.Hit.Fortune.StatusName)).Append("</color>");
                if (cfg.MarkOwned.Value && r.Owned) sb.Append(OwnedTag(r));
                if (cfg.ShowEventNames.Value && r.Hit.RootEvent != null)
                    sb.Append(" <color=#9AA5B1>- ").Append(OptionsManager.Localize(r.Hit.RootEvent.name)).Append(r.Hit.Scripted ? " (quest)" : "").Append("</color>");
            }
            if (rows.Count > max) sb.Append("\n<color=#9AA5B1>+").Append(rows.Count - max).Append(" more</color>");
            if (hidden > 0) sb.Append("\n<color=#9AA5B1>").Append(hidden).Append(" already owned at this level</color>");
            return sb.ToString();
        }
    }
}
