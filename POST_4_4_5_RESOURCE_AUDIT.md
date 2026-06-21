# Post-4.4.5.0 Resource Audit

Baseline: `4c996bf13708ebe746419666168250708eaf593d` (`v4.4.5.0`).

Latest 1.6 source of truth: `origin/master`.

Audited branch result: `HEAD` on `fix/common-root-v445`.

Unstaged local `1.6/` and `Source/` work was ignored; all comparisons below use Git objects.

## Result

No committed runtime-resource changes after `v4.4.5.0` were lost by the root-runtime cleanup.

- `origin/master` has 49 commits after the baseline that touched runtime-resource paths.
- Collapsing overwritten intermediate edits leaves 104 net effective RimWorld 1.6 runtime-resource changes versus the old release: 47 added, 50 modified, 7 removed.
- HEAD root runtime absent: 0 differences.
- HEAD effective 1.4 vs official 4c996bf root: 0 differences.
- HEAD effective 1.6 vs origin/master effective 1.6: 0 differences.

This means the branch preserves the latest committed 1.6 payload while keeping 1.4 equal to the official 4.4.5.0 release payload.

## Scope And Method

Runtime-resource paths audited here are `Assemblies`, `Defs`, `Languages`, `Libraries`, `Patches`, `Resources`, `Sounds`, and `Textures`, including the same folders under version directories such as `1.4/` and `1.6/`.

Excluded paths are `Source/**`, solution/project/version files, docs, scripts, workflows, coverage files, and saves. `LoadFolders.xml` is discussed only as loader behavior, not as runtime content.

The important comparison is the effective RimWorld payload, not raw file location. Same-relative-path shadowing and the current `LoadFolders.xml` rules are applied before comparing payloads.

## Net Effective 1.6 Changes

| Folder | Added | Modified | Removed |
|---|---:|---:|---:|
| `Assemblies` | 1 | 2 | 0 |
| `Defs` | 0 | 17 | 0 |
| `Languages` | 35 | 25 | 2 |
| `Resources` | 0 | 3 | 3 |
| `Sounds` | 10 | 3 | 2 |
| `Textures` | 1 | 0 | 0 |

These are the final surviving effective 1.6 resource changes versus the old release. Intermediate edits that were later overwritten are intentionally not counted here.

