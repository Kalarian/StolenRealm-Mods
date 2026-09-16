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
        private enum Step { WaitLoaded, StartSolo, WaitChoice, PickParty, WaitTown, ProbeFortuneWindow, OpenQuestSelect, PickQuest, WaitIsland, StartBattle, WaitBattle, HandToAi, OverlayOn, OverlayOff, WaitBattleEnd, ProbeStats, ReadStats, Finish, Done }

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
                        BattleInfo test = GlobalSettingsManager.instance.globalSettings.testBattle;
                        if (!string.IsNullOrEmpty(BattleName))
                        {
                            BattleInfo pick = Game.Instance.Battles.FirstOrDefault(b => b != null && b.name.IndexOf(BattleName, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (pick != null) test = pick; else Say("battle '" + BattleName + "' not found, using the test battle");
                        }
                        if (test == null) { Say("no test battle asset"); Finish(false); return; }
                        test.enemyMods = new Dictionary<int, List<EnemyMod>>();
                        GameLogic.instance.CurrentBattle = test;
                        Root.SendOpenBattle();
                        Say("test battle requested (" + test.name + ")");
                        Go(Step.WaitBattle, 1f);
                        break;
                    }

                    case Step.WaitBattle:
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
                        if (!UseGameAi && now >= _nextAct) { _nextAct = now + 0.75f; PlayPartyTurn(); }
                        if (Gui != GUIState.InBattle || (Root != null && !Root.BattleCurrentlyActive))
                        {
                            Say("battle over after " + (int)(now - _stepStarted) + "s, gui " + Gui + ", turn " + GameLogic.instance.turnNumber + " (driver: " + _attacks + " attacks, " + _moves + " moves, " + _turnEnds + " turn ends)");
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
                        ReadStatsWindow();
                        Go(Step.Finish, 0.5f);
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

        /// <summary>Log every row of the post-battle Stats window: label, then one value per character column.</summary>
        private void ReadStatsWindow()
        {
            try
            {
                StatManager sm = StatManager.Instance;
                if (sm == null) { Say("STATS: no StatManager instance"); return; }
                var names = new List<string>();
                Transform nh = sm.StatCharacterNameHolder.transform;
                for (int i = 0; i < nh.childCount; i++) { var t = nh.GetChild(i); if (t.gameObject.activeSelf) { var tmp = t.GetComponent<TMPro.TextMeshProUGUI>(); if (tmp != null) names.Add(Strip(tmp.text)); } }
                Say("STATS columns: " + string.Join(" | ", names.ToArray()));
                Transform lh = sm.StatLineHolder.transform, vh = sm.StatValueHolder.transform;
                int rows = 0;
                for (int r = 0; r < vh.childCount; r++)
                {
                    string label = "?";
                    if (r < lh.childCount) { var tmp = lh.GetChild(r).GetChild(0).GetComponent<TMPro.TextMeshProUGUI>(); if (tmp != null) label = Strip(tmp.text).Trim(); }
                    var vals = new List<string>();
                    Transform vr = vh.GetChild(r);
                    for (int c = 0; c < vr.childCount; c++) { if (!vr.GetChild(c).gameObject.activeSelf) continue; var tmp = vr.GetChild(c).GetComponent<TMPro.TextMeshProUGUI>(); vals.Add(tmp != null ? Strip(tmp.text) : "?"); }
                    Say("STATS row " + r + ": " + label + " | " + string.Join(" | ", vals.ToArray()));
                    rows++;
                }
                Say("STATS: " + rows + " rows, " + names.Count + " columns");
            }
            catch (Exception e) { Say("STATS read failed: " + e); }
        }

        private static string Strip(string richText)
        {
            return richText == null ? "" : System.Text.RegularExpressions.Regex.Replace(richText, "<[^>]+>", "");
        }

        private void Finish(bool ok)
        {
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
