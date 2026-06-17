# Zombie Symbiant Feature Handoff

## Purpose

The Zombie Symbiant is an indoor base infestation with a colonist symbiosis twist. It is not a normal enemy and not a second spitter. The interesting decision is how much visible slime the player tolerates in useful rooms to keep the linked host powerful, versus how much reserve they build before safely severing the bond.

The feature should feel legible and unfair only in a RimWorld sense: it disrupts rooms, pathing, work, and medical logistics, but it does not randomly kill pawns, destroy storage, spread disease, or demand immediate violence.

## Player Loop

- A green side letter announces a Zombie Symbiant in a used indoor room and points to the slime and linked host.
- The Symbiant spreads through used rooms one cell at a time.
- Slime lowers room quality, slows most pawns, and reduces work/tend speed for non-host colonists standing on it.
- The linked host gains visible benefits from integrated, visible slime: skill bonus, pain reduction, capacity stabilization, need/mental protection at stronger bond levels, and zombie targeting protection at medium benefit or better.
- Weapons do not remove the Symbiant or make surgery safer. Attacks are rejected with immediate feedback.
- Feeding with humanlike non-Zombieland corpses or `SymbiantCoagulantPack` builds surgery reserve, can shrink excess cells, pauses growth, and can cancel the next breach.
- Clean removal is host surgery through `SeverSymbiantSymbiosis`, gated by maturity, full reserve, and visible size of 3 cells or fewer.
- Host death collapses the Symbiant and gives no reward. This is an emergency cost, not a clever cleanup route.

## Core Invariants

- One active Symbiant per map.
- The authoritative host link lives on `ZombieSymbiant`; `SymbiantSymbiosis` is display/sync state and is recreated when missing.
- Host benefits, zombie targeting protection, and safe severance are same-map effects only.
- Host selection is independent from spawn room selection.
- Natural spawn requires an eligible host and enough used indoor room capacity for the Symbiant to reach the maturity floor.
- Hostless slime is for debug/test or fallback tools. It has no host benefits, no host trauma, and can be fed away.
- Direct damage is absorbed before RimWorld applies it.
- Non-gameplay cleanup paths detach the link without host trauma.
- The explicit player-facing setting is `symbiantMaxCells`, equivalent in spirit to max zombies.

## Settings

- `symbiantEnabled = true`
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

Visible settings stay compact: enable Zombie Symbiants, max cells, and coagulant pack strength. Event timing, growth cadence, maturity, reserve, and benefit formulas are automatic balancing controls.

## Spawn And Room Selection

- Scheduling is event-style and derived from difficulty, zombie threat, colony points, and used indoor room pressure.
- Candidate rooms are enclosed, non-huge, non-fogged, proper indoor rooms.
- Home area is a strong signal, but not the only signal. Rooms with recent movement pheromones or valuable colony-use objects also qualify so home-area editing cannot trivially suppress the feature.
- Spawn-room scoring prefers recent traffic and useful objects such as owned beds, worktables, storage, nutrient-paste utility, batteries, coolers, heaters, and similar colony infrastructure.
- Natural spawn is skipped when the map has too little used indoor capacity to reach `symbiantSeveranceMaturityMinCells`. This avoids tiny one-room colonies receiving a linked Symbiant that cannot mature for surgery.

## Host Eligibility

Eligible hosts are spawned, living, free player colonists that are humanlike flesh pawns, adult/non-child by RimWorld category, and suitable for normal colony surgery. The selection rejects prisoners, slaves, guests, temporary joiners, quest lodgers, caravan pawns, shuttle occupants, Save Our Ship holograms, non-flesh optional-mod pawns, existing Symbiant hosts, Zombieland pawns, and late/active zombie infection cases.

If the host is temporarily unavailable through despawn-like containment, the bond can persist but active benefits and surgery turn off. If the host is spawned on another map, the bond is released, the host hediff is removed, and the remaining hostless slime can be fed away.

## Spread And Relocation

