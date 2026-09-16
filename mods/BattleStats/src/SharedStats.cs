using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using UnityEngine;

namespace BattleStats
{
    /// <summary>
    /// The extra numbers live in the game's own per-character battle-stat dictionary (Root.BattleStats ->
    /// CharacterBattleStats.BattleStats, an observable int->float map keyed by BattleStat) under keys the game never
    /// uses. The dictionary is replicated to every client exactly like the vanilla stats, so a client running this mod
    /// sees the host's numbers; a client without it ignores the keys. Only the host writes (damage resolves there).
    /// Skills are identified by index into Game.Instance.Actions / Game.Instance.ActionStatuses (same asset order on
    /// every machine); statuses get +STATUS_FLAG.
    /// </summary>
    internal static class SharedStats
    {
        public const int Direct = 1000, Ticks = 1001, Tiles = 1002;
        public const int Hits = 1010, Crits = 1011, Kills = 1012, Overkill = 1013, BiggestHit = 1014, BiggestHitSource = 1015, BestSkillSource = 1016, BestSkillDamage = 1017;
        public const int Casts = 1020, FreeActions = 1021, ManaSpent = 1022, HexesMoved = 1023, TakenFromTicks = 1024, Turns = 1025;
        public const int ElementBase = 1100;      // + (int)DamageType
        public const int TopSource = 1200, TopDamage = 1220, TopHits = 1240, TopCount = 8;
        public const int STATUS_FLAG = 100000;
        public const int NONE = -1;

        // Per-section breakdown: damage and hit count per (bucket, source), so hovering a cell can list the abilities.
        // key = BucketBase + bucket * BucketSpan + code (codes are < BucketSpan: action index, or STATUS_FLAG + status index).
        public const int BucketBase = 2000000, BucketHitsBase = 6000000, BucketSpan = 200000;
        public const int B_Direct = 0, B_Ticks = 1, B_Tiles = 2, B_Summons = 3, B_Thorns = 4, B_ElementBase = 10, B_Untyped = 20, B_Dealt = 98, B_All = 99;

        public static void AddBucket(Character c, int bucket, int code, float damage)
        {
            if (c == null || code == NONE || code < 0 || code >= BucketSpan || bucket < 0 || bucket > 99 || damage == 0f) return;
            Add(c, BucketBase + bucket * BucketSpan + code, damage);
            Add(c, BucketHitsBase + bucket * BucketSpan + code, 1f);
        }

        public sealed class SourceLine { public string Name; public float Damage; public int Hits; }

        /// <summary>Client-side: the abilities behind one bucket for a character, biggest first.</summary>
        public static List<SourceLine> Breakdown(Character c, int bucket)
        {
            var list = new List<SourceLine>();
            try
            {
                CharacterBattleStats cbs = EntryFor(c, false);
                if (cbs == null || cbs.BattleStats == null) return list;
                int dmgLo = BucketBase + bucket * BucketSpan, dmgHi = dmgLo + BucketSpan;
                int hitLo = BucketHitsBase + bucket * BucketSpan;
                var hits = new Dictionary<int, float>();
                var dmg = new Dictionary<int, float>();
                foreach (var kv in cbs.BattleStats)
                {
                    if (kv.Key >= dmgLo && kv.Key < dmgHi) dmg[kv.Key - dmgLo] = kv.Value;
                    else if (kv.Key >= hitLo && kv.Key < hitLo + BucketSpan) hits[kv.Key - hitLo] = kv.Value;
                }
                foreach (var kv in dmg)
                {
                    float h; hits.TryGetValue(kv.Key, out h);
                    list.Add(new SourceLine { Name = SourceName(kv.Key), Damage = kv.Value, Hits = (int)h });
                }
                list.Sort((a, b) => b.Damage.CompareTo(a.Damage));
            }
            catch { }
            return list;
        }

        // host-side per-character per-source totals, used to keep the top list current
        private sealed class SourceTotal { public int Code; public float Damage; public int Hits; }
        private static readonly Dictionary<Character, Dictionary<int, SourceTotal>> _sources = new Dictionary<Character, Dictionary<int, SourceTotal>>();

        internal static void Reset() { _sources.Clear(); }

