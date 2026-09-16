using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using MapNodeSystem;

namespace FortunePreview
{
    /// <summary>One fortune that can be earned on an island, and the root (spawnable) event that starts the chain.</summary>
    internal sealed class FortuneHit
    {
        public EventStatus Fortune;
        public PartyEvent RootEvent;
        public bool Scripted; // came from the quest's own middleEvents rather than the random pool
    }

    /// <summary>
    /// Mirrors the game's own logic: WorldMapGenerator picks island events with PartyEvent.CanSpawn(terrain/time, nodeType,
    /// statuses, spawnType, level), and PartyEvent.LeadsToFortuneHelper walks option -> action -> effects.eventStatuses plus
    /// chainedEvent and battle victory/failure events. We walk the same chain but collect the fortunes instead of a bool.
    /// </summary>
    internal static class FortuneResolver
    {
        private static readonly Dictionary<string, List<FortuneHit>> _cache = new Dictionary<string, List<FortuneHit>>();
        private static readonly NodeType[] EventNodeTypes = { NodeType.Event, NodeType.Mystery, NodeType.Treasure, NodeType.RestSite, NodeType.Commodity, NodeType.Shop, NodeType.NormalFight, NodeType.HardFight, NodeType.Boss };
        private static readonly EventSpawnType[] SpawnTypes = { EventSpawnType.Normal, EventSpawnType.AfterBattle, EventSpawnType.QuestStart };

        public static void ClearCache() { _cache.Clear(); }

        public static List<FortuneHit> ForQuest(QuestInstance quest)
        {
            if (quest == null || Game.Instance == null || Game.Instance.Events == null) return new List<FortuneHit>();
            string key = quest.TerrainType + "|" + quest.TimeOfDay + "|" + quest.QuestLevel + "|" + (quest.MainQuestInfo != null ? quest.MainQuestInfo.name : "-") + "|" + Game.Instance.RoguelikeModeActive;
            List<FortuneHit> hits;
            if (_cache.TryGetValue(key, out hits)) return hits;

            hits = new List<FortuneHit>();
            var seenFortunes = new HashSet<EventStatus>();
            var tt = new TerrainAndTime { TerrainType = quest.TerrainType, TimeOfDay = quest.TimeOfDay };
            var noStatuses = new List<ActionStatus>();
            int level = quest.QuestLevel;

            // 1. Scripted events of the quest itself (main quests).
            if (quest.MainQuestInfo != null && quest.MainQuestInfo.middleEvents != null)
            {
                foreach (PartyEventByIndex pbi in quest.MainQuestInfo.middleEvents)
                {
                    PartyEvent ev = pbi != null ? pbi.PartyEvent as PartyEvent : null;
                    if (ev != null) Collect(ev, ev, true, hits, seenFortunes);
                }
            }

            // 2. Random island events that can spawn on this island and lead to a fortune.
            foreach (PartyEvent ev in Game.Instance.Events)
            {
                if (ev == null || ev.disabled || !ev.LeadsToFortune) continue;
                if (ev.EventSpawnTypes == null || (ev.EventSpawnTypes.Count == 1 && ev.EventSpawnTypes[0] == EventSpawnType.NotInEventPool)) continue; // chained-only
                bool can = false;
                foreach (EventSpawnType st in SpawnTypes)
                {
                    if (!ev.EventSpawnTypes.Contains(st)) continue;
                    foreach (NodeType nt in EventNodeTypes)
                    {
                        try { if (ev.CanSpawn(tt, nt, noStatuses, st, level)) { can = true; break; } }
                        catch { }
                    }
                    if (can) break;
                }
                if (!can) continue;
                Collect(ev, ev, false, hits, seenFortunes);
            }

            hits.Sort((a, b) =>
            {
                int r = b.Fortune.Rarity.CompareTo(a.Fortune.Rarity);
                return r != 0 ? r : string.Compare(a.Fortune.StatusName, b.Fortune.StatusName, StringComparison.OrdinalIgnoreCase);
            });
            _cache[key] = hits;
            if (FortunePreviewPlugin.Cfg.Verbose.Value)
            {
                FortunePreviewPlugin.Log.LogInfo("Quest " + key + ": " + hits.Count + " fortunes");
                foreach (FortuneHit h in hits) FortunePreviewPlugin.Log.LogInfo("  " + h.Fortune.StatusName + " (" + h.Fortune.Rarity + ") <- " + h.RootEvent.name + (h.Scripted ? " [scripted]" : ""));
            }
            return hits;
        }

        private static void Collect(PartyEvent ev, PartyEvent root, bool scripted, List<FortuneHit> hits, HashSet<EventStatus> seen)
        {
            var visited = new HashSet<PartyEvent>();
            Walk(ev, root, scripted, hits, seen, visited, 0);
        }

        private static void Walk(PartyEvent ev, PartyEvent root, bool scripted, List<FortuneHit> hits, HashSet<EventStatus> seen, HashSet<PartyEvent> visited, int depth)
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
                    {
                        foreach (EventActionEffects fx in effects)
                        {
                            if (fx == null || fx.eventStatuses == null) continue;
                            foreach (EventStatus st in fx.eventStatuses)
                            {
                                if (st == null || st.StatusType != StatusType.Fortune) continue;
                                if (seen.Add(st)) hits.Add(new FortuneHit { Fortune = st, RootEvent = root, Scripted = scripted });
                            }
                        }
                    }
                    if (act.chainedEvent != null) Walk(act.chainedEvent, root, scripted, hits, seen, visited, depth + 1);
                    if (act.startBattle)
                    {
                        if (act.manuallySetBattle && act.battle != null)
                        {
                            Walk(act.battle.victoryEvent, root, scripted, hits, seen, visited, depth + 1);
                            Walk(act.battle.failureEvent, root, scripted, hits, seen, visited, depth + 1);
                        }
                        else if (act.battleOverrideInfo != null)
                        {
                            Walk(act.battleOverrideInfo.VictoryEvent, root, scripted, hits, seen, visited, depth + 1);
                            Walk(act.battleOverrideInfo.FailureEvent, root, scripted, hits, seen, visited, depth + 1);
                        }
                    }
                }
            }
        }
    }
}
