Stolen Realm mods: QoL + DropRates + DifficultyXP + TargetTooltip + SpecialTooltips + ScalingTooltips + SharedFortunes
                   + FortunePreview + FortuneUpgrade + LevelSync + AutoSalvage + SharedGold + BattleStats + ThreatOverlay + ModMenu + SharedProgress + NumberFormat
                   (BepInEx 5.4.23.5, x64)

See "Stolen Realm Mods - Read Me.pdf" in this zip for the friendly version: one summary page, then a page per mod.
Easiest install: run "Install Stolen Realm Mods.exe" (finds the game folder, merges your existing settings). The
installer checks GitHub each time you run it and installs the newest pack, so keep the exe and just run it again to update.
Latest version: https://github.com/Kalarian/StolenRealm-Mods/releases/latest

INSTALL BY HAND
  1. Steam > right-click Stolen Realm > Manage > Browse local files.
  2. Extract this zip into that folder, so that winhttp.dll and the BepInEx folder sit next to "Stolen Realm.exe".
     (If Windows asks to merge folders, say yes.)
  3. Launch the game. That's it.

WHAT YOU GET
  QoL             - Noor's item upgrade costs only the level difference.
  DropRates       - better loot rolls: fewer commons, more rares/legendaries/mythics, elites can drop legendaries,
                    champions and bosses can drop mythics, The Merchant is 40% mythic, gambling tuned, and the town
                    armorer/jeweler sell rare or better (94/5/1 in act 1 rising to 76/20/4 in act 4).
  DifficultyXP    - experience scales with difficulty like gold does (Classic +25%, Veteran +50%, Torturous +75%,
                    Heart of the Realm +100%), campfire Prepared bonus included.
  TargetTooltip   - in battle, hovering an enemy shows the attack/skill a click will fire AND the damage it will do
                    to that enemy after its resistances/armor. F10 flips full/compact.
  SpecialTooltips - right-click Examine: the 'Special' entries (Armored, Unpredictable...) get explanatory tooltips.
  ScalingTooltips - every damage number in a skill tooltip shows its scaling, e.g. 118-159 (0.7x AP), plus a
                    Scaling line with the full math for your character.
  SharedFortunes  - fortunes are account-wide across YOUR characters (nothing is ever removed).
  FortunePreview  - quest hover lists the fortunes you can still earn there (hold LShift for the long list);
                    event options say which fortune they lead to; every fortune tooltip says where to find it;
                    the Fortune window also shows every fortune you do not own yet, greyed out.
  FortuneUpgrade  - in town, hover a fortune in the Fortune window and press U to raise it to your level for gold
                    (priced like a two-handed weapon of the same rarity, only the levels gained).
  LevelSync       - every character you own is kept at your highest character's level (points included);
                    existing ones catch up on load, your party levels together. Never lowers anyone.
  AutoSalvage     - white/green/blue equipment and trade commodities are sold the moment they drop; crafting
                    materials (and commodities any recipe needs) go straight to storage. Bought/crafted items untouched.
  SharedGold      - all your campaign characters share one purse; whoever you play holds the whole balance.
                    Only moves your own gold between your own characters, never creates any, never touches a friend's.
  BattleStats     - the post-battle Stats window gains: damage breakdown (direct / over time / ground tiles / summons /
                    thorns), hits and crit rate, kills, biggest hit, best skill, damage per turn, damage by element,
                    casts, mana spent, hexes moved. Hover a name for that character's top skills. Host needs the mod.
  ThreatOverlay   - in battle hold Left Alt: hexes enemies can walk to next turn go red, hexes they could hit from
                    there go orange. Release to clear.

  SharedProgress  - campaign progress (quest map, act, shop level) is shared across YOUR campaign characters;
                    a new character starts where your furthest one is. Nothing is ever taken away.
  NumberFormat    - every number the game shows gets thousands separators (418,218 gold, a 1,250 hit).
  ModMenu         - press F9 in game: a window with a checkbox per mod. Tick/untick, Apply, done (no restart).

  ON/OFF SWITCHBOARD: press F9 in game (ModMenu), or edit BepInEx\config\stolenrealm.mods.cfg by hand - one true/false
  line per mod, plus one VerboseLogging line for all of them. Detailed settings: BepInEx\config\stolenrealm.*.cfg.
  VerboseLogging is ON in these configs on purpose for now, so the log is useful if something goes wrong.

MULTIPLAYER
  Loot is rolled on your own PC, so DropRates only changes YOUR drops. Battle XP follows the HOST's DifficultyXP;
  quest and event XP your own. BattleStats records on the host and everyone with the mod sees the same numbers.
  Everything else is your own screen only. Nobody without the mods is affected.

IF SOMETHING LOOKS WRONG
  Send BepInEx\LogOutput.log. It is stamped with your Steam name near the top so we know whose it is, and it keeps
  every session (look for the "SESSION START" lines). It should contain "Loading [<ModName> 1.0.0]" for all
  seventeen mods and no "[Error" lines.

UNINSTALL
  Run the installer and click "Remove mods", or delete winhttp.dll, doorstop_config.ini, .doorstop_version and the
  BepInEx folder from the game folder. (To remove just one mod, set it to false in stolenrealm.mods.cfg.)
