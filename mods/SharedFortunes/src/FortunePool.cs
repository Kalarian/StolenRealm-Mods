using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Burst2Flame;
using Newtonsoft.Json;

namespace SharedFortunes
{
    /// <summary>
    /// Account-wide record of earned fortunes: fortune Guid -> highest level earned by any character.
    /// Stored as SharedFortunes.json in the game's save folder (Application.persistentDataPath), the same
    /// folder that holds Character0.json, Character1.json... It is seeded by scanning those saves and kept
    /// up to date by the patches. Levels only ever go up; entries are never removed.
    /// </summary>
    internal sealed class FortunePool
    {
        private readonly Dictionary<string, float> _levels = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public int Count => _levels.Count;

        public static string PoolPath => Path.Combine(FileSystem.persistentDataPath, "SharedFortunes.json");

        public IEnumerable<KeyValuePair<string, float>> Entries => _levels;

        public float Get(string guid)
        {
            float v;
            return _levels.TryGetValue(guid, out v) ? v : 0f;
        }

        /// <summary>Raise the pooled level for a fortune. Returns true if the pool changed.</summary>
        public bool Offer(string guid, float level)
        {
            if (string.IsNullOrEmpty(guid) || level <= 0f) return false;
            level = Math.Min(30f, level);
            float cur;
            if (_levels.TryGetValue(guid, out cur) && cur >= level) return false;
            _levels[guid] = level;
            return true;
        }

        public void LoadAndSeed()
        {
            Load();
            int before = Count;
            int changed = SeedFromSaves();
            if (changed > 0 || Count != before) Save();
        }

        private void Load()
        {
            _levels.Clear();
            try
            {
                if (!File.Exists(PoolPath)) return;
                var data = JsonConvert.DeserializeObject<Dictionary<string, float>>(File.ReadAllText(PoolPath));
                if (data == null) return;
                foreach (KeyValuePair<string, float> kv in data) Offer(kv.Key, kv.Value);
            }
            catch (Exception e)
            {
                SharedFortunesPlugin.Log.LogWarning("Could not read " + PoolPath + ": " + e.Message);
            }
        }

        public void Save()
        {
            try
            {
                string tmp = PoolPath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_levels, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(PoolPath)) File.Delete(PoolPath);
                File.Move(tmp, PoolPath);
            }
            catch (Exception e)
            {
                SharedFortunesPlugin.Log.LogWarning("Could not write " + PoolPath + ": " + e.Message);
            }
        }

        /// <summary>Read every CharacterN.json on disk (read-only) and pool its fortunes. Returns number of pool changes.</summary>
        private int SeedFromSaves()
        {
            int changes = 0;
            var folders = new List<string> { FileSystem.persistentDataPath };
            try
            {
                string alt = GameLogic.AlternateCharacterFolderPath;
                if (!string.IsNullOrEmpty(alt) && Directory.Exists(alt) && !folders.Contains(alt)) folders.Add(alt);
            }
            catch { }
            bool includeRogue = SharedFortunesPlugin.Cfg.IncludeRoguelikeCharacters.Value;
            int files = 0;
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
                        files++;
                        if (save.FortuneSaveData == null) continue;
                        foreach (FortuneSaveData f in save.FortuneSaveData)
                        {
                            if (f == null) continue;
                            if (Offer(f.Guid, f.Level))
                            {
                                changes++;
                                if (SharedFortunesPlugin.Cfg.Verbose.Value)
                                    SharedFortunesPlugin.Log.LogInfo("Pooled from " + (save.CharacterName ?? Path.GetFileName(path)) + ": " + f.Guid + " L" + f.Level.ToString("0", CultureInfo.InvariantCulture));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        SharedFortunesPlugin.Log.LogWarning("Skipping " + Path.GetFileName(path) + ": " + e.Message);
                    }
                }
            }
            SharedFortunesPlugin.Log.LogInfo("Scanned " + files + " character saves, " + changes + " new/raised pool entries");
            return changes;
        }
    }
}
