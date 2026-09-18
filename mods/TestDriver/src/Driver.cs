using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Burst2Flame;
using Burst2Flame.Observable;
using HexMapTools;
using UnityEngine;

namespace TestDriver
{
    internal sealed class Driver
    {
        private enum Step { WaitLoaded, StartSolo, WaitChoice, PickParty, WaitTown, ProbeFortuneWindow, OpenQuestSelect, PickQuest, WaitIsland, StartBattle, WaitBattle, HandToAi, OverlayOn, OverlayOff, WaitBattleEnd, ProbeStats, ReadStats, ProbeRunStats, ReadRunStats, AfterBattle, WaitIslandAgain, WaitTownAgain, Finish, Done }

        private readonly ManualLogSource _log;
        private readonly int _partySize;
        private Step _step = Step.WaitLoaded;
        private float _stepStarted;
        private float _wait;
        private readonly float _bootTime = Time.realtimeSinceStartup;
        private readonly List<Character> _party = new List<Character>();
        private bool _battleSeen;
        private bool _placementDone;
        private float _nextAct;
        private int _lastActTurn = -1;
        private readonly HashSet<Character> _movedThisTurn = new HashSet<Character>();
        private int _attacks, _moves, _turnEnds;
        internal static bool UseGameAi;
        internal static string[] WantedNames; // -srchars Test1,Test2: pick these by name instead of the first N
        internal static string BattleName; // -srbattle <name>: a BattleInfo from Game.Instance.Battles instead of the debug test battle
        internal static string ScenarioName; // -srscenario pack|elite|boss: a seeded standard fight built by Scenario.Build
        internal static int Seed; // -srseed N: seeds the battle generation and Unity's RNG at battle start (0 = leave random)
        internal static int Difficulty = -1; // -srdifficulty N: campaign difficulty index 0..5 (3 = Veteran)
        internal static string RotationPath; // -srrotation <file>: play the party from a priority list instead of the dumb policy
        internal static float Speed = 1f; // -srspeed N: Unity time scale while the driver runs (animations, waits, AI turns); 1 = normal
        private Rotation _rotation;
        private float _lowestHealth = 1f; // lowest party health ratio seen during the fight (rubric: closest call)
        internal static List<KeyValuePair<string, int>> Fights = new List<KeyValuePair<string, int>>(); // -srfights pack:11,elite:22,... several fights in one launch
        internal static bool ResetRunsBetweenFights; // -srresetruns: tell BattleStats each fight is its own run (tests that a new run starts a new run file)
        internal static bool ResumeRunsBetweenFights; // -srresumeruns: forget the run in memory after each fight (tests that it is picked back up from its file, as after a restart)
        private int _fightIndex = -1; // index into Fights of the fight being played (-1 = single -srscenario/-srbattle run)
        private int _fightStartLine; // _results index where the current fight's lines begin
        private bool _lastVictory;
        private int _lastWaitDiag = -1;
        private readonly List<string> _fightSummary = new List<string>();
        private readonly List<string> _results = new List<string>();

        private const float StepTimeout = 120f;
        private const float BattleTimeout = 420f;

        public Driver(ManualLogSource log, int partySize) { _log = log; _partySize = partySize; _stepStarted = Time.realtimeSinceStartup; }

        private void Say(string s) { _log.LogMessage("TESTDRIVER: " + s); _results.Add(s); }
        private void Go(Step s, float delay = 0.5f) { _step = s; _stepStarted = Time.realtimeSinceStartup; _wait = delay; Say("step " + s); }
        private static Root Root => NetworkingManager.Instance != null && NetworkingManager.Instance.NetworkManager != null ? NetworkingManager.Instance.NetworkManager.Root : null;
        private static GUIState Gui { get { try { return GUIManager.instance.CurrentGuiState; } catch { return GUIState.InMainMenu; } } }

