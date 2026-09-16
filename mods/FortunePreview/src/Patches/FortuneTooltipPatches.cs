using System;
using Burst2Flame;
using HarmonyLib;

namespace FortunePreview.Patches
{
    /// <summary>
    /// Every fortune tooltip (Fortune window slots, status links in event text, the event-window links we add) ends in
    /// Tooltip.ShowActionStatusTooltip with a StatusType.Fortune ActionStatusInfo, which then calls ShowTooltip once.
    /// We park a "Source" section here and the shared ShowTooltip prefix (QuestTooltipPatches) appends it.
    /// </summary>
    internal static class FortuneTooltipPatches
    {
        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.ShowActionStatusTooltip),
            new[] { typeof(ActionStatusInfo), typeof(Character), typeof(Character), typeof(Item), typeof(PersistentDurationType), typeof(string), typeof(float), typeof(bool), typeof(string), typeof(ActionStatus) })]
        private static class Tooltip_ShowActionStatusTooltip
        {
            private static void Prefix(ActionStatusInfo actionStatusInfo)
            {
                try
                {
                    FortunePreviewConfig cfg = FortunePreviewPlugin.Cfg;
                    if (cfg == null || actionStatusInfo == null || actionStatusInfo.StatusType != StatusType.Fortune) return;
                    string src = "";
                    if (cfg.SourceInTooltip.Value && actionStatusInfo.LinkedEventStatus != null)
                        src = FortuneSources.Describe(actionStatusInfo.LinkedEventStatus, cfg) ?? "";
                    string note = FortuneWindowPatches.GhostNote; // hovering a greyed-out, unowned slot in the Fortune window
                    if (!string.IsNullOrEmpty(note)) src = "\n\n" + note + src;
                    if (!string.IsNullOrEmpty(src)) QuestTooltipPatches.SetPending(src);
                }
                catch (Exception e)
                {
                    if (FortunePreviewPlugin.Cfg != null && FortunePreviewPlugin.Cfg.Verbose.Value) FortunePreviewPlugin.Log.LogWarning("Fortune source failed: " + e);
                }
            }

            private static void Postfix()
            {
                QuestTooltipPatches.SetPending(null);
            }
        }
    }
}
