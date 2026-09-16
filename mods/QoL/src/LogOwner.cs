using System;
using BepInEx.Logging;

namespace QoL
{
    /// <summary>
    /// Stamps the log with who it belongs to, so a LogOutput.log sent by a friend is identifiable.
    /// Windows user/machine immediately; Steam persona name + SteamID once the game has initialised Steam
    /// (Facepunch.Steamworks, the library the game itself uses).
    /// </summary>
    internal static class LogOwner
    {
        private static bool _steamLogged;
        private static float _nextTry;

        public static void LogMachine(ManualLogSource log)
        {
            try
            {
                log.LogInfo("Log owner: Windows user '" + Environment.UserName + "' on '" + Environment.MachineName + "', " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            }
            catch { }
        }

        /// <summary>Call from Update; logs the Steam identity once it is available (cheap, retries every 2 s until done).</summary>
        public static void TryLogSteam(ManualLogSource log)
        {
            if (_steamLogged || UnityEngine.Time.unscaledTime < _nextTry) return;
            _nextTry = UnityEngine.Time.unscaledTime + 2f;
            try
            {
                if (!Steamworks.SteamClient.IsValid) return;
                string name = Steamworks.SteamClient.Name;
                ulong id = Steamworks.SteamClient.SteamId.Value;
                log.LogInfo("Log owner: Steam '" + name + "' (SteamID " + id + ")");
                _steamLogged = true;
            }
            catch (Exception)
            {
                // Steam not up yet or not present (e.g. headless); keep retrying quietly.
            }
        }
    }
}