        public void Tick()
        {
            if (_step == Step.Done) return;
            // The game resets the time scale on some screens and the post-battle flow leaves it at 0 (paused) until a button restores
            // it; the driver skips those buttons, so a paused clock would hang every WaitForSeconds in the next battle setup. Always re-apply.
            if (Speed > 1f && Time.timeScale != Speed) { if (Time.timeScale == 0f) Say("time scale was 0 (paused); restoring " + Speed); Time.timeScale = Speed; }
            else if (Speed <= 1f && Time.timeScale == 0f && _step != Step.Done && _step != Step.WaitLoaded) { Say("time scale was 0 (paused); restoring 1"); Time.timeScale = 1f; }
            float now = Time.realtimeSinceStartup;
            if (now - _stepStarted < _wait) return;
            float limit = _step == Step.WaitBattleEnd ? BattleTimeout : StepTimeout;
            if (now - _stepStarted > limit) { Say("TIMEOUT in step " + _step + " after " + (int)(now - _stepStarted) + "s (gui " + Gui + ")"); Finish(false); return; }
            try
            {
                switch (_step)
                {
                    case Step.WaitLoaded:
                        if (LoadingScreen.Instance != null && LoadingScreen.Instance.InitialLoadComplete && MainMenu.Instance != null && GameLogic.instance != null && GameLogic.instance.FinishedLoadingCharacters)
                        {
                            Say("game loaded in " + (int)(now - _bootTime) + "s, " + GameLogic.instance.AllMyCharacters.Count + " characters on the account");
                            Go(Step.StartSolo, 2f);
                        }
                        break;

                    case Step.StartSolo:
                        MainMenu.Instance.ChooseNormalMode();
                        MainMenu.Instance.StartSinglePlayer(false);
                        Go(Step.WaitChoice, 1f);
                        break;

                    case Step.WaitChoice:
                        if (Gui == GUIState.ChoosingCharacter) Go(Step.PickParty, 1f);
                        break;

                    case Step.PickParty:
                    {
                        var pool = GameLogic.instance.AllMyCharacters.Where(c => c != null && !c.IsRoguelikeCharacter && !c.HardcoreDeath && !c.IsHardcore && !c.IsDeleted).ToList();
                        List<Character> candidates;
                        if (WantedNames != null && WantedNames.Length > 0)
                        {
                            candidates = new List<Character>();
                            foreach (string n in WantedNames) { Character c = pool.FirstOrDefault(x => string.Equals(x.CharacterName, n.Trim(), StringComparison.OrdinalIgnoreCase)); if (c != null) candidates.Add(c); else Say("character '" + n.Trim() + "' not found"); }
                        }
                        else candidates = pool.Take(_partySize).ToList();
                        if (candidates.Count == 0) { Say("no campaign characters to use"); Finish(false); return; }
                        foreach (Character c in candidates)
                        {
                            if (!Root.AllCharacters.Contains(c)) Root.AddToCharacterList(c);
                            c.IsNetworkLoaded = true;
                            c.OwnerID = 0;
                            c.ControllerPlayerId = 0;
                            c.SelectedForBattle = true;
                            _party.Add(c);
                        }
                        Say("party: " + string.Join(", ", _party.Select(c => c.CharacterName + " L" + c.Level).ToArray()));
                        GameLogic.instance.AcceptCharacterChoices();
                        Go(Step.WaitTown, 1f);
                        break;
                    }

                    case Step.WaitTown:
                        if (Gui == GUIState.InTown) { Say("in town after " + (int)(now - _stepStarted) + "s"); Go(Step.ProbeFortuneWindow, 3f); }
                        break;

                    case Step.ProbeFortuneWindow:
                        ProbeFortuneWindow();
                        Go(Step.OpenQuestSelect, 2f);
                        break;

                    case Step.OpenQuestSelect:
                        if (Difficulty >= 0 && _fightIndex < 0)
                        {
                            try { Root.CurrentDifficultyIndex = Difficulty; Say("difficulty set to index " + Difficulty + " (" + GameLogic.instance.CurrentDifficulty.DifficultyName + ")"); }
                            catch (Exception e) { Say("could not set difficulty: " + e.Message); }
                        }
                        if (!string.IsNullOrEmpty(RotationPath) && _rotation == null) _rotation = Rotation.Load(RotationPath, Say);
                        TownManager.Instance.CheckIfQuestSelectIsLoadedAndOpen();
                        Go(Step.PickQuest, 2f);
                        break;

                    case Step.PickQuest:
                    {
                        QuestSelectManager qs = LoadableUIWindow<QuestSelectManager>.Instance;
                        if (qs == null || qs.QuestNodes == null || qs.QuestNodes.Length == 0) break; // not loaded yet, retry next tick
                        var nodes = qs.QuestNodes.Where(n => n != null && n.questNodeType == QuestNodeType.Quest && n.CurrentQuestInstance != null && n.CanNavigateTo).ToList();
                        if (nodes.Count == 0) { Say("no quest node available (" + qs.QuestNodes.Length + " nodes)"); Finish(false); return; }
                        QuestNode pick = nodes.OrderBy(n => n.CurrentQuestInstance.QuestLevel).First();
                        QuestInstance qi = pick.CurrentQuestInstance;
                        Say("quest: " + pick.name + " level " + qi.QuestLevel + " (" + nodes.Count + " available)");
                        qs.SelectedQuestNode = pick;
                        Root.SetLastAcceptedQuest(qi);
                        try { qs.CloseWindow(); } catch { }
                        QuestManager.instance.ActivateQuest();
                        Go(Step.WaitIsland, 2f);
                        break;
                    }

                    case Step.WaitIsland:
                        if (Gui == GUIState.InWorldMap && WorldMapGenerator.instance != null && WorldMapGenerator.instance.CurrentRoom != null && LoadingScreen.Instance != null && !LoadingScreen.Instance.MainFadeActive)
                        {
                            Say("on the island after " + (int)(now - _stepStarted) + "s, room hexes " + WorldMapGenerator.instance.CurrentRoom.Hexes.Count);
                            Go(Step.StartBattle, 3f);
                        }
                        break;

                    case Step.StartBattle:
                    {
                        if (Fights.Count > 0)
                        {
                            _fightIndex++;
                            if (_fightIndex >= Fights.Count) { Go(Step.Finish, 0.5f); break; }
                            ScenarioName = Fights[_fightIndex].Key; Seed = Fights[_fightIndex].Value;
                            _fightStartLine = _results.Count;
                            Say("FIGHT " + (_fightIndex + 1) + "/" + Fights.Count + " " + ScenarioName + " seed " + Seed);
                        }
                        _placementDone = false; _lowestHealth = 1f; _attacks = _moves = _turnEnds = 0;
                        if (_rotation != null) _rotation.Reset();
                        // SetupGame places the party around PortalManager.currentEventHex (or a stale BattleObjectCharacter) and throws
                        // NullReferenceException when it is null, which happens after a won fight. Anchor the fight on the party leader.
                        try
                        {
                            Root.BattleObjectCharacter = null;
                            Character lead = NetworkingManager.Instance.PartyCharacters.FirstOrDefault(p => p != null && p.Cell != null);
                            Say("battle origin: eventHex " + (PortalManager.Instance.currentEventHex != null ? PortalManager.Instance.currentEventHex.Coordinates.ToString() : "NULL") + ", leader cell " + (lead != null ? lead.Cell.Coordinates.ToString() : "NULL"));
                            if (PortalManager.Instance.currentEventHex == null && lead != null) PortalManager.Instance.currentEventHex = lead.Cell;
                        }
                        catch (Exception e) { Say("origin probe failed: " + e.Message); }
                        BattleInfo test = GlobalSettingsManager.instance.globalSettings.testBattle;
                        if (!string.IsNullOrEmpty(BattleName))
                        {
                            BattleInfo pick = Game.Instance.Battles.FirstOrDefault(b => b != null && b.name.IndexOf(BattleName, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (pick != null) test = pick; else Say("battle '" + BattleName + "' not found, using the test battle");
                        }
                        if (test == null) { Say("no test battle asset"); Finish(false); return; }
                        if (Seed != 0) { UnityEngine.Random.InitState(Seed); SeedStaticRandoms(Seed); Say("unity RNG seeded with " + Seed); }
                        if (!string.IsNullOrEmpty(ScenarioName))
                        {
                            test = Scenario.Build(ScenarioName, Seed != 0 ? Seed : UnityEngine.Random.Range(1, int.MaxValue), Say);
                            if (test.enemies == null || test.enemies.Count == 0) { Say("scenario produced no enemies; aborting"); Finish(false); return; }
                        }
                        else test.enemyMods = new Dictionary<int, List<EnemyMod>>();
                        if (Seed != 0) UnityEngine.Random.InitState(Seed);
                        GameLogic.instance.CurrentBattle = test;
                        Root.SendOpenBattle();
                        Say("test battle requested (" + test.name + ")");
                        Go(Step.WaitBattle, 1f);
                        break;
                    }

                    case Step.WaitBattle:
                        if ((int)(now - _stepStarted) % 15 == 14 && (int)(now - _stepStarted) != _lastWaitDiag)
                        {
                            _lastWaitDiag = (int)(now - _stepStarted);
                            try { Say("waiting for battle: gui " + Gui + ", settingUp " + GameLogic.instance.SettingUpBattle + ", placement " + Root.SpawnPlacementActive + ", active " + Root.BattleCurrentlyActive + ", enemies " + Root.CharactersInBattleEnemyTeam.Count + ", islandGen " + PortalManager.Instance.IslandGenerationInProgress + ", spawnWait " + PortalManager.Instance.WaitingForSpawnSet + ", loading " + LoadingScreen.Instance.IsLoading + ", timeScale " + Time.timeScale); } catch (Exception e) { Say("diag failed: " + e.Message); }
                        }
                        // Placement phase: the game waits for every party member to press Ready (EndedTurn) before the fight begins.
                        if (Root != null && Root.SpawnPlacementActive && !_placementDone)
                        {
                            foreach (Character c in NetworkingManager.Instance.PartyCharacters) c.EndedTurn = true;
                            _placementDone = true;
                            Say("placement phase: accepted default positions for " + NetworkingManager.Instance.PartyCharacters.Count + " characters");
                        }
                        if (Gui == GUIState.InBattle && Root != null && Root.BattleCurrentlyActive)
                        {
                            _battleSeen = true;
                            Say("battle active: " + Root.CharactersInBattlePlayerTeam.Count + " players vs " + Root.CharactersInBattleEnemyTeam.Count + " enemies, turn " + GameLogic.instance.turnNumber);
                            try
                            {
                                float ehp = 0f; int en = 0; foreach (Character e in Root.CharactersInBattleEnemyTeam) if (e != null && !e.IsDead) { ehp += e.MaxHealth; en++; }
                                float php = 0f; foreach (Character p in NetworkingManager.Instance.PartyCharacters) if (p != null) php += p.MaxHealth;
                                Say("POOL enemy hp " + (int)ehp + " over " + en + " enemies, party hp " + (int)php);
                            }
                            catch (Exception e) { Say("pool probe failed: " + e.Message); }
                            Go(Step.HandToAi, 2f);
                        }
                        break;

                    case Step.HandToAi:
                        if (!UseGameAi) { Say("driver will play the party itself (basic attacks + moves); pass -srai to use the game's AI instead"); Go(Step.OverlayOn, 1f); break; }
                        foreach (Character c in _party)
                        {
                            try
                            {
                                CharacterAttribute attr = Game.Instance.GetAttribute("AiControlledPlayer");
                                if (attr == null) { Say("no AiControlledPlayer attribute"); break; }
                                if (c["AiControlledPlayer"] <= 0f) c.AddAttribute(attr, 1f);   // saved-map attribute, same mechanism as gold; the run's saves are restored afterwards
                                c.IsAIControlledPlayer = true;
                            }
                            catch (Exception e) { Say("could not hand " + c.CharacterName + " to AI: " + e.Message); }
                        }
                        try
                        {
                            Character cur = GameLogic.instance.CurrentlySelectedCharacter;
                            if (cur != null && _party.Contains(cur) && Root.CurrentTeamTurnIndex == 0) { GameLogic.instance.StartMyAiTurn(cur); Say("AI turn started for " + cur.CharacterName); }
                        }
                        catch (Exception e) { Say("StartMyAiTurn failed: " + e.Message); }
                        Say("party handed to the game's AI");
                        Go(Step.OverlayOn, 3f);
                        break;

                    case Step.OverlayOn:
                        SetOverlay(true);
                        Go(Step.OverlayOff, 3f);
                        break;

                    case Step.OverlayOff:
                        SetOverlay(false);
                        Go(Step.WaitBattleEnd, 1f);
                        break;

                    case Step.WaitBattleEnd:
                        if (!UseGameAi && now >= _nextAct) { _nextAct = now + 0.75f / Mathf.Max(1f, Speed); PlayPartyTurn(); }
                        try { foreach (Character p in NetworkingManager.Instance.PartyCharacters) if (p != null && !p.IsDead && p.MaxHealth > 0f) _lowestHealth = Mathf.Min(_lowestHealth, p.HealthRatio); } catch { }
                        if (Gui != GUIState.InBattle || (Root != null && !Root.BattleCurrentlyActive))
                        {
                            if (_rotation != null) { _attacks = _rotation.Casts + _rotation.Basics; _moves = _rotation.Moves; _turnEnds = _rotation.TurnEnds; foreach (string line in _rotation.Trace) _results.Add("ROTATION " + line); }
                            int alive = 0; float endHealth = 0f; try { alive = NetworkingManager.Instance.PartyCharacters.Count(p => p != null && !p.IsDead); foreach (Character p in NetworkingManager.Instance.PartyCharacters) if (p != null && !p.IsDead) endHealth = Mathf.Max(endHealth, p.HealthRatio); } catch { }
                            Say("HEALTH end " + (int)(endHealth * 100f) + "% lowest " + (int)(Mathf.Max(0f, _lowestHealth) * 100f) + "%");
                            _lastVictory = alive > 0;
                            Say("battle over after " + (int)(now - _stepStarted) + "s, gui " + Gui + ", turn " + GameLogic.instance.turnNumber + ", party alive " + alive + " (driver: " + _attacks + " attacks, " + _moves + " moves, " + _turnEnds + " turn ends)");
                            Go(Step.ProbeStats, 4f); // give the post-battle screen (and the BattleStats summary) time to appear
                        }
                        break;

                    case Step.ProbeStats:
                        try
                        {
                            if (PostBattleManager.Instance == null) { Say("no PostBattleManager; skipping the stats window"); Go(Step.Finish, 0.5f); break; }
                            PostBattleManager.Instance.OpenStats();
                            Say("opened the stats window");
                        }
                        catch (Exception e) { Say("OpenStats failed: " + e.Message); }
                        Go(Step.ReadStats, 2f);
                        break;

                    case Step.ReadStats:
                        ReadStatsWindow("STATS");
                        Go(Step.ProbeRunStats, 0.5f);
                        break;

                    case Step.ProbeRunStats:
                        // BattleStats' run page: the same window with the totals of every fight so far in this launch (the runner's fights never pass through town)
                        try
                        {
                            try { StatManager sm = StatManager.Instance; if (sm != null) sm.CloseWindow(); } catch { }
                            Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("BattleStats.Patches.RunStatsPatches", false)).FirstOrDefault(x => x != null);
                            MethodInfo m = t != null ? t.GetMethod("OpenRunWindow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                            if (m == null) { Say("RUNSTATS: hook not found (BattleStats off or older build)"); Go(Fights.Count > 0 ? Step.AfterBattle : Step.Finish, 0.5f); break; }
                            object ok = m.Invoke(null, null);
                            Say("RUNSTATS: run window " + (ok is bool && (bool)ok ? "opened" : "did not open"));
                        }
                        catch (Exception e) { Say("RUNSTATS: open failed: " + e.Message); }
                        Go(Step.ReadRunStats, 2f);
                        break;

                    case Step.ReadRunStats:
                        ReadStatsWindow("RUNSTATS");
                        try { StatManager sm = StatManager.Instance; if (sm != null) sm.CloseWindow(); } catch { }
                        // the run history list: opening it builds the window and reads the saved runs, both of which must not throw
                        CallStatic("BattleStats.RunHistoryWindow", "Open", "RUNSTATS: opened the run history list");
                        CallStatic("BattleStats.RunHistoryWindow", "Close", "RUNSTATS: closed the run history list", false);
                        Go(Fights.Count > 0 ? Step.AfterBattle : Step.Finish, 0.5f);
                        break;

                    case Step.AfterBattle:
                    {
                        // one file per fight so the runner can pair it with the BattleStats JSON; then leave the post-battle screen
                        if (ResetRunsBetweenFights) ResetRunStats();   // before the fight file is written, so the line lands in this fight's file
                        if (ResumeRunsBetweenFights) CallRunStats("SimulateRestart", "RUNSTATS: forgot the run in memory; it must be resumed from its file");
                        WriteFightFile();
                        _fightSummary.Add(ScenarioName + " s" + Seed + " " + (_lastVictory ? "WIN" : "LOSS"));
                        try { StatManager sm = StatManager.Instance; if (sm != null) sm.CloseWindow(); } catch { }
                        if (_fightIndex + 1 >= Fights.Count) { Go(Step.Finish, 0.5f); break; }
                        if (_lastVictory)
                        {
                            // Reopening the same island after a win left the next battle's setup stuck (no error reaches the log). Re-activating the
                            // quest, exactly what the defeat window's Retry does, gives a fresh island like the first fight, which always works.
                            try { PostBattleManager.Instance.CloseWindow(); } catch { }
                            try { Root.IncomingConnectionGUIState = 3; } catch { }
                            try { PortalManager.Instance.ClearCurrentIsland(); QuestManager.instance.ActivateQuest(skipPortalEffect: true); Say("post-battle: victory, re-activating the quest for a fresh island"); }
                            catch (Exception e) { Say("quest re-activation failed: " + e.Message); Finish(false); return; }
                            Go(Step.WaitIslandAgain, 3f);
                        }
                        else
                        {
                            // Defeat: the post-battle sequence stops at the AdventureRewards window (no ExitCurrentBattle). Its Retry button is
                            // Root.SendQuestFailChoice(retry: true): clears the island and re-activates the quest, so the party lands on a fresh island.
                            try { Root.SendBattleFailureResult(0, NetworkingManager.Instance.NetworkManager.NetworkId); } catch (Exception e) { Say("failure result failed: " + e.Message); }
                            try { Root.SendQuestFailChoice(true, (int)GameLogic.instance.CurrentGameMode); Say("post-battle: defeat, retrying the quest for a fresh island"); }
                            catch (Exception e) { Say("quest retry failed: " + e.Message); Finish(false); return; }
                            Go(Step.WaitIslandAgain, 3f);
                        }
                        break;
                    }

                    case Step.WaitIslandAgain:
                        // OpenIsland regenerates the island in a coroutine (IslandGenerationInProgress) and only then sets InWorldMap; a battle
                        // requested while that runs starts its setup and stalls, so wait for the reload to be observed and finished.
                        // (WaitingForSpawnSet is not a usable signal here: it is only cleared by a spawn position or a loading screen, neither of which happens when the same island stays)
                        if ((int)(now - _stepStarted) % 15 == 14 && (int)(now - _stepStarted) != _lastWaitDiag)
                        {
                            _lastWaitDiag = (int)(now - _stepStarted);
                            try { Say("waiting for island: gui " + Gui + ", islandGen " + PortalManager.Instance.IslandGenerationInProgress + ", spawnWait " + PortalManager.Instance.WaitingForSpawnSet + ", loading " + LoadingScreen.Instance.IsLoading + ", fade " + LoadingScreen.Instance.MainFadeActive + ", room " + (WorldMapGenerator.instance != null && WorldMapGenerator.instance.CurrentRoom != null)); } catch (Exception e) { Say("diag failed: " + e.Message); }
                        }
                        try { if (AdventureRewards.instance != null && AdventureRewards.instance.gameObject.activeInHierarchy) AdventureRewards.instance.CloseWindow(); } catch { }
                        if (now - _stepStarted > 4f && Gui == GUIState.InWorldMap && PortalManager.Instance != null && !PortalManager.Instance.IslandGenerationInProgress
                            && WorldMapGenerator.instance != null && WorldMapGenerator.instance.CurrentRoom != null && LoadingScreen.Instance != null && !LoadingScreen.Instance.MainFadeActive && !LoadingScreen.Instance.IsLoading)
                        {
                            Say("island ready again after " + (int)(now - _stepStarted) + "s");
                            try { foreach (Character c in NetworkingManager.Instance.PartyCharacters) { if (c == null) continue; c.UpdateCellInfo(); Say("  " + c.CharacterName + " cell " + (c.Cell != null ? c.Cell.Coordinates.ToString() : "NULL") + " dead " + c.IsDead + " hp " + (int)c["Health"] + "/" + (int)c.MaxHealth); } } catch (Exception e) { Say("cell probe failed: " + e.Message); }
                            HealParty();
                            Go(Step.StartBattle, 3f);
                        }
                        break;

                    case Step.WaitTownAgain:
                        if (Gui == GUIState.InTown && LoadingScreen.Instance != null && !LoadingScreen.Instance.MainFadeActive)
                        {
                            try { if (AdventureRewards.instance != null) AdventureRewards.instance.CloseWindow(); } catch { }
                            HealParty();
                            Go(Step.OpenQuestSelect, 3f);
                        }
                        break;

                    case Step.Finish:
                        Finish(true);
                        break;
                }
            }
            catch (Exception e)
            {
                Say("EXCEPTION in step " + _step + ": " + e);
                Finish(false);
            }
        }

        /// <summary>A deliberately dumb but varied player: each living party member tries every skill it has not used this
        /// turn (real skills before the weapon), against the nearest enemy, then itself, then an empty neighbour, then an
        /// empty hex next to the enemy, so summons, self-buffs and placed tiles all get cast; otherwise it walks toward the
        /// nearest enemy once, otherwise it ends its turn. Enemies are played by the game's own AI.</summary>
        private readonly Dictionary<Character, HashSet<ActionInfo>> _castThisTurn = new Dictionary<Character, HashSet<ActionInfo>>();

        private void PlayPartyTurn()
        {
            try
            {
                if (Root == null || Root.CurrentTeamTurnIndex != 0 || Root.AnyActingCharactersInBattle || Root.AnyMovingCharactersInBattle) return;
                int turn = GameLogic.instance.turnNumber;
                if (turn != _lastActTurn) { _lastActTurn = turn; _movedThisTurn.Clear(); _castThisTurn.Clear(); }
                var enemies = Root.CharactersInBattleEnemyTeam.Where(e => e != null && !e.IsDead && e.Cell != null).ToList();
                if (enemies.Count == 0) return;
                foreach (Character c in NetworkingManager.Instance.PartyCharacters)
                {
                    if (c == null || c.IsDead || c.EndedTurn || c.Cell == null) continue;
                    if (_rotation != null) { if (_rotation.Act(c, enemies, turn)) return; continue; }
                    Character target = enemies.OrderBy(e => c.Cell.Distance(e.Cell)).First();
                    HashSet<ActionInfo> used;
                    if (!_castThisTurn.TryGetValue(c, out used)) _castThisTurn[c] = used = new HashSet<ActionInfo>();
                    var options = new List<ActionInfo>();
                    try { foreach (ActionAndSkill aas in c.Actions) if (aas.ActionInfo != null && !options.Contains(aas.ActionInfo)) options.Add(aas.ActionInfo); } catch { }
                    var basics = new List<ActionInfo>();
                    if (c.BasicAttacks != null) foreach (ActionInfo ba in c.BasicAttacks) if (ba != null) basics.Add(ba);
                    options.RemoveAll(o => basics.Contains(o));
                    options.AddRange(basics);
                    foreach (ActionInfo ai in options)
                    {
                        if (used.Contains(ai)) continue;
                        int ap = 0, mana = 0;
                        try { ap = ai.GetActionCost(c); mana = ai.GetManaCost(c); } catch { continue; }
                        if (ap > c.ActionPoints || mana > c.Mana) continue;
                        foreach (HexCell cell in CandidateCells(c, target))
                        {
                            bool ok = false;
                            try { ok = c.PlayerMovement.CanCast(new StructList<HexCell> { cell }, ai).CanCast; } catch { }
                            if (!ok) continue;
                            used.Add(ai);
                            c.PerformAction(ai, cell, c.CreateActionDetail());
                            c["ActionPoints"] -= ap; c.ActionPointsUsedThisTurn += ap;
                            if (mana > 0) c["Mana"] -= mana;
                            _attacks++;
                            return; // one thing per tick; wait for the animation
                        }
                    }
                    if (c.FreeMovementPoints > 0f && !_movedThisTurn.Contains(c) && c.Cell.Distance(target.Cell) > 1)
                    {
                        _movedThisTurn.Add(c);
                        var path = new List<HexCoordinates>();
                        HexCellManager.instance.GetHexPathCoordinates(c.Cell.Coordinates, target.Cell.Coordinates, c, ref path);
                        HexCell dest = null; float spent = 0f; HexCoordinates prev = c.Cell.Coordinates;
                        foreach (HexCoordinates hc in path)
                        {
                            if (hc == c.Cell.Coordinates) continue;
                            float step = HexCellManager.instance.HexCost(prev, hc, c, true);
                            if (float.IsInfinity(step) || spent + step > c.FreeMovementPoints) break;
                            HexCell cell = HexCellManager.instance.cells[hc];
                            if (cell == null || cell == target.Cell || cell.Player != null) break;
                            spent += step; dest = cell; prev = hc;
                        }
                        if (dest != null) { c.SendSetDestinationToServer(c.Cell.transform.position, dest.transform.position, c); _moves++; return; }
                    }
                    c.PlayerMovement.EndTurn();
                    _turnEnds++;
                    return;
                }
            }
            catch (Exception e) { Say("party turn failed: " + e.Message); }
        }

        /// <summary>Where a skill might be aimed, in order: the enemy, the caster, an empty hex next to the caster, an empty hex next to the enemy.</summary>
        private static IEnumerable<HexCell> CandidateCells(Character c, Character target)
        {
            yield return target.Cell;
            yield return c.Cell;
            foreach (HexCell n in c.Cell.Neighbors) if (n != null && n.IsEmpty && HexCellManager.IsValidAndEnabled(n)) { yield return n; break; }
            foreach (HexCell n in target.Cell.Neighbors) if (n != null && n.IsEmpty && HexCellManager.IsValidAndEnabled(n)) { yield return n; break; }
        }

        private void ProbeFortuneWindow()
        {
            try
            {
                if (CharacterMenusManager.Instance == null) { Say("fortune window: no CharacterMenusManager"); return; }
                CharacterMenusManager.Instance.OpenFortuneMenu();
                FortuneWindow fw = LoadableUIWindow<FortuneWindow>.Instance;
                int active = 0, total = 0;
                if (fw != null && fw.AvailableSlotHolder != null)
                    foreach (Transform t in fw.AvailableSlotHolder) { total++; if (t.gameObject.activeSelf) active++; }
                Character sel = GameLogic.instance.CurrentlySelectedCharacter;
                Say("fortune window: " + active + " visible slots (" + total + " total) for " + (sel != null ? sel.CharacterName + " who owns " + sel.FortuneData.Count : "?") + "; " + (Game.Instance != null ? Game.Instance.Fortunes.Length : 0) + " fortunes in the game");
                if (fw != null) fw.CloseFortuneWindow();
            }
            catch (Exception e) { Say("fortune window probe failed: " + e.Message); }
        }

        /// <summary>Ask BattleStats to end the current run, the same as leaving town does, so the next fight starts a new run and a new run file.</summary>
        private void ResetRunStats()
        {
            CallRunStats("ResetRun", "RUNSTATS: run reset, the next fight starts a new run", "test flag -srresetruns");
        }

        /// <summary>Call a static method on BattleStats' run-stats patches, if that mod is loaded.</summary>
        private void CallRunStats(string method, string saidOnSuccess, params object[] args)
        {
            CallStatic("BattleStats.Patches.RunStatsPatches", method, saidOnSuccess, args);
        }

        /// <summary>Call a static method on another mod, if it is loaded.</summary>
        private void CallStatic(string typeName, string method, string saidOnSuccess, params object[] args)
        {
            try
            {
                Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName, false)).FirstOrDefault(x => x != null);
                MethodInfo m = t != null ? t.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (m == null) { Say("RUNSTATS: " + method + " hook not found (BattleStats off or older build)"); return; }
                m.Invoke(null, args);
                Say(saidOnSuccess);
            }
            catch (Exception e) { Say("RUNSTATS: " + method + " failed: " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
        }

        private void SetOverlay(bool on)
        {
            try
            {
                Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ThreatOverlay.Patches.OverlayPatches", false)).FirstOrDefault(x => x != null);
                FieldInfo f = t != null ? t.GetField("TestForceHold", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (f == null) { Say("threat overlay: hook not found (mod off or older build)"); return; }
                f.SetValue(null, on);
                Say("threat overlay " + (on ? "forced on" : "released"));
            }
            catch (Exception e) { Say("threat overlay probe failed: " + e.Message); }
        }

        /// <summary>Log every row of the Stats window: label, then one value per character column. Prefix STATS = the post-battle page, RUNSTATS = BattleStats' run page.</summary>
        private void ReadStatsWindow(string prefix)
        {
            try
            {
                StatManager sm = StatManager.Instance;
                if (sm == null) { Say(prefix + ": no StatManager instance"); return; }
                try { var title = sm.Content != null ? sm.Content.transform.Find("Title") : null; var tt = title != null ? title.GetComponent<TMPro.TextMeshProUGUI>() : null; if (tt != null) Say(prefix + " title: " + Strip(tt.text)); } catch { }
                var names = new List<string>();
                Transform nh = sm.StatCharacterNameHolder.transform;
                for (int i = 0; i < nh.childCount; i++) { var t = nh.GetChild(i); if (t.gameObject.activeSelf) { var tmp = t.GetComponent<TMPro.TextMeshProUGUI>(); if (tmp != null) names.Add(Strip(tmp.text)); } }
                Say(prefix + " columns: " + string.Join(" | ", names.ToArray()));
                Transform lh = sm.StatLineHolder.transform, vh = sm.StatValueHolder.transform;
                int rows = 0;
                for (int r = 0; r < vh.childCount; r++)
                {
                    string label = "?";
                    if (r < lh.childCount) { var tmp = lh.GetChild(r).GetChild(0).GetComponent<TMPro.TextMeshProUGUI>(); if (tmp != null) label = Strip(tmp.text).Trim(); }
                    var vals = new List<string>();
                    Transform vr = vh.GetChild(r);
                    for (int c = 0; c < vr.childCount; c++) { if (!vr.GetChild(c).gameObject.activeSelf) continue; var tmp = vr.GetChild(c).GetComponent<TMPro.TextMeshProUGUI>(); vals.Add(tmp != null ? Strip(tmp.text) : "?"); }
                    Say(prefix + " row " + r + ": " + label + " | " + string.Join(" | ", vals.ToArray()));
                    rows++;
                }
                Say(prefix + ": " + rows + " rows, " + names.Count + " columns");
            }
            catch (Exception e) { Say(prefix + " read failed: " + e); }
        }

        private static string Strip(string richText)
        {
            return richText == null ? "" : System.Text.RegularExpressions.Regex.Replace(richText, "<[^>]+>", "");
        }

        /// <summary>The enemy pool is shuffled with ListExtensions' private static System.Random (unseeded), and the world map
        /// generator and deck randomizer keep their own; replace them with seeded instances so the same seed spawns the same fight.</summary>
        private void SeedStaticRandoms(int seed)
        {
            string[][] targets = { new[] { "ListExtensions", "rng" }, new[] { "WorldMapGenerator", "rng" } };
            foreach (string[] tf in targets)
            {
                try
                {
                    Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(tf[0], false)).FirstOrDefault(x => x != null);
                    FieldInfo f = type != null ? type.GetField(tf[1], BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
                    if (f != null && f.FieldType == typeof(System.Random)) f.SetValue(null, new System.Random(seed));
                    else Say("seed: " + tf[0] + "." + tf[1] + " not found");
                }
                catch (Exception e) { Say("seed: " + tf[0] + "." + tf[1] + " failed: " + e.Message); }
            }
        }

        /// <summary>Between batched fights: alive, full health and mana, no leftover statuses. The game itself clears cooldowns
        /// and re-adds per-battle charges when the next battle starts (GameLogic.StartGame).</summary>
        private void HealParty()
        {
            try
            {
                foreach (Character c in NetworkingManager.Instance.PartyCharacters)
                {
                    if (c == null) continue;
                    try { c.ResetCharacter(false); } catch (Exception e) { Say("ResetCharacter failed for " + c.CharacterName + ": " + e.Message); }
                    c.IsDead = false;
                    c["Health"] = c.MaxHealth;
                    c["Mana"] = c.MaxMana;
                }
                Say("party healed for the next fight");
            }
            catch (Exception e) { Say("heal failed: " + e.Message); }
        }

        private void WriteFightFile()
        {
            try
            {
                var lines = new List<string>();
                for (int i = _fightStartLine; i < _results.Count; i++) lines.Add(_results[i]);
                if (_rotation != null) { foreach (string line in _rotation.Trace) lines.Add("ROTATION " + line); _rotation.Trace.Clear(); }
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "TestDriver", "fights");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, ScenarioName + "-s" + Seed + ".txt"), (_lastVictory ? "WIN" : "LOSS") + Environment.NewLine + string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine);
            }
            catch (Exception e) { Say("fight file failed: " + e.Message); }
        }