| Status | Relative Runtime Path |
|---|---|
| modified | `Assemblies/CrossPromotion.dll` |
| added | `Assemblies/RimBridgeServer.Annotations.dll` |
| modified | `Assemblies/ZombieLand.dll` |
| modified | `Defs/Zombie_BodyParts.xml` |
| modified | `Defs/Zombie_Damages.xml` |
| modified | `Defs/Zombie_Hediffs.xml` |
| modified | `Defs/Zombie_Jobs.xml` |
| modified | `Defs/Zombie_Kinds.xml` |
| modified | `Defs/Zombie_Letter.xml` |
| modified | `Defs/Zombie_MentalState.xml` |
| modified | `Defs/Zombie_Needs.xml` |
| modified | `Defs/Zombie_Quests.xml` |
| modified | `Defs/Zombie_Race.xml` |
| modified | `Defs/Zombie_Recipes.xml` |
| modified | `Defs/Zombie_Sounds.xml` |
| modified | `Defs/Zombie_Things.xml` |
| modified | `Defs/Zombie_ThinkTree.xml` |
| modified | `Defs/Zombie_Thoughts.xml` |
| modified | `Defs/Zombie_ToolCapacity.xml` |
| modified | `Defs/Zombie_Workgivers.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/DamageDef/Zombie_Damages.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/FactionDef/Zombie_Faction.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/HediffDef/Zombie_Hediffs.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/JobDef/Zombie_Jobs.xml` |
| removed | `Languages/ChineseSimplified/DefInjected/PawnKindDef/Zombie_Kind.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/RecipeDef/Zombie_Recipes.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/ThingDef/Zombie_Race.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/ThingDef/Zombie_Things.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml` |
| modified | `Languages/ChineseSimplified/DefInjected/WorkGiverDef/Zombie_Workgivers.xml` |
| added | `Languages/ChineseSimplified/Keyed/Help.xml` |
| modified | `Languages/ChineseSimplified/Keyed/Text.xml` |
| modified | `Languages/ChineseSimplified/LanguageInfo.xml` |
| modified | `Languages/English/Keyed/Help.xml` |
| modified | `Languages/English/Keyed/Text.xml` |
| added | `Languages/French/DefInjected/DamageDef/Zombie_Damages.xml` |
| added | `Languages/French/DefInjected/FactionDef/Zombie_Faction.xml` |
| added | `Languages/French/DefInjected/HediffDef/Zombie_Hediffs.xml` |
| added | `Languages/French/DefInjected/JobDef/Zombie_Jobs.xml` |
| added | `Languages/French/DefInjected/RecipeDef/Zombie_Recipes.xml` |
| added | `Languages/French/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml` |
| added | `Languages/French/DefInjected/ThingDef/Zombie_Race.xml` |
| added | `Languages/French/DefInjected/ThingDef/Zombie_Things.xml` |
| added | `Languages/French/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml` |
| added | `Languages/French/DefInjected/WorkGiverDef/Zombie_Workgivers.xml` |
| added | `Languages/French/Keyed/Help.xml` |
| modified | `Languages/French/Keyed/Text.xml` |
| modified | `Languages/German/DefInjected/DamageDef/Zombie_Damages.xml` |
| modified | `Languages/German/DefInjected/HediffDef/Zombie_Hediffs.xml` |
| modified | `Languages/German/DefInjected/JobDef/Zombie_Jobs.xml` |
| removed | `Languages/German/DefInjected/PawnKindDef/Zombie_Kind.xml` |
| modified | `Languages/German/DefInjected/RecipeDef/Zombie_Recipes.xml` |
| modified | `Languages/German/DefInjected/ThingDef/Zombie_Race.xml` |
| modified | `Languages/German/DefInjected/ThingDef/Zombie_Things.xml` |
| modified | `Languages/German/DefInjected/WorkGiverDef/Zombie_Workgivers.xml` |
| added | `Languages/German/Keyed/Help.xml` |
| modified | `Languages/German/Keyed/Text.xml` |
| added | `Languages/Russian/DefInjected/DamageDef/Zombie_Damages.xml` |
| added | `Languages/Russian/DefInjected/FactionDef/Zombie_Faction.xml` |
| added | `Languages/Russian/DefInjected/HediffDef/Zombie_Hediffs.xml` |
| added | `Languages/Russian/DefInjected/JobDef/Zombie_Jobs.xml` |
| added | `Languages/Russian/DefInjected/RecipeDef/Zombie_Recipes.xml` |
| added | `Languages/Russian/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml` |
| added | `Languages/Russian/DefInjected/ThingDef/Zombie_Race.xml` |
| added | `Languages/Russian/DefInjected/ThingDef/Zombie_Things.xml` |
| added | `Languages/Russian/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml` |
| added | `Languages/Russian/DefInjected/WorkGiverDef/Zombie_Workgivers.xml` |
| added | `Languages/Russian/Keyed/Help.xml` |
| modified | `Languages/Russian/Keyed/Text.xml` |
| added | `Languages/Turkish/DefInjected/DamageDef/Zombie_Damages.xml` |
| added | `Languages/Turkish/DefInjected/FactionDef/Zombie_Faction.xml` |
| added | `Languages/Turkish/DefInjected/HediffDef/Zombie_Hediffs.xml` |
| added | `Languages/Turkish/DefInjected/JobDef/Zombie_Jobs.xml` |
| added | `Languages/Turkish/DefInjected/RecipeDef/Zombie_Recipes.xml` |
| added | `Languages/Turkish/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml` |
| added | `Languages/Turkish/DefInjected/ThingDef/Zombie_Race.xml` |
| added | `Languages/Turkish/DefInjected/ThingDef/Zombie_Things.xml` |
| added | `Languages/Turkish/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml` |
| added | `Languages/Turkish/DefInjected/WorkGiverDef/Zombie_Workgivers.xml` |
| added | `Languages/Turkish/Keyed/Help.xml` |
| modified | `Languages/Turkish/Keyed/Text.xml` |
| modified | `Resources/Linux/zombieland` |
| removed | `Resources/Linux/zombieland.manifest` |
| modified | `Resources/MacOS/zombieland` |
| removed | `Resources/MacOS/zombieland.manifest` |
| modified | `Resources/Win64/zombieland` |
| removed | `Resources/Win64/zombieland.manifest` |
| modified | `Sounds/smash/smash1.wav` |
| modified | `Sounds/smash/smash2.wav` |
| modified | `Sounds/smash/smash3.wav` |
| removed | `Sounds/smash/smash4.wav` |
| removed | `Sounds/smash/smash5.wav` |
| added | `Sounds/splash/splash1.wav` |
| added | `Sounds/splash/splash2.wav` |
| added | `Sounds/splash/splash3.wav` |
| added | `Sounds/splash/splash4.wav` |
| added | `Sounds/splash/splash5.wav` |
| added | `Sounds/splash/splash6.wav` |
| added | `Sounds/splash/splash7.wav` |
| added | `Sounds/splash/splash8.wav` |
| added | `Sounds/symbiant-connected.wav` |
| added | `Sounds/symbiant-disconnected.wav` |
| added | `Textures/Symbiant/Symbiant.png` |