- One growth pulse adds one room/door cell, destroys one selected constructed wall and occupies that cell, or does nothing.
- Spread prefers open cells before wall targets.
- Spread never continues outdoors.
- Closed doors are valid spread cells and remain door objects.
- Natural rock and non-constructed blockers are not breached.
- A constructed wall is breached only when the cell directly beyond that wall is a valid indoor room or door target.
- Feeding can set `cancelNextBreach`, so a fed caged Symbiant does not immediately answer the feed with a wall break.
- At `symbiantMaxCells`, expansion stops but the Symbiant remains active.

Relocation handles deconstruction, battle damage, and messy rebuilding:

- Visible cells that stop counting as integrated indoor slime become relocation material.
- If the linked root loses all integrated indoor cells, a grace window lets temporary room openings settle.
- While uprooted during the grace window, ordinary growth and relocation pulses are paused.
- If another used indoor room exists after the grace window, the root reseeds there as one visible cell and carries the old footprint as relocation debt.
- Relocation debt is repaid one cell at a time at double the current adaptive growth speed.
- If no used indoor rooms exist, the Symbiant remains dormant and does not grow outdoors.
- If all valid indoor targets are exhausted or blocked, inspect text reports the contained state.

## Benefits And Disruption

Benefits scale from integrated visible cells:

- full credit for cells in home-area, roofed, proper colony rooms with recent traffic or valuable colony use,
- partial credit for cells with only one of those signals,
- low credit for remote, unused, or intentionally abandoned containment rooms,
- no credit for unroofed, outdoor, fogged, huge, or improper rooms.

The current low-credit value for unused containment is `0.10`. This keeps containment valid without making a remote slime warehouse an efficient permanent super-host farm.

`fullBenefitCells = clamp(ceil(eligibleColonyRoomCells * symbiantFullBenefitRoomCoverage), 20, symbiantMaxCells)`.

`benefitFactor = clamp01(integratedVisibleCells / fullBenefitCells)`.

Room disruption remains non-lethal:

- Symbiant-occupied rooms cannot score as impressive.
- Symbiant cells reduce room beauty.
- Pawns standing on slime have reduced medical tend speed and work speed unless they are the linked same-map host.
- Non-Zombieland pawns crossing slime pay `symbiantPathCost`; the same-map host is exempt.
- Footstep splash feedback is movement-entry feedback, not a tick effect.

## Feeding, Reserve, And Surgery

- One feed item creates one reserve pulse, not one guaranteed removed cell.
- Humanlike corpses start at 1 reserve; fresh or large corpses give more.
- Coagulant packs give 2/3/5 reserve for Cheap/Normal/Expensive potency.
- Large Symbiants add recession strength: +1 at 100 cells, +2 at 200 cells, +3 at 300 cells.
- Feeding is capped by `symbiantDecouplingFeedPulsesPerDay`.
- Feeding before maturity can store reserve, but effective reserve for shrink floors and surgery is limited by historical integration.
- Feeding cannot shrink a linked Symbiant below `safeVisibleMinimum`.
- Full reserve, maturity, and visible size of 3 cells or fewer enable `SeverSymbiantSymbiosis`.
- Surgery consumes one `SymbiantCoagulantPack` plus medicine.
- Successful surgery removes the link and Symbiant without host trauma.
- Failed surgery consumes part of the reserve, injures the host through normal surgery-failure behavior, and keeps the link active.

## Runtime Shape

`ZombieSymbiant : Pawn` is an implementation shell. Semantically it is room-scale slime with a custom renderer, cell set, host link, feed interaction, room disruption, and path cost.

The pawn shell is deliberately isolated from normal pawn/combat systems:

- not a fighter,
- zero combat power,
- hidden from `map.mapPawns`,
- discovered through `ZombieSymbiant.ActiveSymbiant(map)` and `listerThings`,
- skipped by ordinary attack targeting, story danger, fleeing, predation, auto-attack, and explicit attack jobs,
- restricted to the Symbiant job plus inert fallback jobs.

The long-term cleaner type would be a custom `Thing`/`ThingWithComps`, but that migration is separate from the v1 gameplay surface.

## Rendering And Performance

