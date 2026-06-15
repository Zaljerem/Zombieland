# Zombie Symbiant Implementation Plan

## Goal

Make the zombie symbiant an indoor base infestation with a colonist symbiosis twist. It should bring pressure into rooms the player normally treats as safe, but the pressure must be legible and non-cheap: the symbiant disrupts important rooms and asks for ugly management decisions, while the linked colonist gains visible benefits as long as enough goo remains alive.

The symbiant is not a second spitter and not a normal combat target. The interesting decision is how much visible goo the player tolerates to keep the host powerful and protected, versus how much reserve they build before safely severing the relationship.

## Current Design Commitments

- One active symbiant per map.
- A symbiant has one authoritative linked host pawn. The link state lives on the symbiant, not in the host hediff.
- `SymbiantSymbiosis` is a display/sync hediff only. If DLC or another mod removes it while the symbiant link still exists, the symbiant should recreate it. Because RimWorld removes zero-severity hediffs, linked hosts keep a tiny nonzero display severity even when the current benefit factor is zero.
- Natural spawn still chooses a central, trafficked, colony-used room. The host is chosen independently from eligible colonists so players cannot trivially steer the spawn by moving one pawn to a disposable room.
- Natural spawn requires an eligible host. Hostless symbiants are allowed only for debug/test spawning or explicit fallback tools, and hostless slime has its own lesser cleanup behavior rather than the full symbiant loop.
- Safe severance requires symbiosis maturity. The symbiant must have reached a meaningful visible size or medium benefit at least once before reserve can enable surgery.
- Full host benefit and safe severance reserve are separate concepts. Large colonies may require more integrated goo for maximum power, but safe removal must not become a 100-feed janitor marathon.
- Only colony-integrated visible goo counts fully for host benefits. A quarantined remote shed can contain the problem, but it should not provide the same power as goo disrupting active colony rooms.
- Ordinary feeding no longer removes the final cell and no longer serves as complete removal.
- Feeding creates or increases a decoupling reserve, can trigger a recession pulse, pauses growth, and can cancel the next breach, but it cannot shrink below the safe visible minimum.
- The only safe removal path is host surgery: `SeverSymbiantSymbiosis`.
- Unsafe symbiant destruction harms or kills the linked host unless enough effective reserve can absorb the trauma.
- If the linked host dies, the symbiant collapses messily. This is an emergency amputation path, not a clean exploit or reward source.

## Accepted Defaults

- `symbiantEnabled = true`
- `symbiantSpawnCooldownDays = 12`
- `symbiantExpansionIntervalHours = 16`
- `symbiantPostFeedPauseHours = 16`
- `symbiantMaxCells = 400`
- `symbiantFullBenefitRoomCoverage = 0.20`
- `symbiantSeveranceMaturityCoverage = 0.50`
- `symbiantSeveranceMaturityMinCells = 10`
- `symbiantSeveranceMaturityMaxCells = 80`
- `symbiantSeveranceReserveCoverage = 0.25`
- `symbiantSeveranceReserveMin = 12`
- `symbiantSeveranceReserveMax = 60`
- `symbiantDecouplingFeedPulsesPerDay = 2`
- `symbiantMaxSkillBonus = 6`
- `symbiantZombieIgnoreMinBenefit = 0.50`
- `symbiantPathCost = 220`
- `symbiantCanBreakConstructedWalls = true`
- `symbiantCoagulantPotency = Normal`
- Technical render/buffer ceiling is `ZombieSymbiant.MAX_METABALLS = 4000`; the gameplay default cap remains 400. Larger values are for stress testing or later balancing, not the v1 default.
- Visible main settings stay intentionally compact: enable symbiant, days between infestations, hours between growth pulses, max cells, and coagulant potency. The other tuning values remain persisted/defaulted internal balancing controls until they are worth exposing again.

## Runtime Category And Combat Isolation

- V1 keeps `ZombieSymbiant : Pawn` as an implementation shell to avoid a broad save/XML/job/bridge migration while rendering, growth, feeding, and room disruption are still settling.
- Semantically, the symbiant is not a creature. It is room-scale goo with a custom renderer, cell set, feed interaction, host link, path cost, and room disruption.
- Symbiant maintenance is driven by the map-level Zombieland tick manager, with a same-game-tick guard because the legacy symbiant job driver can still call the same method. Do not rely on ordinary pawn-list or zombie-list membership for host sync, growth, or repair.
- The pawn shell must therefore opt out of ordinary pawn and combat systems:
  - `PawnKindDef_ZombieSymbiant` is not a fighter and has zero combat power,
  - the active symbiant unregisters itself from `map.mapPawns` after spawn/load,
  - active symbiant discovery uses the map-keyed symbiant cache and `listerThings`, not `map.mapPawns`,
  - attack target cache registration skips symbiants,
  - ordinary target selection, story danger, fleeing, predation, auto-attack, and explicit attack jobs reject symbiants,
  - the symbiant itself may run only the symbiant job plus inert wait/goto fallback jobs and must not start melee/static attack jobs.
- This is an intentional interim compromise. The cleaner long-term architecture is a custom `ThingWithComps` or `Thing`-based symbiant entity with only the specific systems we need. Do that as a separate migration after v1 behavior and rendering are stable, not mixed into the current combat/rendering stabilization slice.
- Compatibility expectation: hiding from `map.mapPawns` reduces interaction with vanilla and mod code that scans ordinary map pawns. It does not make the symbiant invisible to every possible mod because it is still a spawned `Thing` and still inherits from `Pawn` until the future category migration.

## Spawn And Host Link

