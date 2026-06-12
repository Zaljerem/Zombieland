# Zombie Blob Implementation Plan

## Goal

Make the zombie blob an indoor base infestation with a colonist symbiote twist. It should bring pressure into rooms the player normally treats as safe, but the pressure must be legible and non-cheap: the blob disrupts important rooms and asks for ugly management decisions, while the linked colonist gains visible benefits as long as enough goo remains alive.

The blob is not a second spitter and not a normal combat target. The interesting decision is how much visible goo the player tolerates to keep the host powerful and protected, versus how much reserve they build before safely severing the relationship.

## Current Design Commitments

- One active blob per map.
- A blob has one authoritative linked host pawn. The link state lives on the blob, not in the host hediff.
- `BlobSymbiosis` is a display/sync hediff only. If DLC or another mod removes it while the blob link still exists, the blob should recreate it.
- Natural spawn still chooses a central, trafficked, colony-used room. The host is chosen independently from eligible colonists so players cannot trivially steer the spawn by moving one pawn to a disposable room.
- Natural spawn requires an eligible host. Hostless blobs are allowed only for debug/test spawning or explicit fallback tools, and hostless slime has its own lesser cleanup behavior rather than the full symbiote loop.
- Safe severance requires symbiosis maturity. The blob must have reached a meaningful visible size or medium benefit at least once before reserve can enable surgery.
- Full host benefit and safe severance reserve are separate concepts. Large colonies may require more integrated goo for maximum power, but safe removal must not become a 100-feed janitor marathon.
- Only colony-integrated visible goo counts fully for host benefits. A quarantined remote shed can contain the problem, but it should not provide the same power as goo disrupting active colony rooms.
- Ordinary feeding no longer removes the final cell and no longer serves as complete removal.
- Feeding creates or increases a decoupling reserve, can trigger a recession pulse, pauses growth, and can cancel the next breach, but it cannot shrink below the safe visible minimum.
- The only safe removal path is host surgery: `SeverBlobSymbiosis`.
- Unsafe blob destruction harms or kills the linked host unless enough effective reserve can absorb the trauma.
- If the linked host dies, the blob collapses messily. This is an emergency amputation path, not a clean exploit or reward source.

## Accepted Defaults

- `blobEnabled = true`
- `blobSpawnCooldownDays = 12`
- `blobExpansionIntervalHours = 16`
- `blobPostFeedPauseHours = 16`
- `blobMaxCells = 400`
- `blobFullBenefitRoomCoverage = 0.20`
- `blobSeveranceMaturityCoverage = 0.50`
- `blobSeveranceMaturityMinCells = 10`
- `blobSeveranceMaturityMaxCells = 80`
- `blobSeveranceReserveCoverage = 0.25`
- `blobSeveranceReserveMin = 12`
- `blobSeveranceReserveMax = 60`
- `blobDecouplingFeedPulsesPerDay = 2`
- `blobSymbioteMaxSkillBonus = 6`
- `blobZombieIgnoreMinBenefit = 0.50`
- `blobPathCost = 220`
- `blobCanBreakConstructedWalls = true`
- `blobCoagulantPotency = Normal`
- Technical render/buffer ceiling stays `ZombieBlob.MAX_METABALLS = 800`; the gameplay default cap is 400 and can be raised up to that ceiling.

## Spawn And Host Link

- Spawn only if blob infestation is enabled and no `ZombieBlob` already exists on the map.
- Use a separate cooldown from the zombie spitter.
- Spawn in an enclosed, non-huge, non-fogged, proper room that is home-area or colony-used.
- Score spawn rooms by recent movement pheromone timestamps first, then by valuable room objects such as beds, worktables, storage, power, and kitchen-like utility buildings.
- Initial state is one occupied cell and an alert letter describing the wet seep and the linked colonist.
- Choose a random eligible free colonist after spawn:
  - alive, spawned, humanlike, flesh,
  - player faction colonist,
  - adult or at least non-child by RimWorld age category,
  - regular free colonist, not prisoner, slave, guest, temporary joiner, quest lodger, shuttle occupant, caravan pawn, or otherwise unavailable for ordinary colony surgery,
  - not a Zombieland pawn,
  - not a Save Our Ship hologram or optional-mod non-flesh pawn,
  - not already linked to another blob,
  - not in a late/active zombie infection state.
- If no eligible host exists, natural blob spawning is skipped and the cooldown should not be consumed.
- Debug/test tools may spawn hostless room slime. Hostless slime has no symbiote benefits, no safe severance surgery, no host trauma, and can be fully removed by feeding/cleanup because there is no symbiotic root.