        private void Finish(bool ok)
        {
            if (Fights.Count > 0 && _fightIndex >= 0 && _fightIndex < Fights.Count && _fightSummary.Count <= _fightIndex) { WriteFightFile(); _fightSummary.Add(ScenarioName + " s" + Seed + " " + (ok ? (_lastVictory ? "WIN" : "LOSS") : "INCOMPLETE")); }
            if (_fightSummary.Count > 0) Say("FIGHTS " + string.Join(", ", _fightSummary.ToArray()));
            if (_rotation != null && !_results.Any(r => r.StartsWith("ROTATION "))) foreach (string line in _rotation.Trace) _results.Add("ROTATION " + line);
            try { CharacterAttribute attr = Game.Instance.GetAttribute("AiControlledPlayer"); foreach (Character c in _party) { try { if (attr != null && c["AiControlledPlayer"] > 0f) c.AddAttribute(attr, -c["AiControlledPlayer"]); c.IsAIControlledPlayer = false; } catch { } } } catch { }
            Say((ok ? "PASS" : "FAIL") + " (battle seen: " + _battleSeen + ")");
            try
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "TestDriver");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "result.txt"), (ok ? "PASS" : "FAIL") + Environment.NewLine + string.Join(Environment.NewLine, _results.ToArray()) + Environment.NewLine);
            }
            catch { }
            _step = Step.Done;
            Application.Quit();
        }
    }
}