- Spawn only if symbiant infestation is enabled and no `ZombieSymbiant` already exists on the map.
- Use a separate cooldown from the zombie spitter.
- Spawn in an enclosed, non-huge, non-fogged, proper room that is home-area or colony-used.
- Score spawn rooms by recent movement pheromone timestamps first, then by valuable room objects such as beds, worktables, storage, power, and kitchen-like utility buildings.
- Initial state is one occupied cell and a right-side green symbiant letter describing the wet seep, the room role, and the linked colonist. The letter should use two look targets when possible: one for the seep and one for the host.
- There is no first-time modal dialog. Onboarding text lives in the symbiant letter, symbiant inspect text, host hediff, and the compact symbiant settings help.
- Connection and disconnection use dedicated subtle symbiant sounds. The connection sound is attached to the green arrival letter when letters are enabled, and played directly only when the letter is suppressed.
- Bridge validation uses `zombieland/symbiant_discovery_letter_contract` to spawn a temporary symbiant through `ZombieSymbiant.Spawn`, capture the generated green letter, verify the connection/disconnection defs, count look targets, and clean up the temporary symbiant without host trauma.
- Bridge validation uses `zombieland/symbiant_natural_spawn_contract` to inspect the natural spawn plan, prove active-symbiant/no-host blockers, and optionally create a reversible bedroom fixture to exercise `TrySpawnInBestRoom` with cleanup on a current-loadout map.
- Choose a random eligible free colonist after spawn:
  - alive, spawned, humanlike, flesh,
  - player faction colonist,
  - adult or at least non-child by RimWorld age category,
  - regular free colonist, not prisoner, slave, guest, temporary joiner, quest lodger, shuttle occupant, caravan pawn, or otherwise unavailable for ordinary colony surgery,
  - not a Zombieland pawn,
  - not a Save Our Ship hologram or optional-mod non-flesh pawn,
  - not already linked to another symbiant,
  - not in a late/active zombie infection state.
- If no eligible host exists, natural symbiant spawning is skipped and the cooldown should not be consumed.
- Debug/test tools may spawn hostless room slime. Hostless slime has no symbiant benefits, no safe severance surgery, no host trauma, and can be fully removed by feeding/cleanup because there is no symbiotic root.

## Spread Rules

- Symbiant expansion is controlled by the dedicated symbiant expansion setting.
- One expansion pulse does exactly one action:
  - add one valid room or door cell,
  - destroy one selected constructed wall piece and occupy that cell,
  - or do nothing if capped, paused, safely minimized, or blocked by non-breakable terrain.
- Spread prefers nearby cells with recent traffic signal and colony-room value.
- Closed doors are valid spread cells and remain door objects; the symbiant can visually exist under them.
- Natural rock and non-constructed blockers are not breached.
- If the symbiant is caged by player walls and wants to expand, it may destroy one constructed wall that opens toward a larger or more used room.
- Feeding can set `cancelNextBreach`, so a fed caged symbiant does not immediately answer the feed with a wall break.
- At the configured max cells, expansion stops but the symbiant remains active until severed or collapsed. Existing larger symbiants are not deleted if the cap is lowered; they simply cannot expand further until below the cap.

## Benefit Formula And Integrated Goo

- Benefits scale from visible symbiant cells only. Decoupling reserve never fakes visible size.
- Benefits use integrated visible cells, not raw symbiant cells:
  - full weight for cells in home-area, roofed, proper colony rooms with recent traffic or valuable colony use,
  - reduced weight for remote, unused, or intentionally abandoned containment rooms,
  - no extra credit for unroofed outdoor storage or dead-space warehouses.
- The purpose is to allow containment as a valid control tactic without turning a remote goo warehouse into a permanent super-colonist farm.
- `fullBenefitCells = clamp(ceil(eligibleColonyRoomCells * symbiantFullBenefitRoomCoverage), 20, symbiantMaxCells)`.
- `integratedVisibleCells = weighted count of visible symbiant cells by colony integration`.
- `benefitFactor = clamp01(integratedVisibleCells / fullBenefitCells)`.
- The linked host gains:
  - up to `symbiantMaxSkillBonus` effective skill levels,
  - reduced pain,
  - stabilized capacity toward normal function,
  - dampened mental breaks at medium or better benefit,
  - averaged needs using the existing infected-colonist mechanism,
  - zombie targeting protection only at medium or better benefit.
- Zombie targeting protection should scale with benefit:
  - below `symbiantZombieIgnoreMinBenefit`: no hard exemption; zombies may still prefer other targets if ordinary scoring allows it,
  - at or above `symbiantZombieIgnoreMinBenefit`: zombies deprioritize or ignore the host unless the host attacks/provokes them,
  - hard non-targeting must not activate from a one-cell symbiant.
- Keeping the symbiant tiny gives weak benefits. Keeping it large gives the host more power but creates more room disruption.

## Symbiosis Maturity

- Track `peakVisibleCells`, `peakIntegratedVisibleCells`, and `peakBenefitFactor`.
- `severanceMaturityCells = clamp(ceil(fullBenefitCells * symbiantSeveranceMaturityCoverage), symbiantSeveranceMaturityMinCells, symbiantSeveranceMaturityMaxCells)`.
- `hasMaturedForSeverance` becomes true once:
  - `peakIntegratedVisibleCells >= severanceMaturityCells`, or
  - `peakBenefitFactor >= symbiantZombieIgnoreMinBenefit`.