## Spread Rules

- Blob expansion is controlled by the dedicated blob expansion setting.
- One expansion pulse does exactly one action:
  - add one valid room or door cell,
  - destroy one selected constructed wall piece and occupy that cell,
  - or do nothing if capped, paused, safely minimized, or blocked by non-breakable terrain.
- Spread prefers nearby cells with recent traffic signal and colony-room value.
- Closed doors are valid spread cells and remain door objects; the blob can visually exist under them.
- Natural rock and non-constructed blockers are not breached.
- If the blob is caged by player walls and wants to expand, it may destroy one constructed wall that opens toward a larger or more used room.
- Feeding can set `cancelNextBreach`, so a fed caged blob does not immediately answer the feed with a wall break.
- At the configured max cells, expansion stops but the blob remains active until severed or collapsed. Existing larger blobs are not deleted if the cap is lowered; they simply cannot expand further until below the cap.

## Benefit Formula And Integrated Goo

- Benefits scale from visible blob cells only. Decoupling reserve never fakes visible size.
- Benefits use integrated visible cells, not raw blob cells:
  - full weight for cells in home-area, roofed, proper colony rooms with recent traffic or valuable colony use,
  - reduced weight for remote, unused, or intentionally abandoned containment rooms,
  - no extra credit for unroofed outdoor storage or dead-space warehouses.
- The purpose is to allow containment as a valid control tactic without turning a remote goo warehouse into a permanent super-colonist farm.
- `fullBenefitCells = clamp(ceil(eligibleColonyRoomCells * blobFullBenefitRoomCoverage), 20, blobMaxCells)`.
- `integratedVisibleCells = weighted count of visible blob cells by colony integration`.
- `benefitFactor = clamp01(integratedVisibleCells / fullBenefitCells)`.
- The linked host gains:
  - up to `blobSymbioteMaxSkillBonus` effective skill levels,
  - reduced pain,
  - stabilized capacity toward normal function,
  - dampened mental breaks at medium or better benefit,
  - averaged needs using the existing infected-colonist mechanism,
  - zombie targeting protection only at medium or better benefit.
- Zombie targeting protection should scale with benefit:
  - below `blobZombieIgnoreMinBenefit`: no hard exemption; zombies may still prefer other targets if ordinary scoring allows it,
  - at or above `blobZombieIgnoreMinBenefit`: zombies deprioritize or ignore the host unless the host attacks/provokes them,
  - hard non-targeting must not activate from a one-cell blob.
- Keeping the blob tiny gives weak benefits. Keeping it large gives the host more power but creates more room disruption.

## Symbiosis Maturity

- Track `peakVisibleCells`, `peakIntegratedVisibleCells`, and `peakBenefitFactor`.
- `severanceMaturityCells = clamp(ceil(fullBenefitCells * blobSeveranceMaturityCoverage), blobSeveranceMaturityMinCells, blobSeveranceMaturityMaxCells)`.
- `hasMaturedForSeverance` becomes true once:
  - `peakIntegratedVisibleCells >= severanceMaturityCells`, or
  - `peakBenefitFactor >= blobZombieIgnoreMinBenefit`.
- Maturity is persisted. Once the symbiote has integrated, later shrinkage does not undo maturity.
- Decoupling reserve can be stored before maturity, but it is not fully effective for safe severance until maturity is reached.
- This is the anti-bunker rule: a prepared player cannot keep a brand-new one-cell blob permanently fed and then safely cut it out without ever letting it become a real colony problem.

## Feeding And Reserve

- Selecting a blob exposes a feed-order gizmo.
- Colonists feed it with one non-Zombieland humanlike corpse or one `BlobCoagulantPack`.
- One feed item means one decoupling pulse, not one removed cell.
- A humanlike corpse pulse starts at 1 reserve unit.
- A fresh humanlike corpse is at least 2 reserve units.
- Large/high-body-size humanlike corpses are 2-3 reserve units.
- A coagulant pack is 3 reserve units by default. Cheap coagulant is 2; expensive coagulant is 5.
- Large blob state increases pulse strength: +1 at 100+ cells, +2 at 200+ cells, +3 at 300+ cells.
- Feeding consumes the input, increases decoupling reserve, plays wet/squish feedback, and pauses expansion for `blobPostFeedPauseHours`.
- Feeding is capped by `blobDecouplingFeedPulsesPerDay` to prevent stockpiling enough material to instantly neutralize a newly spawned blob.
- Feeding while caged cancels the next breach opportunity.
- Feeding can be done before maturity, but effective reserve for shrink floors, linked damage, unsafe destruction, and surgery is limited by historical integration:
  - `reserveMaturityFactor = clamp01(peakIntegratedVisibleCells / severanceMaturityCells)`,
  - `effectiveDecouplingReserve = min(decouplingReserve, severanceReserveRequired * reserveMaturityFactor)`.