- Gameplay default cap is 400 cells.
- Technical stress ceiling is `ZombieSymbiant.MAX_METABALLS = 4000`.
- Rendering uses a CPU-generated transparent metaball mask texture drawn on a bounds mesh with the bundled non-compute `Custom/ZombieSymbiant` shader.
- The runtime renderer must not depend on Unity compute shaders.
- Texture rebuilds happen when the cell set changes, not every tick or frame.
- Runtime motion is shader-parameter-only through `_SymbiantNoiseTime`, `_SymbiantFlowSpeed`, opacity, wave shade, and edge contrast.
- The Symbiant mesh draws below walls, doors, buildings, items, pawns, and overlays.
- Hot paths must reject unrelated calls before active-Symbiant lookup. Do not scan `map.mapPawns.AllPawns` from Symbiant hot paths.
- Active-Symbiant caches are map-object keyed and transient. They are cleared on game load, shutdown, and game finalization.

Performance evidence from the implementation pass showed default 400-cell and diagnostic 4000-cell Symbiants close to same-session no-Symbiant baselines after cache, rendering, and hot-path fixes. Rerun those stress checks only when touching rendering, path cost, cell stat effects, Symbiant ticking, host hediff sync, or active-cache behavior.

## Validation Evidence

Core live contracts passed on 2026-06-15:

- `zombieland/symbiant_settings_contract`: enabled/disabled spawn behavior and max-cell cap edges.
- `zombieland/symbiant_feeding_contract`: pulse tiers, daily cap, growth pause, breach cancellation, recession behavior, coagulant potency, and one-cell exploit blocking.
- `zombieland/symbiant_unsafe_damage_contract`: damage rejection, clean debug detachment, and host-death collapse.
- `zombieland/symbiant_combat_isolation_contract`: pawn/combat isolation with feed-job discoverability.
- `zombieland/symbiant_severance_contract`: surgery gating, successful cleanup, and failed-surgery reserve loss.
- `zombieland/symbiant_benefit_contract`: low/high benefit scaling, hediff repair, zombie-targeting protection, and skill bonus.
- `zombieland/symbiant_natural_spawn_contract`: natural spawn plan and reversible fixture spawn.
- `zombieland/symbiant_discovery_letter_contract`: green discovery letter, look targets, sounds, and cleanup.
- `zombieland/symbiant_expansion_contract`: room-cell spread, closed-door spread, and constructed-wall breach into an adjacent indoor room.
- `zombieland/symbiant_map_cache_contract`: empty-map cache, spawn invalidation, cleanup invalidation, explicit cache reset, and same-process save switching.
- `zombieland/symbiant_host_availability_contract`: off-map/despawn and cryptosleep containment/resume behavior.

Focused save/load smoke passed with a linked Symbiant, reserve, feed pause, breach cancellation, missing host display hediff, active renderer, cache recovery, hediff repair, and cleanup.

Detailed historical operation ids and fixture notes live in `TEST_COVERAGE.md` under the Zombie Symbiant evidence entries. Treat that file as the evidence ledger and this file as the design handoff.

## Release Gate

For a release candidate touching Symbiant behavior:

- `scripts/build-quiet.sh`
- XML validation for `Defs`, `1.6/Defs`, `Languages`, and `1.6/Languages`
- one loaded-game smoke that spawns or observes a Symbiant
- clean warning-or-higher logs for that session
- asset bundle rebuild only if shader/material assets changed
- tracked DLLs restored before a normal source-only commit

## Deferred Follow-Up

These are not active blockers for moving to another mod area:

- Add or run a focused live relocation contract for sustained room opening, grace-window pause, reseed, relocation debt, no-room dormancy, and direct-indoor breach behavior.
- Recheck natural spawn with manipulated home areas to prove actual used rooms still qualify through traffic/object signals.
- Broaden save/load and multi-map coverage for feed request persistence, room-integrated maturity/benefit persistence, true multi-map host selection, host deletion, real caravan edges, and gravship/transport containment.
- Rerun render/performance stress only when rendering, pathing, cell-stat, active-cache, or host-sync code changes.