- Maturity is persisted. Once the symbiant has integrated, later shrinkage does not undo maturity.
- Decoupling reserve can be stored before maturity, but it is not fully effective for safe severance until maturity is reached.
- This is the anti-bunker rule: a prepared player cannot keep a brand-new one-cell symbiant permanently fed and then safely cut it out without ever letting it become a real colony problem.

## Feeding And Reserve

- Selecting a symbiant exposes a feed-order gizmo.
- Colonists feed it with one non-Zombieland humanlike corpse or one `SymbiantCoagulantPack`.
- One feed item means one decoupling pulse, not one removed cell.
- A humanlike corpse pulse starts at 1 reserve unit.
- A fresh humanlike corpse is at least 2 reserve units.
- Large/high-body-size humanlike corpses are 2-3 reserve units.
- A coagulant pack is 3 reserve units by default. Cheap coagulant is 2; expensive coagulant is 5.
- Large symbiant state increases pulse strength: +1 at 100+ cells, +2 at 200+ cells, +3 at 300+ cells.
- Feeding consumes the input, increases decoupling reserve, plays wet/squish feedback, and pauses expansion for `symbiantPostFeedPauseHours`.
- Feeding is capped by `symbiantDecouplingFeedPulsesPerDay` to prevent stockpiling enough material to instantly neutralize a newly spawned symbiant.
- Feeding while caged cancels the next breach opportunity.
- Feeding can be done before maturity, but effective reserve for shrink floors, linked damage, unsafe destruction, and surgery is limited by historical integration:
  - `reserveMaturityFactor = clamp01(peakIntegratedVisibleCells / severanceMaturityCells)`,
  - `effectiveDecouplingReserve = min(decouplingReserve, severanceReserveRequired * reserveMaturityFactor)`.
- Feeding may shrink newest cells first, but only down to:
  - `safeVisibleMinimum = max(1, ceil(severanceMaturityCells * (1 - effectiveDecouplingReserve / severanceReserveRequired)))`.
- `severanceReserveRequired = clamp(ceil(fullBenefitCells * symbiantSeveranceReserveCoverage), symbiantSeveranceReserveMin, symbiantSeveranceReserveMax)`.
- The reserve cap is `severanceReserveRequired`. This keeps removal costly without scaling into an absurd number of corpse/coagulant jobs for large colonies.
- Full reserve, maturity, and a symbiant of 3 cells or less enable safe severance surgery.

## Safe Severance

- Add surgery recipe `SeverSymbiantSymbiosis`.
- Surgery is available only on the linked host when:
  - linked symbiant exists,
  - symbiosis has matured,
  - decoupling reserve is full,
  - visible symbiant size is at most 3 cells,
  - doctor can reach the host and normal surgery requirements are met.
- Surgery consumes one `SymbiantCoagulantPack` plus medicine through recipe ingredients.
- On success:
  - remove `SymbiantSymbiosis`,
  - clear authoritative symbiant-host link,
  - remove/collapse the symbiant without host trauma.
- On failure:
  - consume part of the reserve,
  - injure the host using the normal surgery-failure path,
  - keep the link active if the symbiant still exists.

## Unsafe Damage And Collapse

- Ordinary attacks are not the intended solution.
- Direct symbiant damage is converted into reserve loss when effective reserve is available.
- If damage exceeds effective reserve, the leftover trauma is reflected onto the linked host.
- If the symbiant is destroyed without safe severance and effective reserve cannot absorb the trauma, the host is killed or heavily injured. V1 should prefer clear high-stakes consequences over silent bypass.
- If the host dies for any reason, the linked symbiant collapses without recursively killing the already-dead host.
- Host death is allowed as a brutal emergency solution, but it must not feel like clever free removal:
  - no coagulant, resource, or reward drops,
  - any remaining visible goo collapses into inert residue or a temporary room penalty,
  - nearby pawns are not randomly harmed in v1,
  - the event messaging should make it clear that this was an unsafe severance outcome.

## Non-Lethal Room Disruption

- Room disruption remains non-lethal: no disease, infection, random item destruction, pawn mind control, or random filth spread in v1.
- Symbiant-occupied rooms cannot score as impressive while goo remains in the room.
- Symbiant cells reduce room beauty, so bedrooms, dining rooms, and recreation rooms matter by location.
- Pawns standing on symbiant cells have reduced tend speed and general work speed. This covers hospitals, kitchens, and work rooms through existing stat calls.
- Storage and hauling disruption comes from path cost: items on symbiant cells or routes crossing symbiant cells remain usable but awkward to reach.

## Coagulant

- Add `SymbiantCoagulantPack` as a craftable resource.
- Add a crafting recipe using zombie extract plus chemfuel.
- The setting is named `symbiantCoagulantPotency`, not recipe cost.
- Cheap/Normal/Expensive means weak/normal/strong coagulant pulse strength. XML recipe cost remains normal for v1.
- Save migration reads the old `symbiantCoagulantRecipeCost` key and writes `symbiantCoagulantPotency` without changing the player's selected tier.
- Live recipe ingredient mutation by tier is explicitly out of scope until validated.

## Rendering

