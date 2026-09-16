using System;
using System.Globalization;
using System.IO;
using System.Text;
using Burst2Flame;
using Newtonsoft.Json;

namespace LevelSync
{
    /// <summary>
    /// The account's highest ExperienceLevel (a float: level plus progress into it), found by reading every
    /// CharacterN.json in the save folder, and kept current from GiveExperience while playing. Mirrored to
    /// LevelSync.json in the same folder so it is easy to see; the saves themselves are only ever changed through
    /// the game's own save routine.
    /// </summary>
    internal sealed class LevelPool
    {
        public float Highest { get; private set; }
        public string From { get; private set; } = "";
        public int SavesScanned { get; private set; }

        public static string PoolPath => Path.Combine(FileSystem.persistentDataPath, "LevelSync.json");

        public string Describe()
        {
            return Highest <= 0f ? "unknown" : "L" + Highest.ToString("0.##", CultureInfo.InvariantCulture) + (From.Length > 0 ? " (" + From + ")" : "");
        }

        /// <summary>Raise the pooled level. Returns true if it grew.</summary>
        public bool Offer(float level, string from)
        {
            if (level <= Highest) return false;
            Highest = level;
            From = from ?? "";
            return true;
        }

        public void LoadAndSeed()
        {
            Highest = 0f; From = ""; SavesScanned = 0;
            bool includeRogue = LevelSyncPlugin.Cfg.IncludeRoguelikeCharacters.Value;
            string[] paths;
            try { paths = Directory.GetFiles(FileSystem.persistentDataPath, "Character*.json"); }
            catch (Exception e) { LevelSyncPlugin.Log.LogWarning("Could not list saves: " + e.Message); return; }
            foreach (string path in paths)
            {
                if (path.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".temp", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var save = JsonConvert.DeserializeObject<CharacterSaveFile>(File.ReadAllText(path));
                    if (save == null || save.IsDeleted) continue;
                    if (save.IsRoguelikeCharacter && !includeRogue) continue;
                    SavesScanned++;
                    if (Offer(save.ExperienceLevel, save.CharacterName) && LevelSyncPlugin.Cfg.Verbose.Value)
                        LevelSyncPlugin.Log.LogInfo("Highest so far: " + Describe() + " from " + Path.GetFileName(path));
                }
                catch (Exception e)
                {
                    LevelSyncPlugin.Log.LogWarning("Skipping " + Path.GetFileName(path) + ": " + e.Message);
                }
            }
            LevelSyncPlugin.Log.LogInfo("Highest level on account: " + Describe() + " from " + SavesScanned + " saves");
            Save();
        }

        public void Save()
        {
            try
            {
                string json = "{\n  \"HighestLevel\": " + Highest.ToString("0.###", CultureInfo.InvariantCulture) + ",\n  \"From\": " + JsonConvert.ToString(From) + "\n}\n";
                File.WriteAllText(PoolPath, json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                LevelSyncPlugin.Log.LogWarning("Could not write " + PoolPath + ": " + e.Message);
            }
        }
    }
}
