# Common root audit after v4.4.5.0

Baseline tag: `v4.4.5.0`

Baseline commit: `4c996bf13708ebe746419666168250708eaf593d`

Audited current commit: `294249b` (`v5.1.0.2`, `latest`)

Existing uncommitted work at the start of this audit was only under `1.6/` and
`Source/`, so it was treated as unrelated 1.6 work and left untouched.

## Scope

The violation test was current content that still differs from `v4.4.5.0` and
is loaded from the root/common mod folders by both RimWorld 1.4 and 1.6.

Excluded from the violation list:

- `1.6/**`, because that is the intended active port surface.
- `Source/**`, because it builds the 1.6 artifact.
- Repo-only docs, coverage files, scripts, and Unity source/intermediate assets.
- Changes that were made after `v4.4.5.0` but later repaired back to tag content.

Report-only, not reset by this branch:

- `About/About.xml`, `About/Manifest.xml`, and `Directory.Build.props`: these are
  release metadata/version files and were intentionally changed by release
  commits.
- `LoadFolders.xml`: this is the version-routing file that makes the split
  `1.4/` and `1.6/` layout work.

## Load-folder inheritance

RimWorld 1.6 loader behavior was checked against the live `Assembly-CSharp.dll`
with DecompilerServer:

- `Verse.ModContentPack.InitLoadFolders` reverses the folders listed for a
  version. For this repo's `<v1.4><li>/</li><li>1.4</li></v1.4>`, the internal
  search order is `1.4` first, root second.
- `Verse.DirectXmlLoader.XmlAssetsInModFolder` keeps the first XML file seen for
  each relative path, so a file in `1.4/Defs/Foo.xml` shadows
  `Defs/Foo.xml`.
- `Verse.ModContentPack.GetAllFilesForMod` does the same first-path-wins lookup
  for general content files.
- `Verse.ModContentPack.GetAllFilesForModPreserveOrder`, used where order
  matters, still removes duplicate relative paths so the versioned file wins.

That means same-path files in `1.4/` shadow root files, while files missing from
`1.4/` are inherited from root. The fix therefore has to make the effective
`1.4` payload match the official release, not merely copy a few changed files.

## Current violations found

`Defs/` had 16 root XML files still changed from `v4.4.5.0`. The changes were
not just compatibility shims; they added or adjusted gameplay/content surfaces
such as Symbiant defs, 1.6 render fields, toxic resistance fields, jobs, recipes,
letters, sounds, workgivers, and related tuning.

Changed root def files:

- `Defs/Zombie_BodyParts.xml`
- `Defs/Zombie_Damages.xml`
- `Defs/Zombie_Hediffs.xml`
- `Defs/Zombie_Jobs.xml`
- `Defs/Zombie_Kinds.xml`
- `Defs/Zombie_Letter.xml`
- `Defs/Zombie_MentalState.xml`
- `Defs/Zombie_Quests.xml`
- `Defs/Zombie_Race.xml`
- `Defs/Zombie_Recipes.xml`
- `Defs/Zombie_Sounds.xml`
- `Defs/Zombie_Things.xml`
- `Defs/Zombie_ThinkTree.xml`
- `Defs/Zombie_Thoughts.xml`
- `Defs/Zombie_ToolCapacity.xml`
- `Defs/Zombie_Workgivers.xml`

`Languages/` had 58 root language files still changed from `v4.4.5.0`. These
included added help files, added French/Russian/Turkish def-injected files,
Symbiant strings, the keyframe-preview UI string, and other text changes.

Changed root language files:

- `Languages/ChineseSimplified/DefInjected/DamageDef/Zombie_Damages.xml`
- `Languages/ChineseSimplified/DefInjected/HediffDef/Zombie_Hediffs.xml`
- `Languages/ChineseSimplified/DefInjected/JobDef/Zombie_Jobs.xml`
- `Languages/ChineseSimplified/DefInjected/PawnKindDef/Zombie_Kind.xml`
- `Languages/ChineseSimplified/DefInjected/RecipeDef/Zombie_Recipes.xml`
- `Languages/ChineseSimplified/DefInjected/ThingDef/Zombie_Race.xml`
- `Languages/ChineseSimplified/DefInjected/ThingDef/Zombie_Things.xml`
- `Languages/ChineseSimplified/DefInjected/WorkGiverDef/Zombie_Workgivers.xml`
- `Languages/ChineseSimplified/Keyed/Help.xml`
- `Languages/ChineseSimplified/Keyed/Text.xml`
- `Languages/English/Keyed/Help.xml`
- `Languages/English/Keyed/Text.xml`
- `Languages/French/DefInjected/DamageDef/Zombie_Damages.xml`
- `Languages/French/DefInjected/FactionDef/Zombie_Faction.xml`
- `Languages/French/DefInjected/HediffDef/Zombie_Hediffs.xml`
- `Languages/French/DefInjected/JobDef/Zombie_Jobs.xml`
- `Languages/French/DefInjected/RecipeDef/Zombie_Recipes.xml`
- `Languages/French/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml`
- `Languages/French/DefInjected/ThingDef/Zombie_Race.xml`
- `Languages/French/DefInjected/ThingDef/Zombie_Things.xml`
- `Languages/French/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml`
- `Languages/French/DefInjected/WorkGiverDef/Zombie_Workgivers.xml`
- `Languages/French/Keyed/Help.xml`
- `Languages/French/Keyed/Text.xml`
- `Languages/German/DefInjected/DamageDef/Zombie_Damages.xml`
- `Languages/German/DefInjected/HediffDef/Zombie_Hediffs.xml`
- `Languages/German/DefInjected/JobDef/Zombie_Jobs.xml`
- `Languages/German/DefInjected/PawnKindDef/Zombie_Kind.xml`
- `Languages/German/DefInjected/RecipeDef/Zombie_Recipes.xml`
- `Languages/German/DefInjected/ThingDef/Zombie_Race.xml`
- `Languages/German/DefInjected/ThingDef/Zombie_Things.xml`
- `Languages/German/DefInjected/WorkGiverDef/Zombie_Workgivers.xml`
- `Languages/German/Keyed/Help.xml`
- `Languages/German/Keyed/Text.xml`
- `Languages/Russian/DefInjected/DamageDef/Zombie_Damages.xml`
- `Languages/Russian/DefInjected/FactionDef/Zombie_Faction.xml`
- `Languages/Russian/DefInjected/HediffDef/Zombie_Hediffs.xml`
- `Languages/Russian/DefInjected/JobDef/Zombie_Jobs.xml`
- `Languages/Russian/DefInjected/RecipeDef/Zombie_Recipes.xml`
- `Languages/Russian/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml`
- `Languages/Russian/DefInjected/ThingDef/Zombie_Race.xml`
- `Languages/Russian/DefInjected/ThingDef/Zombie_Things.xml`
- `Languages/Russian/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml`
- `Languages/Russian/DefInjected/WorkGiverDef/Zombie_Workgivers.xml`
- `Languages/Russian/Keyed/Help.xml`
- `Languages/Russian/Keyed/Text.xml`
- `Languages/Turkish/DefInjected/DamageDef/Zombie_Damages.xml`
- `Languages/Turkish/DefInjected/FactionDef/Zombie_Faction.xml`
- `Languages/Turkish/DefInjected/HediffDef/Zombie_Hediffs.xml`
- `Languages/Turkish/DefInjected/JobDef/Zombie_Jobs.xml`
- `Languages/Turkish/DefInjected/RecipeDef/Zombie_Recipes.xml`
- `Languages/Turkish/DefInjected/ThingCategoryDef/Zombie_ThingCategories.xml`
- `Languages/Turkish/DefInjected/ThingDef/Zombie_Race.xml`
- `Languages/Turkish/DefInjected/ThingDef/Zombie_Things.xml`
- `Languages/Turkish/DefInjected/ToolCapacityDef/Zombie_ToolCapacity.xml`
- `Languages/Turkish/DefInjected/WorkGiverDef/Zombie_Workgivers.xml`
- `Languages/Turkish/Keyed/Help.xml`
- `Languages/Turkish/Keyed/Text.xml`