- Source-side symbiant capacity remains `ZombieSymbiant.MAX_METABALLS = 4000`; gameplay defaults still cap visible growth at `symbiantMaxCells = 400`.
- V1 runtime rendering uses a CPU-generated transparent metaball mask texture drawn on a bounds mesh. The preferred material is the bundled non-compute `Custom/ZombieSymbiant` shader; if that shader is missing, the renderer falls back to RimWorld's built-in transparent shader and bridge state must expose the fallback.
- The runtime renderer must not depend on Unity compute shaders, `StructuredBuffer`, or the asset-bundle `Metaballs` shader. Current macOS RimWorld starts with `-disable-compute-shaders`, and the prior compute-buffer path produced magenta/green full-rectangle artifacts from shader/buffer interpretation. `Metaballs` remains a dormant legacy bundle asset only.
- Recompute the texture only when the symbiant cell set changes, not on game ticks or rendered frames. The texture resolution is dynamic and based on render bounds, not cell count: long 400-cell strips can require a wider texture than a 4000-cell circle. Use bucketed/capped dimensions so texture allocation changes only when the symbiant crosses meaningful size thresholds.
- The CPU texture should be rectangular when the symbiant bounds are rectangular. Do not stretch a fixed square texture over long thin symbiants; the mesh, render-world dimensions, and texture dimensions should track the current padded bounds.
- Texture fill must use local spatial sampling, not an all-metaballs-for-every-pixel loop. Higher adaptive resolutions are only acceptable because each pixel samples nearby symbiant cells from a precomputed cell-radius map.
- Bulk cell changes, including bridge stress setup and future fixture setup, must batch coordinate changes and rebuild the render mesh/texture once. Never add hundreds or thousands of cells through a loop that rebuilds the CPU texture for every cell.
- Runtime motion must be render-parameter-only. Do not mutate gameplay cells, metaball coordinates, or texture pixels for jiggle or opacity animation; per-cell in/out tweening is deferred until it can be shader- or mesh-parameter-driven without hurting max dev-speed TPS.
- The `Custom/ZombieSymbiant` shader samples the CPU mask and applies a stable, shader-only standing-wave interference field in world X/Z to vary alpha. C# passes `_SymbiantOpacityMin`, `_SymbiantOpacityMax`, `_SymbiantNoiseScale`, `_SymbiantFlowSpeed`, `_SymbiantWaveShadeStrength`, `_SymbiantEdgeContrast`, and `_SymbiantNoiseTime`; the texture remains unchanged while local wave amplitudes rise and fall.
- Opacity modulation is multiplicative over the CPU mask, not a new opaque decal. Current visual-tuning trial values are min 0.42 and max 0.76, keeping stronger low/high contrast while lifting the low-opacity floor.
- The current shader trial intentionally replaces the clustered random noise with x, z, diagonal, and cross-product standing modes at close frequencies plus a small non-translating sine-domain warp. The goal is ocean-like interference where local minima/maxima flip, cancel, and build without a readable travel direction.
- Wave-contour shading is shader-only. A wider, darker green color band follows the midpoint of the opacity wave with soft edges so thicker/thinner regions read as shaded goo rather than only alpha noise.
- Edge contrast is a soft dark-green/near-black shader band based on the CPU mask edge. It must mostly live in the outer falloff: broad body-facing edge bands desaturate the goo without making the visible border read stronger. The current trial uses a narrow outer core plus feather and a small rim-local alpha lift so the area between the green body and the outer fade has outline-like impact without becoming a hard RimWorld-style black cutout.
- `_SymbiantNoiseTime` is normal-speed seconds (`GenTicks.TicksGame / 60`), and `_SymbiantFlowSpeed` is a 0.45 wave-phase speed. At 1x gameplay the interference pattern changes with game ticks through standing-mode amplitude changes, not field translation.
- Bridge render state exposes `wavePhaseSpeed` and `noiseTimeSeconds` so live tests can confirm the loaded DLL is using the interference-wave parameterization.
- Alpha and edge thresholds should preserve the original translucent transfer look from the first working CPU-rendered symbiant. Do not fix hole artifacts by filling the whole field into an opaque decal; real holes should be addressed through draw order, altitude, geometry, or cell-field shape.
- Per-cell metaball radius tuning currently uses a slightly fatter join profile: radius factor 0.45, minimum radius 0.55, max radius 0.95. This is meant to close small gaps between specks without making isolated outliers much larger.
- The symbiant mesh is drawn below walls, doors, buildings, items, pawns, and overlays. RimWorld 1.6 places `Item` above all building layers, so v1 chooses correct wall/building ordering over half-submerged floor items.
- Pawn footstep feedback is handled as a movement-entry side effect, not a tick effect. Patch `Pawn_FilthTracker.Notify_EnteredNewCell`, the RimWorld notification called by `Pawn_PathFollower.TryEnterNextPathCell` immediately after `pawn.Position = nextCell`. Keep cheap exits before symbiant lookup, reuse `ZombieSymbiant.IsSymbiantCellForSlowedPawn`, and play `SymbiantSplash` once for each real path-cell entry into symbiant goo. Keep this patch independent from the existing contamination patch on the same method because contamination can be disabled.
- Doorway symbiant slowdown needs a separate pathing correction. RimWorld spends `nextCellCostLeft` before starting the door-open wait, so a closed door on a symbiant cell can otherwise make a pawn approach slowly, wait for the door, then snap into the doorway. Patch `Pawn_PathFollower.TryEnterNextPathCell` for symbiant doorway wait attempts, refill `nextCellCostLeft` after the wait is started, and extend `Building_Door.ticksUntilClose` for the residual symbiant cost. Without the close-timer extension, a manual door's default 110-tick close delay can expire before `symbiantPathCost` is paid, causing repeated open/reject cycles. Keep this tied to `symbiantPathCost` and `DebugDisablePathCost`.
- Symbiant path-cost slowdown applies to all spawned, non-flying moving pawns except Zombieland pawns (`Zombie`, `ZombieSymbiant`, `ZombieSpitter`), not only colonists. This keeps colony animals, visitors, traders, prisoners, and raiders visually consistent when they cross the goo. Host linking, symbiosis benefits, and room/task stat disruptions remain on the narrower colonist/humanlike predicates.
- The linked symbiant host is adapted to its symbiant and is exempt from symbiant-cell negatives: movement slowdown, doorway slowdown, splash footstep feedback, and room/task stat penalties. Use a current-map linked-host check after cheap pawn filters so hot pathing does not scan all active symbiants for unrelated pawns.
- Suppress RimWorld 1.6 pawn render-tree body drawing for `ZombieSymbiant`; the CPU symbiant mesh is the only symbiant body render. This removes the extra body-shaped artifact while preserving selection and UI overlays.
- When cell bounds change, render bounds, mesh dimensions, texture dimensions, and the cell-radius map must be rebuilt before the CPU mask update. The current runtime should not keep stale normalized metaball positions at all.
- RimWorld's dynamic draw cull must see the symbiant's full visual footprint. `ZombieSymbiant.DrawSize` is therefore derived from relative symbiant bounds plus render padding, and bridge state exposes `occupiedDrawRect` so tests can verify camera/view overlap even when the pawn anchor cell is offscreen.
- Keep the dormant Unity asset-bundle `Metaballs` shader source aligned with the old C# layout only as a legacy reference if it is revived later. It is not the runtime renderer.
- Rebuild asset bundles through `scripts/build-assetbundles.sh`. Use `--current` for fast local macOS visual iteration, `--os Win64|Linux|MacOS` for one explicit target, and `--full` before any release or cross-platform validation. The script writes directly to `Resources/{OS}/zombieland`.

