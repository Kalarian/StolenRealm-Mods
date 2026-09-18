using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Burst2Flame;

namespace BattleStats
{
    /// <summary>
    /// The run-wide totals: every battle since the party left town, folded together per character. The store mirrors
    /// the whole key space of the game's replicated per-battle dictionary (Root.BattleStats: the 12 vanilla BattleStat
    /// keys plus everything SharedStats writes), so the Stats window can be shown with run numbers by pointing its
    /// value lookups here instead of at the live dictionary. Keyed by character name (Character objects are reset on
    /// town return and can be recreated), with the owner id in front so two co-op players with the same name stay apart.
    ///
    /// Merge rules: every key is a sum except the biggest-hit pair (max, copied together) and the derived keys (best
    /// skill, the top-8 source list), which are recomputed from the summed per-source damage bucket after each fold.
    /// The fold happens once per battle end (RunStatsPatches.OnBattleEnded); a client whose replicated dictionary may
    /// still be catching up folds lazily (EnsureFolded) the next time the numbers are needed.
    /// </summary>
    internal static class RunStats
    {
        private sealed class Meta { public string Name; public string Owner; public int Level; public int Order; }

        private static readonly Dictionary<string, Dictionary<int, float>> _byChar = new Dictionary<string, Dictionary<int, float>>();
        private static readonly Dictionary<string, Meta> _meta = new Dictionary<string, Meta>();
        private static readonly Dictionary<int, float> _empty = new Dictionary<int, float>();
        public static int Battles;             // battles folded into the store this run
        private static int _endedSeq, _foldedSeq;  // battles ended vs folded: a fold is due while they differ
        public static int Ended => _endedSeq;
        public static bool FoldPending => _foldedSeq < _endedSeq;

        // what the saved file needs beyond the numbers
        public static DateTime? Started;                                    // first fold of this run
        public static readonly List<RunHistory.Fight> Fights = new List<RunHistory.Fight>();
        public static string FilePath;                                      // the run's file on disk, once saved
        private static string _quest, _difficulty, _mode; private static int _act;
        private static string _questId;                                     // fingerprint of the quest being played (see QuestId)
        private static int _area;                                           // Roguelike only: how deep the run has gone
        public static bool Finished;                                        // the run is over; its file is closed and never resumed

        public static string Key(Character c)
        {
            if (c == null) return "?";
            string owner = ""; try { owner = c.OwnerID.ToString(); } catch { }
            return owner + "|" + c.CharacterName;
        }

        public static IEnumerable<string> Names => _byChar.Keys;

        public static Dictionary<int, float> Store(Character c)
        {
            Dictionary<int, float> d;
            return _byChar.TryGetValue(Key(c), out d) ? d : _empty;
        }

        public static float Get(Character c, int key)
        {
            float v; return Store(c).TryGetValue(key, out v) ? v : 0f;
        }

        /// <summary>True when any character has any of the mod's own keys (a host without the mod never writes them).</summary>
        public static bool HasModKeys()
        {
            foreach (var d in _byChar.Values) foreach (var kv in d) if (kv.Key >= 1000) return true;
            return false;
        }

        public static bool HasAnything()
        {
            foreach (var d in _byChar.Values) if (d.Count > 0) return true;
            return false;
        }

        public static void Clear(string reason)
        {
            bool had = _byChar.Count > 0 || _endedSeq > 0;
            _byChar.Clear(); _meta.Clear(); Battles = 0; _endedSeq = 0; _foldedSeq = 0;
            Started = null; Fights.Clear(); FilePath = null; _quest = _difficulty = _mode = null; _act = 0; _questId = null; _area = 0; Finished = false;
            if (had && BattleStatsPlugin.Cfg != null && BattleStatsPlugin.Cfg.Verbose.Value) BattleStatsPlugin.Log.LogInfo("Run stats: reset (" + reason + ")");
        }