- Feeding may shrink newest cells first, but only down to:
  - `safeVisibleMinimum = max(1, ceil(severanceMaturityCells * (1 - effectiveDecouplingReserve / severanceReserveRequired)))`.
- `severanceReserveRequired = clamp(ceil(fullBenefitCells * blobSeveranceReserveCoverage), blobSeveranceReserveMin, blobSeveranceReserveMax)`.
- The reserve cap is `severanceReserveRequired`. This keeps removal costly without scaling into an absurd number of corpse/coagulant jobs for large colonies.
- Full reserve, maturity, and a blob of 3 cells or less enable safe severance surgery.

## Safe Severance

- Add surgery recipe `SeverBlobSymbiosis`.
- Surgery is available only on the linked host when:
  - linked blob exists,
  - symbiosis has matured,
  - decoupling reserve is full,
  - visible blob size is at most 3 cells,
  - doctor can reach the host and normal surgery requirements are met.
- Surgery consumes one `BlobCoagulantPack` plus medicine through recipe ingredients.
- On success:
  - remove `BlobSymbiosis`,
  - clear authoritative blob-host link,
  - remove/collapse the blob without host trauma.
- On failure:
  - consume part of the reserve,
  - injure the host using the normal surgery-failure path,
  - keep the link active if the blob still exists.

## Unsafe Damage And Collapse

- Ordinary attacks are not the intended solution.
- Direct blob damage is converted into reserve loss when effective reserve is available.
- If damage exceeds effective reserve, the leftover trauma is reflected onto the linked host.
- If the blob is destroyed without safe severance and effective reserve cannot absorb the trauma, the host is killed or heavily injured. V1 should prefer clear high-stakes consequences over silent bypass.
- If the host dies for any reason, the linked blob collapses without recursively killing the already-dead host.
- Host death is allowed as a brutal emergency solution, but it must not feel like clever free removal:
  - no coagulant, resource, or reward drops,
  - any remaining visible goo collapses into inert residue or a temporary room penalty,
  - nearby pawns are not randomly harmed in v1,
  - the event messaging should make it clear that this was an unsafe severance outcome.

## Non-Lethal Room Disruption

- Room disruption remains non-lethal: no disease, infection, random item destruction, pawn mind control, or random filth spread in v1.
- Blob-occupied rooms cannot score as impressive while goo remains in the room.
- Blob cells reduce room beauty, so bedrooms, dining rooms, and recreation rooms matter by location.
- Pawns standing on blob cells have reduced tend speed and general work speed. This covers hospitals, kitchens, and work rooms through existing stat calls.
- Storage and hauling disruption comes from path cost: items on blob cells or routes crossing blob cells remain usable but awkward to reach.

## Coagulant

- Add `BlobCoagulantPack` as a craftable resource.
- Add a crafting recipe using zombie extract plus chemfuel.
- The setting is named `blobCoagulantPotency`, not recipe cost.
- Cheap/Normal/Expensive means weak/normal/strong coagulant pulse strength. XML recipe cost remains normal for v1.
- Save migration reads the old `blobCoagulantRecipeCost` key and writes `blobCoagulantPotency` without changing the player's selected tier.
- Live recipe ingredient mutation by tier is explicitly out of scope until validated.

## Rendering

- Source-side blob render/buffer capacity is raised to 800.
- The shader must use `_MetaballCount` instead of a fixed loop bound, and the C# `Metaball` struct layout must match the shader layout.
- Rebuild and validate the Unity asset bundle after shader source changes. Runtime source validation alone does not prove the shipped shader is updated.

## Implementation Checklist

- Blob state: host reference/id, peak visible cells, peak integrated cells, peak benefit factor, maturity flag, decoupling reserve, severance reserve requirement, feed-per-day counter, safe-severance flags, save/load.
- Host sync: choose eligible host, skip natural spawn if none exists, recreate hediff when missing, remove stale hediffs when link is gone.
- Benefit hooks: integrated-goo benefit factor, zombie targeting protection at medium benefit or better, skill bonus, pain/capacity/need/mental-state effects.
- Feeding: pulse strength, severance reserve cap, effective reserve maturity factor, daily pulse cap, safe visible minimum, growth pause, breach cancellation.
- Surgery: defs, recipe worker, language, failure behavior.
- Unsafe damage/collapse: reserve absorption, host trauma, host-death messy collapse, safe-destroy guard.
- Bridge state: expose host, benefit factor, integrated visible cells, peak cells, maturity state, full-benefit cells, severance maturity cells, safe visible minimum, reserve, effective reserve, reserve required, feed cap, severance readiness.
- Docs: keep this file, `TEST_COVERAGE.md`, `TEST_SCENARIOS.md`, and `coverage/ZL_COVERAGE_INDEX.tsv` aligned with the symbiote design.