## Performance Guardrails

- A 400-cell symbiant must be lightweight at max dev speed. The target is parity with the same-session no-symbiant baseline; anything substantially below that baseline requires profiling before adding more mechanics.
- The bridge can temporarily override the effective max-cell cap up to the technical ceiling for stress tests. This must not mutate the player's `symbiantMaxCells` setting, whose accepted default is still 400.
- 4000-cell tests are diagnostic worst-case rendering/load tests, not a gameplay target. Run them both framed around the symbiant and zoomed out far enough to stress the visible draw path.
- Use bridge-only perf profiles before guessing at bottlenecks:
  - `inert`: symbiant pawn exists, but rendering, symbiant tick, path cost, cell stat effects, hediff sync, and symbiosis benefits are disabled.
  - `renderOnly`: only symbiant rendering is active.
  - `pathOnly`: only symbiant path cost is active.
  - `symbiosisOnly`: symbiant tick, host hediff sync, and symbiosis benefits are active, while rendering/path/cell stat effects are disabled.
  - `noCellStats`, `noPath`, `noRender`, and `noTick`: interaction splits for the default profile.
- Hot Harmony hooks must reject unrelated calls before touching symbiant state. In particular, the `StatExtension.GetStatValue` postfix must first check whether the stat is one of the symbiant-disrupted stats, then check pawn/symbiant membership. Calling `IsSymbiantCell` for every pawn stat query is not acceptable.
- Assume high-pawn-count maps as the normal case: zombies, enemies, animals, guests, caravans, world pawns, and pawns on other active maps can multiply any pawn-side hook cost.
- Separate cheap predicates by interaction:
  - host-only symbiosis checks reject anything that can never be the linked host before symbiant lookup,
  - cell-effect checks reject unspawned pawns, pawns without a current map, Zombieland pawns, non-player pawns, non-player-controlled pawns, and pawns not on the queried map before symbiant lookup.
- Be conservative with limbo pawns: capsules, mothballed pawns, world pawns, generated pawns during world/faction setup, and badly initialized modded pawns are unaffected unless they are clearly spawned on a real map and in a supported interaction category.
- Do not call logging global helpers such as `Faction.OfPlayer` from early pawn predicates. Use null-safe pawn-owned state such as `pawn.Faction?.IsPlayer == true` only after null/destroyed/dead/spawned/map checks.
- Never scan `map.mapPawns.AllPawns` from hot paths. Use cached RimWorld groups such as free colonists when selecting hosts, and maintain a map-keyed active-symbiant cache for runtime interactions.
- Never use `map.mapPawns` as the primary symbiant discovery mechanism. The symbiant is deliberately removed from ordinary pawn lists, so bridge tools, work givers, and runtime symbiant lookups must use `ZombieSymbiant.ActiveSymbiant(map)` or `listerThings`.
- The active-symbiant cache must key by the actual `Map` object, not only `map.uniqueID`. RimWorld save/load can reuse `uniqueID = 0`; a numeric-keyed static cache can otherwise report a stale symbiant on a freshly loaded no-symbiant map.
- `IsSymbiantCell` and `ContainsCell` must remain pure, allocation-free membership checks. They must not normalize symbiant data, rebuild render state, scan rooms, or touch symbiosis metrics.
- Symbiant cell membership should reject by map and symbiant bounds before hash lookup. Room-stat disruption should reject by room/symbiant bounds overlap before walking symbiant cells.
- Current verified measurement on 2026-06-12, MacBook Air M2 with low power mode off, dev map, 400-cell stress symbiant visible at Ultrafast:
  - no-symbiant baseline: 2189 ticks / 5108 ms, about 429 TPS,
  - pre-fix default 400-cell symbiant: 625 ticks / 5135 ms, about 122 TPS,
  - fixed default 400-cell symbiant: 2258 ticks / 5128 ms, about 440 TPS,
  - after high-pawn-count filters: first sample was polluted by an unrelated attack/death event, clean follow-up sample was 4515 ticks / 5050 ms, about 894 TPS,
  - logs clean at warning-or-higher after the fixed retest.
