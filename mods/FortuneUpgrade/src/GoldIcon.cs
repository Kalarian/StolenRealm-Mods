using TMPro;
using UnityEngine;

namespace FortuneUpgrade
{
    /// <summary>
    /// The gold coin sprite. It lives in the game's "Money Icon TMP" sprite asset (sprites: "Gold", "Currency Icons TMP_1"),
    /// which only the tooltip footer references; every other text falls back to the default EmojiOne asset and renders a
    /// plain sprite tag as a yellow "?" box. So the tag names the asset explicitly, and the asset is registered with
    /// TextMeshPro's MaterialReferenceManager the first time we find it loaded, so the lookup by name succeeds anywhere.
    /// </summary>
    internal static class GoldIcon
    {
        public const string AssetName = "Money Icon TMP";
        private static bool _registered;

        public static string Tag(int size = 10)
        {
            Ensure();
            return "<size=" + size + "><sprite=\"" + AssetName + "\" name=\"Gold\"></size>";
        }

        public static void Ensure()
        {
            if (_registered) return;
            try
            {
                foreach (TMP_SpriteAsset sa in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
                {
                    if (sa == null || sa.name != AssetName) continue;
                    MaterialReferenceManager.AddSpriteAsset(sa);
                    _registered = true;
                    try { FortuneUpgradePlugin.Log.LogInfo("Gold icon: registered sprite asset '" + AssetName + "' (" + (sa.spriteCharacterTable != null ? sa.spriteCharacterTable.Count : 0) + " sprites)"); } catch { }
                    return;
                }
            }
            catch { }
        }
    }
}