## Last Writers For Surviving Changes

These commits are responsible for the final net effective 1.6 resource states. Resource-touching commits not listed here were fully superseded, release-only rebuilds with no unique final resource state, or part of the 1.4 preservation/layout move.

| Commit | Surviving Paths | Added | Modified | Removed | Subject |
|---|---:|---:|---:|---:|---|
| `26e0657` | 35 | 23 | 12 | 0 | Add Symbiant translations |
| `54d0698` | 23 | 12 | 11 | 0 | Split XML by RimWorld version |
| `91ebd35` | 8 | 8 | 0 | 0 | Advance zombie blob rendering and feedback |
| `d4c2481` | 7 | 1 | 6 | 0 | Rename zombie blob to symbiant |
| `b6bfea3` | 6 | 0 | 6 | 0 | Move zombie risk controls into vanilla area dialog |
| `44690fa` | 5 | 0 | 3 | 2 | Update smash sounds |
| `729b234` | 4 | 0 | 4 | 0 | Add symbiant damage guard |
| `f46c4c8` | 4 | 2 | 2 | 0 | Add symbiant discovery cues |
| `b22dde0` | 3 | 0 | 0 | 3 | ZombieBlob WIP |
| `24c3b63` | 2 | 0 | 2 | 0 | Refine zombie symbiant relocation and docs |
| `440a2ca` | 2 | 0 | 0 | 2 | Prepare Zombieland 5 release |
| `4d037af` | 2 | 1 | 1 | 0 | Establish RimWorld 1.6 port baseline |
| `294249b` | 1 | 0 | 1 | 0 | Release Zombieland 5.1.0.2 |
| `50970be` | 1 | 0 | 1 | 0 | Fix contamination psychic sensitivity display |
| `b7d4376` | 1 | 0 | 1 | 0 | Add RimHUD contamination bar integration |

## Resource-Touching Commit Catalog

The `Surviving` column counts final net effective 1.6 paths whose last writer/remover is that commit. A zero means the commit touched runtime resources, but those resource changes were later overwritten, reverted, moved into versioned layout, or otherwise do not represent a final net 1.6 resource delta.