- Current 4000-cell stress measurement on 2026-06-12, same MacBook Air M2 power state, fresh dev map, bridge-only `maxCellsOverride = 4000`, saved `symbiantMaxCells` still 400:
  - no-symbiant baseline: 4515 ticks / 5066 ms, about 891 TPS,
  - 4000-cell setup: 3999 cells added, final 4000 cells, radial circle filled all requested cells without square fallback, one-time setup/render rebuild took about 7.7 seconds,
  - closer visible symbiant sample: 4530 ticks / 5075 ms, about 893 TPS,
  - furthest vanilla zoom sample: 4515 ticks / 5056 ms, about 893 TPS,
  - logs clean at warning-or-higher after the 4000-cell retest.
- Current high-map-pawn stress measurement on 2026-06-13 local time, MacBook Air M2 with low power mode off, save `ZL_SymbiantPawnStress_00`, furthest vanilla zoom:
  - fixture: 160 free colonists, 600 player animals using `Chicken`, and a 10000-point `RaidEnemy` with `ImmediateAttack` plus `EdgeWalkIn`; setup spawned 157 colonists, 599 animals, and 120 hostile raiders, then saved paused at tick 13/14,
  - setup used a real RimWorld raid incident, not manually spawned fake hostiles; raid faction was `TribeRoughNeanderthal`, spawn center was edge cell `(0,193)`,
  - the first repeat exposed and fixed a stale active-symbiant cache bug where loading the no-symbiant save after a symbiant map could still return the previous symbiant from the static cache,
  - preliminary 3-second pair after fixture load: no symbiant 273 ticks / 3128 ms, about 87 TPS; 4000-cell symbiant 284 ticks / 3149 ms, about 90 TPS,
  - final 5-second pair after the cache fix: no symbiant 472 ticks / 5116 ms, about 92 TPS; 4000-cell symbiant 475 ticks / 5116 ms, about 93 TPS,
  - both timed windows ended with no downed/dead player pawns and no downed/dead hostiles,
  - warning-or-higher logs contained only repeated vanilla/Ideology-style load noise: `A hidden ritual precept was missing, adding: Funeral (no corpse)`.
- Current inside-symbiant high-map-pawn stress measurement on 2026-06-13 local time, MacBook Air M2 with low power mode off, save `ZL_SymbiantPawnStress_InsideSymbiant_00`, wide visible zoom:
  - the older `ZL_BlobPawnStress_InsideBlob_00` fixture is not compatible with the current minimal loadout because it references missing DLC/Orbit defs and fails during world-layer load before symbiant code can run; use the fresh `ZL_SymbiantPawnStress_InsideSymbiant_00` fixture instead,
  - fixture was created from a fresh dev game with bridge-only `zombieland/symbiant_pawn_stress_state` parameter `colonistsInsideSymbiant = true`, symbiant center `(125,125)`, and cluster radius 16; setup relocated 3 existing colonists, spawned 157 colonists, spawned 599 chickens, triggered a real 10000-point `RaidEnemy` with `ImmediateAttack` plus `EdgeWalkIn`, and saved paused at tick 26,
  - raid faction was `OutlanderRough`, spawn center was edge cell `(249,140)`, and setup ended with 923 spawned pawns: 160 player colonists, 600 player animals, and 106 hostile humanlikes,
  - before the timed symbiant sample, the stress readback reported all 160 player colonists on symbiant cells, with 1 player animal and 0 hostile humanlikes on symbiant cells,
  - 4000-cell setup used bridge-only `maxCellsOverride = 4000`, added 3999 cells for a final 4000-cell radial circle, took about 1.9 seconds, and kept the saved gameplay setting `symbiantMaxCells = 400`,
  - no-symbiant baseline: 384 ticks / 3107 ms, about 124 TPS; a longer same-fixture baseline was 623 ticks / 5141 ms, about 121 TPS, so this pawn-load fixture itself is already heavy,
  - 4000-cell default symbiant with all colonists inside: 442 ticks / 3116 ms, about 142 TPS; a longer same-window default sample was 688 ticks / 5100 ms, about 135 TPS,
  - reset-per-profile samples: `noRender` 429 ticks / 3101 ms, about 138 TPS; `noTick` 469 ticks / 3083 ms, about 152 TPS; `noPath` 305 ticks / 3099 ms, about 98 TPS; `noCellStats` 337 ticks / 3150 ms, about 107 TPS,
  - interpretation: after the host hediff/lifecycle fix, the old 164 TPS inside-symbiant regression no longer reproduces. Rendering is not the bottleneck in this fixture. `noTick` shows symbiant ticking has a small measurable cost. `noPath` and `noCellStats` change pawn movement/work behavior enough that they are not reliable raw-cost proxies in an active raid fixture,
  - follow-up source change: symbiant cell checks now collapse duplicate active-symbiant lookups for colonists, test actual cell membership before linked-host exemption, and skip path-cost symbiant lookups when the existing movement cost is already greater than or equal to `symbiantPathCost`.
  - post-change restart validation: loading the fixture, creating a 4000-cell symbiant, and running the same 3-second Ultrafast window produced 419 ticks / 3133 ms, about 134 TPS, with all 160 colonists still on symbiant cells, no downed/dead player pawns or hostiles, and no warning-or-higher logs except the known GABS bridge-config warning.
