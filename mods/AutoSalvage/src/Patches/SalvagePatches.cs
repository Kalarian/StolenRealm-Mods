using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace AutoSalvage.Patches
{
    /// <summary>
    /// Every item that lands in a bag goes through GameLogic.GiveItem (loot batches through GameLogic.GiveItems, which
    /// calls GiveItem per item). The rules are applied ONLY to the items that were just delivered, never to the rest of
    /// the bag: a bought, crafted or withdrawn item left unequipped must survive the next loot drop. Deliveries that are
    /// not loot (shop purchase, crafting, gambling, moving an item between characters or from storage, roguelike level-up
    /// picks, character creation, loading a save, gifts from a friend which arrive with save=false) are excluded.
    /// Junk equipment is sold exactly the way the shop sells (RemoveFromItemList + GiveGold(SellPrice)), commodities sold,
    /// materials moved with the game's own ItemStashData.AddItemToStash.
    ///
    /// The one-time cleanup of existing bags (SweepBagsOnLoad) runs from the plugin's Update once the game has finished
    /// loading every character, not from the Load hook: Character.Save silently does nothing while a character is still
    /// inside Load, is not yet in GameLogic.AllMyCharacters, or has not joined the party this session (unless forced),
    /// while the stash file is written immediately. Cleaning up inside Load would put the materials in the stash file and
    /// leave them in the bag file as well. The cleanup therefore force-saves each character and checks the write, and
    /// switches itself off afterwards.
    /// </summary>
    internal static class SalvagePatches
    {
        private static int _suppress;                       // >0 while inside a non-loot delivery
        private static bool _inBatch;                       // GiveItems in progress: process once at the end, not per item
        private static readonly List<Item> _batch = new List<Item>();
        private static bool _cleanupDone;                   // one-time bag cleanup finished this launch

        // ---------- loot deliveries ----------

        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.GiveItems))]
        private static class GameLogic_GiveItems
        {
            private static void Prefix() { _inBatch = true; _batch.Clear(); }
            private static void Finalizer(Character target, bool setTimeAcquired)
            {
                _inBatch = false;
                List<Item> delivered = new List<Item>(_batch);
                _batch.Clear();
                if (!setTimeAcquired || delivered.Count == 0) return; // Character.Load hands back the saved bag with setTimeAcquired=false
                try { ProcessDelivered(target, delivered, "loot"); }
                catch (Exception e) { AutoSalvagePlugin.Log.LogWarning("Loot batch: " + e); }
            }
        }

        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.GiveItem))]
        private static class GameLogic_GiveItem
        {
            private static void Postfix(Item item, Character target, bool save, bool setTimeAcquired)
            {
                if (item == null || _suppress > 0 || !setTimeAcquired) return;
                // Inside GiveItems every item is passed with save=false (the batch saves once at the end), so the save flag
                // says nothing there; the batch is judged as a whole in the GiveItems finalizer.
                if (_inBatch) { _batch.Add(item); return; }
                if (!save) return; // standalone save=false: load hand-back, gifts, preset sync
                try { ProcessDelivered(target, new List<Item> { item }, "loot"); }
                catch (Exception e) { AutoSalvagePlugin.Log.LogWarning("Loot item: " + e); }
            }
        }

        // ---------- not loot: suppress ----------

        [HarmonyPatch(typeof(ShopManager), nameof(ShopManager.BuySelectedItem))]
        private static class ShopManager_BuySelectedItem { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        [HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.CraftRecipe))]
        private static class CraftingManager_CraftRecipe { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        [HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.CraftSelectedRecipe))]
        private static class CraftingManager_CraftSelectedRecipe { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        [HarmonyPatch(typeof(GamblingManager), nameof(GamblingManager.GambleItem))]
        private static class GamblingManager_GambleItem { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.GiveItemToAnother))]
        private static class InventoryManager_GiveItemToAnother { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        [HarmonyPatch(typeof(RoguelikeManager), nameof(RoguelikeManager.ConfirmLevelUpSelection))]
        private static class RoguelikeManager_ConfirmLevelUpSelection { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        // A new character's starting kit (spare weapon, hood, jerkin, shield...) is handed out with GiveItem too: not loot.
        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.FinalizeCharacterCreation))]
        private static class GameLogic_FinalizeCharacterCreation { private static void Prefix() { _suppress++; } private static void Finalizer() { _suppress--; } }

        // ---------- delivered items ----------

        /// <summary>Apply the rules to what just arrived. A stackable delivery may have been merged into an existing
        /// unequipped stack of the same item (GiveItem sets its numStacks to 0), in which case the merged stack is the
        /// candidate: materials and commodities are fungible, so that only ever moves more of the same thing.</summary>
        private static void ProcessDelivered(Character c, List<Item> delivered, string reason)
        {
            if (!Active(c) || c.Items == null) return;
            var candidates = new List<Item>();
            foreach (Item d in delivered)
            {
                if (d == null) continue;
                if (c.Items.Contains(d)) { if (!candidates.Contains(d)) candidates.Add(d); continue; }
                if (d.ItemInfo == null || !d.IsStackable) continue;
                foreach (Item s in c.Items)
                    if (s != null && !s.equipped && s.ItemInfo == d.ItemInfo && !candidates.Contains(s)) candidates.Add(s);
            }
            Apply(c, candidates, reason, save: false); // loot arrives in a session, the game's own save follows via GiveGold/RemoveItem
        }

        // ---------- one-time cleanup of existing bags ----------

        /// <summary>Called from the plugin's Update while patched.</summary>
        internal static void Tick()
        {
            if (_cleanupDone) return;
            AutoSalvageConfig cfg = AutoSalvagePlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || !cfg.SweepBagsOnLoad.Value) return;
            GameLogic gl = GameLogic.instance;
            if (gl == null || !gl.FinishedLoadingCharacters || gl.AllMyCharacters == null) return;
            _cleanupDone = true;
            int done = 0, skipped = 0;
            foreach (Character c in gl.AllMyCharacters.ToList())
            {
                try
                {
                    if (!Active(c) || c.Items == null) continue;
                    if (!CanSave(c)) { skipped++; AutoSalvagePlugin.Log.LogWarning(c.CharacterName + ": cannot be saved right now, bag left as is"); continue; }
                    Apply(c, c.Items.Where(i => i != null && !i.equipped).ToList(), "bag cleanup", save: true);
                    done++;
                }
                catch (Exception e) { AutoSalvagePlugin.Log.LogWarning("Cleanup of " + c.CharacterName + ": " + e); }
            }
            if (skipped == 0)
            {
                cfg.SweepBagsOnLoad.Value = false; // one-time: from now on only new loot is touched
                AutoSalvagePlugin.Log.LogInfo("Bag cleanup finished for " + done + " character(s); SweepBagsOnLoad set to false");
            }
            else AutoSalvagePlugin.Log.LogInfo("Bag cleanup: " + done + " done, " + skipped + " skipped (will retry next launch)");
        }

        internal static void Reset() { _suppress = 0; _inBatch = false; _batch.Clear(); }

        // ---------- save safety ----------

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
            catch (Exception e) { AutoSalvagePlugin.Log.LogWarning("Save of " + c.CharacterName + " threw: " + e.Message); return false; }
        }

        // ---------- stash safety ----------

        private static readonly FieldInfo _stashLoadedField = AccessTools.Field(typeof(ItemStashData), "loaded");

        /// <summary>
        /// The game only reads ItemStash.json when you first enter town (TownManager.OpenTown -> LoadStash), and
        /// SaveStash() silently does nothing while the stash is not loaded; LoadStash() then starts by clearing the list.
        /// So an item stashed before town is opened is removed from the bag and then thrown away. Load the stash ourselves
        /// first (LoadStash is public and a no-op once loaded) and refuse to stash at all if it still is not loaded.
        /// </summary>
        private static ItemStashData ReadyStash()
        {
            ItemStashData stash;
            try { stash = ItemStashData.Instance; } catch { return null; }
            if (stash == null) return null;
            try
            {
                if (_stashLoadedField != null && !(bool)_stashLoadedField.GetValue(stash))
                {
                    stash.LoadStash();
                    if (AutoSalvagePlugin.Cfg.Verbose.Value) AutoSalvagePlugin.Log.LogInfo("Stash loaded early (" + stash.Items.Count + " stacks) so items can be stashed before town");
                }
                if (_stashLoadedField == null || !(bool)_stashLoadedField.GetValue(stash))
                {
                    AutoSalvagePlugin.Log.LogWarning("Stash is not loaded; keeping materials in the bag this time");
                    return null;
                }
            }
            catch (Exception e)
            {
                AutoSalvagePlugin.Log.LogWarning("Stash not ready (" + e.Message + "); keeping materials in the bag this time");
                return null;
            }
            return stash;
        }

        // ---------- the rules ----------

        private static bool Active(Character c)
        {
            AutoSalvageConfig cfg = AutoSalvagePlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || _suppress > 0 || c == null || c.IsAI || !c.Owned) return false;
            if (cfg.LeaveRoguelikeUntouched.Value)
            {
                try { if (Game.Instance != null && Game.Instance.RoguelikeModeActive) return false; } catch { }
                try { if (c.IsRoguelikeCharacter || (c.CharacterSaveFile != null && c.CharacterSaveFile.IsRoguelikeCharacter)) return false; } catch { }
            }
            return true;
        }

        private enum Verdict { Keep, Sell, Stash }

        private static Verdict Decide(Item item, Character c, AutoSalvageConfig cfg)
        {
            if (item == null || item.equipped || item.ItemInfo == null) return Verdict.Keep;
            if (item.numStacks <= 0 && item.IsStackable) return Verdict.Keep; // merged away by GiveItem: nothing left in this object
            ItemInfo info = item.ItemInfo;
            switch (info.ItemType)
            {
                case ItemType.Material:
                    return cfg.MaterialsToStash.Value ? Verdict.Stash : Verdict.Keep;
                case ItemType.Commodity:
                    if (!cfg.SellCommodities.Value) return Verdict.Keep;
                    if (cfg.RecipeCommoditiesToStash.Value && IsRecipeIngredient(info)) return Verdict.Stash;
                    return Verdict.Sell;
                case ItemType.Weapon:
                case ItemType.Shield:
                case ItemType.Head:
                case ItemType.Armor:
                case ItemType.Ring:
                case ItemType.Amulet:
                    switch (info.Rarity)
                    {
                        case ItemQuality.Common: return cfg.SellCommon.Value ? Verdict.Sell : Verdict.Keep;
                        case ItemQuality.Uncommon: return cfg.SellUncommon.Value ? Verdict.Sell : Verdict.Keep;
                        case ItemQuality.Rare:
                            if (!cfg.SellRare.Value) return Verdict.Keep;
                            if (cfg.KeepRareAtOrAboveLevel.Value && item.itemLevel >= c.Level) return Verdict.Keep;
                            return Verdict.Sell;
                        default: return Verdict.Keep; // Legendary, Mythic
                    }
                default:
                    return Verdict.Keep; // consumables, tools, quest items
            }
        }

        private static HashSet<ItemInfo> _recipeIngredients;

        /// <summary>ItemInfos used as a Required ingredient in any crafting recipe (built once from Game.Instance.CraftingRecipes).</summary>
        private static bool IsRecipeIngredient(ItemInfo info)
        {
            if (_recipeIngredients == null)
            {
                var set = new HashSet<ItemInfo>();
                try
                {
                    var recipes = Game.Instance != null ? Game.Instance.CraftingRecipes : null;
                    if (recipes != null)
                        foreach (CraftingRecipe r in recipes)
                            if (r != null && r.Required != null)
                                foreach (CraftingMaterial m in r.Required)
                                    if (m != null && m.item != null) set.Add(m.item);
                }
                catch (Exception e) { AutoSalvagePlugin.Log.LogWarning("Recipe scan failed: " + e.Message); }
                if (set.Count == 0) return true; // cannot tell: err on the side of keeping (stash) rather than selling
                _recipeIngredients = set;
                if (AutoSalvagePlugin.Cfg.Verbose.Value) AutoSalvagePlugin.Log.LogInfo("Recipe ingredients indexed: " + set.Count + " item types");
            }
            return _recipeIngredients.Contains(info);
        }

        internal static void ClearRecipeCache() { _recipeIngredients = null; }

        /// <summary>Sell / stash the given items of a character. With save=true the character is force-saved and the write
        /// verified, so the bag file and the stash file agree even for a character that is not in the party.</summary>
        private static void Apply(Character c, List<Item> items, string reason, bool save)
        {
            AutoSalvageConfig cfg = AutoSalvagePlugin.Cfg;
            float gold = 0f; int sold = 0, stashed = 0;
            ItemStashData stash = null;
            bool stashChecked = false;
            foreach (Item item in items)
            {
                Verdict v;
                try { v = Decide(item, c, cfg); } catch { continue; }
                if (v == Verdict.Keep) continue;
                if (v == Verdict.Stash)
                {
                    if (!stashChecked) { stash = ReadyStash(); stashChecked = true; }
                    if (stash == null) continue; // stash unavailable: leave the item in the bag rather than lose it
                }
                try
                {
                    if (v == Verdict.Sell)
                    {
                        float price = item.SellPrice;  // already per whole stack, like the shop
                        c.RemoveFromItemList(item);
                        c.GiveGold(price);
                        gold += price; sold++;
                        if (cfg.Verbose.Value) AutoSalvagePlugin.Log.LogInfo("Sold " + Describe(item) + " for " + price.ToString("0", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        int n = Math.Max(1, item.numStacks);
                        stash.AddItemToStash(item);   // removes it from the owner and stacks it in the stash
                        stashed += n;
                        if (cfg.Verbose.Value) AutoSalvagePlugin.Log.LogInfo("Stashed " + Describe(item));
                    }
                }
                catch (Exception e)
                {
                    AutoSalvagePlugin.Log.LogWarning("Could not process " + Describe(item) + ": " + e.Message);
                }
            }
            if (sold == 0 && stashed == 0) return;
            try { if (stashed > 0 && stash != null) stash.SaveStash(); } catch (Exception e) { AutoSalvagePlugin.Log.LogWarning("Stash save: " + e.Message); }
            if (save)
            {
                if (!SaveNow(c)) AutoSalvagePlugin.Log.LogError(c.CharacterName + ": bag changed but the character could not be saved; the change will be redone next launch and stashed stacks may be duplicated - check storage");
            }
            else { try { c.QueueCharacterSave(); } catch { } }
            string summary = (gold > 0 ? "+" + gold.ToString("N0", CultureInfo.InvariantCulture) + " gold, " : "") + "sold " + sold + ", stashed " + stashed;
            AutoSalvagePlugin.Log.LogInfo(c.CharacterName + " (" + reason + "): " + summary);
            if (cfg.ShowSummary.Value)
            {
                try { c.ShowOverheadMessage(summary, 1.5f, ColorUtility.ToHtmlStringRGB(Color.white)); } catch { }
            }
        }

        private static string Describe(Item item)
        {
            try { return item.ItemName + " (" + item.ItemInfo.Rarity + " " + item.ItemInfo.ItemType + " L" + item.itemLevel + (item.numStacks > 1 ? " x" + item.numStacks : "") + ")"; }
            catch { return "item"; }
        }
    }
}