        private static CharacterBattleStats EntryFor(Character c, bool create)
        {
            try
            {
                Root root = NetworkingManager.Instance != null && NetworkingManager.Instance.NetworkManager != null ? NetworkingManager.Instance.NetworkManager.Root : null;
                if (root == null || root.BattleStats == null || c == null) return null;
                foreach (CharacterBattleStats cbs in root.BattleStats) if (cbs != null && cbs.Character == c) return cbs;
                if (!create) return null;
                CharacterBattleStats made = root.CreateNewCharacterBattleStatSet();
                made.Character = c;
                root.BattleStats.Add(made);
                return made;
            }
            catch { return null; }
        }

        public static float Get(Character c, int key)
        {
            try
            {
                CharacterBattleStats cbs = EntryFor(c, false);
                if (cbs == null || cbs.BattleStats == null || !cbs.BattleStats.ContainsKey(key)) return 0f;
                return cbs.BattleStats[key];
            }
            catch { return 0f; }
        }

        private static void Set(Character c, int key, float v)
        {
            CharacterBattleStats cbs = EntryFor(c, true);
            if (cbs == null || cbs.BattleStats == null) return;
            if (cbs.BattleStats.ContainsKey(key)) { if (cbs.BattleStats[key] != v) cbs.BattleStats[key] = v; }
            else cbs.BattleStats[key] = v;
        }

        public static void Add(Character c, int key, float v) { if (v != 0f) Set(c, key, Get(c, key) + v); }
        public static void Max(Character c, int key, float v) { if (v > Get(c, key)) Set(c, key, v); }
        public static void Put(Character c, int key, float v) { Set(c, key, v); }

        /// <summary>Encode a damage source: an action by index, or a status by index + STATUS_FLAG.</summary>
        public static int Code(ActionInfo action, ActionStatusInfo status)
        {
            try
            {
                if (status != null && Game.Instance != null && Game.Instance.ActionStatuses != null) { int i = Game.Instance.ActionStatuses.IndexOf(status); if (i >= 0) return STATUS_FLAG + i; }
                if (action != null) { int i = action.ActionIndex; if (i >= 0) return i; }
            }
            catch { }
            return NONE;
        }

        public static string SourceName(int code)
        {
            try
            {
                if (code == NONE) return "?";
                if (code >= STATUS_FLAG)
                {
                    int i = code - STATUS_FLAG;
                    var list = Game.Instance.ActionStatuses;
                    if (list != null && i < list.Count && list[i] != null) return (Loc(list[i].Name) ?? list[i].name) + " (tick)";
                    return "status " + i;
                }
                var acts = Game.Instance.Actions;
                if (acts != null && code < acts.Count && acts[code] != null) return Loc(acts[code].ActionName) ?? acts[code].name;
                return "action " + code;
            }
            catch { return "?"; }
        }

        private static string Loc(string key) { try { return string.IsNullOrEmpty(key) ? null : OptionsManager.Localize(key); } catch { return key; } }

        /// <summary>Record one damage credit for the per-source top list and push the top entries into the dictionary.</summary>
        public static void RecordSource(Character c, int code, float damage)
        {
            if (c == null || code == NONE) return;
            Dictionary<int, SourceTotal> mine;
            if (!_sources.TryGetValue(c, out mine)) _sources[c] = mine = new Dictionary<int, SourceTotal>();
            SourceTotal st;
            if (!mine.TryGetValue(code, out st)) mine[code] = st = new SourceTotal { Code = code };
            st.Damage += damage; st.Hits++;
            var top = mine.Values.OrderByDescending(s => s.Damage).Take(TopCount).ToList();
            for (int i = 0; i < TopCount; i++)
            {
                if (i < top.Count) { Put(c, TopSource + i, top[i].Code); Put(c, TopDamage + i, top[i].Damage); Put(c, TopHits + i, top[i].Hits); }
            }
            if (top.Count > 0) { Put(c, BestSkillSource, top[0].Code); Put(c, BestSkillDamage, top[0].Damage); }
        }

        /// <summary>Client-side: the top list for a character as (name, damage, hits).</summary>
        public static List<Tuple<string, float, int>> TopList(Character c)
        {
            var list = new List<Tuple<string, float, int>>();
            for (int i = 0; i < TopCount; i++)
            {
                float code = Get(c, TopSource + i);
                float dmg = Get(c, TopDamage + i);
                if (dmg <= 0f) continue;
                list.Add(Tuple.Create(SourceName((int)code), dmg, (int)Get(c, TopHits + i)));
            }
            return list;
        }
    }
}