- Current adaptive-texture render measurement on 2026-06-13 local time, same inside-symbiant fixture:
  - 4000-cell radial stress symbiant reports render world size 76x75, draw/cull size 77x77, `occupiedDrawRect` x84..160 and z82..158, and adaptive texture size 512x512,
	- one-time 4000-cell stress setup/render rebuild was about 1.85 seconds after switching texture fill to nearby-cell spatial sampling; the earlier all-metaballs-per-pixel CPU texture path took about 7.7 seconds,
	- warning-or-higher logs after the adaptive-texture rebuild contained only the repeated hidden ritual precept warning.
- Current GPU opacity shader validation on 2026-06-13 local time:
  - `scripts/build-assetbundles.sh --current` rebuilt and validated the MacOS bundle with `ZombieSymbiant=Custom/ZombieSymbiant`,
  - a second `dotnet build Source/ZombieLand.csproj -v:minimal` deployed the rebuilt MacOS bundle to the copied RimWorld mod; repo and active-mod bundle hashes matched,
  - live dev-map stress symbiant at 26 cells reported `renderShader = Custom/ZombieSymbiant`, `renderUsesSymbiantShader = true`, opacity min 0.58, opacity max 1.00, noise scale 0.36, and noise drift 0.16,
  - a 5-second Ultrafast sanity window kept the custom shader active and produced no warning-or-higher logs,
  - focused screenshot `zl_symbiant_shader_noise_smoke__cell_rect.png` showed clustered translucent green goo with no pink shader failure and no full-rectangle artifact.
- Current 6x opacity-frequency test on 2026-06-13 local time:
  - `symbiant rendering minimal` save loads centered and visual-ready with the extended zoom range from RimBridgeServer,
  - live bridge state reports 60 symbiant cells, draw size 11x11, `renderShader = Custom/ZombieSymbiant`, `renderUsesSymbiantShader = true`, opacity min 0.15, opacity max 1.00, noise scale 1.44, and noise drift 0.05,
  - focused screenshot `zl_symbiant_noise_6x_symbiant_rendering_minimal__cell_rect.png` shows the opacity variation clearly; this is useful as an extreme-frequency reference, not necessarily the final frequency,
  - warning-or-higher logs contain only `[RimBridge] GABS environment differs from bridge config at /Users/ap/.gabs/rimworld/bridge.json; using bridge config.`, with no shader or symbiant errors.
- Current 4x opacity-frequency plus 4x temporal-response test on 2026-06-13 local time:
  - live bridge state in `symbiant rendering minimal` confirmed opacity min 0.28, opacity max 0.78, noise scale 0.96, noise drift 0.05, and noise tick scale 0.0016,
  - static screenshot `zl_symbiant_noise_4x_time_4x_symbiant_rendering_minimal__cell_rect.png` shows a calmer pattern than the 6x pass,
  - the attempted unpaused temporal-response run hit unrelated save pawn ticking errors in `Verse.Pawn_AgeTracker.AgeTickInterval` for Jake/Jackalope; later decompiler inspection mapped the stack offset to `ExpectationsUtility.CurrentExpectationFor(pawn.MapHeld).order`, so this is treated as converted-save/map expectation noise for this fixture, not a symbiant-rendering failure signal.
- Current standing-wave opacity trial on 2026-06-13 local time:
  - visible wave scale remains `_SymbiantNoiseScale = 2.00`, which is 75% of the prior 1.50-scale pass's feature size,
  - `_SymbiantFlowSpeed = 0.45`, a 1.8x increase over the prior 0.25 standing-wave phase speed,
  - opacity is min 0.42 and max 0.76 for stronger contrast with a lifted low-opacity floor,
  - shader-only contour shading follows a widened soft band around the midpoint of the opacity wave; current strength is 0.68 and the test shade target remains a deliberately very dark green,
  - edge contrast follow-up after the 0.70 two-lobe pass lost overall saturation while leaving the border too similar: `_SymbiantEdgeContrast = 0.95`, with the broad inner lobe removed, a narrower outer mask-falloff band, near-black green edge tint, and a rim-local alpha lift so the visible border gets more impact without washing down the symbiant body.

## Implementation Checklist

- Symbiant state: host reference/id, peak visible cells, peak integrated cells, peak benefit factor, maturity flag, decoupling reserve, severance reserve requirement, feed-per-day counter, safe-severance flags, save/load.
- Host sync: choose eligible host, skip natural spawn if none exists, recreate hediff when missing, remove stale hediffs when link is gone.
- Benefit hooks: integrated-goo benefit factor, zombie targeting protection at medium benefit or better, skill bonus, pain/capacity/need/mental-state effects.
- Feeding: pulse strength, severance reserve cap, effective reserve maturity factor, daily pulse cap, safe visible minimum, growth pause, breach cancellation.
- Surgery: defs, recipe worker, language, failure behavior.
- Unsafe damage/collapse: reserve absorption, host trauma, host-death messy collapse, safe-destroy guard.
- Bridge state: expose host, host display-hediff severity, benefit factor, integrated visible cells, peak cells, maturity state, full-benefit cells, severance maturity cells, safe visible minimum, reserve, effective reserve, reserve required, feed cap, severance readiness, and stress-test symbiant occupancy counts. Keep a bridge action that removes the host display hediff so repair behavior remains directly testable.
- Docs: keep this file, `TEST_COVERAGE.md`, `TEST_SCENARIOS.md`, and `coverage/ZL_COVERAGE_INDEX.tsv` aligned with the symbiant design.

## Implementation Stages

1. Indoor spawn, strict eligible-host selection, authoritative host link, visible cells, and save/load.
2. Room/door spread and constructed-wall breakout.
3. Path cost plus room beauty/impressiveness disruption.
4. Feeding, severance reserve, maturity gate, safe visible minimum, and feed caps.
5. Host benefits using integrated visible goo.
6. Safe severance surgery.
7. Unsafe damage reflection and host-death messy collapse.
8. Rendering cap, CPU metaball mask polish, GPU opacity shader polish, dormant renderer cleanup, and balancing passes.