        /// <summary>A battle just ended on this machine; the fold follows (now on the host, lazily on a client).</summary>
        public static void BattleEnded(bool victory, int turns)
        {
            _endedSeq++;
            Fights.Add(new RunHistory.Fight { Index = _endedSeq, Victory = victory, Turns = turns });
            CaptureContext();
        }

        /// <summary>Where this run is being played, for the saved file's headline (read once, kept).</summary>
        private static void CaptureContext()
        {
            try
            {
                Root root = LiveRoot();
                if (root == null) return;
                bool rogue = root.PlayingRoguelike;
                if (_mode == null) _mode = rogue ? "Roguelike" : "Campaign";
                if (_difficulty == null)
                {
                    var ds = GlobalSettingsManager.instance != null ? GlobalSettingsManager.instance.difficultySettings : null;
                    var list = ds == null ? null : (rogue ? ds.DifficultiesRoguelike : ds.Difficulties);
                    int i = root.CurrentDifficultyIndex;
                    _difficulty = list != null && i >= 0 && i < list.Count && list[i] != null ? list[i].DifficultyName : "difficulty " + i;
                }
                if (rogue)
                {
                    // no quests in a Roguelike run: the run is identified by the end boss it is heading for, and how deep it has gone
                    if (string.IsNullOrEmpty(_questId)) _questId = CurrentQuestId();
                    ObservableQuestData q = QuestDataOf(root);
                    if (q != null) { try { _area = Math.Max(_area, q.CurrentRoguelikeLevelIndex + 1); } catch { } }
                }
                else if (QuestManager.instance != null && QuestManager.instance.CurrentQuest != null)
                {
                    if (string.IsNullOrEmpty(_quest)) _quest = QuestName(QuestManager.instance.CurrentQuest);
                    if (string.IsNullOrEmpty(_questId)) _questId = QuestId(QuestManager.instance.CurrentQuest);
                    try { if (_act == 0) _act = QuestManager.instance.CurrentAct + 1; } catch { }
                }
            }
            catch { }
        }

        /// <summary>Fold the live dictionary into the store if a battle ended and was not folded yet.</summary>
        public static bool EnsureFolded(string why)
        {
            return FoldPending && Fold(why);
        }

        /// <summary>Add the battle that just ended to the run. False when it credited nobody anything, in which case it
        /// is not counted at all: the game shows the post-battle screen in places where no battle of ours was fought
        /// (loading back onto an island after a defeat, for one), and an empty battle is not part of the run.</summary>
        public static bool Fold(string why)
        {
            Root root = LiveRoot();
            int chars = 0;
            // the game keeps a stat entry per character it credited, enemies and summons included; only the party has a column
            var party = new HashSet<Character>();
            try { if (NetworkingManager.Instance != null && NetworkingManager.Instance.PartyCharacters != null) foreach (Character c in NetworkingManager.Instance.PartyCharacters) if (c != null) party.Add(c); } catch { }
            if (root != null && root.BattleStats != null)
            {
                foreach (CharacterBattleStats cbs in root.BattleStats)
                {
                    if (cbs == null || cbs.Character == null || cbs.BattleStats == null) continue;
                    if (party.Count > 0 && !party.Contains(cbs.Character)) continue;
                    if (string.IsNullOrEmpty(cbs.Character.CharacterName)) continue;
                    var snap = new Dictionary<int, float>(cbs.BattleStats);
                    if (snap.Count == 0) continue;
                    string key = Key(cbs.Character);
                    Dictionary<int, float> run;
                    if (!_byChar.TryGetValue(key, out run)) _byChar[key] = run = new Dictionary<int, float>();
                    MergeInto(run, snap);
                    if (!_meta.ContainsKey(key))
                    {
                        var m = new Meta { Name = cbs.Character.CharacterName, Owner = "", Level = 0, Order = _meta.Count };
                        try { m.Owner = cbs.Character.OwnerID.ToString(); } catch { }
                        try { m.Level = cbs.Character.Level; } catch { }
                        _meta[key] = m;
                    }
                    chars++;
                    if (BattleStatsPlugin.Cfg != null && BattleStatsPlugin.Cfg.Verbose.Value)
                    {
                        float dealt, taken, healed; run.TryGetValue((int)BattleStat.DamageDealt, out dealt); run.TryGetValue((int)BattleStat.DamageTaken, out taken); run.TryGetValue((int)BattleStat.HealingAdministered, out healed);
                        BattleStatsPlugin.Log.LogInfo("Run stats: folded battle " + (Battles + 1) + " (" + why + "): " + cbs.Character.CharacterName + " DamageDealt=" + dealt.ToString("0") + " DamageTaken=" + taken.ToString("0") + " HealingAdministered=" + healed.ToString("0") + " keys=" + run.Count + " " + Describe(run));
                    }
                }
            }
            if (chars == 0)
            {
                // take the ended battle back: it was never one of ours
                if (Fights.Count > 0) Fights.RemoveAt(Fights.Count - 1);
                if (_endedSeq > 0) _endedSeq--;
                _foldedSeq = _endedSeq;
                BattleStatsPlugin.Log.LogInfo("Run stats: a battle ended (" + why + ") with nothing credited to the party, so it is not counted in the run");
                return false;
            }
            Battles++;
            _foldedSeq = _endedSeq;
            if (Started == null) Started = DateTime.Now;
            return true;
        }

