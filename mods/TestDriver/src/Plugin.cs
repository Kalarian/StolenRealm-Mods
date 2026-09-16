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
            _driver = new Driver(Log, Math.Max(1, Math.Min(6, party)));
            Log.LogMessage("TESTDRIVER: armed (party size " + party + ")");
        }

        private void Update()
        {
            if (_driver != null) _driver.Tick();
        }
    }
}
