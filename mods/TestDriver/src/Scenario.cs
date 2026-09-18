using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using UnityEngine;

namespace TestDriver
{
    /// <summary>
    /// Builds a standard test fight at the island's level and terrain, seeded so the same build meets the same enemies twice.
    ///   pack  : a normal 12-point battle from one enemy group with the game's usual type mix, no super-unique
    ///   elite : a hard 22-point battle of Elite/Champion enemies
    ///   boss  : one Champion made super-unique (three champion affixes) plus 22 points of Soldier/Elite escorts
    /// The debug test battle asset is cloned so the game's own SetupGame/spawn path is used unchanged.
    /// </summary>
    internal static class Scenario
    {
        public static readonly string[] Names = { "pack", "elite", "boss" };

        public static BattleInfo Build(string name, int seed, Action<string> say)
        {
            GlobalSettings gs = GlobalSettingsManager.instance.globalSettings;
            BattleInfo b = UnityEngine.Object.Instantiate(gs.testBattle);
            b.name = "Scenario-" + name;
            int lvl = Game.Instance.CurrentActiveGameLevel;
            TerrainType terrain = Game.Instance.CurrentRoomTerrainType;
            TimeOfDay time = Game.Instance.CurrentRoomTimeOfDay;
            b.mLvl = lvl; b.terrainType = terrain; b.timeOfDay = time;
            b.RandomSeed = seed;
            b.enemyMods = new Dictionary<int, List<EnemyMod>>();
            b.superEliteIndicies = new List<int>();
            b.enemyNameOverrides = new Dictionary<int, string>();
            b.battleOverrideInfo = null;
            b.guaranteedEnemies = null;
            b.dontFillOutBattlePoints = true;

            var rng = new System.Random(seed);
            List<EnemyGroup> groups = gs.GetPossibleEnemyGroups(terrain, time, lvl);
            if (groups == null || groups.Count == 0) groups = new List<EnemyGroup> { gs.GetEnemyGroup(terrain, time, lvl) };
            List<EnemyType> types; int points; bool superUnique = false;
            switch (name.ToLowerInvariant())
            {
                case "elite": types = new List<EnemyType> { EnemyType.Elite, EnemyType.Champion }; points = gs.GeneratedBattlePointTotal + gs.HardBattlePointAdder; break;
                case "boss": types = new List<EnemyType> { EnemyType.Soldier, EnemyType.Elite }; points = gs.GeneratedBattlePointTotal + gs.HardBattlePointAdder; superUnique = true; break;
                default: name = "pack"; types = new List<EnemyType> { EnemyType.Fodder, EnemyType.Soldier, EnemyType.Elite, EnemyType.Champion }; points = gs.GeneratedBattlePointTotal; break;
            }
            // pick a group (seeded order) that actually has enemies of the wanted types at this level; some groups have no elites
            var order = groups.OrderBy(g => rng.Next()).ToList();
            EnemyGroup group = order[0];
            bool found = false;
            foreach (EnemyGroup g in order)
            {
                var pool = gs.GetEnemyPool(g, lvl, types);
                if (pool != null && pool.Count > 0) { group = g; found = true; break; }
            }
            if (!found)
            {
                say("scenario: no group on this island has " + string.Join("/", types.Select(x => x.ToString()).ToArray()) + " enemies at level " + lvl + "; widening to all types");
                types = new List<EnemyType> { EnemyType.Fodder, EnemyType.Soldier, EnemyType.Elite, EnemyType.Champion };
            }

            var enemies = new List<CharacterInfo>();
            if (superUnique)
            {
                List<CharacterInfo> champs = gs.GetEnemyPool(group, lvl, new List<EnemyType> { EnemyType.Champion });
                if (champs == null || champs.Count == 0) champs = gs.GetEnemyPool(group, lvl, new List<EnemyType> { EnemyType.Elite });
                if (champs != null && champs.Count > 0) enemies.Add(champs[rng.Next(champs.Count)]);
                else say("scenario: no champion in group " + group + ", boss scenario degrades to elite escorts only");
            }
            float spent = enemies.Sum(x => x.EnemyBasePointValue);
            var fill = GameLogic.GetEnemiesByGroupAndPoints(new List<EnemyGroup> { group }, Math.Max(0, points - spent), lvl, types);
            if (fill != null) enemies.AddRange(fill);
            var extra = new List<CharacterInfo>();
            foreach (CharacterInfo ci in enemies) if (ci.SpawnsWith != null) foreach (CharacterInfo sw in ci.SpawnsWith) if (sw != null) extra.Add(sw);
            enemies.AddRange(extra);
            if (enemies.Count == 0) { say("scenario: no enemies could be generated (group " + group + ", level " + lvl + ")"); }
            b.enemies = enemies;
            b.battlePointValue = points;

            // affixes: the game's own roll for the battle, then the super-unique's three champion affixes on index 0
            try
            {
                float pct = gs.EnemyModPercentageNodes.GetMultipler(lvl);
                try { pct += GameLogic.instance.CurrentDifficulty.AdditionalEnemyModsPerc; } catch { }
                var mods = GameLogic.GetEnemyModsForBattle(b, pct, true); // ignoreSuperElite: only the boss scenario gets one, added below
                if (mods != null) b.enemyMods = mods;
            }
            catch (Exception e) { say("scenario: enemy affix roll failed (" + e.Message + "); no affixes"); b.enemyMods = new Dictionary<int, List<EnemyMod>>(); }
            if (superUnique && enemies.Count > 0)
            {
                List<EnemyMod> m;
                if (!b.enemyMods.TryGetValue(0, out m) || m == null) b.enemyMods[0] = m = new List<EnemyMod>();
                m.RemoveAll(x => x == null);
                for (int i = m.Count; i < 3; i++)
                {
                    try { EnemyMod em = GameLogic.GetEnemyMod(EnemyType.Champion, lvl, terrain, null, false, m, true); if (em != null) m.Add(em); } catch (Exception e) { say("scenario: champion affix roll failed: " + e.Message); break; }
                }
                b.superEliteIndicies.Add(0);
            }

            var desc = new List<string>();
            for (int i = 0; i < enemies.Count; i++)
            {
                List<EnemyMod> m; b.enemyMods.TryGetValue(i, out m);
                string affixes = m == null ? "" : string.Join("/", m.Where(x => x != null).Select(x => x.name).ToArray()); // the game's roll leaves null entries for "no affix"
                desc.Add(enemies[i].name + " [" + enemies[i].enemyType + (b.superEliteIndicies.Contains(i) ? ", SUPER-UNIQUE" : "") + (affixes.Length > 0 ? ": " + affixes : "") + "]");
            }
            say("SCENARIO " + name + " seed " + seed + ": level " + lvl + ", " + terrain + " " + time + ", group " + group + ", " + points + " points, " + enemies.Count + " enemies: " + string.Join(", ", desc.ToArray()));
            return b;
        }
    }
}
