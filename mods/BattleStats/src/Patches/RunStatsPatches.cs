using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattleStats.Patches
{
    /// <summary>
    /// Run-wide stats: the same Stats window the game shows after a battle, opened from a HUD button (or a key) at any
    /// time with the totals of every battle since the party left town (RunStats). "Run mode" is a flag the window
    /// patches read: while it is set, StatsWindowPatches rewrites the game's rows and its own rows from the RunStats
    /// store instead of the live per-battle dictionary, and the title says how many battles are in.
    ///
    /// Fold: once per battle end, from the recorder's OpenPostBattleMenu prefix (after the recorder wrote its last
    /// numbers). The host folds at once; a client's replicated dictionary may still be catching up, so it folds the
    /// next time the numbers are needed (the run window opening, the post-battle screen closing, the next battle).
    /// Reset (every machine, no server-only hook): leaving town for the island (GUI state InTown -> InWorldMap), the
    /// defeat Retry (SendQuestFailChoice(retry) is an All RPC) and a Roguelike retry; going back to the main menu or
    /// character select only drops the run from memory, so the same quest can pick it up again. Arriving in town keeps
    /// the totals so the run just finished can still be read.
    /// The on-screen button is a clone of the post-battle menu's own Stats button, parked under the gold and difficulty
    /// box at the top right and hidden whenever any window, the post-battle screen or a loading screen is up.
    /// </summary>
    internal static class RunStatsPatches
    {
        internal static bool RunMode;                 // the Stats window is open (or opening) with run numbers
        internal static RunHistory.RunFile HistoryRun;    // set when the window shows a saved run instead of the live one
        private static readonly List<Character> _historySlots = new List<Character>();   // column -> the live Character standing in for HistoryRun.Characters[i]
        private static GameObject _historyButton;     // "History" button inside the Stats window
        private static RectTransform _anchorBox;      // the gold + difficulty panel the HUD button sits under
        private static bool _inBattle;                // Root.StartGameForAll seen, no battle end yet
        private static bool _wasInTown;               // GUI state has been InTown since the last island
        private static GUIState _lastGui = GUIState.InMainMenu;
        private static bool _guiSeen;
        private static bool _hudDumped;
        private static bool _buildFailed;             // the button could not be cloned; stop retrying until the next reload
        private static int _opaqueTicks;              // frames left to re-assert the button's own colours after cloning
        private static bool _resumeChecked;           // an unfinished run was looked for once since entering the game
        private static int _rowMismatchLogged;

        // HUD button
        private static GameObject _button;
        private static Button _btn;
        private static DisabledButtonTooltip _tip;
        private static string _tipText;

        // title
        private static TextMeshProUGUI _title;
        private static string _origTitle;
        private static Vector2 _origTitleSize;
        private static bool _origTitleWrap;
        private static bool _titleChanged;

        private static BattleStatsConfig Cfg => BattleStatsPlugin.Cfg;
        private static bool Verbose => Cfg != null && Cfg.Verbose.Value;
        private static bool IsServer { get { try { return NetworkingManager.Instance != null && NetworkingManager.Instance.IsServer; } catch { return false; } } }
        private static GUIState Gui { get { try { return GUIManager.instance != null ? GUIManager.instance.CurrentGuiState : GUIState.InMainMenu; } catch { return GUIState.InMainMenu; } } }
        internal static bool InBattle => _inBattle;
        private static bool WindowOpenInRunMode { get { return RunMode && StatsWindowOpen(); } }

        /// <summary>Is the Stats window actually on screen? The game loads it once and leaves the object ACTIVE with its
        /// Content switched off (StatManager.InitSingleton), so "the object is active" means loaded, not open. Open means
        /// the game registered it as a window, or its Content is showing.</summary>
        private static bool StatsWindowOpen()
        {
            try
            {
                StatManager sm = StatManager.Instance;
                if (sm == null || !sm.gameObject.activeInHierarchy) return false;
                try { if (UIWindowManager.Instance != null && UIWindowManager.Instance.OpenedWindows != null && UIWindowManager.Instance.OpenedWindows.Contains(sm)) return true; } catch { }
                return sm.Content != null && sm.Content.activeInHierarchy;
            }
            catch { return false; }
        }

        internal static void Reset()
        {
            try { if (WindowOpenInRunMode) StatManager.Instance.CloseWindow(); } catch { }
            RestoreTitle();
            RunMode = false; HistoryRun = null; _historySlots.Clear();
            _inBattle = false; _wasInTown = false; _guiSeen = false; _resumeChecked = false; _buildFailed = false; _rowMismatchLogged = 0;
            try { if (_button != null) UnityEngine.Object.Destroy(_button); } catch { }
            try { if (_historyButton != null) UnityEngine.Object.Destroy(_historyButton); } catch { }
            _button = null; _btn = null; _tip = null; _tipText = null; _historyButton = null; _anchorBox = null;
            RunHistoryWindow.Reset();
            RunStats.Clear("mod switched off");
        }

        // ---------- battle boundaries ----------

        /// <summary>Called by the recorder's OpenPostBattleMenu prefix after it finished its own summary (host and clients).</summary>
        internal static void OnBattleEnded(bool victory)
        {
            _inBattle = false;
            int turns = 0; try { turns = GameLogic.instance != null ? GameLogic.instance.turnNumber : 0; } catch { }
            RunStats.BattleEnded(victory, turns);
            if (IsServer) { if (RunStats.Fold("battle end" + (victory ? "" : ", defeat"))) SaveRun("battle end"); }
            else if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats: battle ended on a client; folding when the numbers are next needed");
            try { if (WindowOpenInRunMode) StatManager.Instance.CloseWindow(); } catch { }
            UpdateButton();
        }

        /// <summary>Write the run so far to its own file (rewritten after every battle, so nothing is lost to a crash).</summary>
        internal static void SaveRun(string why)
        {
            if (Cfg == null || !Cfg.SaveRuns.Value || RunStats.Battles <= 0) return;
            try
            {
                RunHistory.RunFile file = RunStats.BuildFile();
                if (file.Characters.Count == 0) { BattleStatsPlugin.Log.LogInfo("Run stats: nothing to save (no character has any numbers in this run)"); return; }
                RunStats.FilePath = RunHistory.Save(file, RunStats.FilePath);
                RunHistory.Prune(Cfg.KeepRuns.Value, RunStats.FilePath);
                if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats: saved " + System.IO.Path.GetFileName(RunStats.FilePath) + " (" + RunStats.Battles + " battles, " + why + ")");
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run stats: could not save the run (" + e.Message + ")"); }
        }

        /// <summary>Fold a battle a client could not fold at the time, and save the run when that happens.</summary>
        private static void EnsureFolded(string why)
        {
            if (!RunStats.FoldPending) return;
            if (RunStats.Fold(why)) SaveRun(why);
        }

        [HarmonyPatch(typeof(Root), nameof(Root.StartGameForAll))]
        private static class Root_StartGameForAll
        {
            private static void Prefix() { try { EnsureFolded("next battle starting"); } catch { } }
            private static void Postfix() { _inBattle = true; UpdateButton(); }
        }

        [HarmonyPatch(typeof(PostBattleManager), nameof(PostBattleManager.CloseWindow))]
        private static class PostBattleManager_CloseWindow
        {
            private static void Postfix() { try { EnsureFolded("post-battle screen closed"); UpdateButton(); } catch { } }
        }

        // the post-battle Stats button always shows the battle, never the run
        [HarmonyPatch(typeof(PostBattleManager), nameof(PostBattleManager.OpenStats))]
        private static class PostBattleManager_OpenStats
        {
            private static void Prefix()
            {
                try { if (WindowOpenInRunMode) StatManager.Instance.CloseWindow(); } catch { }
                RunMode = false;
            }
        }

        // ---------- run boundaries ----------

        [HarmonyPatch(typeof(Root), nameof(Root.SendQuestFailChoice))]
        private static class Root_SendQuestFailChoice
        {
            private static void Postfix(bool retry) { if (retry) ResetRun("quest retried"); }
        }

        [HarmonyPatch(typeof(RoguelikeManager), nameof(RoguelikeManager.StartRoguelikeRetryWithSameSetupAsLastRun))]
        private static class RoguelikeManager_Retry
        {
            private static void Prefix() { ResetRun("roguelike retry"); }
        }

        // a Roguelike run has no town to come home to: it ends here, win or lose
        [HarmonyPatch(typeof(Root), nameof(Root.SendRoguelikeComplete))]
        private static class Root_SendRoguelikeComplete
        {
            private static void Postfix(bool victory)
            {
                try
                {
                    EnsureFolded("roguelike run over");
                    CloseRunFile();     // the totals stay on screen until the next run starts, like a campaign run does in town
                    BattleStatsPlugin.Log.LogInfo("Run stats: the roguelike run " + (victory ? "was completed" : "ended") + " after " + RunStats.Battles + (RunStats.Battles == 1 ? " battle" : " battles") + "; its file is closed");
                    UpdateButton();
                }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run stats: closing the roguelike run failed (" + e.Message + ")"); }
            }
        }

        [HarmonyPatch(typeof(Root), nameof(Root.SendStartNewRoguelikeArea))]
        private static class Root_SendStartNewRoguelikeArea
        {
            private static void Postfix() { if (Cfg != null && Cfg.RoguelikeAreaResets.Value) ResetRun("new roguelike area"); }
        }

        [HarmonyPatch(typeof(TownManager), nameof(TownManager.OpenTown))]
        private static class TownManager_OpenTown
        {
            private static void Postfix() { try { if (WindowOpenInRunMode) StatManager.Instance.CloseWindow(); } catch { } }
        }

        /// <summary>The run is over for good (town, retry, a new quest): its file is closed so it is never picked up again.</summary>
        private static void ResetRun(string reason)
        {
            CloseRunFile();
            DropRun(reason);
        }

        /// <summary>Forget the run in memory but leave its file open, so closing the game and coming back to the same
        /// quest continues it instead of starting over.</summary>
        private static void DropRun(string reason)
        {
            try { if (WindowOpenInRunMode) StatManager.Instance.CloseWindow(); } catch { }
            _inBattle = false;
            RunStats.Clear(reason);
            UpdateButton();
        }

        private static void CloseRunFile()
        {
            if (Cfg == null || !Cfg.SaveRuns.Value || RunStats.Battles <= 0 || string.IsNullOrEmpty(RunStats.FilePath)) return;
            try
            {
                RunStats.Finished = true;          // BuildFile writes this as "closed": the run is never picked up again
                RunHistory.RunFile f = RunStats.BuildFile();
                RunHistory.Save(f, RunStats.FilePath);
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run stats: could not close the run file (" + e.Message + ")"); }
        }

        /// <summary>Test hook (the harness calls this by reflection): forget the run the way closing the game does, so
        /// the next tick has to find it again through TryResume. Never called during normal play.</summary>
        internal static void SimulateRestart()
        {
            DropRun("simulated restart");
            _resumeChecked = false;
        }

        /// <summary>After a restart, pick the run back up: the newest unfinished run saved for this quest and this party.</summary>
        private static void TryResume()
        {
            if (_resumeChecked || Cfg == null || !Cfg.SaveRuns.Value || !Cfg.ResumeRuns.Value) return;
            GUIState g = Gui;
            if (g != GUIState.InWorldMap && g != GUIState.InBattle) return;   // only out on a quest; in town the run is over
            string questId = RunStats.CurrentQuestId();
            if (string.IsNullOrEmpty(questId)) return;                        // the quest is not loaded yet, try again next frame
            List<Character> party = null;
            try { party = NetworkingManager.Instance != null ? NetworkingManager.Instance.PartyCharacters : null; } catch { }
            if (party == null || party.Count == 0) return;
            _resumeChecked = true;
            if (RunStats.Battles > 0) return;                                 // a run is already going in this session
            try
            {
                var names = new HashSet<string>(party.Where(c => c != null).Select(c => c.CharacterName));
                foreach (RunHistory.RunFile f in RunHistory.LoadAll())        // newest first
                {
                    if (f.Closed || f.Battles <= 0 || string.IsNullOrEmpty(f.QuestId) || f.QuestId != questId) continue;
                    if (!new HashSet<string>(f.Characters.Select(c => c.Name)).SetEquals(names)) continue;
                    RunStats.Adopt(f);
                    UpdateButton();
                    BattleStatsPlugin.Log.LogInfo("Run stats: resumed " + System.IO.Path.GetFileName(f.Path) + " (" + f.Battles + (f.Battles == 1 ? " battle" : " battles") + " already fought on this quest)");
                    return;
                }
                if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats: nothing to resume for this quest (" + questId + ")");
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run stats: resume failed (" + e.Message + ")"); }
        }

        // ---------- the window in run mode ----------

        /// <summary>Open the Stats window with the run totals. False when the game is not ready for it.</summary>
        internal static bool OpenRunWindow()
        {
            try
            {
                if (GUIManager.instance == null || NetworkingManager.Instance == null || NetworkingManager.Instance.PartyCharacters == null) return false;
                EnsureFolded("run window opened");
                StatManager.LoadInstanceReference();
                if (StatManager.Instance == null) return false;
                CloseWindowIfOpen();          // never register the window twice with the game's window stack
                HistoryRun = null; _historySlots.Clear();
                RunMode = true;   // stays set across the window's first-open frame wait; cleared by CloseWindow
                StatManager.Instance.SetCharacters(NetworkingManager.Instance.PartyCharacters);
                StatManager.Instance.OpenWindow();
                if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats window opened (" + RunStats.Ended + " battles" + (_inBattle ? " + current" : "") + ")");
                return true;
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run stats window failed to open: " + e); RunMode = false; return false; }
        }

        /// <summary>Clicked in the run history list: the run in progress opens live, any other from its file.</summary>
        internal static void OpenFromHistory(RunHistory.RunFile run)
        {
            if (run == null) return;
            bool opened = run.IsCurrent ? OpenRunWindow() : OpenHistoryRun(run);
            // the list has done its job; close it after the page is up, so a failed open leaves the list to pick from again
            if (opened) RunHistoryWindow.Close(false);
        }

        /// <summary>Show a saved run in the Stats window. Its characters are laid onto the window's columns: the live
        /// character of the same name where there is one, any spare live character otherwise (only the column's name and
        /// numbers are ours, so a stand-in is invisible); columns beyond the live characters cannot be shown.</summary>
        internal static bool OpenHistoryRun(RunHistory.RunFile run)
        {
            try
            {
                if (run == null || run.Characters == null || run.Characters.Count == 0) return false;
                StatManager.LoadInstanceReference();
                if (StatManager.Instance == null) return false;
                var live = new List<Character>();
                try { if (NetworkingManager.Instance != null && NetworkingManager.Instance.PartyCharacters != null) live.AddRange(NetworkingManager.Instance.PartyCharacters.Where(c => c != null)); } catch { }
                try { if (GameLogic.instance != null && GameLogic.instance.AllMyCharacters != null) foreach (Character c in GameLogic.instance.AllMyCharacters) if (c != null && !live.Contains(c)) live.Add(c); } catch { }
                if (live.Count == 0) { BattleStatsPlugin.Log.LogWarning("Run history: no characters loaded to show a saved run in"); return false; }
                var slots = new List<Character>();
                var used = new HashSet<Character>();
                foreach (RunHistory.Char rc in run.Characters)
                {
                    Character match = live.FirstOrDefault(c => !used.Contains(c) && c.CharacterName == rc.Name) ?? live.FirstOrDefault(c => !used.Contains(c));
                    if (match == null) break;      // more characters in the run than the game has columns for
                    used.Add(match); slots.Add(match);
                }
                if (slots.Count == 0) return false;
                if (slots.Count < run.Characters.Count)
                    BattleStatsPlugin.Log.LogWarning("Run history: the saved run has " + run.Characters.Count + " characters but only " + slots.Count + " can be shown (the others are not loaded)");
                CloseWindowIfOpen();
                _historySlots.Clear(); _historySlots.AddRange(slots);
                HistoryRun = run;
                RunMode = true;
                StatManager.Instance.SetCharacters(slots);
                StatManager.Instance.OpenWindow();
                if (Verbose) BattleStatsPlugin.Log.LogInfo("Run history: opened " + (run.Path != null ? System.IO.Path.GetFileName(run.Path) : "a saved run") + " (" + run.Battles + " battles, " + slots.Count + " columns)");
                return true;
            }
            catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Run history: could not open the saved run: " + e); RunMode = false; HistoryRun = null; return false; }
        }

        private static void CloseWindowIfOpen()
        {
            try { if (StatsWindowOpen()) StatManager.Instance.CloseWindow(); } catch { }
        }

        /// <summary>In history mode the column names come from the saved run, not from the stand-in characters.</summary>
        internal static void ApplyHistoryNames(StatManager sm)
        {
            if (HistoryRun == null || sm == null || sm.StatCharacterNameHolder == null) return;
            try
            {
                Transform holder = sm.StatCharacterNameHolder.transform;
                for (int i = 0; i < holder.childCount && i < HistoryRun.Characters.Count; i++)
                {
                    if (!holder.GetChild(i).gameObject.activeSelf) continue;
                    var tmp = holder.GetChild(i).GetComponent<TextMeshProUGUI>();
                    if (tmp != null) tmp.text = HistoryRun.Characters[i].Name;
                }
            }
            catch { }
        }

        /// <summary>The name to show for a column: the saved run's, or the character's own.</summary>
        internal static string DisplayName(Character c)
        {
            try
            {
                if (HistoryRun != null)
                {
                    int i = _historySlots.IndexOf(c);
                    if (i >= 0 && i < HistoryRun.Characters.Count) return HistoryRun.Characters[i].Name;
                }
            }
            catch { }
            return c != null ? c.CharacterName : "";
        }

        internal static void Toggle()
        {
            if (WindowOpenInRunMode) { try { StatManager.Instance.CloseWindow(); } catch { } return; }
            if (RunHistoryWindow.IsOpen) { RunHistoryWindow.Close(false); return; }
            string blocked = WhyNotOpen();
            if (blocked != null) { BattleStatsPlugin.Log.LogInfo("Run stats: the button did nothing because " + blocked); return; }
            // nothing fought yet in this run: the saved runs are what there is to look at, so go straight to them
            if (RunStats.Battles == 0 && !_inBattle) { RunHistoryWindow.Open(); return; }
            OpenRunWindow();
        }

        /// <summary>Why a click cannot open anything right now, or null when it can. Logged, because a button that
        /// silently does nothing is the hardest kind of bug to report.</summary>
        private static string WhyNotOpen()
        {
            GUIState g = Gui;
            if (g != GUIState.InTown && g != GUIState.InWorldMap && g != GUIState.InBattle) return "the game is in " + g;
            try { if (PostBattleManager.IsNotNullAndIsActive) return "the post-battle screen is up"; } catch { }
            if (StatsWindowOpen()) return "the stats window is already open";
            try
            {
                if (UIWindowManager.Instance != null && UIWindowManager.Instance.OpenedWindows != null && UIWindowManager.Instance.OpenedWindows.Count > 0)
                {
                    UIWindow top = UIWindowManager.Instance.OpenedWindows[UIWindowManager.Instance.OpenedWindows.Count - 1];
                    return "another window is open (" + (top != null ? top.name : "?") + ")";
                }
            }
            catch { }
            return null;
        }

        /// <summary>From StatsWindowPatches' OpenWindow postfix: put the run title on while in run mode.</summary>
        internal static void ApplyTitle(StatManager sm)
        {
            try
            {
                if (!RunMode || sm == null || sm.Content == null) { RestoreTitle(); return; }
                if (_title == null)
                {
                    Transform t = sm.Content.transform.Find("Title");
                    _title = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
                    if (_title == null) _title = sm.Content.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(x => x.name == "Title");
                    if (_title == null) return;
                    _origTitle = _title.text; _origTitleSize = _title.rectTransform.sizeDelta; _origTitleWrap = _title.enableWordWrapping;
                }
                if (HistoryRun != null)
                {
                    DateTime d = HistoryRun.StartedAt;
                    string when = d == DateTime.MinValue ? "saved run" : d.ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    _title.text = "Run Stats - " + when + " - " + HistoryRun.Battles + (HistoryRun.Battles == 1 ? " battle" : " battles");
                }
                else
                {
                    int n = RunStats.Ended;
                    _title.text = "Run Stats - " + n + (n == 1 ? " battle" : " battles") + (_inBattle ? " + current" : "");
                }
                _title.enableWordWrapping = false;
                _title.rectTransform.sizeDelta = new Vector2(Mathf.Max(_origTitleSize.x, 420f), _origTitleSize.y);
                _titleChanged = true;
                MaintainHistoryButton(sm);
            }
            catch (Exception e) { if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats title: " + e.Message); }
        }

        private static void RestoreTitle()
        {
            if (!_titleChanged) return;
            try { if (_title != null) { _title.text = _origTitle; _title.rectTransform.sizeDelta = _origTitleSize; _title.enableWordWrapping = _origTitleWrap; } } catch { }
            _titleChanged = false;
        }

        [HarmonyPatch(typeof(StatManager), nameof(StatManager.CloseWindow))]
        private static class StatManager_CloseWindow
        {
            private static void Postfix()
            {
                if (!RunMode) return;
                RunMode = false; HistoryRun = null; _historySlots.Clear();
                RestoreTitle();
                try { if (_historyButton != null) _historyButton.SetActive(false); } catch { }
            }
        }

        /// <summary>A "History" button beside the window's Close button, shown only while the window is in run mode.</summary>
        private static void MaintainHistoryButton(StatManager sm)
        {
            try
            {
                if (_historyButton == null)
                {
                    Button src = null;
                    foreach (Button b in sm.GetComponentsInChildren<Button>(true))
                    {
                        if (b == null || !b.gameObject.activeInHierarchy) continue;
                        if (src == null || b.GetComponentInChildren<TextMeshProUGUI>(true) != null) src = b;
                        if (b.name.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0) { src = b; break; }
                    }
                    if (src == null) return;
                    GameObject go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
                    go.name = "BattleStatsHistoryButton";
                    foreach (Component c in go.GetComponentsInChildren<Component>(true))
                        if (c != null && c.GetType().Name.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0) UnityEngine.Object.Destroy(c);
                    Button btn = go.GetComponent<Button>();
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(() => RunHistoryWindow.Open());
                    btn.interactable = true;
                    var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (tmp != null) tmp.text = "History";
                    RectTransform rt = go.GetComponent<RectTransform>(), srt = src.GetComponent<RectTransform>();
                    rt.anchorMin = srt.anchorMin; rt.anchorMax = srt.anchorMax; rt.pivot = srt.pivot;
                    rt.anchoredPosition = srt.anchoredPosition + new Vector2(-230f, 0f);
                    _historyButton = go;
                    if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats: History button added next to " + src.name);
                }
                _historyButton.transform.SetAsLastSibling();
                if (!_historyButton.activeSelf) _historyButton.SetActive(true);
            }
            catch (Exception e) { if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats: History button not added (" + e.Message + ")"); }
        }

        /// <summary>The run-mode value source for a character: a saved run's column, or the live store plus the battle in progress.</summary>
        internal static Dictionary<int, float> ViewFor(Character c)
        {
            if (HistoryRun != null)
            {
                int i = _historySlots.IndexOf(c);
                if (i >= 0 && i < HistoryRun.Characters.Count) return HistoryRun.Characters[i].Values;
                return new Dictionary<int, float>();
            }
            bool live = _inBattle && Cfg != null && Cfg.IncludeCurrentBattle.Value;
            return RunStats.View(c, live);
        }

        internal static float Get(Character c, int key)
        {
            float v; return ViewFor(c).TryGetValue(key, out v) ? v : 0f;
        }

        internal static bool HasModKeys()
        {
            if (HistoryRun != null)
            {
                foreach (RunHistory.Char c in HistoryRun.Characters) foreach (var kv in c.Values) if (kv.Key >= 1000) return true;
                return false;
            }
            if (RunStats.HasModKeys()) return true;
            if (_inBattle && Cfg != null && Cfg.IncludeCurrentBattle.Value) return StatsWindowPatches.LiveHostHasData();
            return false;
        }

        internal static void NoteRowMismatch(int got, int want)
        {
            if (_rowMismatchLogged++ == 0) BattleStatsPlugin.Log.LogWarning("Run stats: the game built " + got + " stat rows but the run rewrite made " + want + "; the game rows keep their per-battle values");
        }

        // ---------- per frame: GUI transitions, hotkey, HUD button ----------

        internal static void Tick()
        {
            GUIState g = Gui;
            if (!_guiSeen || g != _lastGui)
            {
                if (_guiSeen) OnGuiChanged(_lastGui, g);
                _lastGui = g; _guiSeen = true;
            }
            TryResume();
            if (Cfg != null && Cfg.RunStatsKey.Value.IsDown() && !TypingInAnyTextField() && !BindingControls()) Toggle();
            MaintainButton();
        }

        private static void OnGuiChanged(GUIState from, GUIState to)
        {
            if (to == GUIState.InTown) _wasInTown = true;
            if (to == GUIState.InWorldMap && _wasInTown) { _wasInTown = false; ResetRun("left town for the island"); }
            // leaving to the menu or the character list is not the end of a run: the quest is saved and can be resumed
            if (to == GUIState.ChoosingCharacter || to == GUIState.CreatingCharacter || to == GUIState.InMainMenu) { _wasInTown = false; _resumeChecked = false; DropRun("left the game (" + to + ")"); }
            if (WindowOpenInRunMode && to != from) { try { StatManager.Instance.CloseWindow(); } catch { } }
        }

        private static void MaintainButton()
        {
            try
            {
                if (Cfg == null) return;
                if (Cfg.DebugHooks.Value && !_hudDumped && CurrentCharacterUI.Instance != null)
                { _hudDumped = true; StatsWindowPatches.DumpHierarchy(CurrentCharacterUI.Instance.transform, "HUD"); }
                bool want = Cfg.RunStatsButton.Value && ShouldShowButton();
                if (_button == null)
                {
                    if (!want || _buildFailed) return;
                    BuildButton();
                    if (_button == null) return;
                }
                if (_button.activeSelf != want) _button.SetActive(want);
                if (want)
                {
                    UpdateButton();
                    PlaceButton();
                    if (_opaqueTicks > 0) { _opaqueTicks--; Opaque(_button, _btn); }
                }
            }
            catch (Exception e) { if (Verbose) BattleStatsPlugin.Log.LogInfo("Run stats button: " + e.Message); }
        }

        /// <summary>The button yields to everything: any open window, the post-battle screen, a loading screen, a
        /// cutscene, or the game hiding its own HUD all take it off the screen.</summary>
        private static bool ShouldShowButton()
        {
            GUIState g = Gui;
            if (g != GUIState.InTown && g != GUIState.InWorldMap && g != GUIState.InBattle) return false;
            try { if (UIWindowManager.Instance != null && UIWindowManager.Instance.AnyUIWindowOpen) return false; } catch { }
            try { if (PostBattleManager.IsNotNullAndIsActive) return false; } catch { }
            try { if (!CurrentCharacterUI.IsNotNullAndIsActive) return false; } catch { }
            try { if (LoadingScreen.Instance != null && (LoadingScreen.Instance.IsLoading || LoadingScreen.Instance.MainFadeActive)) return false; } catch { }
            try { if (GUIManager.instance != null && GUIManager.instance.PerformingCameraFocusedEvent) return false; } catch { }
            return true;
        }

        /// <summary>The box at the top right holding the party's gold and the difficulty. The difficulty holder is a
        /// field on GUIManager; the box around it is the nearest parent that also contains a gold display, which is
        /// what the player sees as one panel. Kept small on purpose: a match that turns out to be half the screen is
        /// not a panel, so the search gives up and the button goes to the corner instead.</summary>
        private static RectTransform TopRightBox()
        {
            if (_anchorBox != null) return _anchorBox;
            try
            {
                GameObject dh = GUIManager.instance != null ? GUIManager.instance.difficultyHolder : null;
                if (dh == null) return null;
                RectTransform best = dh.transform as RectTransform;
                Transform p = dh.transform.parent;
                for (int depth = 0; p != null && depth < 4; p = p.parent, depth++)
                {
                    RectTransform rt = p as RectTransform;
                    if (rt == null) continue;
                    if (p.GetComponentInChildren<GoldDisplay>(true) == null) continue;
                    if (rt.rect.width > 700f || rt.rect.height > 400f) break;   // that is the whole screen, not the panel
                    best = rt;
                    break;
                }
                _anchorBox = best;
                if (best != null) BattleStatsPlugin.Log.LogInfo("Run stats: button anchored under '" + best.name + "' (" + (int)best.rect.width + "x" + (int)best.rect.height + ")");
            }
            catch { }
            return _anchorBox;
        }

        /// <summary>Put the button just below the gold and difficulty box, lined up with its right edge.</summary>
        private static void PlaceButton()
        {
            try
            {
                if (_button == null || Cfg == null) return;
                RectTransform rt = _button.GetComponent<RectTransform>();
                RectTransform parent = rt.parent as RectTransform;
                float gap = Mathf.Max(0f, Cfg.RunStatsButtonGap.Value);
                RectTransform box = TopRightBox();
                if (parent != null && box != null)
                {
                    if (!box.gameObject.activeInHierarchy) return;   // the box is hidden for a moment: stay put rather than jump to the corner
                    Canvas canvas = parent.GetComponentInParent<Canvas>();
                    Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    var corners = new Vector3[4];
                    box.GetWorldCorners(corners);                                        // 0 bottom-left ... 3 bottom-right
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);
                    Vector2 local;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out local))
                    {
                        rt.anchorMin = rt.anchorMax = parent.pivot;                      // local point is measured from the parent's pivot
                        rt.pivot = new Vector2(1f, 1f);                                   // our top-right corner meets the box's bottom-right
                        Vector2 want = new Vector2(local.x, local.y - gap);
                        if ((rt.anchoredPosition - want).sqrMagnitude > 0.25f) rt.anchoredPosition = want;
                        return;
                    }
                }
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);                        // no box found: the top right corner
                rt.pivot = new Vector2(1f, 1f);
                Vector2 corner = new Vector2(-12f, -gap);
                if ((rt.anchoredPosition - corner).sqrMagnitude > 0.25f) rt.anchoredPosition = corner;
            }
            catch { }
        }

        private static void UpdateButton()
        {
            try
            {
                if (_btn == null) return;
                if (!_btn.interactable) _btn.interactable = true;   // always usable: with no run going it opens the saved runs
                if (_tip != null)
                {
                    int n = RunStats.Ended;
                    string text = n > 0 ? "Run Stats (" + n + (n == 1 ? " battle" : " battles") + (_inBattle ? " + current" : "") + ")"
                        : _inBattle ? "Run Stats (this battle)" : "Run history (no battles fought yet on this run)";
                    if (text != _tipText) { _tipText = text; _tip.HoverText = text; }
                }
            }
            catch { }
        }

        /// <summary>Where the button lives: the same container the game's own windows are placed in, so it scales with
        /// them and sits at the very back of it (every window draws over it).</summary>
        private static Transform ButtonParent()
        {
            try { if (ReferenceLoader.Instance != null && ReferenceLoader.Instance.StatManagerPlaceholder != null) return ReferenceLoader.Instance.StatManagerPlaceholder.parent; } catch { }
            try { if (ConfirmWindow.Instance != null) return ConfirmWindow.Instance.transform.parent; } catch { }
            return null;
        }

        /// <summary>The post-battle menu's own Stats button, taken from the live menu or straight from its prefab, so
        /// ours is the same widget the player already knows. Falls back to the other large buttons of the same style.</summary>
        private static Button FindStatsButton()
        {
            try
            {
                PostBattleManager pbm = null;
                try { pbm = PostBattleManager.Instance; } catch { }
                if (pbm == null) { try { pbm = ReferenceLoader.Instance != null ? ReferenceLoader.Instance.PostBattleManager : null; } catch { } }
                if (pbm != null)
                    foreach (Button b in pbm.GetComponentsInChildren<Button>(true))
                    {
                        if (b == null || b.onClick == null) continue;
                        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                            if (string.Equals(b.onClick.GetPersistentMethodName(i), "OpenStats", StringComparison.OrdinalIgnoreCase)) return b;
                    }
            }
            catch { }
            try
            {
                if (StatManager.Instance != null)
                    foreach (Button b in StatManager.Instance.GetComponentsInChildren<Button>(true))
                        if (b != null && b.name.IndexOf("Large", StringComparison.OrdinalIgnoreCase) >= 0) return b;
            }
            catch { }
            try { if (ConfirmWindow.Instance != null) return ConfirmWindow.Instance.ConfirmButton; } catch { }
            return null;
        }

        /// <summary>The post-battle menu fades itself in, so its buttons carry see-through colours and canvas groups.
        /// On the HUD the button has to be solid, in every state the button can be in.</summary>
        private static void Opaque(GameObject go, Button btn)
        {
            try
            {
                foreach (CanvasGroup cg in go.GetComponentsInChildren<CanvasGroup>(true)) { cg.alpha = 1f; cg.blocksRaycasts = true; cg.interactable = true; }
                foreach (Image im in go.GetComponentsInChildren<Image>(true)) im.color = new Color(im.color.r, im.color.g, im.color.b, 1f);
                if (btn != null)
                {
                    ColorBlock cb = btn.colors;
                    cb.normalColor = Solid(cb.normalColor);
                    cb.highlightedColor = Solid(cb.highlightedColor);
                    cb.pressedColor = Solid(cb.pressedColor);
                    cb.selectedColor = Solid(cb.selectedColor);
                    cb.disabledColor = Solid(cb.disabledColor);
                    cb.colorMultiplier = 1f;
                    btn.colors = cb;
                }
            }
            catch { }
        }

        private static Color Solid(Color c) { return new Color(c.r, c.g, c.b, 1f); }

        private static void BuildButton()
        {
            GameObject go = null;
            try
            {
                Transform parent = ButtonParent();
                Button src = FindStatsButton();
                if (parent == null || src == null) return;      // the UI is not up yet: try again on a later tick
                go = UnityEngine.Object.Instantiate(src.gameObject, parent);
                go.name = "BattleStatsRunButton";
                go.SetActive(true);
                go.transform.SetAsFirstSibling();               // lowest priority: anything else in this container draws over it
                foreach (ControlGlyph glyph in go.GetComponentsInChildren<ControlGlyph>(true))
                    if (glyph != null) UnityEngine.Object.Destroy(glyph.gameObject);   // gamepad glyphs belong to the menu, not to a HUD button
                foreach (Component c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    // UIButtonController rewrites the button's colours (alpha included) from the game's settings in its
                    // own Start, a frame after we set them, and re-adds the hover detector: it has to go.
                    if (tn.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0 || c is ButtonHoverDectector || c is ObjectActivationController || c is UIButtonController) UnityEngine.Object.Destroy(c);
                }
                Button btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
                if (btn == null) throw new Exception("the cloned button has no Button component");
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(() => Toggle());
                btn.interactable = true;
                var tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null)
                {
                    tmp.text = "Run Stats"; tmp.enableWordWrapping = false;
                    try { tmp.parseCtrlCharacters = false; } catch { }
                    tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, 1f);
                }
                Opaque(go, btn);
                RectTransform rt = go.GetComponent<RectTransform>();
                PlaceButton();   // under the gold and difficulty box at the top right
                var tip = go.GetComponent<DisabledButtonTooltip>() ?? go.AddComponent<DisabledButtonTooltip>();
                tip.ShowHoverText = true; tip.HoverText = "Run Stats"; tip.DisabledText = "No battles this run yet";
                _tip = tip; _tipText = null;
                _button = go; _btn = btn;
                _opaqueTicks = 5;      // anything on the clone that recolours itself in Start gets overruled
                UpdateButton();
                BattleStatsPlugin.Log.LogInfo("Run stats: button built from '" + src.name + "' under " + parent.name + " (" + (int)rt.rect.width + "x" + (int)rt.rect.height + ")");
            }
            catch (Exception e)
            {
                BattleStatsPlugin.Log.LogWarning("Run stats button could not be built: " + e.Message + " (the key still opens the page)");
                try { if (go != null) UnityEngine.Object.Destroy(go); } catch { }
                _button = null; _btn = null; _tip = null;
                _buildFailed = true;                            // do not retry every frame; F9 clears this
            }
        }

        // ---------- input guards (same rules as the mod menu) ----------

        internal static bool BindingControls()
        {
            try { return OptionsManager.instance != null && OptionsManager.instance.CurrentlyBindingControls; } catch { return false; }
        }

        internal static bool TypingInAnyTextField()
        {
            try
            {
                if (MessageWindowManager.instance != null && MessageWindowManager.instance.TextInputIsFocused) return true;
                var es = EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null) return false;
                var f = go.GetComponent<InputField>(); if (f != null && f.isFocused) return true;
                var t = go.GetComponent<TMP_InputField>(); if (t != null && t.isFocused) return true;
            }
            catch { }
            return false;
        }
    }
}
