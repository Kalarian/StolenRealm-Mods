using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace SharedGold.Patches
{
    /// <summary>
    /// Gold is a per-character attribute; the game's "party gold" (Root.MyPartyGold) is the SUM over the characters in
    /// your party, and spending (ShopMenusManager.SpendGoldAsParty) drains them one after another. So a shared pool
    /// needs no new accounting at all: keep exactly ONE of your loaded campaign characters holding everything and the
    /// rest at zero. Gold can neither appear nor vanish because every move is an explicit transfer between two of your
    /// own characters, and each half of a transfer is written to disk and verified before the next step.
    ///
    /// Two game facts drive the design:
    /// 1. The character list comes from the game's own lists (GameLogic.AllMyCharacters, your party, the selected
    ///    character), never from a list of our own: the game rebuilds its Character objects whenever it reloads the
    ///    saves, and a stale object would count its old gold twice.
    /// 2. Character.Save silently does nothing unless force is set (only characters that joined the party this
    ///    session are "network loaded"), or while the character is still inside Load, or when it is not in
    ///    AllMyCharacters; and QueueCharacterSave is not a queue, it saves immediately. So a drained character that
    ///    was never played this session would keep its old gold on disk while the holder's file gained it:
    ///    duplication on the next launch. Every transfer therefore forces a save of the giver and checks that the
    ///    file really changed before crediting the holder; if anything fails the move is undone in memory.
    /// The holder is the character you are currently controlling, else the first of your party, else the first listed.
    /// Only characters you own are ever touched, so co-op partners' gold is untouched on both sides.
    /// </summary>
    internal static class GoldPatches
    {
        private static bool _busy;
        private static bool _dirty;      // a character was loaded: consolidate on the next tick, once the game has listed it
        private static float _nextTick;
        private static Character _lastHolder;

        [HarmonyPatch(typeof(Character), nameof(Character.Load), new[] { typeof(int) })]
        private static class Character_Load
        {
            [HarmonyPriority(Priority.Low)] // after LevelSync / SharedFortunes / AutoSalvage (attribute must sit on the method: Harmony drops a class-level priority)
            private static void Postfix(int saveIndex)
            {
                if (saveIndex < 0) return;
                _dirty = true; // GameLogic.LoadCharacters adds the character to AllMyCharacters only after Load returns
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GiveGold))]
        private static class Character_GiveGold
        {
            private static void Postfix(Character __instance, float goldValue)
            {
                if (_busy) return; // our own AddAttribute never routes through GiveGold, but be safe
                try
                {
                    if (!Eligible(__instance)) return;
                    Consolidate("gold change on " + __instance.CharacterName + " (" + goldValue.ToString("0", CultureInfo.InvariantCulture) + ")");
                }
                catch (Exception e) { SharedGoldPlugin.Log.LogWarning("Consolidate on gold change failed: " + e); }
            }
        }

        /// <summary>Called from the plugin's Update while patched: catches loads, party and selection changes a couple of times a second.</summary>
        internal static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            SharedGoldConfig cfg = SharedGoldPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            if (_dirty)
            {
                if (GameLogic.instance == null || !GameLogic.instance.FinishedLoadingCharacters) return; // wait for the whole roster
                _dirty = false; Consolidate("characters loaded"); return;
            }
            Character holder = PickHolder(Loaded());
            if (holder != null && holder != _lastHolder) Consolidate("holder is now " + holder.CharacterName);
        }

        internal static void Reset() { _dirty = false; _lastHolder = null; }

        // ---------- rules ----------

        private static bool Eligible(Character c)
        {
            SharedGoldConfig cfg = SharedGoldPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || c == null || c.IsAI || !c.Owned) return false;
            CharacterSaveFile save = c.CharacterSaveFile;
            if (save == null) return false;                 // not a save-backed character
            if (save.IsRoguelikeCharacter || c.IsRoguelikeCharacter) return false; // live flag too: it is set before the first save copies it into the file
            if (save.IsDeleted || c.IsDeleted) return false;
            if (c.HardcoreDeath) return false;              // a dead hardcore character can never be played again; leave its purse alone
            if (!cfg.IncludeHardcore.Value && save.IsHardcore) return false;
            try { if (!FileSystem.Exists(c.CurrentFilename)) return false; } catch { return false; } // never saved yet (mid-creation): joins the pool at its first save
            return true;
        }

        private static bool CreatingCharacter()
        {
            try { return GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.CreatingCharacter; }
            catch { return false; }
        }

        /// <summary>Can Character.Save(force: true) actually write this character right now? (Mirrors the guards in Character.Save.)</summary>
        private static bool CanSave(Character c)
        {
            try
            {
                if (c == null || c.SavingDisabled || c.IsLocalOnlyCharacter) return false;
                if (GameLogic.instance == null || !GameLogic.instance.AllMyCharacters.Contains(c)) return false;
                bool loading;
                if (Character.IsLoadingMap.TryGetValue(c.SaveIndex, out loading) && loading) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Force-save and verify the file changed. Returns false if the game skipped the write.</summary>
        private static bool SaveNow(Character c)
        {
            if (!CanSave(c)) return false;
            try
            {
                string path = c.CurrentFilename;
                bool existed = File.Exists(path);
                DateTime before = existed ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                long lenBefore = existed ? new FileInfo(path).Length : -1;
                c.Save(force: true);
                if (!File.Exists(path)) return false;
                return File.GetLastWriteTimeUtc(path) != before || new FileInfo(path).Length != lenBefore;
            }
            catch (Exception e) { SharedGoldPlugin.Log.LogWarning("Save of " + c.CharacterName + " threw: " + e.Message); return false; }
        }

        /// <summary>All eligible characters, from the game's own lists: GameLogic.AllMyCharacters (every save, filled at startup),
        /// plus your party and the selected character as a safety net. One entry per save slot.</summary>
        private static string _lastScanSig;
        private static List<Character> Loaded()
        {
            var list = new List<Character>();
            var slots = new HashSet<int>();
            int seen = 0; string dbg = "";
            try
            {
                var all = new List<Character>();
                try { var mine = GameLogic.instance != null ? GameLogic.instance.AllMyCharacters : null; if (mine != null) all.AddRange(mine); } catch { }
                try
                {
                    var party = NetworkingManager.Instance != null ? NetworkingManager.Instance.MyPartyCharacters : null;
                    if (party != null) foreach (Character c in party) if (c != null && !all.Contains(c)) all.Add(c);
                }
                catch { }
                try { Character sel = GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null; if (sel != null && !all.Contains(sel)) all.Add(sel); } catch { }
                foreach (Character c in all)
                {
                    if (c == null) continue;
                    seen++;
                    if (!Eligible(c))
                    {
                        dbg += " [" + c.CharacterName + ": owned=" + c.Owned + " save=" + (c.CharacterSaveFile != null) + (c.CharacterSaveFile != null ? " rogue=" + c.CharacterSaveFile.IsRoguelikeCharacter : "") + (c.HardcoreDeath ? " dead" : "") + "]";
                        continue;
                    }
                    if (!slots.Add(c.SaveIndex)) { dbg += " [" + c.CharacterName + ": duplicate of slot " + c.SaveIndex + ", ignored]"; continue; }
                    list.Add(c);
                }
            }
            catch (Exception e) { dbg += " EX:" + e.Message; }
            string sig = seen + "/" + list.Count + dbg;
            if (SharedGoldPlugin.Cfg != null && SharedGoldPlugin.Cfg.Verbose.Value && sig != _lastScanSig)
            {
                _lastScanSig = sig; // log only when the picture changes
                SharedGoldPlugin.Log.LogInfo("Scan: " + seen + " player characters, " + list.Count + " eligible" + dbg);
            }
            return list;
        }

        private static Character PickHolder(List<Character> all)
        {
            if (all.Count == 0) return null;
            try
            {
                Character sel = GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null;
                if (sel != null && all.Contains(sel)) return sel;
            }
            catch { }
            try
            {
                var party = NetworkingManager.Instance != null ? NetworkingManager.Instance.MyPartyCharacters : null;
                if (party != null)
                    foreach (Character p in party) if (p != null && all.Contains(p)) return p;
            }
            catch { }
            // Nothing selected and no party (character-select screen at startup): keep the purse where it already is,
            // i.e. on the character holding the most gold, so a launch does not shuffle it around for nothing.
            Character best = all[0];
            foreach (Character c in all) if (c.Gold > best.Gold) best = c;
            return best;
        }

        /// <summary>The gold actually stored in the save map (never drain more than that, so the map cannot go negative:
        /// a negative raw value makes the game's Gold getter add 2.1 billion).</summary>
        private static float RawGold(Character c)
        {
            try
            {
                Guid g = Game.GoldAttribute.Guid;
                if (c.SavedMap != null && c.SavedMap.ContainsKey(g)) return c.SavedMap[g];
            }
            catch { }
            return c.Gold;
        }

        /// <summary>Move all gold onto the holder, one verified transfer per character. Sum-preserving by construction.</summary>
        internal static void Consolidate(string why)
        {
            if (_busy) return;
            SharedGoldConfig cfg = SharedGoldPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            if (CreatingCharacter()) { _dirty = true; return; } // wait until the new character is a real, saved one
            List<Character> all = Loaded();
            Character holder = PickHolder(all);
            if (holder == null) return;
            _busy = true;
            try
            {
                int moved = 0, skipped = 0;
                CharacterAttribute goldAttr = Game.GoldAttribute;
                foreach (Character c in all)
                {
                    if (c == holder) continue;
                    float g = Mathf.Floor(Mathf.Min(c.Gold, RawGold(c)));
                    if (g <= 0f) continue;
                    if (!CanSave(c) || !CanSave(holder)) { skipped++; continue; } // cannot persist both halves: leave it where it is
                    c.AddAttribute(goldAttr, -g);
                    if (!SaveNow(c))
                    {
                        c.AddAttribute(goldAttr, g);
                        skipped++;
                        SharedGoldPlugin.Log.LogWarning("Could not save " + c.CharacterName + "; its " + g.ToString("N0", CultureInfo.InvariantCulture) + " gold stays put");
                        continue;
                    }
                    holder.AddAttribute(goldAttr, g);
                    if (!SaveNow(holder))
                    {
                        holder.AddAttribute(goldAttr, -g);
                        c.AddAttribute(goldAttr, g);
                        bool restored = SaveNow(c);
                        SharedGoldPlugin.Log.LogError("Could not save holder " + holder.CharacterName + "; transfer from " + c.CharacterName + " undone" + (restored ? "" : " (and its save failed too: check " + c.CharacterName + "'s gold)"));
                        skipped++;
                        break;
                    }
                    moved++;
                    if (cfg.Verbose.Value) SharedGoldPlugin.Log.LogInfo("  " + c.CharacterName + ": " + g.ToString("N0", CultureInfo.InvariantCulture) + " -> " + holder.CharacterName + " (both saved)");
                }
                float total = 0f;
                foreach (Character c in all) total += Mathf.Max(0f, Mathf.Floor(c.Gold));
                bool holderChanged = holder != _lastHolder;
                _lastHolder = holder;
                if (moved > 0 || holderChanged || skipped > 0)
                {
                    SharedGoldPlugin.Log.LogInfo("Pool " + total.ToString("N0", CultureInfo.InvariantCulture) + " gold held by " + holder.CharacterName + " (" + why + ", " + all.Count + " characters" + (skipped > 0 ? ", " + skipped + " not moved" : "") + ")");
                    WriteMirror(total, holder.CharacterName);
                    try { if (ShopMenusManager.Instance != null && ShopMenusManager.Instance.OnPlayerGoldChanged != null) ShopMenusManager.Instance.OnPlayerGoldChanged.Invoke(); } catch { }
                }
            }
            finally { _busy = false; }
        }

        private static void WriteMirror(float total, string holder)
        {
            try
            {
                string path = Path.Combine(FileSystem.persistentDataPath, "SharedGold.json");
                File.WriteAllText(path, "{\n  \"Gold\": " + total.ToString("0", CultureInfo.InvariantCulture) + ",\n  \"HeldBy\": \"" + holder.Replace("\"", "'") + "\"\n}\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