| Commit | Touched Paths | Versions | Folders | Statuses | Surviving | Subject |
|---|---:|---|---|---|---:|---|
| `614a9db` | 1 | root:1 | Assemblies:1 | M:1 | 0 | fixes a nre without base |
| `b22dde0` | 12 | root:12 | Assemblies:1, Defs:5, Resources:6 | D:3, M:9 | 3 | ZombieBlob WIP |
| `95b3471` | 3 | root:3 | Assemblies:1, Defs:1, Textures:1 | A:1, M:2 | 0 | fixes deep drill contamination, adds blob image |
| `87db240` | 2 | root:2 | Assemblies:1, Defs:1 | M:2 | 0 | fixes graphics xml error |
| `063d722` | 1 | root:1 | Assemblies:1 | M:1 | 0 | fixes frame building |
| `cdc96d0` | 4 | root:4 | Assemblies:1, Resources:3 | M:4 | 0 | wip |
| `4d037af` | 10 | 1.4:2, 1.6:3, root:5 | Assemblies:8, Defs:2 | A:3, D:1, M:2, R:4 | 2 | Establish RimWorld 1.6 port baseline |
| `6cd30f5` | 1 | root:1 | Defs:1 | M:1 | 0 | Make dark slimer smoke dense |
| `951a872` | 1 | root:1 | Defs:1 | M:1 | 0 | Render tar smoke as black gas |
| `41b0b7c` | 1 | root:1 | Defs:1 | M:1 | 0 | Fix tar smoke rendering and verify zombie ball flight |
| `0abe354` | 1 | root:1 | Defs:1 | M:1 | 0 | Restore zombie blood filth contract |
| `b627006` | 3 | root:3 | Resources:3 | M:3 | 0 | Rebuild Unity asset bundles from generated sources |
| `fd90087` | 3 | root:3 | Resources:3 | M:3 | 0 | Refresh Unity asset bundles |
| `28b6d59` | 3 | root:3 | Resources:3 | M:3 | 0 | Restore thumper shockwave effect |
| `04d2fce` | 1 | root:1 | Defs:1 | M:1 | 0 | Cover 1.6 runtime audit slices |
| `81b2ce1` | 1 | root:1 | Defs:1 | M:1 | 0 | Cover special visual save-load gauntlet |
| `9a92c11` | 12 | root:12 | Defs:10, Languages:2 | M:12 | 0 | Polish English Zombieland wording |
| `ee79206` | 6 | root:6 | Languages:6 | M:6 | 0 | Update snow and sand language text |
| `2610f2b` | 2 | root:2 | Defs:1, Languages:1 | M:2 | 0 | Adopt fork core loot fixes |
| `440a2ca` | 48 | root:48 | Languages:48 | A:35, D:2, M:11 | 2 | Prepare Zombieland 5 release |
| `d5d199f` | 2 | 1.4:1, 1.6:1 | Assemblies:2 | A:2 | 0 | Fix release workflow packaging |
| `54d0698` | 185 | 1.4:93, 1.6:92 | Assemblies:1, Defs:44, Languages:138, Patches:2 | A:184, M:1 | 23 | Split XML by RimWorld version |
| `b319a6d` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Prepare Zombieland 5.0.0.2 release |
| `a9321ce` | 24 | 1.6:12, root:12 | Languages:24 | M:24 | 0 | Add awareness cue settings and zombie resurrection fixes |
| `37852da` | 24 | 1.6:12, root:12 | Languages:24 | M:24 | 0 | Add Anomaly targeting controls |
| `a88d236` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Prepare Zombieland 5.0.1.0 release |
| `44690fa` | 5 | root:5 | Sounds:5 | D:2, M:3 | 5 | Update smash sounds |
| `577fb09` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Make release DLL rebuilds deterministic |
| `223db28` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Prepare Zombieland 5.0.3.0 release |
| `ccb0176` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Prepare Zombieland 5.0.4.0 release |
| `b3ebae4` | 14 | 1.6:7, root:7 | Defs:10, Languages:4 | M:14 | 0 | Implement zombie blob symbiosis |
| `8ec63d6` | 1 | root:1 | Resources:1 | M:1 | 0 | Tune zombie blob rendering |
| `91ebd35` | 15 | 1.6:2, root:13 | Defs:4, Resources:3, Sounds:8 | A:8, M:7 | 8 | Advance zombie blob rendering and feedback |
| `d4c2481` | 29 | 1.6:12, root:17 | Defs:20, Languages:4, Resources:3, Textures:2 | M:27, R:2 | 7 | Rename zombie blob to symbiant |
| `e876a82` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Release 5.0.4.1 |
| `f46c4c8` | 10 | 1.6:4, root:6 | Defs:4, Languages:4, Sounds:2 | A:2, M:8 | 4 | Add symbiant discovery cues |
| `707c84c` | 2 | 1.6:1, root:1 | Languages:2 | M:2 | 0 | Validate symbiant feeding pulses |
| `307f847` | 4 | 1.6:2, root:2 | Languages:4 | M:4 | 0 | Stabilize symbiant safety and settings search |
| `40d9d37` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Release Zombieland 5.0.4.2 |
| `24c3b63` | 16 | 1.6:8, root:8 | Defs:12, Languages:4 | M:16 | 2 | Refine zombie symbiant relocation and docs |
| `729b234` | 12 | 1.6:6, root:6 | Defs:8, Languages:4 | M:12 | 4 | Add symbiant damage guard |
| `095f0dd` | 2 | 1.6:1, root:1 | Languages:2 | M:2 | 0 | Add symbiant relocation coverage and UI checks |
| `26e0657` | 80 | 1.6:40, root:40 | Languages:80 | M:80 | 35 | Add Symbiant translations |
| `9fcbf87` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Release Zombieland 5.1.0.0 |
| `41045af` | 1 | 1.6:1 | Assemblies:1 | M:1 | 0 | Release Zombieland 5.1.0.1 |
| `294249b` | 7 | 1.6:7 | Assemblies:1, Languages:6 | M:7 | 1 | Release Zombieland 5.1.0.2 |
| `50970be` | 1 | 1.6:1 | Defs:1 | M:1 | 1 | Fix contamination psychic sensitivity display |
| `b7d4376` | 1 | 1.6:1 | Defs:1 | M:1 | 1 | Add RimHUD contamination bar integration |
| `b6bfea3` | 6 | 1.6:6 | Languages:6 | M:6 | 6 | Move zombie risk controls into vanilla area dialog |

## Verification

Commands run for this audit:

```text
./scripts/check-versioned-xml.sh
git diff --check -- POST_4_4_5_RESOURCE_AUDIT.md
```

The Git-object verifier produced:

```text
HEAD root runtime absent: 0 differences
HEAD effective 1.4 vs official 4c996bf root: 0 differences
HEAD effective 1.6 vs origin/master effective 1.6: 0 differences
```
