using System;
using System.Globalization;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace FortuneUpgrade.Patches
{
    /// <summary>
    /// Hovering a FortuneSlot calls FortuneSlot.ShowTooltip -> Tooltip.ShowActionStatusTooltip -> Tooltip.ShowTooltip.
    /// We remember the hovered slot, append an "Upgrade" line to that tooltip, and on the upgrade key ask for
    /// confirmation, charge the party (ShopMenusManager.SpendGoldAsParty, the same call item upgrades use) and raise
    /// the fortune with Character.AddFortune (which re-applies it, saves, and lets SharedFortunes pool the new level).
    /// Cost = two-handed weapon price of the same rarity: GlobalSettings.GetItemPurchasePrice without an Item, i.e.
    /// QuestGold(level) x ItemGoldRatioBase x rarity multiplier x weapon multiplier x TwoHandedGoldModifier, rounded.
    /// </summary>
    internal static class UpgradePatches
    {
        private static FortuneSlot _hovered;
        private static string _pending;

        /// <summary>Forget the hovered slot when the mod is switched off (its HideTooltip postfix would no longer clear it).</summary>
        /// <summary>True while the keyboard focus is in any text field (chat, the Fortune window's search bar, inventory search...).</summary>
        internal static bool TypingInAnyTextField()
        {
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null) return false;
                var f = go.GetComponent<UnityEngine.UI.InputField>();
                if (f != null && f.isFocused) return true;
                var t = go.GetComponent<TMPro.TMP_InputField>();
                if (t != null && t.isFocused) return true;
            }
            catch { }
            return false;
        }

        internal static void Reset() { _hovered = null; _pending = null; }

        [HarmonyPatch(typeof(FortuneSlot), nameof(FortuneSlot.ShowTooltip))]
        private static class FortuneSlot_ShowTooltip
        {
            private static void Prefix(FortuneSlot __instance)
            {
                _hovered = null; _pending = null;
                try
                {
                    FortuneUpgradeConfig cfg = FortuneUpgradePlugin.Cfg;
                    if (cfg == null || !cfg.Enabled.Value || __instance == null || __instance.FortuneSaveData == null) return;
                    _hovered = __instance;
                    Character c = GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null;
                    string why;
                    float cost;
                    int target;
                    if (CanUpgrade(__instance.FortuneSaveData, c, out target, out cost, out why))
                    {
                        _pending = "\n\n<color=#CBB396>" + OptionsManager.Localize("Upgrade") + "</color> " + "to" + " L" + target + ": "
                            + "<color=#FFFFFF>" + Gold(cost) + "</color> <size=10><sprite name=\"Gold\"></size> <color=#9AA5B1>(" + "press" + " " + cfg.UpgradeKey.Value.MainKey + ")</color>";
                    }
                    else if (!string.IsNullOrEmpty(why))
                    {
                        _pending = "\n\n<color=#9AA5B1>" + why + "</color>";
                    }
                }
                catch (Exception e)
                {
                    if (FortuneUpgradePlugin.Cfg != null && FortuneUpgradePlugin.Cfg.Verbose.Value) FortuneUpgradePlugin.Log.LogWarning("hover: " + e);
                }
            }

            private static void Postfix() { _pending = null; }
        }

        [HarmonyPatch(typeof(FortuneSlot), nameof(FortuneSlot.HideTooltip))]
        private static class FortuneSlot_HideTooltip
        {
            private static void Postfix(FortuneSlot __instance)
            {
                if (_hovered == __instance) _hovered = null;
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowTooltip))]
        private static class Tooltip_ShowTooltip
        {
            private static void Prefix(ref string description)
            {
                if (string.IsNullOrEmpty(_pending)) return;
                description = (description ?? "") + _pending;
                _pending = null;
            }
        }

        internal static void Tick()
        {
            FortuneUpgradeConfig cfg = FortuneUpgradePlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || _hovered == null) return;
            // Never trust a flag for this: the dialog can be closed by Escape or a scene change without firing our callbacks.
            if (ConfirmWindow.Instance != null && ConfirmWindow.Instance.gameObject.activeInHierarchy) return;
            if (!cfg.UpgradeKey.Value.IsDown()) return;
            // Typing "u" into co-op chat or a search box with the mouse resting on a fortune must not trigger an upgrade.
            try { if (MessageWindowManager.instance != null && MessageWindowManager.instance.TextInputIsFocused) return; } catch { }
            if (TypingInAnyTextField()) return; // game 1.3.1 added Fortune and inventory search bars: a typed 'u' is not a hotkey
            FortuneSlot slot = _hovered;
            FortuneSaveData data = slot.FortuneSaveData;
            Character c = GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null;
            int target = 0; float cost = 0f; string why = null;
            if (data == null || !CanUpgrade(data, c, out target, out cost, out why))
            {
                if (!string.IsNullOrEmpty(why) && ConfirmWindow.Instance != null) ConfirmWindow.Instance.ShowPopupMessage("Upgrade Fortune", why);
                return;
            }
            ActionStatusInfo info = FortuneWindow.GetFortuneByGuid(data.Guid);
            string name = info != null ? OptionsManager.Localize(info.Name) : data.Guid;
            string msg = name + "\nL" + data.Level.ToString("0", CultureInfo.InvariantCulture) + " -> L" + target + "\n\n" + "Cost" + ": " + Gold(cost) + " <size=10><sprite name=\"Gold\"></size>";
            if (ConfirmWindow.Instance == null) { DoUpgrade(c, data, target, cost, name); return; }
            ConfirmWindow.Instance.ShowConfirmMessage("Upgrade Fortune", msg,
                () => { },
                () => { try { DoUpgrade(c, data, target, cost, name); } catch (Exception e) { FortuneUpgradePlugin.Log.LogError("upgrade: " + e); } });
        }

        private static void DoUpgrade(Character c, FortuneSaveData data, int target, float cost, string name)
        {
            if (ShopMenusManager.Instance != null && ShopMenusManager.Instance.PartyGold < cost)
            {
                ConfirmWindow.Instance?.ShowPopupMessage("Upgrade Fortune", "Not enough gold");
                return;
            }
            if (ShopMenusManager.Instance != null) ShopMenusManager.Instance.SpendGoldAsParty(cost);
            float before = data.Level;
            c.AddFortune(data.Guid, target);   // raises Level, re-applies, saves; SharedFortunes' postfix pools it
            if (data.Level < target) { data.Level = target; c.LoadFortunes(); c.QueueCharacterSave(); }
            data.IsNew = false;
            try
            {
                FortuneWindow fw = LoadableUIWindow<FortuneWindow>.Instance;
                if (fw != null && fw.gameObject.activeInHierarchy) fw.UpdateFortuneWindow(c);
            }
            catch { }
            FortuneUpgradePlugin.Log.LogInfo(c.CharacterName + " upgraded " + name + " L" + before.ToString("0", CultureInfo.InvariantCulture) + " -> L" + target + " for " + Gold(cost) + " gold");
            GUIManager.instance?.tooltip?.HideTooltip();
        }

        /// <summary>Whether the hovered fortune can be upgraded now; fills target level, cost and a reason when not.</summary>
        private static bool CanUpgrade(FortuneSaveData data, Character c, out int target, out float cost, out string why)
        {
            target = 0; cost = 0f; why = null;
            FortuneUpgradeConfig cfg = FortuneUpgradePlugin.Cfg;
            if (c == null) return false;
            // A slot whose data is not in the character's own list is a catalogue entry (FortunePreview's greyed-out
            // unowned fortunes): upgrading it would hand the fortune out for free, so refuse.
            try { if (c.FortuneData == null || !c.FortuneData.Contains(data)) { why = null; return false; } } catch { return false; }
            if (GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.InBattle) { why = OptionsManager.Localize("You cannot change Fortunes while in combat!"); return false; }
            if (cfg.OnlyInTown.Value && (GUIManager.instance == null || GUIManager.instance.CurrentGuiState != GUIState.InTown))
            {
                why = OptionsManager.Localize("Upgrade") + ": " + "only in town";
                return false;
            }
            target = Mathf.Min(30, c.Level);
            int current = Mathf.RoundToInt(data.Level);
            if (current >= target) { why = OptionsManager.Localize("Upgrade") + ": " + "already at your level"; return false; }
            ActionStatusInfo info = FortuneWindow.GetFortuneByGuid(data.Guid);
            ItemQuality rarity = info != null ? info.Rarity : ItemQuality.Common;
            float full = TwoHandedPrice(target, rarity);
            float already = cfg.CostByLevelGap.Value ? TwoHandedPrice(current, rarity) : 0f;
            float mult = cfg.CostMultiplier.Value;
            cost = Mathf.Max(0f, (full - already) * mult);
            cost = Rounder.Round(cost, GlobalSettingsManager.instance.globalSettings.PriceRoundToNearest);
            if (cfg.Verbose.Value)
                FortuneUpgradePlugin.Log.LogInfo("cost " + (info != null ? info.Name : data.Guid) + " " + rarity + " L" + current + "->L" + target + ": full " + full + " - already " + already + " x" + mult + " = " + cost);
            return true;
        }

        /// <summary>GlobalSettings.GetItemPurchasePrice for a hypothetical two-handed weapon of this rarity.</summary>
        private static float TwoHandedPrice(int level, ItemQuality rarity)
        {
            GlobalSettings gs = GlobalSettingsManager.instance.globalSettings;
            float questGold = gs.GetQuestGold(level);
            float rar = gs.GoldMultipliersPerItemRarity.Where(x => x.rarity == rarity).Select(x => x.multiplier).DefaultIfEmpty(1f).First();
            float typ = gs.GoldMultipliersPerItemType.Where(x => x.itemType == ItemType.Weapon).Select(x => x.multiplier).DefaultIfEmpty(1f).First();
            float raw = questGold * gs.ItemGoldRatioBase * rar * typ * gs.TwoHandedGoldModifier;
            return Rounder.Round(raw, gs.PriceRoundToNearest);
        }

        private static string Gold(float v)
        {
            return v.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