## Implementation Stages

1. Indoor spawn, strict eligible-host selection, authoritative host link, visible cells, and save/load.
2. Room/door spread and constructed-wall breakout.
3. Path cost plus room beauty/impressiveness disruption.
4. Feeding, severance reserve, maturity gate, safe visible minimum, and feed caps.
5. Host benefits using integrated visible goo.
6. Safe severance surgery.
7. Unsafe damage reflection and host-death messy collapse.
8. Rendering cap, shader asset bundle, and balancing passes.

## Validation Checklist

- Static/decompiler pass for the blob Harmony targets: skill level, pain, capacity, need, mental-state, pawn kill, damage pre-apply, stat, room beauty/impressiveness, and path-cost hooks.
- `scripts/build-quiet.sh`
- XML validation for `Defs`, `1.6/Defs`, and `Languages`.
- Spawn in a used bedroom/kitchen-style room: one initial cell, alert letter, and linked host when an eligible colonist exists.
- Natural spawn with no eligible host is skipped without consuming the cooldown; debug hostless slime uses the lesser cleanup behavior.
- Host eligibility rejects children, prisoners, slaves, guests, temporary joiners, quest lodgers, caravaning pawns, unavailable surgery targets, holograms, non-flesh optional-mod pawns, existing blob hosts, and late/active zombie infection cases.
- Save/load preserves ordered cells, host link, recreated hediff, reserve, daily feed counter, feed request, cooldowns, and paused growth.
- Save/load preserves peak visible cells, peak integrated cells, peak benefit factor, maturity state, reserve requirement, and feed cap reset timing.
- Expansion into room cells and under a closed door.
- Player wall cage breakout destroys one constructed wall and does not breach natural rock.
- Colonist path cost increases on blob cells without direct injury, disease, random filth, or item destruction.
- Blob-occupied rooms lose beauty and cannot count as impressive while pawns standing on goo suffer work/tend speed penalties.
- Integrated goo benefit test: active colony-room goo counts fully, while a remote abandoned containment shed gives reduced benefit.
- Feed with a humanlike non-Zombieland corpse and verify corpse quality/body size affects reserve and recession size.
- Feed with `BlobCoagulantPack` and verify coagulant setting affects reserve and recession size.
- Verify feed pulses/day cap prevents instant stockpile neutralization of a new blob.
- Prepared-player exploit test: keep a one-cell blob fed twice per day and verify this does not produce safe severance before maturity.
- Verify maturity triggers only after reaching `severanceMaturityCells` or medium benefit once, then persists after shrinkage.
- Verify feeding cannot remove the final cell or shrink below the safe visible minimum.
- Verify reserve requirement is bounded separately from full benefit cells in small and large colonies.
- Verify full reserve plus maturity plus 3-or-fewer visible cells enables `SeverBlobSymbiosis`.
- Verify safe severance removes the link and blob without host trauma.
- Verify unsafe destruction without reserve harms/kills the linked host.
- Verify host death by ordinary damage, player action, despawn/deletion edge cases, and caravan/map-leave edges collapses the blob without recursion and without reward drops.
- Verify linked host temporarily leaving the map preserves the link if the pawn is expected to return, but prevents benefit and surgery while unavailable.
- Verify `BlobSymbiosis` hediff removal by another system is repaired from blob state.
- Verify zombie targeting behavior at low, medium, and high benefit; hard ignore must not apply to a one-cell blob.
- Verify disabling blob events while a blob already exists stops future spawns but does not silently delete the active blob.
- Verify lowering `blobMaxCells` below the current cell count prevents further expansion without deleting existing cells.
- Verify multi-map behavior: one active blob per map, no cross-map host selection, and bridge state reports the intended map.
- Stress default 400-cell cap and confirm expansion stops.
- Raise `blobMaxCells` to 800, stress 800 cells, and confirm no buffer/cap errors.
- `rimbridge/list_logs minimumLevel=warning` remains clean after runtime scenarios.
