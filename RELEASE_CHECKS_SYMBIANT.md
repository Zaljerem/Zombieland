# Symbiant Release Checks

Scope: player-facing Symbiant behavior added or materially changed after `v5.0.4.1`.

Baseline:
- Release baseline: `v5.0.4.1` / `e876a82`.
- Current source checkpoint: `729b234 Add symbiant damage guard` plus working-tree bridge coverage fixes for Symbiant feed discovery and relocation testing.
- Runtime target: local RimWorld app-bundle mod path reported by RimWorld as `.../RimWorldMac.app/Mods/ZombieLand`.
- Working save fixture: `SYMBIANT-TEST` for the current minimal-mod loadout; it is an empty visibility-friendly map with sterile tiles.
- Current stance: broad empirical checks first; avoid chasing minor edge details unless they undermine release confidence.

## Claims

| Area | Main player-facing claim | Evidence target | Status |
| --- | --- | --- | --- |
| Deployment/startup | Current Symbiant build loads from the mod path GABS starts, with no warning-or-higher logs. | Mod configuration + log read | PASS |
| Spawn/discovery | A Zombie Symbiant appears in a used indoor room, links to a host when available, and presents clear letter/look-target feedback. | `symbiant_discovery_letter_contract`, `symbiant_natural_spawn_contract` | PASS |
| Settings | Enabling/disabling events and lowering max cells behave like a cap, not deletion. Player-facing settings stay compact. | `symbiant_settings_contract`, settings read | PASS |
| Growth/rooms | It spreads through room/door cells, handles constructed-wall escape only into valid indoor targets, and does not grow outdoors. | `symbiant_expansion_contract`; relocation-specific pass if needed | PASS |
| Relocation/dormancy | Deconstructed/open rooms, old outdoor cells, reseeding, no-room dormancy, and double-speed relocation converge without trivial deconstruction exploits. | Existing relocation docs plus live targeted pass | PASS |
| Feeding | Feeding with corpses/coagulant restores guard, builds reserve, shrinks only safely, pauses growth, respects daily caps, and blocks one-cell early removal. | `symbiant_feeding_contract` | PASS |
| Benefits/disruption | Host benefit scales with integrated useful-room slime; remote containment has reduced value; slime disrupts rooms/work/pathing without direct injury. | `symbiant_benefit_contract` plus room/work evidence | PASS |
| Surgery | Safe severance requires maturity, full reserve, and <=3 visible cells; success cleans up; failure costs reserve and injures normally. | `symbiant_severance_contract` | PASS |
| Combat/damage | Ordinary hits drain visible guard, guard rupture kills linked host, thumper damage is ignored, and normal destruction/debug cleanup is not a shortcut. | `symbiant_unsafe_damage_contract` | PASS |
| Selection/UI | Selecting a Symbiant shows useful inspect text and does not expose normal pawn tabs such as Mood, Gear, Health, or Combat Log. | `symbiant_unsafe_damage_contract`, optional UI read | PASS |
| Combat isolation | Zombies and other systems do not treat the Symbiant like an ordinary hostile pawn, while feed jobs still find it. | `symbiant_combat_isolation_contract` | PASS |
| Host availability | Temporary host unavailability keeps the bond marker but disables active effects until the same host returns. | `symbiant_host_availability_contract` | PASS |
| Save/cache | Active Symbiant state survives save/load and same-process save switching without stale active-cache behavior. | `symbiant_infestation_state` smoke, `symbiant_map_cache_contract` | PASS |
| Stress/performance | Default cap remains stable enough for release; technical cap remains guarded. | `symbiant_infestation_state` stress or existing evidence | PASS |

## Evidence

