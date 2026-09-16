using System;
using Burst2Flame;
using Burst2Flame.Observable;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TargetTooltip.Patches
{
    /// <summary>
    /// Port of the "target-hover action tooltip" feature from Eradev's BetterTooltips (GPL-3.0, 2023),
    /// updated for the 2025 game build. When the hovered battle cell is a valid target, a tooltip names the
    /// action that a click would fire: the basic attack the game will pick (Movement state + attack cursor),
    /// or the currently selected skill (Action state). Everything else in BetterTooltips is not ported.
    ///
    /// The hovered-cell setter runs every frame from GUIManager's raycast (it early-returns when the cell is
    /// unchanged, but a Harmony postfix still runs), so we track what we last showed and only touch the tooltip
    /// when the target/action changes, and we hide our own tooltip when the cell stops being a valid target
    /// (the game's 'hideOnNotHoveringGO' flag that upstream relied on is never read by this build).
    /// </summary>
    internal static class HoverTooltipPatches
    {
        // Reentrancy guard: true while we are calling into Tooltip.ShowTooltip/ShowActionTooltip ourselves.
        private static bool _showing;
        // True while the visible tooltip is one we put up (cleared when any other code shows a tooltip).
        private static bool _shownByUs;
        // True while our compact tooltip has the background Image disabled; the ShowTooltip prefix restores it.
        private static bool _imageDisabled;
        // Damage-vs-target text to append to the tooltip being shown (consumed by the ShowTooltip prefix).
        private static string _pendingPreview;
        // The ground-effect tooltip text the game built for the hovered cell (captured so we can fold it into ours).
        private static string _groundText;

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowGroundEffectTooltip))]
        private static class Tooltip_ShowGroundEffectTooltip
        {
            private static void Postfix(Tooltip __instance)
            {
                try
                {
                    _groundText = (__instance.ShowingGroundEffect && __instance.Description != null) ? __instance.Description.text : null;
                }
                catch { _groundText = null; }
            }
        }

        private static HexCell _lastCell;
        private static ActionInfo _lastAction;
        private static PlayerState _lastState;
        private static TooltipStyle _lastStyle;

        [HarmonyPatch(typeof(HexCellManager), nameof(HexCellManager.CurrentlyHoveringHexCell), MethodType.Setter)]
        private static class HexCellManager_CurrentlyHoveringHexCell_Set
        {
            private static void Postfix(HexCellManager __instance)
            {
                try
                {
                    OnHoverChanged(__instance);
                }
                catch (Exception e)
                {
                    // Never let a tooltip problem break the game's hover handling.
                    if (TargetTooltipPlugin.Cfg != null && TargetTooltipPlugin.Cfg.Verbose.Value)
                        TargetTooltipPlugin.Log.LogWarning("Hover tooltip error: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowTooltip))]
        private static class Tooltip_ShowTooltip
        {
            private static void Prefix(Tooltip __instance, ref string description)
            {
                // Damage preview line for the tooltip we are showing right now.
                if (_showing && !string.IsNullOrEmpty(_pendingPreview))
                {
                    description = string.IsNullOrEmpty(description) ? _pendingPreview : description + "\n\n" + _pendingPreview;
                    _pendingPreview = null;
                }
                // Any tooltip shown after our compact one gets its background back. Restore on the MAIN tooltip: the item
                // comparison tooltip is a second Tooltip instance that also passes through here.
                if (_imageDisabled)
                {
                    Tooltip main = GUIManager.instance != null ? GUIManager.instance.tooltip : __instance;
                    Image img = (main != null ? main : __instance).GetComponent<Image>();
                    if (img != null) img.enabled = true;
                    _imageDisabled = false;
                }
                // Someone else (skill bar, item, status...) is showing a tooltip: it is no longer ours to hide.
                if (!_showing) _shownByUs = false;
            }
        }

        private static void OnHoverChanged(HexCellManager hcm)
        {
            TargetTooltipConfig cfg = TargetTooltipPlugin.Cfg;
            GUIManager gui = GUIManager.instance;
            Tooltip tooltip = gui != null ? gui.tooltip : null;
            if (cfg == null || tooltip == null) return;

            ActionInfo action = null;
            HexCell cell = null;
            PlayerState state = PlayerState.Waiting;
            bool enemyCell = false;

            GameLogic gl = GameLogic.instance;
            if (cfg.Enabled.Value && gl != null && CursorManager.instance != null
                && !gl.CurrentlyPlacingCharacters && !gl.gameComplete
                && gui.CurrentGuiState == GUIState.InBattle
                && !CursorManager.instance.HoveringUseableObject)
            {
                cell = hcm.CurrentlyHoveringHexCell;
                Character selected = gl.CurrentlySelectedCharacter;
                PlayerMovement pm = hcm.MyPlayer;
                if (cell != null && selected != null && pm != null && selected.Cell != null && cell != selected.Cell)
                {
                    enemyCell = cell.HasEnemy(selected);
                    state = hcm.CurrentState;
                    switch (state)
                    {
                        case PlayerState.Movement:
                            if (enemyCell && CursorManager.instance.CurrentCursorType == CursorType.Attack)
                                action = PickBasicAttack(pm, selected, cell);
                            break;

                        case PlayerState.Action:
                            if ((enemyCell || cfg.ShowOnEmptyCells.Value) && pm.CurrentAction != null && IsValidCellForAction(pm, selected, cell))
                                action = pm.CurrentAction;
                            break;
                    }
                }
            }

            if (action == null)
            {
                // Not a valid target any more: take down our tooltip (only ours) and forget what we showed.
                if (_shownByUs && tooltip.gameObject.activeSelf)
                {
                    tooltip.HideTooltip();
                    RestoreTooltipVisuals();
                }
                _shownByUs = false;
                _lastCell = null;
                _lastAction = null;
                return;
            }

            bool unchanged = _shownByUs && tooltip.gameObject.activeSelf
                && ReferenceEquals(cell, _lastCell) && ReferenceEquals(action, _lastAction)
                && state == _lastState && cfg.Style.Value == _lastStyle;
            if (unchanged) return;

            // The game's ground-effect tooltip (if any) was shown for this cell just before us; keep its text and replace it.
            string ground = (cfg.IncludeTileEffects.Value && tooltip.ShowingGroundEffect) ? _groundText : null;
            // Hiding first avoids a stale tooltip flickering under the new one (same trick as upstream).
            tooltip.HideTooltip();
            Character target = enemyCell ? cell.Player : null;
            Show(tooltip, action, cfg, target, ground);
            // The game's ShowActionTooltip returns without showing anything while skill selection is disabled or no
            // player is selected; then nothing is ours and the parked preview must not wait for the next tooltip.
            if (!tooltip.gameObject.activeSelf) { _shownByUs = false; _pendingPreview = null; _lastCell = null; _lastAction = null; return; }
            _shownByUs = true;
            _lastCell = cell;
            _lastAction = action;
            _lastState = state;
            _lastStyle = cfg.Style.Value;
            if (cfg.Verbose.Value)
                TargetTooltipPlugin.Log.LogInfo("Hover " + cell.Coordinates + " (" + state + (enemyCell ? ", enemy" : "") + ") -> " + action.name);
        }

        /// <summary>Same order the game uses in PlayerMovement when deciding which basic attack a click will fire.</summary>
        private static ActionInfo PickBasicAttack(PlayerMovement pm, Character selected, HexCell cell)
        {
            var attacks = selected.BasicAttacks;
            if (attacks == null || attacks.Count == 0) return null;
            foreach (ActionInfo a in attacks)
                if (a != null && pm.CanCast(new StructList<HexCell> { cell }, a).CanCast) return a;
            foreach (ActionInfo a in attacks)
                if (a != null && pm.CanCast(new StructList<HexCell> { cell }, a).OnlyNeedsRange) return a;
            return attacks[0];
        }

        private static bool IsValidCellForAction(PlayerMovement pm, Character selected, HexCell cell)
        {
            if (!pm.CanCast(new StructList<HexCell> { cell }, pm.CurrentAction).CanCast) return false;
            return !cell.GetLineOfSightHitPoint(selected.Cell).HasValue;
        }

        private static void Show(Tooltip tooltip, ActionInfo action, TargetTooltipConfig cfg, Character target, string groundText)
        {
            SkillInfo skill = Game.Instance != null ? Game.Instance.GetSkillFromActionInfo(action) : null;
            _pendingPreview = null;
            if (!string.IsNullOrEmpty(groundText))
                _pendingPreview = "<color=#CBB396>" + OptionsManager.Localize("On this tile") + "</color>\n" + groundText.Trim();
            if (cfg.DamagePreview.Value && target != null)
            {
                try
                {
                    Character src = GameLogic.instance != null ? GameLogic.instance.CurrentlySelectedCharacter : null;
                    string preview = DamagePreview.Build(src, target, action, cfg);
                    if (cfg.Verbose.Value && preview != null) TargetTooltipPlugin.Log.LogInfo("Preview " + action.name + " -> " + target.LocalizedCharacterName + ": " + preview.Replace("\n", " | "));
                    if (!string.IsNullOrEmpty(preview))
                        _pendingPreview = string.IsNullOrEmpty(_pendingPreview) ? preview : preview + "\n\n" + _pendingPreview;
                }
                catch (Exception e)
                {
                    // keep the tile-effect text even if the preview failed
                    if (cfg.Verbose.Value) TargetTooltipPlugin.Log.LogWarning("Damage preview failed: " + e);
                }
            }
            string title = (action.OverrideDescriptionAndName || skill == null)
                ? OptionsManager.Localize(action.ActionName)
                : OptionsManager.Localize(skill.SkillName);
            Sprite icon = action.IconOverride != null ? action.IconOverride : (skill != null ? skill.Icon : null);

            _showing = true;
            try
            {
                if (cfg.Style.Value == TooltipStyle.Full && skill != null)
                {
                    tooltip.ShowActionTooltip(new ActionAndSkill { ActionInfo = action, SkillInfo = skill }, null);
                    return;
                }

                tooltip.ShowTooltip(title, "", icon, "", null, Color.white, null, icon != null, false, 0.7f);
                Image img = tooltip.GetComponent<Image>();
                if (img != null)
                {
                    img.enabled = false;
                    _imageDisabled = true;
                }
                if (tooltip.TitleSep != null) tooltip.TitleSep.SetActive(false);
            }
            finally
            {
                _showing = false;
            }
        }

        internal static void RestoreTooltipVisuals()
        {
            if (!_imageDisabled) return;
            GUIManager gui = GUIManager.instance;
            if (gui != null && gui.tooltip != null)
            {
                Image img = gui.tooltip.GetComponent<Image>();
                if (img != null) img.enabled = true;
            }
            _imageDisabled = false;
        }
    }
}
