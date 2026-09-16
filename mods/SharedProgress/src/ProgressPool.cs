using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Burst2Flame;
using Newtonsoft.Json;

namespace SharedProgress
{
    /// <summary>
    /// The account's campaign progress: the union of every campaign character's completed quest/town nodes and legacy
    /// main-quest flags, and the maxima of the numbers the game derives act, quest level and shop stock from.
    /// Kept in SharedProgress.json in the save folder (loaded first, then re-seeded from every CharacterN.json, so a
    /// deleted save never loses progress for the others). Only ever grows.
    /// </summary>
    internal sealed class ProgressPool
    {
        public sealed class Data
        {
            public List<string> Nodes = new List<string>();
            public List<string> MainQuests = new List<string>();
            public int LastMainQuestLevelCompleted;
            public int HighestCompletedLevel;
            public int ShopHighestLevel;
            public int LastVisitedActIndex;
            public int CurrentMainQuestIndex;
            public string From = "";
        }

        private readonly HashSet<string> _nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _mainQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int LastMainQuestLevelCompleted { get; private set; }
        public int HighestCompletedLevel { get; private set; }
        public int ShopHighestLevel { get; private set; }
        public int LastVisitedActIndex { get; private set; }
        public int CurrentMainQuestIndex { get; private set; }
        public string From { get; private set; } = "";
        public int SavesScanned { get; private set; }
        public int NodeCount => _nodes.Count;

        public static string PoolPath => Path.Combine(FileSystem.persistentDataPath, "SharedProgress.json");

        public string Describe()
        {
            return _nodes.Count + " quest nodes, main quest L" + LastMainQuestLevelCompleted + ", act " + (LastVisitedActIndex + 1) + ", shop L" + ShopHighestLevel + (From.Length > 0 ? " (from " + From + ")" : "");
        }

        public bool HasNode(string guid) { return guid != null && _nodes.Contains(guid); }
        public IEnumerable<string> Nodes => _nodes;
        public IEnumerable<string> MainQuests => _mainQuests;

        // ---------- offering progress into the pool ----------

        private bool OfferValues(IEnumerable<string> nodes, IEnumerable<string> mainQuests, int lastMain, int highest, int shop, int act, int mainIndex, string from)
        {
            bool grew = false;
            if (nodes != null) foreach (string n in nodes) if (!string.IsNullOrEmpty(n) && _nodes.Add(n)) grew = true;
            if (mainQuests != null) foreach (string m in mainQuests) if (!string.IsNullOrEmpty(m) && _mainQuests.Add(m)) grew = true;
            if (lastMain > LastMainQuestLevelCompleted) { LastMainQuestLevelCompleted = lastMain; From = from ?? ""; grew = true; }
            if (highest > HighestCompletedLevel) { HighestCompletedLevel = highest; grew = true; }
            if (shop > ShopHighestLevel) { ShopHighestLevel = shop; grew = true; }
            if (act > LastVisitedActIndex) { LastVisitedActIndex = act; grew = true; }
            if (mainIndex > CurrentMainQuestIndex) { CurrentMainQuestIndex = mainIndex; grew = true; }
            return grew;
        }

        public bool Offer(CharacterSaveFile save)
        {
            if (save == null) return false;
            return OfferValues(save.CompletedQuestNodes, save.CompletedMainQuests != null ? save.CompletedMainQuests.Where(kv => kv.Value).Select(kv => kv.Key) : null,
                save.LastMainQuestLevelCompleted, save.HighestCompletedLevel, save.ShopHighestLevel, save.LastVisitedActIndex, save.CurrentMainQuestIndex, save.CharacterName);
        }

        public bool Offer(Character c)
        {
            if (c == null) return false;
            List<string> nodes = null, mains = null;
            try { if (c.CompletedQuestNodes != null) nodes = c.CompletedQuestNodes.ToList(); } catch { }
            try { if (c.CompletedMainQuests != null) mains = c.CompletedMainQuests.Where(kv => kv.Value).Select(kv => kv.Key.ToString()).ToList(); } catch { }
            return OfferValues(nodes, mains, c.LastMainQuestLevelCompleted, c.HighestCompletedLevel, c.ShopHighestLevel, c.LastVisitedActIndex, c.CurrentMainQuestIndex, c.CharacterName);
        }

        // ---------- pool file ----------

        public void LoadAndSeed()
        {
            _nodes.Clear(); _mainQuests.Clear();
            LastMainQuestLevelCompleted = HighestCompletedLevel = ShopHighestLevel = LastVisitedActIndex = CurrentMainQuestIndex = 0; From = ""; SavesScanned = 0;
            Load();
            SeedFromSaves();
            Save();
            SharedProgressPlugin.Log.LogInfo("Account campaign progress: " + Describe() + " from " + SavesScanned + " saves");
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(PoolPath)) return;
                Data d = JsonConvert.DeserializeObject<Data>(File.ReadAllText(PoolPath));
                if (d == null) return;
                OfferValues(d.Nodes, d.MainQuests, d.LastMainQuestLevelCompleted, d.HighestCompletedLevel, d.ShopHighestLevel, d.LastVisitedActIndex, d.CurrentMainQuestIndex, d.From);
            }
            catch (Exception e) { SharedProgressPlugin.Log.LogWarning("Could not read " + PoolPath + ": " + e.Message); }
        }

        private void SeedFromSaves()
        {
            bool includeRogue = SharedProgressPlugin.Cfg.IncludeRoguelikeCharacters.Value;
            var folders = new List<string> { FileSystem.persistentDataPath };
            try { string alt = GameLogic.AlternateCharacterFolderPath; if (!string.IsNullOrEmpty(alt) && Directory.Exists(alt) && !folders.Contains(alt)) folders.Add(alt); } catch { }
            foreach (string folder in folders)
            {
                string[] paths;
                try { paths = Directory.GetFiles(folder, "Character*.json"); } catch { continue; }
                foreach (string path in paths)
                {
                    if (path.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".temp", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var save = JsonConvert.DeserializeObject<CharacterSaveFile>(File.ReadAllText(path));
                        if (save == null || save.IsDeleted) continue;
                        if (save.IsRoguelikeCharacter && !includeRogue) continue;
                        SavesScanned++;
                        if (Offer(save) && SharedProgressPlugin.Cfg.Verbose.Value) SharedProgressPlugin.Log.LogInfo("Progress so far: " + Describe() + " after " + Path.GetFileName(path));
                    }
                    catch (Exception e) { SharedProgressPlugin.Log.LogWarning("Skipping " + Path.GetFileName(path) + ": " + e.Message); }
                }
            }
        }

        public void Save()
        {
            try
            {
                var d = new Data
                {
                    Nodes = _nodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    MainQuests = _mainQuests.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    LastMainQuestLevelCompleted = LastMainQuestLevelCompleted, HighestCompletedLevel = HighestCompletedLevel, ShopHighestLevel = ShopHighestLevel,
                    LastVisitedActIndex = LastVisitedActIndex, CurrentMainQuestIndex = CurrentMainQuestIndex, From = From
                };
                string tmp = PoolPath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(d, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(PoolPath)) File.Delete(PoolPath);
                File.Move(tmp, PoolPath);
            }
            catch (Exception e) { SharedProgressPlugin.Log.LogWarning("Could not write " + PoolPath + ": " + e.Message); }
        }
    }
}