| Result | Area | Operation / evidence | Notes |
| --- | --- | --- | --- |
| PASS | Deployment/startup | `get_mod_configuration_status`: Zombieland loaded from app-bundle `Mods/ZombieLand`; `list_logs minimumLevel=warning`: empty. | GABS launch path and deploy path match. |
| PASS | Build/deploy | `RIMWORLD_MOD_DIR=... ./scripts/build-quiet.sh`: build succeeded with 0 warnings and 0 errors after bridge changes. | The script stops RimWorld before deploying; GABS was restarted afterward. |
| PASS | UI/text visual pass | RegionShot screenshots: `/tmp/zombieland-settings-symbiant-filtered-window.png`, `/tmp/zombieland-settings-symbiant-help-enable-down1.png`, `/tmp/zombieland-settings-symbiant-help-max-size.png`, `/tmp/zombieland-settings-symbiant-help-coagulant-2.png`, `/tmp/zombieland-settings-symbiant-help-guard.png`, `/tmp/zombieland-symbiant-letter.png`, `/tmp/zombieland-symbiant-selected-post-inspect-order-2.png`, `/tmp/zombieland-symbiant-host-selected.png`. | Settings filter reduces the list to the four visible Symbiant settings; help text fits in the right pane; the letter wraps cleanly; Symbiant selection shows damage guard and weapon behavior above the initial inspect fold; host selection keeps normal pawn tabs and shows the soft host effect. |
| PASS | Spawn/discovery | `op_8924af8c1534456b9a16c0362446be30`: discovery letter; `op_f78384d891a14618ab5b662f1ea4c978`: natural spawn positive path; `op_9ca7426e65a24234a0a7a09ab56fcb8a`: empty map refuses natural spawn. | Letter label/look-targets are clear; host-linked letter names the room and host; no-room map has no eligible plan. |
| PASS | Settings | `op_1dc187b7f29f47b1a608d71f0cc231c8`: settings contract. | Disabled events block natural spawn; disabling does not delete an existing active Symbiant; lowered max acts as a cap. |
| PASS | Growth/rooms | `op_b72101903fc74262abdc37cf6e030c21`: expansion contract. | Open room cell, door cell, and constructed wall breach into the adjacent indoor room passed; door was occupied without destruction. |
| PASS | Relocation/dormancy | `op_47812747ea6248f3b5ca10b36d2a3e11`: relocation contract. | Deconstructed source room respects grace, then reseeds into the remaining used room with relocation debt; old outdoor cell is moved into the indoor room; no-room state stays dormant and does not grow outdoors. |
| PASS | Feeding | `op_fbe88c74e6df47658620684e50804ecf`: feeding contract. | Guard/recovery reserve, shrink pulses, growth pause, breach cancellation, daily cap, coagulant potency, and one-cell exploit guard passed. |
| PASS | Benefits/disruption | `op_b97ee70df14347ad88e7caef42ebcde8`: benefit contract; static checks in `Patches.cs` for path cost, work-speed stat factor, and room beauty/impressiveness. | Live check covered host hediff repair, benefit scaling, zombie targeting threshold, and skill bonus. Room/work/path disruption was not walked through in UI; source patch points are present and tied to Symbiant cell checks. |
| PASS | Surgery | `op_31728ce59c6c471ba81afc13c4122309`: severance contract. | Recipe ingredients and body-part availability gate on maturity/reserve/size; forced success destroys Symbiant safely; forced failure keeps bond and spends reserve. |
| PASS | Combat/damage + selection | `op_28bf4f3c4ca945e8865b5f905d1de68c`: unsafe damage contract. | Ordinary damage drains guard, overkill ruptures guard and kills host, host death collapses Symbiant, thumper damage has no effect, uncontrolled destroy detaches, selected Symbiant has no normal pawn tabs and inspect text includes guard. |
| PASS | Combat isolation | `op_78880bc5f9c944f3a04a8a0b354d5a1c`: combat isolation contract. | Player/enemy/animal/predator targeting excludes Symbiant; forced attack jobs are rejected; manhunter/prey/story-danger paths ignore it; feed job finds valid feed. |
| PASS | Host availability | `op_32a1f41c9d3441e691e10ba89fd567b5`: host availability contract. | Cryptosleep/off-map host keeps the bond marker but benefits/surgery are inactive until the same host returns. |
| PASS | Save/cache | `op_1ff0e7f436ce43b9aef17c082b97fb7c`: map cache contract; `op_f736a8b377a44140b3355d57571acc33`, `op_5b90385de84042a1a220c386a8b34b8e`, `op_b944797fb0bf4b889a4cfcfcbb86adea`, `op_26e377b2f8f049feb22d2eba7da1c428`: temporary save/load smoke. | Active/empty caches invalidate correctly; a one-cell Symbiant survived save/load in `SYMBIANT-TEST-AUTO-SYMBIANT`, was cleaned afterward, and the temporary save file was removed. |
| PASS | Stress/performance | `op_c7eee7105c834d84a5ae668c504989f7`: 400-cell stress; `op_686d89dc374a4754b92b833159eb72c4`: cleanup. | Default max reached 400/400, render shader metadata reported `Custom/ZombieSymbiant`, pawn-system isolation stayed false for hostile/threat registration, and cleanup removed the active Symbiant. |
| PASS | Final log state | `op_ef08692dd3244a5bbdd2677856d6a1cf`: warning-or-higher log read after visual UI pass and fixture reload. | Clean after all current-pass contracts; live session was restored to `SYMBIANT-TEST`. |

## Current Assessment

No release-blocking Symbiant issue was found in the broad player-facing pass on `SYMBIANT-TEST`.

Remaining non-blockers:
- The pass used the current minimal mod loadout, not the DLC-heavy `EMPTY` fixture.
- Screenshot/UI visual pass covered settings filtering/help, discovery letter layout, Symbiant selection, and host selection on `SYMBIANT-TEST`.
- No full pawn/raid stress fixture was run; the default 400-cell Symbiant stress path was clean.
- `SYMBIANT-TEST-AUTO-SYMBIANT.rws` was created for the persistence smoke and removed afterward.

## Release Notes

- Do not require every known edge gap to be closed before release. Promote only broad player-facing regressions or obvious exploits to blockers.
- `1.6/Assemblies/ZombieLand.dll` may be dirty during live validation. Restore tracked DLLs before the next normal source-only commit.
