using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FortunePreview.Patches
{
    /// <summary>
    /// The event window shows each option's outcome text via EventOptionSelectionItem.PopulateResultEffects, which fills
    /// SingleActionEffectText (plain options) or SuccessEffectText / FailureEffectText (dice-roll options) using the game's
    /// GetActionDetails. That text already names statuses granted directly by a VISIBLE outcome (with hover links), so we
    /// only add what the player cannot otherwise see:
    ///   - fortunes behind hidden effect text or a mystery roll (RevealHidden),
    ///   - fortunes that come later in the chain: a follow-up event, or the victory/failure event of a fight (ShowChained).
    /// Names are TMP links registered with TextLinkManager, so hovering them opens the game's own fortune tooltip.
    /// </summary>
    internal static class EventOptionPatches
    {
        [HarmonyPatch(typeof(EventOptionSelectionItem), nameof(EventOptionSelectionItem.PopulateResultEffects))]
        private static class EventOptionSelectionItem_PopulateResultEffects
        {
            private static void Postfix(EventOptionSelectionItem __instance, PartyEvent partyEvent, EventOption eventOption)
            {
                try
                {
                    FortunePreviewConfig cfg = FortunePreviewPlugin.Cfg;
                    if (cfg == null || !cfg.EventWindow.Value || eventOption == null || eventOption.eventActions == null || partyEvent == null) return;
                    bool hidden = eventOption.hideOptionEffectText;
                    if (!__instance.IsRoll)
                    {
                        EventAction a = eventOption.eventActions.FirstOrDefault();
                        Annotate(__instance.SingleActionEffectText, a, !hidden, cfg, partyEvent, eventOption, "");
                    }
                    else
                    {
                        bool mystery = eventOption.mysteryRollValue;
                        EventAction s = eventOption.eventActions.FirstOrDefault(x => x != null && x.eventResultSetting != null && x.eventResultSetting.EventResultType == EventResultType.Success);
                        EventAction f = eventOption.eventActions.FirstOrDefault(x => x != null && x.eventResultSetting != null && x.eventResultSetting.EventResultType == EventResultType.Failure);
                        Annotate(__instance.SuccessEffectText, s, !hidden && !mystery, cfg, partyEvent, eventOption, "success");
                        Annotate(__instance.FailureEffectText, f, !hidden && !mystery, cfg, partyEvent, eventOption, "failure");
                    }
                }
                catch (Exception e)
                {
                    if (FortunePreviewPlugin.Cfg != null && FortunePreviewPlugin.Cfg.Verbose.Value) FortunePreviewPlugin.Log.LogWarning("Event option fortune annotate failed: " + e);
                }
            }
        }

        private sealed class Found
        {
            public EventStatus Fortune;
            public string When;   // "" = this outcome; "after winning" / "after losing" / "next event"
        }

        private static void Annotate(EventResultEffect ui, EventAction action, bool outcomeVisible, FortunePreviewConfig cfg, PartyEvent partyEvent, EventOption option, string which)
        {
            if (ui == null || ui.EffectText == null || action == null) return;
            var direct = new List<Found>();
            var later = new List<Found>();
            var seen = new HashSet<EventStatus>();

            // 1. Direct grants of this outcome.
            string directNote = "";
            List<EventActionEffects> effects = null;
            try { effects = action.GetEventActionEffects(); } catch { }
            if (effects != null)
            {
                foreach (EventActionEffects fx in effects)
                {
                    if (fx == null || fx.eventStatuses == null) continue;
                    List<EventStatus> pool = fx.eventStatuses.Where(s => s != null && s.StatusType == StatusType.Fortune).ToList();
                    if (pool.Count == 0) continue;
                    bool randomPick = fx.pickRandomStatus && fx.eventStatuses.Count > fx.randomStatusCount;
                    List<EventStatus> rolled = fx.rolledEventStatuses != null ? fx.rolledEventStatuses.Where(s => s != null && s.StatusType == StatusType.Fortune).ToList() : null;
                    List<EventStatus> show = (randomPick && cfg.RevealRolled.Value && rolled != null && rolled.Count > 0) ? rolled : pool;
                    if (randomPick && !(cfg.RevealRolled.Value && rolled != null && rolled.Count > 0)) directNote = " (one of)";
                    foreach (EventStatus s in show) if (seen.Add(s)) direct.Add(new Found { Fortune = s, When = "" });
                }
            }

            // 2. Follow-ups: chained event, fight outcomes (and deeper).
            if (cfg.ShowChained.Value)
            {
                var visited = new HashSet<PartyEvent>();
                if (action.chainedEvent != null) Walk(action.chainedEvent, "next event", later, seen, visited, 0);
                if (action.startBattle)
                {
                    if (action.manuallySetBattle && action.battle != null)
                    {
                        Walk(action.battle.victoryEvent, "after winning", later, seen, visited, 0);
                        Walk(action.battle.failureEvent, "after losing", later, seen, visited, 0);
                    }
                    else if (action.battleOverrideInfo != null)
                    {
                        Walk(action.battleOverrideInfo.VictoryEvent, "after winning", later, seen, visited, 0);
                        Walk(action.battleOverrideInfo.FailureEvent, "after losing", later, seen, visited, 0);
                    }
                }
            }

            // The game already names direct grants when the outcome text is visible.
            bool showDirect = direct.Count > 0 && !outcomeVisible && cfg.RevealHidden.Value;
            if (!showDirect && later.Count == 0) return;

            TextMeshProUGUI tmp = ui.EffectText;
            var sb = new StringBuilder();
            if (showDirect)
            {
                sb.Append("\n<size=").Append(cfg.EventSizePercent.Value).Append("%><color=#CBB396>").Append(OptionsManager.Localize("Fortune")).Append(directNote).Append(":</color> ");
                sb.Append(string.Join(", ", direct.Select(d => Link(tmp, d.Fortune)).ToArray())).Append("</size>");
            }
            if (later.Count > 0)
            {
                sb.Append("\n<size=").Append(cfg.EventSizePercent.Value).Append("%><color=#CBB396>").Append(OptionsManager.Localize("Fortune")).Append(" later:</color> ");
                sb.Append(string.Join(", ", later.Select(d => Link(tmp, d.Fortune) + " <color=#9AA5B1>(" + d.When + ")</color>").ToArray())).Append("</size>");
            }
            string add = sb.ToString();
            if (add.Length == 0) return;
            if (string.IsNullOrEmpty(tmp.text)) add = add.TrimStart('\n');
            tmp.text += add;
            ui.gameObject.TweenShow();
            if (cfg.Verbose.Value)
                FortunePreviewPlugin.Log.LogInfo("Event '" + partyEvent.name + "' option '" + OptionsManager.Localize(option.title) + "' " + which + ": direct " + direct.Count + (showDirect ? "" : " (already visible)") + ", later " + later.Count);
        }

        private static void Walk(PartyEvent ev, string when, List<Found> outp, HashSet<EventStatus> seen, HashSet<PartyEvent> visited, int depth)
        {
            if (ev == null || depth > 6 || !visited.Add(ev) || ev.eventOptions == null) return;
            foreach (EventOption opt in ev.eventOptions)
            {
                if (opt == null || opt.eventActions == null) continue;
                foreach (EventAction act in opt.eventActions)
                {
                    if (act == null) continue;
                    List<EventActionEffects> effects = null;
                    try { effects = act.GetEventActionEffects(); } catch { }
                    if (effects != null)
                        foreach (EventActionEffects fx in effects)
                            if (fx != null && fx.eventStatuses != null)
                                foreach (EventStatus s in fx.eventStatuses)
                                    if (s != null && s.StatusType == StatusType.Fortune && seen.Add(s)) outp.Add(new Found { Fortune = s, When = when });
                    if (act.chainedEvent != null) Walk(act.chainedEvent, when, outp, seen, visited, depth + 1);
                    if (act.startBattle)
                    {
                        if (act.manuallySetBattle && act.battle != null)
                        {
                            Walk(act.battle.victoryEvent, when, outp, seen, visited, depth + 1);
                            Walk(act.battle.failureEvent, when, outp, seen, visited, depth + 1);
                        }
                        else if (act.battleOverrideInfo != null)
                        {
                            Walk(act.battleOverrideInfo.VictoryEvent, when, outp, seen, visited, depth + 1);
                            Walk(act.battleOverrideInfo.FailureEvent, when, outp, seen, visited, depth + 1);
                        }
                    }
                }
            }
        }

        /// <summary>Rarity-coloured name wrapped in a TMP link registered with the game's TextLinkManager (hover = fortune tooltip).</summary>
        private static string Link(TextMeshProUGUI tmp, EventStatus fortune)
        {
            string color = "FFFFFF";
            try { color = ColorUtility.ToHtmlStringRGB(GlobalSettingsManager.instance.globalSettings.GetItemQualityColor(fortune.Rarity)); } catch { }
            string name = OptionsManager.Localize(fortune.StatusName);
            TextLinkManager mgr = TextLinkManager.Instance;
            if (mgr == null || tmp == null) return "<color=#" + color + ">[" + name + "]</color>";
            string id = mgr.GetLinkID().ToString();
            mgr.linkDict[new TextLinkSet { textComp = tmp, index = id }] = fortune;
            return "<link=" + id + "><color=#" + color + ">[" + name + "]</color></link>";
        }
    }
}
