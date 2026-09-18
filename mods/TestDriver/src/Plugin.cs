using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;

namespace TestDriver
{
    /// <summary>
    /// Development-only smoke-test driver. Does NOTHING unless the game was launched with the "-srtest" command-line
    /// argument, so it is inert during normal play even if left installed. Never ships in the friends' pack.
    /// With -srtest: waits for the game to load, starts a single-player session with the first few campaign characters,
    /// opens the Fortune window once in town, starts the game's built-in debug test battle, hands the party to the
    /// game's own AI, holds the threat overlay for a moment, waits for the post-battle screen, then quits.
    /// Every step logs "TESTDRIVER: ..." so a run can be judged from LogOutput.log alone.
    /// </summary>
    [BepInPlugin("stolenrealm.testdriver", "TestDriver", "1.0.0")]
    public class TestDriverPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Driver _driver;

        private void Awake()
        {
            Log = Logger;
            string[] args = Environment.GetCommandLineArgs();
            bool armed = args.Any(a => a.Equals("-srtest", StringComparison.OrdinalIgnoreCase));
            int party = 3;
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srparty", StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out party);
            if (!armed) { Log.LogInfo("TestDriver idle (launch with -srtest to run the smoke test)"); return; }
            Driver.UseGameAi = args.Any(a => a.Equals("-srai", StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srchars", StringComparison.OrdinalIgnoreCase)) Driver.WantedNames = args[i + 1].Split(',');
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srbattle", StringComparison.OrdinalIgnoreCase)) Driver.BattleName = args[i + 1];
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srscenario", StringComparison.OrdinalIgnoreCase)) Driver.ScenarioName = args[i + 1];
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srseed", StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out Driver.Seed);
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srdifficulty", StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out Driver.Difficulty);
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srrotation", StringComparison.OrdinalIgnoreCase)) Driver.RotationPath = args[i + 1];
            for (int i = 0; i < args.Length; i++) if (args[i].Equals("-srresetruns", StringComparison.OrdinalIgnoreCase)) Driver.ResetRunsBetweenFights = true;
            for (int i = 0; i < args.Length; i++) if (args[i].Equals("-srresumeruns", StringComparison.OrdinalIgnoreCase)) Driver.ResumeRunsBetweenFights = true;
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srfights", StringComparison.OrdinalIgnoreCase))
                foreach (string f in args[i + 1].Split(',')) { string[] kv = f.Split(':'); int sd = 0; if (kv.Length > 1) int.TryParse(kv[1], out sd); Driver.Fights.Add(new System.Collections.Generic.KeyValuePair<string, int>(kv[0].Trim(), sd)); }
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srspeed", StringComparison.OrdinalIgnoreCase)) { float sp; if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sp)) Driver.Speed = Math.Max(1f, Math.Min(20f, sp)); }
            // -srsaves <dir>: redirect every save/pool file (the game and all our mods go through FileSystem.persistentDataPath,
            // which prefers the private cached field) so several instances can run at once and the real saves are never touched.
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("-srsaves", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string dir = args[i + 1];
                    System.IO.Directory.CreateDirectory(dir);
                    var f = typeof(FileSystem).GetField("_cachedPersistentDataPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) { f.SetValue(null, dir); Log.LogMessage("TESTDRIVER: saves redirected to " + dir + " (FileSystem.persistentDataPath = " + FileSystem.persistentDataPath + ")"); }
                    else Log.LogWarning("TESTDRIVER: FileSystem._cachedPersistentDataPath not found; saves NOT redirected");
                }
                catch (Exception e) { Log.LogWarning("TESTDRIVER: save redirect failed: " + e.Message); }
            }
            // The game switches Unity's logger off, so an exception inside a game coroutine (e.g. battle setup) is invisible in
            // LogOutput.log. While the driver is armed, turn it back on and echo errors/exceptions with their stack.
            try
            {
                UnityEngine.Debug.unityLogger.logEnabled = true;
                UnityEngine.Application.logMessageReceived += (msg, stack, type) =>
                {
                    if (type == UnityEngine.LogType.Exception || type == UnityEngine.LogType.Error)
                        Log.LogWarning("UNITY " + type + ": " + msg + (string.IsNullOrEmpty(stack) ? "" : "\n" + stack));
                };
            }
            catch (Exception e) { Log.LogWarning("could not hook Unity log: " + e.Message); }
            _driver = new Driver(Log, Math.Max(1, Math.Min(6, party)));
            Log.LogMessage("TESTDRIVER: armed (party size " + party + ")");
        }

        private void Update()
        {
            if (_driver != null) _driver.Tick();
        }
    }
}