## Validation Checklist

- Static/decompiler pass for the symbiant Harmony targets: skill level, pain, capacity, need, mental-state, pawn kill, damage pre-apply, stat, room beauty/impressiveness, and path-cost hooks.
- `scripts/build-quiet.sh`
- XML validation for `Defs`, `1.6/Defs`, and `Languages`.
- Spawn in a used bedroom/kitchen-style room: one initial cell, alert letter, and linked host when an eligible colonist exists.
- Natural spawn with no eligible host is skipped without consuming the cooldown; debug hostless slime uses the lesser cleanup behavior.
- Host eligibility rejects children, prisoners, slaves, guests, temporary joiners, quest lodgers, caravaning pawns, unavailable surgery targets, holograms, non-flesh optional-mod pawns, existing symbiant hosts, and late/active zombie infection cases.
- Save/load preserves ordered cells, host link, recreated hediff, reserve, daily feed counter, feed request, cooldowns, and paused growth.
- Save/load preserves peak visible cells, peak integrated cells, peak benefit factor, maturity state, reserve requirement, and feed cap reset timing.
- Expansion into room cells and under a closed door.
- Player wall cage breakout destroys one constructed wall and does not breach natural rock.
- Colonist path cost increases on symbiant cells without direct injury, disease, random filth, or item destruction.
- Symbiant-occupied rooms lose beauty and cannot count as impressive while pawns standing on goo suffer work/tend speed penalties.
- Integrated goo benefit test: active colony-room goo counts fully, while a remote abandoned containment shed gives reduced benefit.
- Feed with a humanlike non-Zombieland corpse and verify corpse quality/body size affects reserve and recession size.
- Feed with `SymbiantCoagulantPack` and verify coagulant setting affects reserve and recession size.
- Verify feed pulses/day cap prevents instant stockpile neutralization of a new symbiant.
- Prepared-player exploit test: keep a one-cell symbiant fed twice per day and verify this does not produce safe severance before maturity.
- Verify maturity triggers only after reaching `severanceMaturityCells` or medium benefit once, then persists after shrinkage.
- Verify feeding cannot remove the final cell or shrink below the safe visible minimum.
- Verify reserve requirement is bounded separately from full benefit cells in small and large colonies.
- Verify full reserve plus maturity plus 3-or-fewer visible cells enables `SeverSymbiantSymbiosis`.
- Verify safe severance removes the link and symbiant without host trauma.
- Verify unsafe destruction without reserve harms/kills the linked host.
- Verify host death by ordinary damage, player action, despawn/deletion edge cases, and caravan/map-leave edges collapses the symbiant without recursion and without reward drops.
- Verify linked host temporarily leaving the map preserves the link if the pawn is expected to return, but prevents benefit and surgery while unavailable.
- Verify `SymbiantSymbiosis` hediff removal by another system is repaired from symbiant state, including an early zero-benefit symbiant where the display hediff must not be removed by RimWorld's base zero-severity rule. Use the bridge `removeHostHediff` mode as the deterministic stand-in for DLC/mod removal.
- Verify zombie targeting behavior at low, medium, and high benefit; hard ignore must not apply to a one-cell symbiant.
- Verify disabling symbiant events while a symbiant already exists stops future spawns but does not silently delete the active symbiant.
- Verify lowering `symbiantMaxCells` below the current cell count prevents further expansion without deleting existing cells.
- Verify multi-map and save/load behavior: one active symbiant per map, no cross-map host selection, bridge state reports the intended map, and loading a no-symbiant save after a symbiant map does not return stale static-cache symbiant state.
- Verify combat isolation: bridge state must report `registeredInMapPawnLists = false`, `hostileToPlayer = false`, `activeThreatToPlayer = false`, `kindIsFighter = false`, and `combatPower = 0`; drafted colonists, animals, enemies, predators, and forced attack jobs must not attack the symbiant, while the feed job still finds it.
- Runtime render smoke: stress a 15-20 cell symbiant, capture the map, and verify it renders as connected translucent goo with no magenta shader failure and no full-square bounds artifact.
- Shader activation smoke: bridge state for the active symbiant must report `renderShader = Custom/ZombieSymbiant` and `renderUsesSymbiantShader = true` after rebuilding the current-platform asset bundle and restarting RimWorld.
- In the `symbiant rendering` save, verify walls are above the symbiant, the old body-shaped artifact near the meal is gone, and items/overlays render consistently above the symbiant.
- Large-symbiant culling smoke: move the camera so the pawn anchor cell is outside the screen but the symbiant `occupiedDrawRect` still overlaps the camera view; the symbiant must remain rendered until its bounds leave the view.
- Max dev-speed performance smoke: compare TPS with and without a symbiant in the same session and power state. If the symbiant scene is substantially below the no-symbiant baseline, especially around 240 TPS when that is the baseline, the renderer is not acceptable.
- If max-speed performance fails, use the bridge-only perf profiles to split rendering, path cost, cell stat effects, symbiant tick, hediff sync, and symbiosis benefits before optimizing.
- Stress default 400-cell cap and confirm expansion stops.
- Use the bridge-only max-cell override to stress 4000 cells without changing the saved `symbiantMaxCells` setting. Prefer a circle; if the map cannot supply enough walkable cells in the circle, fill from a centered square and record which shape was used.
- `rimbridge/list_logs minimumLevel=warning` remains clean after runtime scenarios.
