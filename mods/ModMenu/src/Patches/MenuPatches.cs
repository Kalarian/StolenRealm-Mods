namespace ModMenu.Patches
{
    /// <summary>The menu needs no Harmony patches; this container exists so the generated Plugin.cs has something to
    /// iterate ("Patched 0 methods"). Reset() tears the window down when the mod is switched off.</summary>
    internal static class MenuPatches
    {
        internal static void Reset() { MenuWindow.Reset(); }
    }
}