        /// <summary>A quest's name for the history list. Generated names are built from templates with [tokens] the game
        /// fills in later; a name still holding one (the town's "[townQuestName]") says nothing, so it is dropped and the
        /// quest level is shown instead. An empty result lets a later battle of the same run supply a better one.</summary>
        private static string QuestName(QuestInstance q)
        {
            string name = null;
            try { name = q.questName; } catch { }
            try { if (!string.IsNullOrEmpty(name)) name = OptionsManager.Localize(name) ?? name; } catch { }
            if (!string.IsNullOrEmpty(name))
            {
                // a name still holding a token was never filled in ("[townQuestName]"); half of one reads worse than none
                if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\[[^\]]*\]")) name = null;
                else
                {
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim(" -,:".ToCharArray());
                    if (name.Length < 3) name = null;
                }
            }
            if (string.IsNullOrEmpty(name))
            {
                try { int lvl = q.questLevel; if (lvl > 0) return "level " + lvl; } catch { }
                return null;
            }
            return name;
        }

        /// <summary>What makes this quest this quest. Quest instances carry no id of their own, but the boss, level,
        /// terrain, time of day and task together identify the one the party is on, and the game saves all of them
        /// (QuestSaveCampaign.json), so the same run can be picked up again after the game is closed and reopened.</summary>
        public static string QuestId(QuestInstance q)
        {
            if (q == null) return null;
            try
            {
                return string.Join("|", new[] {
                    "b" + q.bossCharacterIndex, "l" + q.questLevel, "t" + q.terrainTypeIndex,
                    "d" + q.timeOfDayIndex, q.bossName ?? "", q.questTask ?? "" });
            }
            catch { return null; }
        }

        private static ObservableQuestData QuestDataOf(Root root)
        {
            try { return root != null ? root.ObservableQuestData : null; } catch { return null; }
        }

        /// <summary>What the run being played is called, for picking it up again after a restart. A Roguelike run has no
        /// quest, but it does pick its final boss when it starts and keeps it to the end, so that is the run's name.</summary>
        public static string CurrentQuestId()
        {
            try
            {
                Root root = LiveRoot();
                if (root != null && root.PlayingRoguelike)
                {
                    ObservableQuestData q = QuestDataOf(root);
                    string guid = q != null ? q.RoguelikeEndBossGuid : null;
                    return string.IsNullOrEmpty(guid) ? null : "roguelike|" + guid;
                }
                return QuestManager.instance != null ? QuestId(QuestManager.instance.CurrentQuest) : null;
            }
            catch { return null; }
        }