Root binary/media payload also differed:

- `Assemblies/CrossPromotion.dll`
- `Assemblies/Newtonsoft.Json.dll`
- `Assemblies/ZombieLand.dll`
- `Libraries/Newtonsoft.Json.dll`
- `Resources/Linux/zombieland`
- `Resources/Linux/zombieland.manifest`
- `Resources/MacOS/zombieland`
- `Resources/MacOS/zombieland.manifest`
- `Resources/Win64/zombieland`
- `Resources/Win64/zombieland.manifest`
- `Sounds/smash/smash1.wav`
- `Sounds/smash/smash2.wav`
- `Sounds/smash/smash3.wav`
- `Sounds/smash/smash4.wav`
- `Sounds/smash/smash5.wav`
- `Sounds/splash/splash1.wav`
- `Sounds/splash/splash2.wav`
- `Sounds/splash/splash3.wav`
- `Sounds/splash/splash4.wav`
- `Sounds/splash/splash5.wav`
- `Sounds/splash/splash6.wav`
- `Sounds/splash/splash7.wav`
- `Sounds/splash/splash8.wav`
- `Sounds/symbiant-connected.wav`
- `Sounds/symbiant-disconnected.wav`
- `Textures/Symbiant/Symbiant.png`

## Fix applied on this branch

Root `Assemblies/`, `Defs/`, `Languages/`, `Libraries/`, `Patches/`,
`Resources/`, `Sounds/`, and `Textures/` were restored from
`4c996bf13708ebe746419666168250708eaf593d` (`v4.4.5.0`).

The same official-release root payload was also staged under `1.4/` for:

- `1.4/Assemblies`
- `1.4/Defs`
- `1.4/Languages`
- `1.4/Libraries`
- `1.4/Patches`
- `1.4/Resources`
- `1.4/Sounds`
- `1.4/Textures`

The `1.4/` staging used the exact Git blobs from the official release commit to
avoid line-ending normalization changing old XML files.

Before restoring those root folders, the current 1.6-only binary/media payload
was copied to versioned 1.6 folders so the 1.6 content keeps its own assets:

- `1.6/Resources/Linux/zombieland`
- `1.6/Resources/MacOS/zombieland`
- `1.6/Resources/Win64/zombieland`
- `1.6/Sounds/smash/smash1.wav`
- `1.6/Sounds/smash/smash2.wav`
- `1.6/Sounds/smash/smash3.wav`
- `1.6/Sounds/splash/splash1.wav`
- `1.6/Sounds/splash/splash2.wav`
- `1.6/Sounds/splash/splash3.wav`
- `1.6/Sounds/splash/splash4.wav`
- `1.6/Sounds/splash/splash5.wav`
- `1.6/Sounds/splash/splash6.wav`
- `1.6/Sounds/splash/splash7.wav`
- `1.6/Sounds/splash/splash8.wav`
- `1.6/Sounds/symbiant-connected.wav`
- `1.6/Sounds/symbiant-disconnected.wav`
- `1.6/Textures/Symbiant/Symbiant.png`

## Verification

Two static checks were run against the staged index:

- Root payload check: staged root `Assemblies`, `Defs`, `Languages`,
  `Libraries`, `Patches`, `Resources`, `Sounds`, and `Textures` have no diff
  from `4c996bf13708ebe746419666168250708eaf593d`.
- Effective 1.4 inheritance check: applying RimWorld's first-path-wins
  `1.4`-then-root lookup to those same content folders produces the exact same
  relative file set and blob IDs as the official release root payload.
