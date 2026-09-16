using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Burst2Flame;
using MapNodeSystem;

namespace FortunePreview
{
    /// <summary>Reverse index: fortune -> the root (spawnable) events whose chain grants it, and the main quests each root can appear on.</summary>
    internal static class FortuneSources
    {
        private sealed class Root
        {
            public PartyEvent Event;
            public List<QuestInfo> ScriptedOn = new List<QuestInfo>(); // quests that force this event (middleEvents)
        }

        private static Dictionary<EventStatus, List<Root>> _byFortune;
        private static readonly NodeType[] EventNodeTypes = { NodeType.Event, NodeType.Mystery, NodeType.Treasure, NodeType.RestSite, NodeType.Commodity, NodeType.Shop, NodeType.NormalFight, NodeType.HardFight, NodeType.Boss };
        private static readonly EventSpawnType[] SpawnTypes = { EventSpawnType.Normal, EventSpawnType.AfterBattle, EventSpawnType.QuestStart };

        public static void ClearCache() { _byFortune = null; }

        private static void Build()
        {
            _byFortune = new Dictionary<EventStatus, List<Root>>();
            if (Game.Instance == null || Game.Instance.Events == null) return;
            var roots = new Dictionary<PartyEvent, Root>();

            // Scripted quest events first so they are tagged with their quest.
            List<QuestInfo> quests = MainQuests();
            foreach (QuestInfo q in quests)
            {
                if (q == null || q.middleEvents == null) continue;
                foreach (PartyEventByIndex pbi in q.middleEvents)
                {
                    PartyEvent ev = pbi != null ? pbi.PartyEvent as PartyEvent : null;
                    if (ev == null) continue;
                    Root r;
                    if (!roots.TryGetValue(ev, out r)) { r = new Root { Event = ev }; roots[ev] = r; }
                    if (!r.ScriptedOn.Contains(q)) r.ScriptedOn.Add(q);
                }
            }
            foreach (PartyEvent ev in Game.Instance.Events)
            {
                if (ev == null || ev.disabled || !ev.LeadsToFortune) continue;
                if (ev.EventSpawnTypes == null || (ev.EventSpawnTypes.Count == 1 && ev.EventSpawnTypes[0] == EventSpawnType.NotInEventPool)) continue;
                if (!roots.ContainsKey(ev)) roots[ev] = new Root { Event = ev };
            }
            foreach (Root r in roots.Values)
            {
                var found = new List<EventStatus>();
                Walk(r.Event, found, new HashSet<PartyEvent>(), 0);
                foreach (EventStatus f in found)
                {
                    List<Root> list;
                    if (!_byFortune.TryGetValue(f, out list)) { list = new List<Root>(); _byFortune[f] = list; }
                    if (!list.Contains(r)) list.Add(r);
                }
            }
        }

        private static void Walk(PartyEvent ev, List<EventStatus> outp, HashSet<PartyEvent> visited, int depth)
        {
            if (ev == null || depth > 8 || !visited.Add(ev) || ev.eventOptions == null) return;
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
                                    if (s != null && s.StatusType == StatusType.Fortune && !outp.Contains(s)) outp.Add(s);
                    if (act.chainedEvent != null) Walk(act.chainedEvent, outp, visited, depth + 1);
                    if (act.startBattle)
                    {
                        if (act.manuallySetBattle && act.battle != null)
                        {
                            Walk(act.battle.victoryEvent, outp, visited, depth + 1);
                            Walk(act.battle.failureEvent, outp, visited, depth + 1);
                        }
                        else if (act.battleOverrideInfo != null)
                        {
                            Walk(act.battleOverrideInfo.VictoryEvent, outp, visited, depth + 1);
                            Walk(act.battleOverrideInfo.FailureEvent, outp, visited, depth + 1);
                        }
                    }
                }
            }
        }

        private static List<QuestInfo> MainQuests()
        {
            try
            {
                var ms = GlobalSettingsManager.instance != null ? GlobalSettingsManager.instance.miscSettings : null;
                if (ms != null && ms.ReleaseMainQuests != null) return ms.ReleaseMainQuests.Where(q => q != null).ToList();
            }
            catch { }
            return new List<QuestInfo>();
        }

        private static bool CanAppearOn(PartyEvent ev, QuestInfo q)
        {
            var tt = new TerrainAndTime { TerrainType = q.terrainType, TimeOfDay = q.timeOfDay };
            var none = new List<ActionStatus>();
            foreach (EventSpawnType st in SpawnTypes)
            {
                if (ev.EventSpawnTypes == null || !ev.EventSpawnTypes.Contains(st)) continue;
                foreach (NodeType nt in EventNodeTypes)
                {
                    try { if (ev.CanSpawn(tt, nt, none, st, q.questLevel)) return true; } catch { }
                }
            }
            return false;
        }

        /// <summary>Tooltip text: where this fortune comes from. Null if unknown.</summary>
        public static string Describe(EventStatus fortune, FortunePreviewConfig cfg)
        {
            if (fortune == null) return null;
            if (_byFortune == null) Build();
            List<Root> roots;
            if (!_byFortune.TryGetValue(fortune, out roots) || roots.Count == 0) return null;

            List<QuestInfo> quests = MainQuests();
            var sb = new StringBuilder();
            sb.Append("\n\n<color=#CBB396>").Append(OptionsManager.Localize("Source")).Append("</color>");
            int shownRoots = 0;
            foreach (Root r in roots)
            {
                if (shownRoots++ >= 3) { sb.Append("\n<color=#9AA5B1>+").Append(roots.Count - 3).Append(" more sources</color>"); break; }
                PartyEvent ev = r.Event;
                sb.Append("\n<color=#FFFFFF>").Append(OptionsManager.Localize("Where")).Append(":</color> <color=#9AA5B1>");
                var where = new List<string>();
                if (ev.allowOnAllTerrainTypes || ev.allowedTerrainTypes == null || ev.allowedTerrainTypes.Length == 0) where.Add(OptionsManager.Localize("any terrain"));
                else where.Add(string.Join("/", ev.allowedTerrainTypes.Select(t => OptionsManager.LocalizeEnum(t)).ToArray()));
                if (!ev.allowAtAllTimesOfDay) where.Add(OptionsManager.LocalizeEnum(ev.allowedTimeOfDay));
                if (!ev.allowAtAllLevels && ev.minLevel > 1) where.Add("L" + ev.minLevel + "+");
                sb.Append(string.Join(" · ", where.ToArray())).Append("</color>");

                var qnames = new List<string>();
                foreach (QuestInfo q in r.ScriptedOn) qnames.Add(QuestLabel(q) + " (scripted)");
                foreach (QuestInfo q in quests)
                    if (!r.ScriptedOn.Contains(q) && CanAppearOn(ev, q)) qnames.Add(QuestLabel(q));
                if (qnames.Count > 0)
                {
                    int max = cfg.SourceMaxQuests.Value;
                    sb.Append("\n<size=85%><color=#9AA5B1>").Append(OptionsManager.Localize("Quests")).Append(": </color>");
                    sb.Append(string.Join(", ", qnames.Take(max).ToArray()));
                    if (qnames.Count > max) sb.Append(" <color=#9AA5B1>+").Append(qnames.Count - max).Append(" more</color>");
                    sb.Append("</size>");
                }
            }
            return sb.ToString();
        }

        private static string QuestLabel(QuestInfo q)
        {
            string n = OptionsManager.Localize(string.IsNullOrEmpty(q.questName) ? q.name : q.questName);
            return n + " <color=#9AA5B1>(A" + q.act + " L" + q.questLevel + ")</color>";
        }
    }
}