        /// <summary>Continue a run that was saved before the game was closed: its numbers become the live store and the
        /// next battle is folded into the same file.</summary>
        public static void Adopt(RunHistory.RunFile f)
        {
            Clear("making room for the run being resumed");
            foreach (RunHistory.Char c in f.Characters)
            {
                string key = (c.Owner ?? "") + "|" + c.Name;
                _byChar[key] = new Dictionary<int, float>(c.Values);
                _meta[key] = new Meta { Name = c.Name, Owner = c.Owner ?? "", Level = c.Level, Order = _meta.Count };
            }
            Battles = f.Battles;
            _endedSeq = _foldedSeq = f.Battles;
            Fights.Clear(); if (f.Fights != null) Fights.AddRange(f.Fights);
            DateTime d; Started = DateTime.TryParse(f.Started, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)DateTime.Now;
            _quest = f.Quest; _questId = f.QuestId; _difficulty = f.Difficulty; _mode = f.Mode; _act = f.Act; _area = f.Area;
            Finished = false;
            FilePath = f.Path;
        }

        /// <summary>The run as it goes to disk: the readable summary plus every key the Stats window reads.</summary>
        public static RunHistory.RunFile BuildFile()
        {
            CaptureContext();
            var f = new RunHistory.RunFile
            {
                Started = (Started ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"),
                Ended = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Mode = _mode ?? "Campaign",
                Difficulty = _difficulty,
                Quest = _quest,
                Act = _act,
                Area = _area,
                QuestId = _questId,
                Closed = Finished,
                Battles = Battles,
                Wins = Fights.Count(x => x.Victory),
                Fights = new List<RunHistory.Fight>(Fights),
            };
            foreach (var kv in _byChar.OrderBy(x => _meta.ContainsKey(x.Key) ? _meta[x.Key].Order : 99))
            {
                Meta m; _meta.TryGetValue(kv.Key, out m);
                var c = new RunHistory.Char { Name = m != null ? m.Name : kv.Key, Owner = m != null ? m.Owner : "", Level = m != null ? m.Level : 0 };
                foreach (var e in kv.Value.OrderBy(x => x.Key)) c.Keys[e.Key.ToString(CultureInfo.InvariantCulture)] = e.Value;
                float v;
                foreach (BattleStat s in Enum.GetValues(typeof(BattleStat)))
                    if (kv.Value.TryGetValue((int)s, out v) && v != 0f) c.Summary[s.ToString()] = (float)Math.Round(v, 1);
                if (kv.Value.TryGetValue(SharedStats.Kills, out v) && v != 0f) c.Summary["Kills"] = v;
                if (kv.Value.TryGetValue(SharedStats.Hits, out v) && v != 0f) c.Summary["Hits"] = v;
                if (kv.Value.TryGetValue(SharedStats.Crits, out v) && v != 0f) c.Summary["Crits"] = v;
                if (kv.Value.TryGetValue(SharedStats.BiggestHit, out v) && v != 0f)
                {
                    c.Summary["BiggestHit"] = (float)Math.Round(v, 1);
                    float src; if (kv.Value.TryGetValue(SharedStats.BiggestHitSource, out src)) c.Summary["BiggestHitSource"] = SharedStats.SourceName((int)src);
                }
                if (kv.Value.TryGetValue(SharedStats.BestSkillDamage, out v) && v != 0f)
                {
                    c.Summary["BestSkillDamage"] = (float)Math.Round(v, 1);
                    float src; if (kv.Value.TryGetValue(SharedStats.BestSkillSource, out src)) c.Summary["BestSkill"] = SharedStats.SourceName((int)src);
                }
                f.Characters.Add(c);
            }
            return f;
        }

        /// <summary>The store for a character, plus the live battle on top while one is running (so the page opened mid-fight shows the run so far).</summary>
        public static Dictionary<int, float> View(Character c, bool includeLive)
        {
            Dictionary<int, float> run = Store(c);
            if (!includeLive) return run;
            try
            {
                Root root = LiveRoot();
                if (root == null || root.BattleStats == null) return run;
                foreach (CharacterBattleStats cbs in root.BattleStats)
                {
                    if (cbs == null || cbs.Character != c || cbs.BattleStats == null || cbs.BattleStats.Count == 0) continue;
                    var merged = new Dictionary<int, float>(run);
                    MergeInto(merged, new Dictionary<int, float>(cbs.BattleStats));
                    return merged;
                }
            }
            catch { }
            return run;
        }

        private static Root LiveRoot()
        {
            try { return NetworkingManager.Instance != null && NetworkingManager.Instance.NetworkManager != null ? NetworkingManager.Instance.NetworkManager.Root : null; }
            catch { return null; }
        }

        // ---------- merge ----------

        private static bool IsDerived(int k)
        {
            return k == SharedStats.BestSkillSource || k == SharedStats.BestSkillDamage
                || (k >= SharedStats.TopSource && k < SharedStats.TopHits + SharedStats.TopCount);
        }

        internal static void MergeInto(Dictionary<int, float> run, Dictionary<int, float> battle)
        {
            foreach (var kv in battle)
            {
                int k = kv.Key;
                if (k == SharedStats.BiggestHit || k == SharedStats.BiggestHitSource || IsDerived(k)) continue;
                float have; run.TryGetValue(k, out have);
                run[k] = have + kv.Value;
            }
            float bBig, rBig;
            if (battle.TryGetValue(SharedStats.BiggestHit, out bBig) && (!run.TryGetValue(SharedStats.BiggestHit, out rBig) || bBig > rBig))
            {
                run[SharedStats.BiggestHit] = bBig;
                float src; run[SharedStats.BiggestHitSource] = battle.TryGetValue(SharedStats.BiggestHitSource, out src) ? src : SharedStats.NONE;
            }
            Rederive(run);
        }

        /// <summary>Best skill and the top-8 list from the summed Damage Dealt bucket (the same input the per-battle top list is built from).</summary>
        private static void Rederive(Dictionary<int, float> run)
        {
            int dLo = SharedStats.BucketBase + SharedStats.B_Dealt * SharedStats.BucketSpan, dHi = dLo + SharedStats.BucketSpan;
            int hLo = SharedStats.BucketHitsBase + SharedStats.B_Dealt * SharedStats.BucketSpan;
            var top = run.Where(kv => kv.Key >= dLo && kv.Key < dHi && kv.Value > 0f).OrderByDescending(kv => kv.Value).Take(SharedStats.TopCount).ToList();
            for (int i = 0; i < SharedStats.TopCount; i++) { run.Remove(SharedStats.TopSource + i); run.Remove(SharedStats.TopDamage + i); run.Remove(SharedStats.TopHits + i); }
            for (int i = 0; i < top.Count; i++)
            {
                int code = top[i].Key - dLo;
                float h; run.TryGetValue(hLo + code, out h);
                run[SharedStats.TopSource + i] = code; run[SharedStats.TopDamage + i] = top[i].Value; run[SharedStats.TopHits + i] = h;
            }
            if (top.Count > 0) { run[SharedStats.BestSkillSource] = top[0].Key - dLo; run[SharedStats.BestSkillDamage] = top[0].Value; }
            else { run.Remove(SharedStats.BestSkillSource); run.Remove(SharedStats.BestSkillDamage); }
        }

        /// <summary>Short key=value list of the scalar keys (vanilla + the mod's 1000-1099 range), for the fold log line the harness checks.</summary>
        private static string Describe(Dictionary<int, float> d)
        {
            var parts = new List<string>();
            foreach (var kv in d.OrderBy(x => x.Key))
            {
                if (kv.Key >= SharedStats.ElementBase) break;
                string name = Enum.IsDefined(typeof(BattleStat), kv.Key) ? ((BattleStat)kv.Key).ToString() : "key" + kv.Key;
                parts.Add(name + "=" + kv.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Join(" ", parts.ToArray());
        }
    }
}
