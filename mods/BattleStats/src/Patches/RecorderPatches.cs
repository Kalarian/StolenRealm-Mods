using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace BattleStats.Patches
{
    /// <summary>
    /// Phase 1 recorder. The game credits every damage / heal / block through StatManager.ModifyBattleStat, called from
    /// the innermost Character.ApplyAction (the effects overload) while it resolves one action against one target.
    /// We wrap the callers that give a credit its meaning so the postfix on ModifyBattleStat can tag it:
    ///   ApplyAction(ActionStatus)          -> a status tick (poison, burn...)          path StatusTick
    ///   GroundEffect.ExecuteActionOnEnter  -> a tile's on-enter action                path GroundEnter
    ///   GameLogic.StartNewTurn             -> turn-start ticks incl. burning tiles     path TurnStart (unless a status tick is inside)
    ///   GameLogic.AddGroundEffect(ActionInfo..) -> a tile firing as it is placed      path GroundCreate
    ///   otherwise                          -> a direct cast                            path Direct
    /// The effects-overload prefix captures the action name, the target and its health, so overkill and kills can be
    /// derived; ProcessSkillTriggers(OnCrit) marks the last credit as a crit; ProcessDeath marks the kill.
    /// Battle start = StatManager.ClearStats; battle end = PostBattleManager.OpenPostBattleMenu.
    /// All of this runs on the machine that resolves the battle (the host).
    /// </summary>
    internal static class RecorderPatches
    {
        private static BattleRecord _battle;
        private static readonly List<CreditPath> _pathStack = new List<CreditPath>();
        private static readonly List<string> _statusStack = new List<string>();
        private static readonly List<ActionStatusInfo> _statusInfoStack = new List<ActionStatusInfo>();

        // innermost ApplyAction context
        private sealed class ActionCtx { public string Action; public string ActionDisplay; public ActionInfo Info; public Character Source; public Character Target; public float TargetHealthBefore; public int Seq; }
        private static readonly List<ActionCtx> _actionStack = new List<ActionCtx>();
        private static CreditEvent _lastDamage;                       // last DamageDealt credit (for crit marking)
        private struct HealthMod { public float Val; public DamageType Type; public int Seq; }
        private static int _actionSeq;                                 // bumped per innermost ApplyAction, so a health change can be tied to the credit that follows it
        private static readonly Dictionary<Character, HealthMod> _lastHealth = new Dictionary<Character, HealthMod>(); // per target, from ModifyHealth: the element of the last change
        private static readonly Dictionary<Character, float> _moveLast = new Dictionary<Character, float>();      // MovementThisTurn as last sampled
        private static readonly Dictionary<Character, CreditEvent> _lastHitOn = new Dictionary<Character, CreditEvent>(); // per target: last damaging credit (for kills)
        private static readonly HashSet<Character> _pendingKill = new HashSet<Character>(); // died inside an action whose stat credit has not arrived yet (the game processes death from the health setter, before the credit)
        private static readonly HashSet<Character> _killed = new HashSet<Character>();
        private static bool _redirecting;   // inside our own ModifyBattleStat call that re-credits a poison tick
        private static bool _cancelling;    // inside our own ModifyBattleStat call that takes a self-credit back
        // who poisoned whom this battle (stacks applied per victim per crediting player): the fallback when the victim's
        // live status list no longer shows the stacks at tick time
        private static readonly Dictionary<Character, Dictionary<Character, float>> _poisonApplied = new Dictionary<Character, Dictionary<Character, float>>();  // victims already credited as killed (one kill per life; a revived victim is removed when it takes a hit above 0 health)

        private static BattleStatsConfig Cfg => BattleStatsPlugin.Cfg;
        private static bool On => Cfg != null && Cfg.Enabled.Value;
        private static int Turn { get { try { return GameLogic.instance != null ? GameLogic.instance.turnNumber : 0; } catch { return 0; } } }

        internal static void Reset() { _battle = null; _pathStack.Clear(); _statusStack.Clear(); _statusInfoStack.Clear(); _actionStack.Clear(); _lastDamage = null; _lastHitOn.Clear(); _killed.Clear(); _pendingKill.Clear(); _lastHealth.Clear(); _moveLast.Clear(); _poisonApplied.Clear(); SharedStats.Reset(); }

        // ---------- movement (hexes walked), sampled from the game's own MovementThisTurn counter ----------

        private static void CaptureMovement()
        {
            if (_battle == null) return;
            try
            {
                var root = NetworkingManager.Instance != null ? NetworkingManager.Instance.NetworkManager.Root : null;
                var players = root != null ? root.CharactersInBattlePlayerTeam : null;
                if (players == null) return;
                foreach (Character c in players)
                {
                    if (c == null || c.IsAI) continue;
                    float cur = 0f; try { cur = c["MovementThisTurn"]; } catch { continue; }
                    float last; _moveLast.TryGetValue(c, out last);
                    if (cur > last)
                    {
                        string key = c.CharacterName ?? "?";
                        float have; _battle.HexesMoved.TryGetValue(key, out have);
                        _battle.HexesMoved[key] = have + (cur - last);
                        SharedStats.Add(c, SharedStats.HexesMoved, cur - last);
                    }
                    _moveLast[c] = cur;
                }
            }
            catch { }
        }

        // the game zeroes MovementThisTurn here at the start of the character's turn; sample first, then forget the old value
        [HarmonyPatch(typeof(Character), nameof(Character.ProcessAttributesOnNewTurn))]
        private static class Character_ProcessAttributesOnNewTurn
        {
            private static void Prefix(Character __instance) { if (_battle != null && __instance != null && !__instance.IsAI) CaptureMovement(); }
            private static void Postfix(Character __instance)
            {
                if (__instance == null) return;
                _moveLast[__instance] = 0f;
                if (_battle != null && !__instance.IsAI && __instance.TeamIndex == 0) SharedStats.Add(__instance, SharedStats.Turns, 1f);
            }
        }

        // ---------- casts (what each player actually did) ----------

        [HarmonyPatch(typeof(Character), nameof(Character.ExecutePerformAction))]
        private static class Character_ExecutePerformAction
        {
            private static void Prefix(Character __instance, ActionInfo actionInfo)
            {
                if (!On || _battle == null || __instance == null || actionInfo == null) return;
                try
                {
                    if (__instance.IsAI || __instance.TeamIndex != 0) return;
                    if (_pathStack.Count > 0) return; // a tile's turn-start action or similar, performed by the game on the caster's behalf: not a cast
                    string kind = "Action";
                    try { kind = actionInfo.GetActionType(__instance).ToString(); } catch { }
                    int ap = 0, mana = 0;
                    try { ap = actionInfo.GetActionCost(__instance); } catch { }
                    try { mana = actionInfo.GetManaCost(__instance); } catch { }
                    string name = actionInfo.name; try { name = OptionsManager.Localize(actionInfo.ActionName) ?? name; } catch { }
                    _battle.Casts.Add(new CastEvent { Turn = Turn, Character = __instance.CharacterName ?? "?", Action = name ?? "?", Kind = kind, ActionCost = ap, ManaCost = mana });
                    SharedStats.Add(__instance, SharedStats.Casts, 1f);
                    if (kind == "FreeAction") SharedStats.Add(__instance, SharedStats.FreeActions, 1f);
                    SharedStats.Add(__instance, SharedStats.ManaSpent, mana);
                    if (Cfg.LogEveryHit.Value) BattleStatsPlugin.Log.LogInfo("CAST t" + Turn + " " + __instance.CharacterName + ": " + name + " (" + kind + (mana > 0 ? ", " + mana + " mana" : "") + ")");
                }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("cast record failed: " + e.Message); }
            }
        }

        // ---------- element: ModifyHealth carries the damage type; it is called right before the stat credit ----------

        [HarmonyPatch]
        private static class Character_ModifyHealth
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return AccessTools.GetDeclaredMethods(typeof(Character)).Where(m => m.Name == "ModifyHealth" && m.GetParameters().Length >= 3 && m.GetParameters()[2].ParameterType == typeof(DamageType));
            }
            private static void Prefix(Character att, float val, DamageType damageType)
            {
                // only damaging changes count as element evidence: a death (health reset, on-death heals) must not overwrite them
                if (att != null && val < 0f && damageType != DamageType.Healing && damageType != DamageType.Mana && damageType != DamageType.None) _lastHealth[att] = new HealthMod { Val = val, Type = damageType, Seq = _actionSeq };
            }
        }

        // ---------- battle start / end ----------

        [HarmonyPatch(typeof(StatManager), nameof(StatManager.ClearStats))]
        private static class StatManager_ClearStats
        {
            private static void Postfix()
            {
                if (!On) return;
                _battle = new BattleRecord();
                _lastDamage = null; _lastHitOn.Clear(); _killed.Clear(); _pendingKill.Clear(); _lastHealth.Clear(); _moveLast.Clear(); _poisonApplied.Clear(); SharedStats.Reset();
                BattleStatsPlugin.Log.LogInfo("===== BATTLE START (turn " + Turn + ") =====");
            }
        }

        [HarmonyPatch(typeof(PostBattleManager), nameof(PostBattleManager.OpenPostBattleMenu))]
        private static class PostBattleManager_OpenPostBattleMenu
        {
            private static void Prefix(bool victory)
            {
                if (!On || _battle == null) return;
                try { FinishBattle(victory); }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("Battle summary failed: " + e); }
            }
        }

        private static void FinishBattle(bool victory)
        {
            CaptureMovement();
            _battle.Victory = victory;
            _battle.Turns = Math.Max(1, Turn);
            _battle.Ended = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _battle.Summarise(Cfg.TopSkills.Value, GameStatsFor);
            foreach (string line in _battle.SummaryLines()) BattleStatsPlugin.Log.LogInfo(line);
            if (Cfg.WriteJson.Value)
            {
                try
                {
                    string path = _battle.WriteJson(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "BattleStats"));
                    BattleStatsPlugin.Log.LogInfo("Battle written to " + path);
                }
                catch (Exception e) { BattleStatsPlugin.Log.LogWarning("JSON write failed: " + e.Message); }
            }
            _battle = null;
        }

        /// <summary>The game's own BattleStats dictionary for a character, by stat name.</summary>
        private static Dictionary<string, float> GameStatsFor(string characterName)
        {
            var d = new Dictionary<string, float>();
            var root = NetworkingManager.Instance != null ? NetworkingManager.Instance.NetworkManager.Root : null;
            if (root == null || root.BattleStats == null) return d;
            foreach (CharacterBattleStats cbs in root.BattleStats)
            {
                if (cbs == null || cbs.Character == null || cbs.Character.CharacterName != characterName || cbs.BattleStats == null) continue;
                foreach (var kv in cbs.BattleStats)
                {
                    if (kv.Key >= SharedStats.BucketBase) continue; // per-ability breakdown keys: too many to print
                    string key = Enum.IsDefined(typeof(BattleStat), kv.Key) ? ((BattleStat)kv.Key).ToString() : "key" + kv.Key;
                    d[key] = kv.Value;
                }
            }
            return d;
        }

        // ---------- path context ----------

        private static void Push(CreditPath p, string status, ActionStatusInfo statusInfo = null)
        {
            _pathStack.Add(p); _statusStack.Add(status); _statusInfoStack.Add(statusInfo);
            if (Cfg != null && Cfg.DebugHooks.Value) BattleStatsPlugin.Log.LogInfo("  > " + p + (status != null ? " [" + status + "]" : ""));
        }
        private static void Pop()
        {
            if (_pathStack.Count > 0) { _pathStack.RemoveAt(_pathStack.Count - 1); _statusStack.RemoveAt(_statusStack.Count - 1); _statusInfoStack.RemoveAt(_statusInfoStack.Count - 1); }
        }
        private static CreditPath CurrentPath(out string status, out ActionStatusInfo statusInfo)
        {
            status = null; statusInfo = null;
            for (int i = _pathStack.Count - 1; i >= 0; i--)
            {
                if (_pathStack[i] == CreditPath.StatusTick) { status = _statusStack[i]; statusInfo = _statusInfoStack[i]; return CreditPath.StatusTick; }
            }
            if (_pathStack.Count == 0) return CreditPath.Direct;
            return _pathStack[_pathStack.Count - 1];
        }

        // status tick: Character.ApplyAction(ActionStatus status, bool ignoreProcs, bool calculateAllStacksInDamage)
        [HarmonyPatch]
        private static class Character_ApplyAction_Status
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(Character)).First(m => m.Name == "ApplyAction" && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ActionStatus));
            }
            private static void Prefix(ActionStatus status)
            {
                string name = null;
                try { name = status != null && status.ActionStatusInfo != null ? (OptionsManager.Localize(status.ActionStatusInfo.Name) ?? status.ActionStatusInfo.name) : null; } catch { }
                Push(CreditPath.StatusTick, name ?? "status", status != null ? status.ActionStatusInfo : null);
            }
            private static void Finalizer() { Pop(); }
        }

        [HarmonyPatch(typeof(GroundEffect), nameof(GroundEffect.ExecuteActionOnEnter))]
        private static class GroundEffect_ExecuteActionOnEnter
        {
            private static void Prefix() { Push(CreditPath.GroundEnter, null); }
            private static void Finalizer() { Pop(); }
        }

        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.StartNewTurn))]
        private static class GameLogic_StartNewTurn
        {
            private static void Prefix() { Push(CreditPath.TurnStart, null); }
            private static void Finalizer() { Pop(); }
        }

        // GameLogic.AddGroundEffect(ActionInfo actionInfo, IEnumerable<HexCell> cells, Character source, HexCell origin, float delayRef, int expireActionIndex)
        [HarmonyPatch]
        private static class GameLogic_AddGroundEffect
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(GameLogic)).First(m => m.Name == "AddGroundEffect" && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ActionInfo));
            }
            private static void Prefix() { Push(CreditPath.GroundCreate, null); }
            private static void Finalizer() { Pop(); }
        }

        // innermost: Character.ApplyAction(Character source, Character target, IEffectInfo[] effects, ActionProperties properties, ...)
        [HarmonyPatch]
        private static class Character_ApplyAction_Effects
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(Character)).First(m => m.Name == "ApplyAction" && m.GetParameters().Length > 3 && m.GetParameters()[0].ParameterType == typeof(Character) && m.GetParameters()[1].ParameterType == typeof(Character));
            }
            private static void Prefix(Character source, Character target, ActionProperties properties)
            {
                _actionSeq++;
                var ctx = new ActionCtx { Source = source, Target = target, Seq = _actionSeq };
                try
                {
                    ActionInfo ai = properties.ActionInfo; // ActionProperties is a struct
                    ctx.Info = ai;
                    if (ai != null) { ctx.Action = ai.name; try { ctx.ActionDisplay = OptionsManager.Localize(ai.ActionName); } catch { ctx.ActionDisplay = ai.ActionName; } }
                    if (target != null) ctx.TargetHealthBefore = target["Health"];
                }
                catch { }
                _actionStack.Add(ctx);
            }
            private static void Finalizer()
            {
                if (_actionStack.Count == 0) return;
                ActionCtx ctx = _actionStack[_actionStack.Count - 1];
                _actionStack.RemoveAt(_actionStack.Count - 1);
                if (ctx.Target != null && _pendingKill.Remove(ctx.Target)) ResolveKill(ctx.Target);
            }
        }

        /// <summary>Credit a confirmed death to the last damaging hit on the victim, or log it as unattributed.</summary>
        private static void ResolveKill(Character victim)
        {
            CreditEvent last;
            if (_lastHitOn.TryGetValue(victim, out last)) MarkKill(victim, last, FindPlayer(last.Character), null);
            else if (Cfg.LogEveryHit.Value) BattleStatsPlugin.Log.LogInfo("  DEATH " + victim.CharacterName + " #" + victim.GetHashCode() + (!victim.IsAI && victim.TeamIndex == 0 ? " [PLAYER DOWN]" : " (no player hit recorded: environment / summon / prop)"));
        }

        // ---------- the credit itself ----------

        [HarmonyPatch(typeof(StatManager), nameof(StatManager.ModifyBattleStat))]
        private static class StatManager_ModifyBattleStat
        {
            private static void Postfix(Character character, BattleStat battleStat, float amount)
            {
                if (!On || _battle == null || character == null) return;
                try
                {
                    // Summon credits arrive twice: once for the summon itself (AI, ignored by the game) and once re-routed to
                    // its master as SummonDamageDealt. Record only what the game records: non-AI, team 0.
                    if (_cancelling) return;
                    ActionCtx ctx = _actionStack.Count > 0 ? _actionStack[_actionStack.Count - 1] : null;
                    // Poison is dealt by the victim to itself (the global PoisonTickAction), so the game credits the victim:
                    // nothing for an enemy, and a player's own Damage Dealt when a player is poisoned. Re-credit it to the
                    // characters whose Poisoned stacks are on the victim, in proportion to what each stack contributes.
                    // Only when the game credited the VICTIM itself. With Contagion the game already credits the contagion source
                    // (a player) for the same tick, so that credit must simply be recorded, not redirected on top.
                    if (!_redirecting && battleStat == BattleStat.DamageDealt && ctx != null && IsPoisonTick(ctx) && (ctx.Target == null || character == ctx.Target)) { RedirectPoisonTick(ctx.Target ?? character, amount, ctx); return; }
                    if (character.IsAI || character.TeamIndex != 0) return;
                    if (amount == 0f) return; // the game reports exact 0 for blocked stats on self-buffs: noise (fractions are real and add up)
                    string status; ActionStatusInfo statusInfo;
                    CreditPath path = CurrentPath(out status, out statusInfo);
                    if (_redirecting || (ctx != null && IsPoisonTick(ctx))) { path = CreditPath.StatusTick; status = (!_redirecting && ctx != null && character != ctx.Target) ? "Poisoned (Contagion spread)" : "Poisoned"; statusInfo = GameLogic.instance != null ? GameLogic.instance.PoisonedStatus : null; }
                    if (battleStat == BattleStat.SummonDamageDealt || battleStat == BattleStat.SummonDamageTaken) path = CreditPath.Summon;
                    if (battleStat == BattleStat.DamageReturned) path = CreditPath.Returned;
                    var e = new CreditEvent
                    {
                        Turn = Turn,
                        Stat = battleStat.ToString(),
                        Character = character.CharacterName ?? "?",
                        Amount = amount,
                        Path = path.ToString(),
                        Status = status,
                        Action = ctx != null ? ctx.Action : null,
                        ActionDisplay = ctx != null ? ctx.ActionDisplay : null,
                    };
                    Character target = null;
                    if (ctx != null)
                    {
                        // For a credit to the attacker the "other" is the target; for DamageTaken/HealingReceived it is the source.
                        bool creditedIsSource = ctx.Source == character;
                        target = creditedIsSource ? ctx.Target : ctx.Source;
                        if (target != null) { e.Target = target.CharacterName; e.TargetIsPlayer = !target.IsAI && target.TeamIndex == 0; }
                        if ((battleStat == BattleStat.DamageDealt || battleStat == BattleStat.SummonDamageDealt) && ctx.Target != null)
                        {
                            e.TargetHealthBefore = ctx.TargetHealthBefore;
                            float hp = 0f; try { hp = ctx.Target["Health"]; } catch { }
                            if (hp <= 0f && ctx.TargetHealthBefore > 0f) e.Overkill = Mathf.Max(0f, amount - ctx.TargetHealthBefore); // provisional: counts only if the game really processes the death
                            _lastHitOn[ctx.Target] = e;
                            if (_pendingKill.Remove(ctx.Target)) MarkKill(ctx.Target, e, character, status); // the death the game processed a moment ago belongs to this credit
                            ctx.TargetHealthBefore = Mathf.Max(0f, ctx.TargetHealthBefore - amount); // several credits can land on one target inside one action
                        }
                    }
                    DamageType element = DamageType.None;
                    if (battleStat == BattleStat.DamageDealt || battleStat == BattleStat.DamageTaken || battleStat == BattleStat.SummonDamageDealt)
                    {
                        Character victim = battleStat != BattleStat.DamageTaken ? (ctx != null ? ctx.Target : null) : character;
                        HealthMod hm;
                        // The health change that produced this credit is the last one on the victim inside the same action
                        // (exact amounts differ when the hit overkills, is split by shields, or is a summon's). Outside an
                        // action context (ticks), accept it only when the amount matches.
                        if (victim != null && _lastHealth.TryGetValue(victim, out hm) && hm.Type != DamageType.Healing && hm.Type != DamageType.Mana && hm.Type != DamageType.None
                            && ((ctx != null && hm.Seq == ctx.Seq) || Mathf.Abs(Mathf.Ceil(-hm.Val) - amount) < 1.5f)) { element = hm.Type; e.Element = hm.Type.ToString(); }
                        // a status tick with no matching health change: the status declares its own damage type
                        if (element == DamageType.None && path == CreditPath.StatusTick && statusInfo != null)
                        {
                            DamageType dt = statusInfo.DamageType;
                            if (dt == DamageType.Physical || dt == DamageType.Fire || dt == DamageType.Cold || dt == DamageType.Lightning || dt == DamageType.Shadow || dt == DamageType.Holy) { element = dt; e.Element = dt.ToString() + "*"; }
                        }
                    }
                    if (battleStat == BattleStat.DamageDealt) _lastDamage = e;
                    _battle.Events.Add(e);
                    Share(character, battleStat, amount, path, e, ctx, statusInfo, element);
                    if (Cfg.LogEveryHit.Value)
                    {
                        string kind = battleStat.ToString().StartsWith("Blocked") ? "BLOCK" : battleStat.ToString().Contains("Heal") ? "HEAL" : battleStat == BattleStat.DamageTaken || battleStat == BattleStat.SummonDamageTaken ? "TAKEN" : "HIT";
                        BattleStatsPlugin.Log.LogInfo(kind + " t" + e.Turn + " " + e.Character + (e.Target != null ? " -> " + e.Target : "") + ": " + amount.ToString("0", CultureInfo.InvariantCulture)
                            + " " + battleStat + (e.Element != null ? " " + e.Element : "") + " [" + e.Path + (status != null ? ": " + status : "") + (e.Action != null ? " | " + (e.ActionDisplay ?? e.Action) : "") + "]"
                            + (e.Overkill > 0 ? " overkill " + e.Overkill.ToString("0", CultureInfo.InvariantCulture) : ""));
                    }
                }
                catch (Exception ex) { BattleStatsPlugin.Log.LogWarning("record failed: " + ex.Message); }
            }
        }

        // ---------- poison: the victim ticks itself, so the credit has to be handed to whoever poisoned it ----------

        private static Character Creditor(Character source)
        {
            if (source == null) return null;
            if (source.IsAI && source.IsSummon && source.SummonMaster != null) source = source.SummonMaster;
            return (!source.IsAI && source.TeamIndex == 0) ? source : null;
        }

        // GameLogic.CreateActionStatus(Character source, Character target, ActionStatusInfo statusInfo, ...): one call per stack
        [HarmonyPatch]
        private static class GameLogic_CreateActionStatus
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.GetDeclaredMethods(typeof(GameLogic)).First(m => m.Name == "CreateActionStatus" && m.GetParameters().Length > 2 && m.GetParameters()[2].ParameterType == typeof(ActionStatusInfo));
            }
            private static void Postfix(Character source, Character target, ActionStatusInfo statusInfo, ActionStatus __result)
            {
                if (!On || _battle == null || __result == null || target == null || statusInfo == null) return;
                try
                {
                    ActionStatusInfo poisoned = GameLogic.instance != null ? GameLogic.instance.PoisonedStatus : null;
                    if (poisoned != null ? statusInfo != poisoned : (statusInfo.name ?? "").IndexOf("Poisoned", StringComparison.OrdinalIgnoreCase) < 0) return;
                    Character who = Creditor(source);
                    if (Cfg.DebugHooks.Value) BattleStatsPlugin.Log.LogInfo("  POISONED " + target.CharacterName + " by " + (source != null ? source.CharacterName : "?") + (who != null && who != source ? " (credit " + who.CharacterName + ")" : "") + " (path " + string.Join(">", _pathStack.Select(x => x.ToString()).ToArray()) + ")");
                    if (who == null) return;
                    Dictionary<Character, float> d;
                    if (!_poisonApplied.TryGetValue(target, out d)) _poisonApplied[target] = d = new Dictionary<Character, float>();
                    float have; d.TryGetValue(who, out have); d[who] = have + 1f;
                }
                catch { }
            }
        }

        private static bool IsPoisonTick(ActionCtx ctx)
        {
            try
            {
                if (ctx.Info == null) return false;
                ActionInfo tick = Game.Instance != null ? Game.Instance.PoisonTickAction : null;
                if (tick != null) return ctx.Info == tick;
                return ctx.Source == ctx.Target && ctx.Info.name != null && ctx.Info.name.IndexOf("Poison", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static void RedirectPoisonTick(Character victim, float amount, ActionCtx ctx)
        {
            try
            {
                if (victim == null || amount == 0f) return;
                ActionStatusInfo poisoned = GameLogic.instance != null ? GameLogic.instance.PoisonedStatus : null;
                var shares = new List<KeyValuePair<Character, float>>(); var viaSummon = new HashSet<Character>();
                float total = 0f; var note = new StringBuilder();
                if (victim.ActionStatuses != null)
                {
                    foreach (ActionStatus st in victim.ActionStatuses)
                    {
                        if (st == null || st.Source == null || st.ActionStatusInfo == null) continue;
                        if (poisoned != null ? st.ActionStatusInfo != poisoned : (st.ActionStatusInfo.name ?? "").IndexOf("Poisoned", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        int stacks = 0; try { stacks = st.TotalStacks; } catch { }
                        if (stacks <= 0) continue;
                        float per = 1f; try { per = Mathf.Max(1f, st.Source.SpellPower("Shadow") * 0.1f); } catch { }
                        float w = stacks * per;
                        total += w;
                        Character credit = st.Source;
                        bool summon = false;
                        if (credit.IsAI && credit.IsSummon && credit.SummonMaster != null) { credit = credit.SummonMaster; summon = true; }
                        note.Append(" ").Append(st.Source.CharacterName).Append("x").Append(stacks);
                        if (credit.IsAI || credit.TeamIndex != 0) continue; // an enemy's poison: nobody to credit, but its share still counts toward the total
                        int at = shares.FindIndex(k => k.Key == credit);
                        if (at >= 0) shares[at] = new KeyValuePair<Character, float>(credit, shares[at].Value + w); else shares.Add(new KeyValuePair<Character, float>(credit, w));
                        if (summon) viaSummon.Add(credit);
                    }
                }
                if (shares.Count == 0)
                {
                    // no live stacks with a player source: fall back to who applied poison to this victim during the fight
                    Dictionary<Character, float> hist;
                    if (_poisonApplied.TryGetValue(victim, out hist))
                    {
                        total = 0f;
                        foreach (var kv in hist) { if (kv.Key == null || kv.Value <= 0f) continue; shares.Add(kv); total += kv.Value; }
                        if (shares.Count > 0) note.Append(" | from history:").Append(string.Join(",", hist.Select(k => k.Key.CharacterName + "x" + k.Value).ToArray()));
                    }
                }
                if (Cfg.DebugHooks.Value)
                {
                    var sb = new StringBuilder();
                    try
                    {
                        foreach (ActionStatus st in victim.ActionStatuses)
                        {
                            if (st == null || st.ActionStatusInfo == null) continue;
                            int rem = -1; try { var oldest = st.GetOldestStack(); if (oldest != null) rem = oldest.GetTurnsRemaining(); } catch { }
                            sb.Append(" ").Append(st.ActionStatusInfo.name).Append("(").Append(st.Source != null ? st.Source.CharacterName : "null").Append(",").Append(st.TotalStacks).Append("/").Append(st.StackInfo != null ? st.StackInfo.Count : -1).Append(",rem").Append(rem).Append(")");
                        }
                        float pd = 0f; try { pd = victim["PoisonDamage"]; } catch { }
                        sb.Append(" | PoisonDamage=").Append(pd.ToString("0.#", CultureInfo.InvariantCulture)).Append(" | path ").Append(string.Join(">", _pathStack.Select(x => x.ToString()).ToArray()));
                    }
                    catch (Exception ex) { sb.Append(" (status list failed: " + ex.Message + ")"); }
                    BattleStatsPlugin.Log.LogInfo("  POISON DIAG " + victim.CharacterName + ":" + sb);
                }
                bool victimIsPlayer = !victim.IsAI && victim.TeamIndex == 0;
                if (victimIsPlayer)
                {
                    _cancelling = true;
                    try { StatManager.ModifyBattleStat(victim, BattleStat.DamageDealt, -amount); } finally { _cancelling = false; }
                }
                if (Cfg.LogEveryHit.Value) BattleStatsPlugin.Log.LogInfo("POISON t" + Turn + " " + victim.CharacterName + " takes " + amount.ToString("0", CultureInfo.InvariantCulture) + (ctx.Source != null && ctx.Source != victim ? " (tick source " + ctx.Source.CharacterName + ")" : " (self tick)") + " from its stacks [" + note.ToString().Trim() + "]" + (victimIsPlayer ? " (self-credit taken back)" : "") + (shares.Count == 0 ? " -> no player to credit" : ""));
                if (total <= 0f) return;
                foreach (KeyValuePair<Character, float> sh in shares)
                {
                    float part = amount * sh.Value / total;
                    if (part <= 0f) continue;
                    _redirecting = true;
                    try { StatManager.ModifyBattleStat(sh.Key, viaSummon.Contains(sh.Key) ? BattleStat.SummonDamageDealt : BattleStat.DamageDealt, part); }
                    finally { _redirecting = false; }
                }
            }
            catch (Exception ex) { BattleStatsPlugin.Log.LogWarning("poison redirect failed: " + ex); }
        }

        /// <summary>Write the credit into the replicated per-character dictionary (SharedStats) so the Stats window can show it on every machine.</summary>
        private static void Share(Character character, BattleStat stat, float amount, CreditPath path, CreditEvent e, ActionCtx ctx, ActionStatusInfo statusInfo, DamageType element)
        {
            try
            {
                if (stat == BattleStat.DamageDealt)
                {
                    int bucket = path == CreditPath.StatusTick ? SharedStats.Ticks : (path == CreditPath.GroundEnter || path == CreditPath.TurnStart || path == CreditPath.GroundCreate) ? SharedStats.Tiles : SharedStats.Direct;
                    SharedStats.Add(character, bucket, amount);
                    SharedStats.Add(character, SharedStats.Hits, 1f);
                    if (element != DamageType.None) SharedStats.Add(character, SharedStats.ElementBase + (int)element, amount);
                    int code = SharedStats.Code(ctx != null ? ctx.Info : null, path == CreditPath.StatusTick ? statusInfo : null);
                    if (amount > SharedStats.Get(character, SharedStats.BiggestHit)) { SharedStats.Put(character, SharedStats.BiggestHit, amount); SharedStats.Put(character, SharedStats.BiggestHitSource, code); }
                    SharedStats.RecordSource(character, code, amount);
                    // per-section breakdown for the hover tooltips
                    int b = bucket == SharedStats.Ticks ? SharedStats.B_Ticks : bucket == SharedStats.Tiles ? SharedStats.B_Tiles : SharedStats.B_Direct;
                    SharedStats.AddBucket(character, b, code, amount);
                    SharedStats.AddBucket(character, element != DamageType.None ? SharedStats.B_ElementBase + (int)element : SharedStats.B_Untyped, code, amount);
                    SharedStats.AddBucket(character, SharedStats.B_Dealt, code, amount);
                    SharedStats.AddBucket(character, SharedStats.B_All, code, amount);
                }
                else if (stat == BattleStat.DamageTaken && path == CreditPath.StatusTick) SharedStats.Add(character, SharedStats.TakenFromTicks, amount);
                else if (stat == BattleStat.SummonDamageDealt || stat == BattleStat.DamageReturned)
                {
                    int code = SharedStats.Code(ctx != null ? ctx.Info : null, path == CreditPath.StatusTick ? statusInfo : null);
                    SharedStats.AddBucket(character, stat == BattleStat.SummonDamageDealt ? SharedStats.B_Summons : SharedStats.B_Thorns, code, amount);
                    SharedStats.AddBucket(character, SharedStats.B_All, code, amount);
                }
            }
            catch (Exception ex) { if (Cfg.Verbose.Value) BattleStatsPlugin.Log.LogWarning("share failed: " + ex.Message); }
        }

        // crit: source.ProcessSkillTriggers(target, properties, TriggerType.OnCrit) is called right after the damage credit
        [HarmonyPatch(typeof(Character), nameof(Character.ProcessSkillTriggers))]
        private static class Character_ProcessSkillTriggers
        {
            private static void Prefix(Character __instance, Character target, TriggerType triggerType)
            {
                if (!On || _battle == null || triggerType != TriggerType.OnCrit || _lastDamage == null) return;
                try
                {
                    if (_lastDamage.Character == __instance.CharacterName && (target == null || _lastDamage.Target == target.CharacterName) && !_lastDamage.Crit)
                    {
                        _lastDamage.Crit = true;
                        SharedStats.Add(__instance, SharedStats.Crits, 1f);
                        if (Cfg.LogEveryHit.Value) BattleStatsPlugin.Log.LogInfo("  CRIT (" + _lastDamage.Amount.ToString("0", CultureInfo.InvariantCulture) + " " + (_lastDamage.ActionDisplay ?? _lastDamage.Action) + ")");
                    }
                }
                catch { }
            }
        }

        /// <summary>One kill per victim per life: the credit that took it to 0 (or, failing that, the last hit before ProcessDeath).</summary>
        private static void MarkKill(Character victim, CreditEvent e, Character killer, string status)
        {
            if (victim == null || e == null || _killed.Contains(victim)) return;
            _killed.Add(victim);
            e.Kill = true;
            if (killer != null) { SharedStats.Add(killer, SharedStats.Kills, 1f); SharedStats.Add(killer, SharedStats.Overkill, e.Overkill); }
            if (Cfg.LogEveryHit.Value) BattleStatsPlugin.Log.LogInfo("  KILL " + victim.CharacterName + " #" + victim.GetHashCode() + " by " + (killer != null ? killer.CharacterName : e.Character) + " (" + (e.ActionDisplay ?? e.Action ?? e.Status ?? status ?? "?") + ", " + e.Path + ")" + (!victim.IsAI && victim.TeamIndex == 0 ? " [PLAYER DOWN]" : ""));
        }

        private static Character FindPlayer(string name)
        {
            try
            {
                var root = NetworkingManager.Instance != null ? NetworkingManager.Instance.NetworkManager.Root : null;
                if (root == null || root.CharactersInBattlePlayerTeam == null) return null;
                foreach (Character c in root.CharactersInBattlePlayerTeam) if (c != null && !c.IsAI && c.CharacterName == name) return c;
            }
            catch { }
            return null;
        }

        // kill: the game calls ProcessDeath on the server when health drops below 1 in battle. A hit that takes health to 0
        // is not always a kill (Dark Ritual and similar put the character back to 1 health inside ProcessDeath), so a
        // kill is credited only once the game has set IsDead, to the last damaging credit on that victim.
        [HarmonyPatch(typeof(Character), nameof(Character.ProcessDeath))]
        private static class Character_ProcessDeath
        {
            private static bool _wasDead;
            private static void Prefix(Character __instance) { _wasDead = __instance == null || __instance.IsDead; }
            private static void Postfix(Character __instance)
            {
                if (!On || _battle == null || __instance == null || _wasDead || !__instance.IsDead) return;
                try
                {
                    if (_killed.Contains(__instance)) return;
                    // Death is processed from the health setter, i.e. inside the action that dealt the blow and BEFORE its
                    // stat credit. If we are inside such an action, wait for that credit; otherwise use the last hit.
                    ActionCtx ctx = _actionStack.Count > 0 ? _actionStack[_actionStack.Count - 1] : null;
                    if (ctx != null && ctx.Target == __instance) { _pendingKill.Add(__instance); return; }
                    ResolveKill(__instance);
                }
                catch { }
            }
        }
    }
}
