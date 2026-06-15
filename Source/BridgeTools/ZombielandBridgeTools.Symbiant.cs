using HarmonyLib;
using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/symbiant_discovery_letter_contract", Description = "Spawn a temporary symbiant through the runtime spawn path and verify the green discovery letter, sound def, look targets, host link, and cleanup behavior.")]
		public static object SymbiantDiscoveryLetterContract(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 for automatic placement.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 for automatic placement.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Destroy the temporary contract symbiant without host trauma after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var existingActive = ZombieSymbiant.ActiveSymbiant(map);
			var existingActiveId = ZombieRuntimeActions.StableThingId(existingActive);
			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 18f, out var cell, out var cellError) == false)
				return cellError;

			var originalShowLetters = ZombieSettings.Values.showZombieEventLetters;
			var beforeSymbiantIds = CurrentZombies(map)
				.OfType<ZombieSymbiant>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
			ZombieSymbiant spawned = null;
			object spawnError = null;
			object result;

			try
			{
				ZombieSettings.Values.showZombieEventLetters = true;
				ZombieSymbiant.Spawn(map, cell);
				spawned = CurrentZombies(map)
					.OfType<ZombieSymbiant>()
					.Where(symbiant => beforeSymbiantIds.Contains(ZombieRuntimeActions.StableThingId(symbiant)) == false)
					.OrderBy(symbiant => symbiant.Position.DistanceToSquared(cell))
					.FirstOrDefault();
				spawned ??= ZombieSymbiant.ActiveSymbiant(map);
			}
			catch (Exception ex)
			{
				spawnError = ex.ToString();
			}
			finally
			{
				ZombieSettings.Values.showZombieEventLetters = originalShowLetters;
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var matchingLetters = newLetters
				.Where(letter => letter?.def == CustomDefs.SymbiantConnection)
				.ToArray();
			var host = spawned?.LinkedHost;
			var expectedLabel = host == null
				? "LetterLabelZombieSymbiantNoHost".Translate().ToString()
				: "LetterLabelZombieSymbiant".Translate(host.LabelShortCap).ToString();
			var primaryLetter = matchingLetters.FirstOrDefault();
			var lookTargetCount = primaryLetter?.lookTargets?.targets?.Count ?? 0;
			var expectedLookTargetCount = host == null ? 1 : 2;
			var connectionColor = CustomDefs.SymbiantConnection?.color;
			var colorOk = connectionColor != null
				&& connectionColor.Value.g > connectionColor.Value.r
				&& connectionColor.Value.g > connectionColor.Value.b
				&& connectionColor.Value.g >= 0.4f;
			var defOk = CustomDefs.SymbiantConnection != null
				&& CustomDefs.SymbiantConnected != null
				&& CustomDefs.SymbiantDisconnected != null
				&& CustomDefs.SymbiantConnection.arriveSound == CustomDefs.SymbiantConnected
				&& colorOk;
			var success = spawnError == null
				&& spawned?.Spawned == true
				&& matchingLetters.Length == 1
				&& primaryLetter?.Label.ToString() == expectedLabel
				&& lookTargetCount >= expectedLookTargetCount
				&& defOk;

			var cleanupResult = CleanupTemporarySymbiant(map, spawned, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);

			result = new
			{
				success,
				sourcePath = "ZombieSymbiant.Spawn -> CustomDefs.SymbiantConnection -> Find.LetterStack.ReceiveLetter",
				spawnError,
				requestedCell = ZombieRuntimeActions.DescribeCell(root),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				existingActiveSymbiantBefore = existingActiveId,
				activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup),
				restoredExistingActive = existingActive == null
					? activeAfterCleanup == null || cleanup == false
					: activeAfterCleanup == existingActive || cleanup == false,
				spawned = spawned == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(spawned),
					spawned = spawned.Spawned,
					destroyed = spawned.Destroyed,
					cellCount = spawned.CellCount,
					position = spawned.Spawned ? ZombieRuntimeActions.DescribeCell(spawned.Position) : null,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						hasSymbiosisHediff = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null
					}
				},
				defs = new
				{
					connectionLetter = CustomDefs.SymbiantConnection?.defName,
					connectionLetterArriveSound = CustomDefs.SymbiantConnection?.arriveSound?.defName,
					connectedSound = CustomDefs.SymbiantConnected?.defName,
					disconnectedSound = CustomDefs.SymbiantDisconnected?.defName,
					connectionLetterColor = CustomDefs.SymbiantConnection == null ? null : DescribeColor(CustomDefs.SymbiantConnection.color),
					colorOk,
					defOk
				},
				expectedLabel,
				expectedLookTargetCount,
				newLetterCount = newLetters.Length,
				matchingLetterCount = matchingLetters.Length,
				letters,
				cleanup = cleanupResult,
				letterCleanup
			};

			return result;
		}

		[Tool("zombieland/symbiant_natural_spawn_contract", Description = "Inspect the natural symbiant spawn plan and optionally exercise TrySpawnInBestRoom with cleanup.")]
		public static object SymbiantNaturalSpawnContract(
			[ToolParameter(Description = "Run TrySpawnInBestRoom after inspecting the plan. If false, this is read-only.", Required = false, DefaultValue = false)] bool spawn = false,
			[ToolParameter(Description = "Create a reversible bedroom fixture first when no active symbiant exists, so the positive natural-spawn path can be tested.", Required = false, DefaultValue = false)] bool setupFixture = false,
			[ToolParameter(Description = "Destroy a symbiant and fixture created by this contract without host trauma and remove generated letters.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			var activeBeforeId = ZombieRuntimeActions.StableThingId(activeBefore);
			var initialPlan = ZombieSymbiant.DebugNaturalSpawnPlan(map);
			SymbiantNaturalSpawnFixture fixture = null;
			object fixtureSetup = null;
			if (setupFixture && activeBefore == null)
				fixtureSetup = TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) ? DescribeSymbiantNaturalSpawnFixture(fixture) : fixtureError;

			var planBefore = ZombieSymbiant.DebugNaturalSpawnPlan(map);
			var expectedCanSpawn = ZombieSymbiant.CanNaturalSpawnNow(map);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
			var originalShowLetters = ZombieSettings.Values.showZombieEventLetters;
			ZombieSymbiant spawned = null;
			var trySpawnResult = false;
			object spawnError = null;

			if (spawn)
			{
				try
				{
					ZombieSettings.Values.showZombieEventLetters = true;
					trySpawnResult = ZombieSymbiant.TrySpawnInBestRoom(map);
					spawned = activeBefore == null ? ZombieSymbiant.ActiveSymbiant(map) : null;
				}
				catch (Exception ex)
				{
					spawnError = ex.ToString();
				}
				finally
				{
					ZombieSettings.Values.showZombieEventLetters = originalShowLetters;
				}
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
			var host = spawned?.LinkedHost;
			var spawnedRoom = spawned?.Spawned == true ? spawned.Position.GetRoom(map) : null;
			var spawnedRoomInfo = spawnedRoom == null ? null : new
			{
				role = spawnedRoom.Role?.defName,
				roleLabel = spawnedRoom.Role?.LabelCap.ToString(),
				cellCount = spawnedRoom.CellCount
			};
			var spawnedInFixtureRoom = fixture?.room.interiorRect.Contains(spawned?.Position ?? IntVec3.Invalid) == true;
			var cleanupResult = activeBefore == null ? CleanupTemporarySymbiant(map, spawned, cleanup) : new { requested = cleanup, cleaned = false, reason = "Existing active symbiant was present before the contract." };
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var activeAfterFixtureCleanup = ZombieSymbiant.ActiveSymbiant(map);

			var success = spawn == false
				? true
				: expectedCanSpawn
					? spawnError == null && trySpawnResult && spawned != null && host != null && newLetters.Any(letter => letter?.def == CustomDefs.SymbiantConnection) && (setupFixture == false || spawnedInFixtureRoom)
					: spawnError == null && trySpawnResult == false && spawned == null && activeAfterFixtureCleanup == activeBefore;

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TrySpawnInBestRoom -> BestSpawnRoom -> TryFindBestSpawnCell -> ZombieSymbiant.Spawn",
				spawnRequested = spawn,
				setupFixture,
				expectedCanSpawn,
				trySpawnResult,
				spawnError,
				activeSymbiantBefore = activeBeforeId,
				activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterFixtureCleanup),
				restoredExistingActive = activeBefore == null
					? activeAfterFixtureCleanup == null || cleanup == false
					: activeAfterFixtureCleanup == activeBefore,
				initialPlan,
				fixtureSetup,
				planBefore,
				spawned = spawned == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(spawned),
					spawned = spawned.Spawned,
					destroyed = spawned.Destroyed,
					cellCount = spawned.CellCount,
					position = spawned.Spawned ? ZombieRuntimeActions.DescribeCell(spawned.Position) : null,
					room = spawnedRoomInfo,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						hasSymbiosisHediff = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null
					}
				},
				spawnedInFixtureRoom,
				newLetterCount = newLetters.Length,
				matchingLetterCount = newLetters.Count(letter => letter?.def == CustomDefs.SymbiantConnection),
				letters,
				cleanup = cleanupResult,
				letterCleanup,
				fixtureCleanup,
				planAfter = ZombieSymbiant.DebugNaturalSpawnPlan(map)
			};
		}

		sealed class SymbiantNaturalSpawnFixture
		{
			public FogRoomFixture room;
			public CellRect fixtureRect;
			public Building_Bed bed;
			public Pawn host;
			public readonly Dictionary<IntVec3, bool> originalHome = new();
			public readonly Dictionary<IntVec3, RoofDef> originalRoof = new();
		}

		static bool TrySetupSymbiantNaturalSpawnFixture(Map map, out SymbiantNaturalSpawnFixture fixture, out object error)
		{
			fixture = null;
			error = null;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 48f, out var room, out error) == false)
				return false;

			var fixtureRect = CellRect.FromLimits(room.interiorRect.minX - 1, room.interiorRect.minZ - 1, room.interiorRect.maxX + 1, room.interiorRect.maxZ + 1).ClipInsideMap(map);
			fixture = new SymbiantNaturalSpawnFixture
			{
				room = room,
				fixtureRect = fixtureRect
			};

			foreach (var cell in fixtureRect.Cells)
			{
				fixture.originalHome[cell] = map.areaManager.Home[cell];
				fixture.originalRoof[cell] = map.roofGrid.RoofAt(cell);
				map.areaManager.Home[cell] = true;
			}
			foreach (var cell in room.interiorRect.ClipInsideMap(map).Cells)
				map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

			var bedCell = room.interiorRect.CenterCell;
			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				error = new { success = false, error = "Could not create a bed for the symbiant natural-spawn fixture." };
				return false;
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);
			fixture.bed = bed;

			var hostCell = room.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn || thing.def.category == ThingCategory.Building) == false)
				.OrderByDescending(cell => cell.DistanceToSquared(bedCell))
				.FirstOrDefault();
			if (hostCell.IsValid == false)
			{
				error = new { success = false, error = "Could not find a clear host cell in the symbiant natural-spawn fixture." };
				return false;
			}

			var host = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(host, hostCell, map, Rot4.South);
			DisablePawnWork(host);
			host.needs?.AddOrRemoveNeedsAsAppropriate();
			host.mindState?.mentalStateHandler?.Reset();
			fixture.host = host;
			bed.CompAssignableToPawn?.TryAssignPawn(host);
			bed.NotifyRoomAssignedPawnsChanged();

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return true;
		}

		static object DescribeSymbiantNaturalSpawnFixture(SymbiantNaturalSpawnFixture fixture)
		{
			if (fixture == null)
				return null;
			var room = fixture.bed?.GetRoom(RegionType.Set_All) ?? fixture.room.interiorRect.CenterCell.GetRoom(fixture.bed?.Map);
			return new
			{
				success = true,
				fixtureRect = ZombieRuntimeActions.DescribeCellRect(fixture.fixtureRect),
				interiorRect = ZombieRuntimeActions.DescribeCellRect(fixture.room.interiorRect),
				bed = ZombieRuntimeActions.StableThingId(fixture.bed),
				bedCell = fixture.bed?.Spawned == true ? ZombieRuntimeActions.DescribeCell(fixture.bed.Position) : null,
				host = ZombieRuntimeActions.StableThingId(fixture.host),
				hostLabel = fixture.host?.LabelShortCap,
				hostCell = fixture.host?.Spawned == true ? ZombieRuntimeActions.DescribeCell(fixture.host.Position) : null,
				room = room == null ? null : new
				{
					role = room.Role?.defName,
					roleLabel = room.Role?.LabelCap.ToString(),
					cellCount = room.CellCount,
					isHuge = room.IsHuge,
					properRoom = room.ProperRoom,
					usesOutdoorTemperature = room.UsesOutdoorTemperature
				}
			};
		}

		static object CleanupSymbiantNaturalSpawnFixture(Map map, SymbiantNaturalSpawnFixture fixture, bool cleanup)
		{
			if (fixture == null)
				return new { removed = 0, restoredCells = 0, skipped = cleanup == false };
			if (cleanup == false)
				return new { removed = 0, restoredCells = 0, skipped = true };

			var removed = 0;
			if (fixture.host != null && fixture.host.Destroyed == false)
			{
				fixture.host.Destroy(DestroyMode.Vanish);
				removed++;
			}

			foreach (var thing in fixture.fixtureRect.Cells
				.SelectMany(cell => cell.GetThingList(map))
				.Where(thing => thing is Building)
				.Distinct()
				.ToArray())
			{
				if (thing.Destroyed)
					continue;
				thing.Destroy(DestroyMode.Vanish);
				removed++;
			}

			foreach (var pair in fixture.originalHome)
				map.areaManager.Home[pair.Key] = pair.Value;
			foreach (var pair in fixture.originalRoof)
				map.roofGrid.SetRoof(pair.Key, pair.Value);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return new { removed, restoredCells = fixture.originalHome.Count, skipped = false };
		}

		[Tool("zombieland/symbiant_feeding_contract", Description = "Verify symbiant feeding pulse sizing, reserve gain, safe minimum, growth pause, breach cancellation, daily cap, and coagulant potency tiers.")]
		public static object SymbiantFeedingContract(
			[ToolParameter(Description = "Destroy temporary symbiants, host, feed items, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };
			if (CustomDefs.SymbiantCoagulantPack == null)
				return new { success = false, error = "SymbiantCoagulantPack def is missing." };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 32f, out var hostCell, out var hostCellError) == false)
				return hostCellError;
			if (TryFindClearSpawnCell(map, hostCell + new IntVec3(4, 0, 0), 32f, out var symbiantCell, out var symbiantCellError) == false)
				return symbiantCellError;

			Pawn tempHost = null;
			ZombieSymbiant defaultSymbiant = null;
			ZombieSymbiant potencySymbiant = null;
			object defaultScenario = null;
			object potencyScenario = null;
			object cleanupDefault = null;
			object cleanupPotency = null;
			object cleanupHost = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Normal;
					settings.symbiantDecouplingFeedPulsesPerDay = 2;
					settings.symbiantPostFeedPauseHours = 16;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});

				tempHost = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(tempHost, hostCell, map, Rot4.South);
				DisablePawnWork(tempHost);
				tempHost.needs?.AddOrRemoveNeedsAsAppropriate();
				tempHost.mindState?.mentalStateHandler?.Reset();

				ZombieSymbiant.Spawn(map, symbiantCell);
				defaultSymbiant = ZombieSymbiant.ActiveSymbiant(map);
				defaultScenario = RunSymbiantDefaultFeedingScenario(map, defaultSymbiant);
				cleanupDefault = CleanupTemporarySymbiant(map, defaultSymbiant, cleanup);
				defaultSymbiant = null;

				ApplyZombieSettingsOverride(settings =>
				{
					settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Cheap;
					settings.symbiantDecouplingFeedPulsesPerDay = 10;
				});

				ZombieSymbiant.Spawn(map, symbiantCell);
				potencySymbiant = ZombieSymbiant.ActiveSymbiant(map);
				potencyScenario = RunSymbiantCoagulantPotencyScenario(potencySymbiant);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				cleanupPotency = CleanupTemporarySymbiant(map, potencySymbiant, cleanup);
				if (cleanup && tempHost != null && tempHost.Destroyed == false)
				{
					var id = ZombieRuntimeActions.StableThingId(tempHost);
					tempHost.Destroy(DestroyMode.Vanish);
					cleanupHost = new { cleaned = tempHost.Destroyed, host = id };
				}
				else
					cleanupHost = new { cleaned = false, skipped = cleanup == false, host = ZombieRuntimeActions.StableThingId(tempHost) };
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(defaultScenario)
				&& ScenarioSucceeded(potencyScenario)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TryFeed -> RecessionPulseSize -> ShrinkCells",
				error,
				spawn = new
				{
					hostCell = ZombieRuntimeActions.DescribeCell(hostCell),
					symbiantCell = ZombieRuntimeActions.DescribeCell(symbiantCell),
					tempHost = ZombieRuntimeActions.StableThingId(tempHost)
				},
				defaultScenario,
				potencyScenario,
				cleanup = new
				{
					defaultSymbiant = cleanupDefault,
					potencySymbiant = cleanupPotency,
					host = cleanupHost,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantDefaultFeedingScenario(Map map, ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return new { success = false, error = "No symbiant was spawned for the default feeding scenario." };

			var host = symbiant.LinkedHost;
			var initialCells = symbiant.CellCount;
			var first = FeedSymbiantCoagulant(symbiant, "normal one-cell feed", 3);
			var oneCellStayedVisible = first.afterCells == initialCells && first.removedCells == 0;
			var pauseTicks = Math.Max(0, ZombieSettings.Values.symbiantPostFeedPauseHours) * GenDate.TicksPerHour;
			var pauseApplied = first.feedPausedUntilTick >= first.beforeTick + pauseTicks;
			var cancelBreach = symbiant.CancelNextBreach;

			var targetCells = GenRadial.RadialCellsAround(symbiant.Position, 28f, true)
				.Where(cell => cell.InBounds(map) && symbiant.ContainsCell(cell) == false)
				.Take(119)
				.ToArray();
			var addedCells = ZombieSymbiant.AddCells(map, targetCells);
			var largeBeforeCells = symbiant.CellCount;
			var second = FeedSymbiantCoagulant(symbiant, "normal large-state feed", 4);
			var third = FeedSymbiantCoagulant(symbiant, "daily cap rejection", 4);
			var thirdBlocked = third.fed == false
				&& Approximately(third.reserveDelta, 0f)
				&& third.removedCells == 0
				&& third.afterCells == third.beforeCells
				&& symbiant.DecouplingFeedPulsesToday == 2;

			var success = host != null
				&& initialCells == 1
				&& first.success
				&& oneCellStayedVisible
				&& pauseApplied
				&& cancelBreach
				&& addedCells >= 99
				&& largeBeforeCells >= 100
				&& second.success
				&& second.removedCells == 4
				&& thirdBlocked
				&& symbiant.CellCount >= symbiant.SafeVisibleMinimum;

			return new
			{
				success,
				host = host == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(host),
					label = host.LabelShortCap
				},
				initialCells,
				first,
				oneCellStayedVisible,
				pauseTicks,
				pauseApplied,
				cancelBreach,
				addedCells,
				largeBeforeCells,
				second,
				third,
				thirdBlocked,
				final = DescribeSymbiantFeedingState(symbiant)
			};
		}

		static object RunSymbiantCoagulantPotencyScenario(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return new { success = false, error = "No symbiant was spawned for the coagulant potency scenario." };

			ApplyZombieSettingsOverride(settings => settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Cheap);
			var cheap = FeedSymbiantCoagulant(symbiant, "cheap coagulant", 2);
			ApplyZombieSettingsOverride(settings => settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Normal);
			var normal = FeedSymbiantCoagulant(symbiant, "normal coagulant", 3);
			ApplyZombieSettingsOverride(settings => settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Expensive);
			var expensive = FeedSymbiantCoagulant(symbiant, "expensive coagulant", 5);

			var success = symbiant.LinkedHost != null
				&& cheap.success
				&& normal.success
				&& expensive.success
				&& cheap.removedCells == 0
				&& normal.removedCells == 0
				&& expensive.removedCells == 0
				&& symbiant.CellCount == 1
				&& symbiant.DecouplingFeedPulsesToday == 3;

			return new
			{
				success,
				cheap,
				normal,
				expensive,
				final = DescribeSymbiantFeedingState(symbiant)
			};
		}

		sealed class SymbiantFeedStep
		{
			public string label { get; set; }
			public int expectedPulse { get; set; }
			public int beforeTick { get; set; }
			public int beforeCells { get; set; }
			public int afterCells { get; set; }
			public float reserveBefore { get; set; }
			public float reserveAfter { get; set; }
			public float reserveDelta { get; set; }
			public int feedPulsesBefore { get; set; }
			public int feedPulsesAfter { get; set; }
			public bool fed { get; set; }
			public int removedCells { get; set; }
			public int feedPausedUntilTick { get; set; }
			public bool cancelNextBreach { get; set; }
			public bool success { get; set; }
		}

		static SymbiantFeedStep FeedSymbiantCoagulant(ZombieSymbiant symbiant, string label, int expectedPulse)
		{
			var beforeTick = GenTicks.TicksGame;
			var beforeCells = symbiant?.CellCount ?? 0;
			var reserveBefore = symbiant?.DecouplingReserve ?? 0f;
			var feedPulsesBefore = symbiant?.DecouplingFeedPulsesToday ?? 0;
			var pack = ThingMaker.MakeThing(CustomDefs.SymbiantCoagulantPack);
			var fed = symbiant?.TryFeed(pack) == true;
			if (fed == false && pack?.Destroyed == false)
				pack.Destroy(DestroyMode.Vanish);
			var reserveAfter = symbiant?.DecouplingReserve ?? reserveBefore;
			var afterCells = symbiant?.Destroyed == true ? 0 : symbiant?.CellCount ?? 0;
			var reserveDelta = reserveAfter - reserveBefore;
			var removedCells = symbiant?.LastRecessionPulseCells ?? 0;
			var success = fed
				&& Approximately(reserveDelta, expectedPulse)
				&& feedPulsesBefore + 1 == (symbiant?.DecouplingFeedPulsesToday ?? feedPulsesBefore)
				&& removedCells <= expectedPulse;
			return new SymbiantFeedStep
			{
				label = label,
				expectedPulse = expectedPulse,
				beforeTick = beforeTick,
				beforeCells = beforeCells,
				afterCells = afterCells,
				reserveBefore = reserveBefore,
				reserveAfter = reserveAfter,
				reserveDelta = reserveDelta,
				feedPulsesBefore = feedPulsesBefore,
				feedPulsesAfter = symbiant?.DecouplingFeedPulsesToday ?? feedPulsesBefore,
				fed = fed,
				removedCells = removedCells,
				feedPausedUntilTick = symbiant?.FeedPausedUntilTick ?? 0,
				cancelNextBreach = symbiant?.CancelNextBreach ?? false,
				success = success
			};
		}

		static object DescribeSymbiantFeedingState(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return null;
			return new
			{
				cellCount = symbiant.CellCount,
				decouplingReserve = symbiant.DecouplingReserve,
				decouplingReserveMax = symbiant.DecouplingReserveMax,
				reserveMaturityFactor = symbiant.ReserveMaturityFactor,
				effectiveDecouplingReserve = symbiant.EffectiveDecouplingReserve,
				safeVisibleMinimum = symbiant.SafeVisibleMinimum,
				feedPulsesToday = symbiant.DecouplingFeedPulsesToday,
				feedPulsesPerDay = symbiant.DecouplingFeedPulsesPerDay,
				feedPulsesRemaining = symbiant.FeedPulsesRemaining,
				lastRecessionPulseCells = symbiant.LastRecessionPulseCells,
				feedPausedUntilTick = symbiant.FeedPausedUntilTick,
				cancelNextBreach = symbiant.CancelNextBreach
			};
		}

		static bool ScenarioSucceeded(object scenario)
		{
			if (scenario == null)
				return false;
			var property = scenario.GetType().GetProperty("success");
			return property?.GetValue(scenario) is bool success && success;
		}

		[Tool("zombieland/symbiant_combat_isolation_contract", Description = "Verify the symbiant Pawn shell is isolated from ordinary combat targeting while feed jobs can still discover it.")]
		public static object SymbiantCombatIsolationContract(
			[ToolParameter(Description = "Destroy temporary pawns, feed item, letter, and symbiant after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };
			if (CustomDefs.SymbiantCoagulantPack == null)
				return new { success = false, error = "SymbiantCoagulantPack def is missing." };

			var hostileFaction = Find.FactionManager?.AllFactionsListForReading?
				.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.def?.humanlikeFaction == true)
				?? Find.FactionManager?.AllFactionsListForReading?
					.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer));
			if (hostileFaction == null)
				return new { success = false, error = "Could not find a hostile faction for the combat-isolation fixture." };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var spawnedThings = new List<Thing>();
			ZombieSymbiant symbiant = null;
			Thing pack = null;
			object result;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.attackMode = AttackMode.Everything;
					settings.enemiesAttackZombies = true;
					settings.animalsAttackZombies = true;
					settings.symbiantDecouplingFeedPulsesPerDay = Math.Max(2, settings.symbiantDecouplingFeedPulsesPerDay);
				});

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindSymbiantCombatFixtureCells(map, root, 6, out var cells, out var cellError) == false)
					return cellError;

				var player = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_Player", cells[0], Faction.OfPlayer, spawnedThings);
				var enemy = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_Enemy", cells[1], hostileFaction, spawnedThings);
				var animal = SpawnAreaWorkflowAnimal(map, "ZL_SymbiantCombat_Animal", cells[2], Faction.OfPlayer, spawnedThings, def => def.combatPower > 0f);
				var predator = SpawnAreaWorkflowAnimal(map, "ZL_SymbiantCombat_Predator", cells[3], Faction.OfPlayer, spawnedThings, def => def.RaceProps?.predator == true || def.combatPower >= 1f);
				ZombieSymbiant.Spawn(map, cells[4]);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				if (symbiant != null)
					symbiant.Name = new NameSingle("ZL_SymbiantCombat_Goo");
				pack = ThingMaker.MakeThing(CustomDefs.SymbiantCoagulantPack);
				GenSpawn.Spawn(pack, cells[5], map, Rot4.South);
				spawnedThings.Add(pack);

				RefreshZombieTargetCache(map);
				symbiant?.RequestFeed(true);

				var pawnSystems = DescribeSymbiantCombatPawnSystems(map, symbiant, player, enemy);
				var targetFinding = new
				{
					player = DescribeBestSymbiantTarget(player, symbiant),
					enemy = DescribeBestSymbiantTarget(enemy, symbiant),
					animal = DescribeBestSymbiantTarget(animal, symbiant),
					predator = DescribeBestSymbiantTarget(predator, symbiant)
				};
				var forcedJobs = new
				{
					playerMelee = VerifySymbiantAttackJobRejected(player, symbiant, JobDefOf.AttackMelee),
					playerStatic = VerifySymbiantAttackJobRejected(player, symbiant, JobDefOf.AttackStatic),
					enemyMelee = VerifySymbiantAttackJobRejected(enemy, symbiant, JobDefOf.AttackMelee),
					symbiantMelee = VerifySymbiantAttackJobRejected(symbiant, player, JobDefOf.AttackMelee)
				};
				var animalResponse = new
				{
					manhunterChance = animal == null || symbiant == null ? null : (float?)PawnUtility.GetManhunterOnDamageChance(animal, symbiant, animal.Position.DistanceTo(symbiant.Position)),
					preyScore = predator == null || symbiant == null ? null : (float?)FoodUtility.GetPreyScoreFor(predator, symbiant)
				};
				var feed = WorkGiver_FeedZombieSymbiant.FindClosestFeed(player, symbiant);
				var feedJob = new WorkGiver_FeedZombieSymbiant().JobOnThing(player, symbiant, true);
				var feedDiscovery = new
				{
					feedRequested = symbiant?.FeedRequested ?? false,
					closestFeed = feed == null ? null : ZombieRuntimeActions.StableThingId(feed),
					closestFeedDef = feed?.def?.defName,
					foundSpawnedPack = feed == pack,
					jobDef = feedJob?.def?.defName,
					jobTargetA = ZombieRuntimeActions.StableThingId(feedJob?.targetA.Thing),
					jobTargetB = ZombieRuntimeActions.StableThingId(feedJob?.targetB.Thing),
					success = feed == pack && feedJob?.def == CustomDefs.FeedZombieSymbiant && feedJob.targetA.Thing == symbiant && feedJob.targetB.Thing == pack
				};
				var patchTargets = new
				{
					availableShootingTargets = PatchedMethodsForPatchClass("AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch"),
					bestAttackTarget = PatchedMethodsForPatchClass("AttackTargetFinder_BestAttackTarget_Patch"),
					hostileThingThing = PatchedMethodsForPatchClass("GenHostility_HostileTo_Thing_Thing_Patch"),
					hostileThingFaction = PatchedMethodsForPatchClass("GenHostility_HostileTo_Thing_Faction_Patch"),
					activeThreat = PatchedMethodsForPatchClass("GenHostility_IsActiveThreat_Patch"),
					registerTarget = PatchedMethodsForPatchClass("AttackTargetsCache_RegisterTarget_Patch"),
					startJob = PatchedMethodsForPatchClass("Pawn_JobTracker_StartJob_Patch"),
					danger = PatchedMethodsForPatchClass("DangerWatcher_AffectsStoryDanger_Patch"),
					flee = PatchedMethodsForPatchClass("FleeUtility_ShouldFleeFrom_Patch"),
					manhunter = PatchedMethodsForPatchClass("PawnUtility_GetManhunterOnDamageChance_Patch"),
					prey = PatchedMethodsForPatchClass("FoodUtility_GetPreyScoreFor_Patch")
				};

				var success = symbiant?.Spawned == true
					&& ScenarioSucceeded(pawnSystems)
					&& ScenarioSucceeded(targetFinding.player)
					&& ScenarioSucceeded(targetFinding.enemy)
					&& ScenarioSucceeded(targetFinding.animal)
					&& ScenarioSucceeded(targetFinding.predator)
					&& ScenarioSucceeded(forcedJobs.playerMelee)
					&& ScenarioSucceeded(forcedJobs.playerStatic)
					&& ScenarioSucceeded(forcedJobs.enemyMelee)
					&& ScenarioSucceeded(forcedJobs.symbiantMelee)
					&& animalResponse.manhunterChance == 0f
					&& animalResponse.preyScore <= -9999f
					&& feedDiscovery.success
					&& patchTargets.bestAttackTarget.Length > 0
					&& patchTargets.hostileThingThing.Length > 0
					&& patchTargets.activeThreat.Length > 0
					&& patchTargets.startJob.Length > 0;

				result = new
				{
					success,
					sourcePath = "Patches_Hostility + Pawn_JobTracker_StartJob_Patch + WorkGiver_FeedZombieSymbiant",
					fixtureCells = cells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					pawns = new
					{
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						animal = DescribePawn(animal),
						predator = DescribePawn(predator),
						symbiant = DescribePawn(symbiant)
					},
					pawnSystems,
					targetFinding,
					forcedJobs,
					animalResponse,
					feedDiscovery,
					patchTargets
				};
			}
			catch (Exception ex)
			{
				result = new { success = false, error = ex.ToString() };
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				if (cleanup)
					foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).Distinct().ToArray())
						thing.Destroy(DestroyMode.Vanish);
				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				_ = CleanupTemporaryLetters(newLetters, cleanup);
				RestoreZombieSettings(settingsSnapshot);
			}

			return result;
		}

		static bool TryFindSymbiantCombatFixtureCells(Map map, IntVec3 root, int count, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 24f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.GetEdifice(map) == null)
				.Distinct()
				.Take(count)
				.ToArray();
			error = cells.Length >= count
				? null
				: new { success = false, error = "Could not find enough clear cells for the symbiant combat-isolation fixture.", requested = count, found = cells.Length };
			return error == null;
		}

		static object DescribeSymbiantCombatPawnSystems(Map map, ZombieSymbiant symbiant, Pawn player, Pawn enemy)
		{
			var playerFaction = Find.FactionManager?.AllFactionsListForReading?.FirstOrDefault(faction => faction?.def?.isPlayer == true);
			var dangerMethod = AccessTools.Method(typeof(DangerWatcher), "AffectsStoryDanger");
			var danger = dangerMethod != null && symbiant != null && (bool)dangerMethod.Invoke(null, new object[] { symbiant });
			var flee = player != null && symbiant != null && FleeUtility.ShouldFleeFrom(symbiant, player, true, false);
			var targetsHostile = map?.attackTargetsCache?.TargetsHostileToColony?.Contains(symbiant) ?? false;
			var hostileToPlayer = symbiant != null && playerFaction != null && symbiant.HostileTo(playerFaction);
			var playerHostileToSymbiant = player != null && symbiant != null && player.HostileTo(symbiant);
			var enemyHostileToSymbiant = enemy != null && symbiant != null && enemy.HostileTo(symbiant);
			var activeThreatToPlayer = symbiant != null && playerFaction != null && GenHostility.IsActiveThreatTo(symbiant, playerFaction, false, false);
			var success = symbiant != null
				&& symbiant.RegisteredInMapPawnLists == false
				&& targetsHostile == false
				&& hostileToPlayer == false
				&& playerHostileToSymbiant == false
				&& enemyHostileToSymbiant == false
				&& activeThreatToPlayer == false
				&& danger == false
				&& flee == false
				&& symbiant.kindDef?.isFighter == false
				&& Mathf.Approximately(symbiant.kindDef?.combatPower ?? 0f, 0f);
			return new
			{
				success,
				registeredInMapPawnLists = symbiant?.RegisteredInMapPawnLists ?? false,
				attackTargetsHostileToColony = targetsHostile,
				hostileToPlayer,
				playerHostileToSymbiant,
				enemyHostileToSymbiant,
				activeThreatToPlayer,
				affectsStoryDanger = danger,
				shouldFleeFrom = flee,
				kindIsFighter = symbiant?.kindDef?.isFighter ?? false,
				combatPower = symbiant?.kindDef?.combatPower ?? 0f
			};
		}

		static object DescribeBestSymbiantTarget(Pawn searcher, ZombieSymbiant symbiant)
		{
			var target = searcher == null || symbiant == null
				? null
				: AttackTargetFinder.BestAttackTarget(searcher, TargetScanFlags.NeedThreat, thing => thing == symbiant, 0f, 999f);
			return new
			{
				success = target == null,
				searcher = ZombieRuntimeActions.StableThingId(searcher),
				searcherDef = searcher?.def?.defName,
				searcherKind = searcher?.kindDef?.defName,
				currentVerb = searcher?.CurrentEffectiveVerb?.ToString(),
				target = DescribeTarget(target)
			};
		}

		static object VerifySymbiantAttackJobRejected(Pawn actor, Thing target, JobDef jobDef)
		{
			if (actor == null || target == null || jobDef == null)
				return new { success = false, error = "Missing actor, target, or jobDef.", actor = ZombieRuntimeActions.StableThingId(actor), target = ZombieRuntimeActions.StableThingId(target), jobDef = jobDef?.defName };
			var beforeJob = actor.CurJob;
			var beforeJobDef = beforeJob?.def?.defName;
			var beforeTarget = ZombieRuntimeActions.StableThingId(beforeJob?.targetA.Thing);
			var job = JobMaker.MakeJob(jobDef, target);
			actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			var afterJob = actor.CurJob;
			var accepted = afterJob != null && afterJob.def == jobDef && afterJob.targetA.Thing == target;
			if (accepted == false)
				actor.jobs.StopAll(false, true);
			return new
			{
				success = accepted == false,
				actor = ZombieRuntimeActions.StableThingId(actor),
				target = ZombieRuntimeActions.StableThingId(target),
				jobDef = jobDef.defName,
				beforeJobDef,
				beforeTarget,
				afterJobDef = afterJob?.def?.defName,
				afterTarget = ZombieRuntimeActions.StableThingId(afterJob?.targetA.Thing),
				accepted
			};
		}

		[Tool("zombieland/symbiant_severance_contract", Description = "Verify safe-severance surgery gates, recipe ingredients, deterministic success cleanup, and deterministic failure reserve loss.")]
		public static object SymbiantSeveranceContract(
			[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };
			if (CustomDefs.SeverSymbiantSymbiosis == null)
				return new { success = false, error = "SeverSymbiantSymbiosis recipe def is missing." };
			if (CustomDefs.SymbiantCoagulantPack == null)
				return new { success = false, error = "SymbiantCoagulantPack def is missing." };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantNaturalSpawnFixture fixture = null;
			Pawn doctor = null;
			ZombieSymbiant successSymbiant = null;
			ZombieSymbiant failureSymbiant = null;
			object fixtureSetup = null;
			object successScenario = null;
			object failureScenario = null;
			object cleanupSuccess = null;
			object cleanupFailure = null;
			object cleanupDoctor = null;
			object fixtureCleanup = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Expensive;
					settings.symbiantDecouplingFeedPulsesPerDay = 20;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});

				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				if (TryFindClearSpawnCell(map, fixture.room.interiorRect.CenterCell + new IntVec3(5, 0, 0), 24f, out var doctorCell, out var doctorCellError) == false)
					return doctorCellError;
				doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(doctor, doctorCell, map, Rot4.South);
				DisablePawnWork(doctor);
				doctor.needs?.AddOrRemoveNeedsAsAppropriate();
				doctor.mindState?.mentalStateHandler?.Reset();

				successSymbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				successScenario = RunSymbiantSeveranceScenario(map, fixture, doctor, successSymbiant, true);
				successSymbiant = null;

				failureSymbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				failureScenario = RunSymbiantSeveranceScenario(map, fixture, doctor, failureSymbiant, false);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				cleanupSuccess = CleanupTemporarySymbiant(map, successSymbiant, cleanup);
				cleanupFailure = CleanupTemporarySymbiant(map, failureSymbiant, cleanup);
				if (cleanup && doctor != null && doctor.Destroyed == false)
				{
					var id = ZombieRuntimeActions.StableThingId(doctor);
					doctor.Destroy(DestroyMode.Vanish);
					cleanupDoctor = new { cleaned = doctor.Destroyed, doctor = id };
				}
				else
					cleanupDoctor = new { cleaned = false, skipped = cleanup == false, doctor = ZombieRuntimeActions.StableThingId(doctor) };
				fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var ingredients = DescribeSymbiantSeveranceRecipeIngredients();
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(ingredients)
				&& ScenarioSucceeded(successScenario)
				&& ScenarioSucceeded(failureScenario)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "Recipe_SeverSymbiantSymbiosis.GetPartsToApplyOn/ApplyOnPawn -> ZombieSymbiant.TrySeverSymbiosis",
				error,
				fixtureSetup,
				ingredients,
				successScenario,
				failureScenario,
				cleanup = new
				{
					successSymbiant = cleanupSuccess,
					failureSymbiant = cleanupFailure,
					doctor = cleanupDoctor,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static ZombieSymbiant SpawnAssignedSymbiantForSeveranceContract(Map map, SymbiantNaturalSpawnFixture fixture)
		{
			var spawnCell = fixture.room.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.OrderBy(cell => cell.DistanceToSquared(fixture.room.interiorRect.CenterCell))
				.FirstOrDefault();
			if (spawnCell.IsValid == false)
				throw new InvalidOperationException("Could not find a clear symbiant severance spawn cell.");

			ZombieSymbiant.Spawn(map, spawnCell);
			var symbiant = ZombieSymbiant.ActiveSymbiant(map) ?? throw new InvalidOperationException("Symbiant spawn did not create an active symbiant.");
			var originalHost = symbiant.LinkedHost;
			if (originalHost != null && originalHost != fixture.host)
				AccessTools.Method(typeof(ZombieSymbiant), "RemoveHostHediff")?.Invoke(null, new object[] { originalHost });
			AccessTools.Method(typeof(ZombieSymbiant), "AssignHost")?.Invoke(symbiant, new object[] { fixture.host });
			return symbiant;
		}

		static object RunSymbiantSeveranceScenario(Map map, SymbiantNaturalSpawnFixture fixture, Pawn doctor, ZombieSymbiant symbiant, bool forceSuccess)
		{
			if (symbiant == null)
				return new { success = false, error = "No symbiant was spawned for the severance scenario." };
			var recipe = CustomDefs.SeverSymbiantSymbiosis;
			var worker = recipe.Worker as Recipe_SeverSymbiantSymbiosis;
			var host = fixture.host;
			if (worker == null || host == null || doctor == null)
				return new { success = false, error = "Recipe worker, host, or doctor is missing." };

			var beforeReadyParts = worker.GetPartsToApplyOn(host, recipe).ToArray();
			var prepare = PrepareSymbiantForSafeSeverance(map, fixture, symbiant);
			var readyParts = worker.GetPartsToApplyOn(host, recipe).ToArray();
			var torso = readyParts.FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
			if (torso == null)
				return new
				{
					success = false,
					error = "Prepared symbiant did not expose torso surgery target.",
					beforeReadyParts = beforeReadyParts.Length,
					readyParts = readyParts.Select(part => part.def.defName).ToArray(),
					prepare
				};

			var doctorMedicine = doctor.skills.GetSkill(SkillDefOf.Medicine);
			doctorMedicine.Level = forceSuccess ? 20 : 0;
			var chance = Mathf.Clamp(0.55f + doctorMedicine.Level * 0.03f, 0.55f, 0.95f);
			var seed = FindSurgeryOutcomeSeed(chance, forceSuccess);
			var reserveBefore = symbiant.DecouplingReserve;
			var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
			Rand.PushState(seed);
			try
			{
				worker.ApplyOnPawn(host, torso, doctor, new List<Thing>(), null);
			}
			finally
			{
				Rand.PopState();
			}

			var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
			var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
			var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
			var success = forceSuccess
				? symbiant.Destroyed
					&& activeAfter == null
					&& linkedAfter == null
					&& hediffBefore
					&& hediffAfter == false
					&& host.Dead == false
				: symbiant.Destroyed == false
					&& activeAfter == symbiant
					&& linkedAfter == symbiant
					&& hediffBefore
					&& hediffAfter
					&& symbiant.DecouplingReserve < reserveBefore
					&& host.Dead == false;

			return new
			{
				success,
				forceSuccess,
				chance,
				seed,
				beforeReadyParts = beforeReadyParts.Select(part => part.def.defName).ToArray(),
				readyParts = readyParts.Select(part => part.def.defName).ToArray(),
				prepare,
				reserveBefore,
				reserveAfter = symbiant.Destroyed ? 0f : symbiant.DecouplingReserve,
				symbiantDestroyed = symbiant.Destroyed,
				activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
				linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
				hediffBefore,
				hediffAfter,
				hostDead = host.Dead
			};
		}

		static object PrepareSymbiantForSafeSeverance(Map map, SymbiantNaturalSpawnFixture fixture, ZombieSymbiant symbiant)
		{
			var roomCells = fixture.room.interiorRect.Cells
				.Where(cell => cell.InBounds(map) && cell.Standable(map))
				.ToArray();
			var addedCells = ZombieSymbiant.AddCells(map, roomCells);
			var feeds = 0;
			while ((symbiant.DecouplingReserve < symbiant.DecouplingReserveMax - 0.001f || symbiant.CellCount > 3) && feeds < 20)
			{
				var pack = ThingMaker.MakeThing(CustomDefs.SymbiantCoagulantPack);
				if (symbiant.TryFeed(pack) == false)
				{
					if (pack.Destroyed == false)
						pack.Destroy(DestroyMode.Vanish);
					break;
				}
				feeds++;
			}
			return new
			{
				addedCells,
				feeds,
				cellCount = symbiant.CellCount,
				hasMaturedForSeverance = symbiant.HasMaturedForSeverance,
				decouplingReserve = symbiant.DecouplingReserve,
				decouplingReserveMax = symbiant.DecouplingReserveMax,
				effectiveDecouplingReserve = symbiant.EffectiveDecouplingReserve,
				safeVisibleMinimum = symbiant.SafeVisibleMinimum,
				canSafelySever = symbiant.CanSafelySever
			};
		}

		[Tool("zombieland/symbiant_benefit_contract", Description = "Verify host display-hediff repair, low/high benefit scaling, zombie targeting threshold, and skill bonus behavior.")]
		public static object SymbiantBenefitContract(
			[ToolParameter(Description = "Destroy the temporary symbiant, colonist, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object error = null;
			object initial = null;
			object repair = null;
			object high = null;
			object skill = null;
			var addedCells = 0;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = 25;
					settings.symbiantFullBenefitRoomCoverage = 0.01f;
					settings.symbiantZombieIgnoreMinBenefit = 0.50f;
					settings.symbiantMaxSkillBonus = 6;
				});
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				RepairHostLink(symbiant);
				initial = DescribeSymbiantBenefitCheck(symbiant, host);

				var removedHediffs = RemoveSymbiantHediffs(host);
				var afterRemoval = DescribeSymbiantBenefitCheck(symbiant, host);
				RepairHostLink(symbiant);
				var afterRepair = DescribeSymbiantBenefitCheck(symbiant, host);
				repair = new
				{
					removedHediffs,
					afterRemoval,
					afterRepair,
					minZeroBenefitSeverity = ZombieSymbiant.HostHediffSeverity(0f),
					success = removedHediffs > 0
						&& BenefitCheckHasHediff(afterRemoval) == false
						&& BenefitCheckHasHediff(afterRepair)
						&& BenefitCheckHediffSeverity(afterRepair) >= 0.001f
						&& ZombieSymbiant.HostHediffSeverity(0f) >= 0.001f
				};

				var roomCells = fixture.room.interiorRect.Cells
					.Where(cell => cell.InBounds(map) && cell.Standable(map))
					.ToArray();
				addedCells = ZombieSymbiant.AddCells(map, roomCells);
				RepairHostLink(symbiant);
				high = DescribeSymbiantBenefitCheck(symbiant, host);
				skill = DescribeSymbiantSkillBonus(host);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& BenefitCheckHasHediff(initial)
				&& BenefitCheckFactor(initial) < 0.5f
				&& BenefitCheckHasZombieProtection(initial) == false
				&& ScenarioSucceeded(repair)
				&& addedCells > 0
				&& BenefitCheckFactor(high) >= 0.5f
				&& BenefitCheckHasZombieProtection(high)
				&& ScenarioSucceeded(skill)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.EnsureHostLink/EnsureHostHediff -> BenefitFactor -> HasZombieTargetingProtection -> ApplySymbiantSkillBonus",
				error,
				fixtureSetup,
				initial,
				repair,
				addedCells,
				high,
				skill,
				cleanup = new
				{
					symbiant = cleanupResult,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static void RepairHostLink(ZombieSymbiant symbiant)
		{
			AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
		}

		static int RemoveSymbiantHediffs(Pawn host)
		{
			var hediffs = host?.health?.hediffSet?.hediffs?
				.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis)
				.ToArray() ?? Array.Empty<Hediff>();
			foreach (var hediff in hediffs)
				host.health.RemoveHediff(hediff);
			return hediffs.Length;
		}

		static object DescribeSymbiantBenefitCheck(ZombieSymbiant symbiant, Pawn host)
		{
			var hediff = host?.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			return new
			{
				cellCount = symbiant?.CellCount ?? 0,
				fullBenefitCells = symbiant?.FullBenefitCells ?? 0,
				integratedVisibleCells = symbiant?.IntegratedVisibleCells ?? 0f,
				peakIntegratedVisibleCells = symbiant?.PeakIntegratedVisibleCells ?? 0f,
				benefitFactor = symbiant?.BenefitFactor ?? 0f,
				peakBenefitFactor = symbiant?.PeakBenefitFactor ?? 0f,
				zombieIgnoreMinBenefit = ZombieSymbiant.ZombieIgnoreMinBenefit,
				hasZombieTargetingProtection = ZombieSymbiant.HasZombieTargetingProtection(host),
				hasHediff = hediff != null,
				hediffSeverity = hediff?.Severity ?? 0f,
				hediffSymbiantThingId = hediff?.symbiantThingId,
				expectedHostHediffSeverity = ZombieSymbiant.HostHediffSeverity(symbiant?.BenefitFactor ?? 0f)
			};
		}

		static object DescribeSymbiantSkillBonus(Pawn host)
		{
			var skill = host?.skills?.GetSkill(SkillDefOf.Construction);
			if (skill == null)
				return new { success = false, error = "Linked host has no Construction skill record." };
			var previousProfile = ZombieSymbiant.DebugPerfProfile;
			object restoreAction = null;
			try
			{
				_ = ZombieSymbiant.SetDebugPerfProfile("noTick");
				skill.Level = 10;
				var raw = skill.Level;
				restoreAction = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
				var patched = skill.Level;
				var bonus = Math.Max(1, Mathf.RoundToInt(ZombieSettings.Values.symbiantMaxSkillBonus * ZombieSymbiant.SymbiantBenefitFactor(host)));
				var expected = Mathf.Clamp(raw + bonus, 0, SkillRecord.MaxLevel);
				return new
				{
					success = raw == 10 && patched == expected && patched > raw,
					skill = skill.def.defName,
					raw,
					patched,
					bonus,
					expected,
					benefitFactor = ZombieSymbiant.SymbiantBenefitFactor(host),
					maxSkillBonus = ZombieSettings.Values.symbiantMaxSkillBonus,
					previousProfile,
					restoreAction
				};
			}
			finally
			{
				if (ZombieSymbiant.DebugPerfProfile != previousProfile)
					_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
			}
		}

		static bool BenefitCheckHasHediff(object check)
		{
			return (bool?)check?.GetType().GetProperty("hasHediff")?.GetValue(check) == true;
		}

		static float BenefitCheckHediffSeverity(object check)
		{
			return (float?)check?.GetType().GetProperty("hediffSeverity")?.GetValue(check) ?? 0f;
		}

		static float BenefitCheckFactor(object check)
		{
			return (float?)check?.GetType().GetProperty("benefitFactor")?.GetValue(check) ?? 0f;
		}

		static bool BenefitCheckHasZombieProtection(object check)
		{
			return (bool?)check?.GetType().GetProperty("hasZombieTargetingProtection")?.GetValue(check) == true;
		}

		static int FindSurgeryOutcomeSeed(float chance, bool desiredOutcome)
		{
			for (var seed = 1; seed < 10000; seed++)
			{
				Rand.PushState(seed);
				bool outcome;
				try
				{
					outcome = Rand.Chance(chance);
				}
				finally
				{
					Rand.PopState();
				}
				if (outcome == desiredOutcome)
					return seed;
			}
			throw new InvalidOperationException($"Could not find deterministic surgery seed for chance {chance:0.000} and desired outcome {desiredOutcome}.");
		}

		static object DescribeSymbiantSeveranceRecipeIngredients()
		{
			var recipe = CustomDefs.SeverSymbiantSymbiosis;
			var filter = recipe?.fixedIngredientFilter;
			var allowsCoagulant = filter?.Allows(CustomDefs.SymbiantCoagulantPack) == true;
			var allowsMedicine = filter?.Allows(ThingDefOf.MedicineIndustrial) == true;
			var ingredientCount = recipe?.ingredients?.Count ?? 0;
			var hasCoagulantIngredient = recipe?.ingredients?.Any(ingredient => ingredient.filter.Allows(CustomDefs.SymbiantCoagulantPack) && Mathf.Approximately(ingredient.GetBaseCount(), 1f)) == true;
			var hasMedicineIngredient = recipe?.ingredients?.Any(ingredient => ingredient.filter.Allows(ThingDefOf.MedicineIndustrial) && Mathf.Approximately(ingredient.GetBaseCount(), 1f)) == true;
			return new
			{
				success = recipe != null
					&& recipe.workerClass == typeof(Recipe_SeverSymbiantSymbiosis)
					&& recipe.targetsBodyPart
					&& ingredientCount == 2
					&& allowsCoagulant
					&& allowsMedicine
					&& hasCoagulantIngredient
					&& hasMedicineIngredient,
				recipe = recipe?.defName,
				workerClass = recipe?.workerClass?.FullName,
				targetsBodyPart = recipe?.targetsBodyPart ?? false,
				ingredientCount,
				allowsCoagulant,
				allowsMedicine,
				hasCoagulantIngredient,
				hasMedicineIngredient
			};
		}

		[Tool("zombieland/symbiant_unsafe_damage_contract", Description = "Verify unsafe symbiant damage reserve absorption, host trauma overflow, uncontrolled destruction, and host-death collapse.")]
		public static object SymbiantUnsafeDamageContract(
			[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			object reserveAbsorption = null;
			object overflowTrauma = null;
			object uncontrolledDestroyNoReserve = null;
			object hostDeathCollapse = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantCoagulantPotency = SymbiantCoagulantPotency.Expensive;
					settings.symbiantDecouplingFeedPulsesPerDay = 20;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});
				reserveAbsorption = RunSymbiantUnsafeDamageScenario(map, "reserveAbsorption", cleanup);
				overflowTrauma = RunSymbiantUnsafeDamageScenario(map, "overflowTrauma", cleanup);
				uncontrolledDestroyNoReserve = RunSymbiantUnsafeDamageScenario(map, "uncontrolledDestroyNoReserve", cleanup);
				hostDeathCollapse = RunSymbiantUnsafeDamageScenario(map, "hostDeathCollapse", cleanup);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var patchTargets = new
			{
				preApplyDamage = PatchedMethodsForPatchClass("Pawn_HealthTracker_PreApplyDamage_Patch"),
				pawnKill = PatchedMethodsForPatchClass("Pawn_Kill_Patch")
			};
			var success = error == null
				&& patchTargets.preApplyDamage.Length > 0
				&& patchTargets.pawnKill.Length > 0
				&& ScenarioSucceeded(reserveAbsorption)
				&& ScenarioSucceeded(overflowTrauma)
				&& ScenarioSucceeded(uncontrolledDestroyNoReserve)
				&& ScenarioSucceeded(hostDeathCollapse)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "Pawn_HealthTracker.PreApplyDamage -> ZombieSymbiant.PreApplyLinkedDamage; Pawn.Kill -> ZombieSymbiant.NotifyHostKilled",
				error,
				patchTargets,
				reserveAbsorption,
				overflowTrauma,
				uncontrolledDestroyNoReserve,
				hostDeathCollapse,
				cleanup = new
				{
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantUnsafeDamageScenario(Map map, string scenario, bool cleanup)
		{
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object prepare = null;
			object action;

			try
			{
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				if (scenario == "reserveAbsorption" || scenario == "overflowTrauma")
					prepare = PrepareSymbiantForSafeSeverance(map, fixture, symbiant);

				var hostInjuryBefore = TotalInjurySeverity(host);
				var reserveBefore = symbiant.DecouplingReserve;
				if (scenario == "reserveAbsorption")
				{
					var damage = Mathf.Min(5f, Mathf.Max(1f, symbiant.EffectiveDecouplingReserve));
					var dinfo = new DamageInfo(DamageDefOf.Cut, damage, 0f, -1f, null);
					symbiant.PreApplyLinkedDamage(ref dinfo);
					var hostInjuryAfter = TotalInjurySeverity(host);
					action = new
					{
						damage,
						remainingDamage = dinfo.Amount,
						reserveBefore,
						reserveAfter = symbiant.DecouplingReserve,
						hostInjuryBefore,
						hostInjuryAfter,
						success = Mathf.Approximately(dinfo.Amount, 0f)
							&& Mathf.Approximately(reserveBefore - symbiant.DecouplingReserve, damage)
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& symbiant.Destroyed == false
							&& host.Dead == false
					};
				}
				else if (scenario == "overflowTrauma")
				{
					var overflow = 8f;
					var damage = symbiant.EffectiveDecouplingReserve + overflow;
					var dinfo = new DamageInfo(DamageDefOf.Cut, damage, 0f, -1f, null);
					symbiant.PreApplyLinkedDamage(ref dinfo);
					var hostInjuryAfter = TotalInjurySeverity(host);
					action = new
					{
						damage,
						overflow,
						remainingDamage = dinfo.Amount,
						reserveBefore,
						reserveAfter = symbiant.DecouplingReserve,
						hostInjuryBefore,
						hostInjuryAfter,
						success = Mathf.Approximately(dinfo.Amount, overflow)
							&& Mathf.Approximately(symbiant.DecouplingReserve, 0f)
							&& hostInjuryAfter > hostInjuryBefore
							&& symbiant.Destroyed == false
							&& host.Dead == false
					};
				}
				else if (scenario == "uncontrolledDestroyNoReserve")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					symbiant.Destroy(DestroyMode.Vanish);
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					action = new
					{
						hediffBefore,
						hediffAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						success = hediffBefore && hediffAfter == false && host.Dead && symbiant.Destroyed && linkedAfter == null
					};
					symbiant = null;
				}
				else if (scenario == "hostDeathCollapse")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					host.Kill(null);
					var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					action = new
					{
						hediffBefore,
						hediffAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
						success = hediffBefore && hediffAfter == false && host.Dead && symbiant.Destroyed && activeAfter == null
					};
					symbiant = null;
				}
				else
					action = new { success = false, error = $"Unknown unsafe-damage scenario '{scenario}'." };

				return new
				{
					success = ScenarioSucceeded(action),
					scenario,
					fixtureSetup,
					prepare,
					action
				};
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				_ = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			}
		}

		[Tool("zombieland/symbiant_expansion_contract", Description = "Build a reversible two-room fixture and verify symbiant expansion into room cells, under a closed door, and through one constructed wall.")]
		public static object SymbiantExpansionContract(
			[ToolParameter(Description = "Destroy the temporary symbiant and two-room fixture after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			if (TrySetupSymbiantExpansionFixture(map, out var fixture, out var fixtureError) == false)
				return fixtureError;
			var fixtureDescription = DescribeSymbiantExpansionFixture(fixture);

			ZombieSymbiant symbiant = null;
			object spawnError = null;
			try
			{
				ZombieSymbiant.Spawn(map, fixture.spawnCell);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
			}
			catch (Exception ex)
			{
				spawnError = ex.ToString();
			}

			var openBefore = symbiant?.AbsoluteCells.ToHashSet() ?? new HashSet<IntVec3>();
			var openPulse = symbiant?.TryExpansionPulse() == true;
			var openNewCell = symbiant?.AbsoluteCells.FirstOrDefault(cell => openBefore.Contains(cell) == false) ?? IntVec3.Invalid;

			var leftFillAdded = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells);
			var doorBeforeDestroyed = fixture.door.Destroyed;
			var doorPulse = symbiant?.TryExpansionPulse() == true;
			var doorOccupied = symbiant?.ContainsCell(fixture.doorCell) == true;
			var doorAfterDestroyed = fixture.door.Destroyed;

			var rightInteriorBefore = fixture.rightInterior.Cells.Any(cell => symbiant?.ContainsCell(cell) == true);
			var dividerBefore = fixture.dividerWalls
				.Select(wall => new { cell = ZombieRuntimeActions.DescribeCell(wall.Position), destroyed = wall.Destroyed })
				.ToArray();
			var breachPulse = symbiant?.TryExpansionPulse() == true;
			var breachedCell = fixture.dividerWalls
				.Select(wall => wall.Position)
				.FirstOrDefault(cell => symbiant?.ContainsCell(cell) == true);
			var breachedWallGone = breachedCell.IsValid && breachedCell.GetEdifice(map) == null;
			var rightFillAdded = ZombieSymbiant.AddCells(map, fixture.rightInterior.Cells);

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);

			var success = spawnError == null
				&& symbiant != null
				&& openPulse
				&& openNewCell.IsValid
				&& fixture.leftInterior.Contains(openNewCell)
				&& leftFillAdded > 0
				&& doorPulse
				&& doorOccupied
				&& doorBeforeDestroyed == false
				&& doorAfterDestroyed == false
				&& rightInteriorBefore == false
				&& breachPulse
				&& breachedCell.IsValid
				&& breachedWallGone
				&& rightFillAdded > 0
				&& activeAfterCleanup == null;

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TryExpansionPulse -> FindExpansionTarget -> room/door target or BreakableConstructedWall",
				spawnError,
				fixture = fixtureDescription,
				spawned = symbiant == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					destroyed = symbiant.Destroyed,
					cellCount = symbiant.Destroyed ? 0 : symbiant.CellCount
				},
				openExpansion = new
				{
					pulse = openPulse,
					newCell = openNewCell.IsValid ? ZombieRuntimeActions.DescribeCell(openNewCell) : null,
					inLeftInterior = openNewCell.IsValid && fixture.leftInterior.Contains(openNewCell)
				},
				doorExpansion = new
				{
					leftFillAdded,
					pulse = doorPulse,
					doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
					occupied = doorOccupied,
					doorBeforeDestroyed,
					doorAfterDestroyed
				},
				wallBreach = new
				{
					rightInteriorBefore,
					dividerBefore,
					pulse = breachPulse,
					breachedCell = breachedCell.IsValid ? ZombieRuntimeActions.DescribeCell(breachedCell) : null,
					breachedWallGone,
					rightFillAdded
				},
				letters,
				cleanup = cleanupResult,
				letterCleanup,
				fixtureCleanup,
				activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
			};
		}

		sealed class SymbiantExpansionFixture
		{
			public CellRect fixtureRect;
			public CellRect leftInterior;
			public CellRect rightInterior;
			public IntVec3 spawnCell;
			public IntVec3 doorCell;
			public Building_Door door;
			public readonly List<Building> buildings = new();
			public readonly List<Building> dividerWalls = new();
			public readonly Dictionary<IntVec3, bool> originalHome = new();
			public readonly Dictionary<IntVec3, RoofDef> originalRoof = new();
		}

		static bool TrySetupSymbiantExpansionFixture(Map map, out SymbiantExpansionFixture fixture, out object error)
		{
			fixture = null;
			error = null;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindSymbiantExpansionFixtureRoot(map, root, 56f, out var center, out error) == false)
				return false;

			var leftInterior = CellRect.FromLimits(center.x - 5, center.z - 2, center.x - 1, center.z + 2);
			var rightInterior = CellRect.FromLimits(center.x + 1, center.z - 2, center.x + 5, center.z + 2);
			var fixtureRect = CellRect.FromLimits(center.x - 6, center.z - 3, center.x + 6, center.z + 3).ClipInsideMap(map);
			var doorCell = new IntVec3(center.x - 3, 0, center.z - 3);
			fixture = new SymbiantExpansionFixture
			{
				fixtureRect = fixtureRect,
				leftInterior = leftInterior,
				rightInterior = rightInterior,
				spawnCell = leftInterior.CenterCell,
				doorCell = doorCell
			};

			foreach (var cell in fixtureRect.Cells)
			{
				fixture.originalHome[cell] = map.areaManager.Home[cell];
				fixture.originalRoof[cell] = map.roofGrid.RoofAt(cell);
				map.areaManager.Home[cell] = true;
				map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
			}

			var wallDef = ThingDefOf.Wall;
			var doorDef = ThingDefOf.Door;
			var stuffDef = ThingDefOf.WoodLog;
			for (var x = fixtureRect.minX; x <= fixtureRect.maxX; x++)
				for (var z = fixtureRect.minZ; z <= fixtureRect.maxZ; z++)
				{
					var cell = new IntVec3(x, 0, z);
					var edge = x == fixtureRect.minX || x == fixtureRect.maxX || z == fixtureRect.minZ || z == fixtureRect.maxZ;
					var divider = x == center.x && z >= center.z - 2 && z <= center.z + 2;
					if (edge == false && divider == false)
						continue;
					if (cell == doorCell)
					{
						var door = ThingMaker.MakeThing(doorDef, stuffDef) as Building_Door;
						if (door == null)
						{
							error = new { success = false, error = "Could not create symbiant expansion fixture door." };
							return false;
						}
						GenSpawn.Spawn(door, cell, map, WipeMode.Vanish);
						door.SetFaction(Faction.OfPlayer);
						fixture.door = door;
						fixture.buildings.Add(door);
						continue;
					}

					var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
					if (wall == null)
						continue;
					GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					fixture.buildings.Add(wall);
					if (divider)
						fixture.dividerWalls.Add(wall);
				}

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var leftRoom = fixture.spawnCell.GetRoom(map);
			var rightRoom = rightInterior.CenterCell.GetRoom(map);
			if (leftRoom == null || rightRoom == null || leftRoom == rightRoom || leftRoom.ProperRoom == false || rightRoom.ProperRoom == false || leftRoom.UsesOutdoorTemperature || rightRoom.UsesOutdoorTemperature)
			{
				error = new
				{
					success = false,
					leftRoom = DescribeRoom(leftRoom),
					rightRoom = DescribeRoom(rightRoom),
					error = "The symbiant expansion fixture did not produce two distinct proper indoor rooms."
				};
				return false;
			}
			return true;
		}

		static bool TryFindSymbiantExpansionFixtureRoot(Map map, IntVec3 root, float radius, out IntVec3 center, out object error)
		{
			center = IntVec3.Invalid;
			error = null;
			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var rect = CellRect.FromLimits(candidate.x - 6, candidate.z - 3, candidate.x + 6, candidate.z + 3);
				if (rect.InBounds(map) == false)
					continue;
				var clear = true;
				foreach (var cell in rect.Cells)
				{
					if (cell.Fogged(map)
						|| cell.Standable(map) == false
						|| cell.GetEdifice(map) != null
						|| cell.GetFirstThing<Mineable>(map) != null
						|| cell.GetThingList(map).Any(thing => thing is Pawn))
					{
						clear = false;
						break;
					}
				}
				if (clear)
				{
					center = candidate;
					return true;
				}
			}
			error = new
			{
				success = false,
				error = $"No clear symbiant expansion fixture area was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static object DescribeSymbiantExpansionFixture(SymbiantExpansionFixture fixture)
		{
			if (fixture == null)
				return null;
			var map = fixture.door?.Map;
			return new
			{
				fixtureRect = ZombieRuntimeActions.DescribeCellRect(fixture.fixtureRect),
				leftInterior = ZombieRuntimeActions.DescribeCellRect(fixture.leftInterior),
				rightInterior = ZombieRuntimeActions.DescribeCellRect(fixture.rightInterior),
				spawnCell = ZombieRuntimeActions.DescribeCell(fixture.spawnCell),
				doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
				leftRoom = map == null ? null : DescribeRoom(fixture.spawnCell.GetRoom(map)),
				rightRoom = map == null ? null : DescribeRoom(fixture.rightInterior.CenterCell.GetRoom(map)),
				dividerWallCells = fixture.dividerWalls.Select(wall => ZombieRuntimeActions.DescribeCell(wall.Position)).ToArray()
			};
		}

		static object DescribeRoom(Room room)
		{
			if (room == null)
				return null;
			return new
			{
				role = room.Role?.defName,
				roleLabel = room.Role?.LabelCap.ToString(),
				cellCount = room.CellCount,
				isHuge = room.IsHuge,
				properRoom = room.ProperRoom,
				usesOutdoorTemperature = room.UsesOutdoorTemperature
			};
		}

		static object CleanupSymbiantExpansionFixture(Map map, SymbiantExpansionFixture fixture, bool cleanup)
		{
			if (fixture == null)
				return new { removed = 0, restoredCells = 0, skipped = cleanup == false };
			if (cleanup == false)
				return new { removed = 0, restoredCells = 0, skipped = true };

			var removed = 0;
			foreach (var thing in fixture.buildings.Where(thing => thing != null).ToArray())
			{
				if (thing.Destroyed)
					continue;
				thing.Destroy(DestroyMode.Vanish);
				removed++;
			}
			foreach (var pair in fixture.originalHome)
				map.areaManager.Home[pair.Key] = pair.Value;
			foreach (var pair in fixture.originalRoof)
				map.roofGrid.SetRoof(pair.Key, pair.Value);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return new { removed, restoredCells = fixture.originalHome.Count, skipped = false };
		}

		[Tool("zombieland/symbiant_infestation_state", Description = "Inspect or exercise the zombie symbiant state with spawn, expand, feedCoagulant, removeHostHediff, and stress modes.")]
		public static object SymbiantInfestationState(
			[ToolParameter(Description = "Mode: read, spawn, expand, feedCoagulant, removeHostHediff, stress.", Required = false, DefaultValue = "read")] string mode = "read",
			[ToolParameter(Description = "Target x coordinate for spawn/stress. Use -1 with z -1 for automatic placement.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate for spawn/stress. Use -1 with x -1 for automatic placement.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Number of expansion pulses or stress cells.", Required = false, DefaultValue = 1)] int count = 1,
			[ToolParameter(Description = "Bridge-only debug performance profile: default, inert, renderOnly, pathOnly, symbiosisOnly, noRender, noPath, noCellStats, or noTick.", Required = false, DefaultValue = "")] string perfProfile = "",
			[ToolParameter(Description = "Bridge-only max-cell override for stress testing. Use 0 to keep normal settings.", Required = false, DefaultValue = 0)] int maxCellsOverride = 0)
		{
			object perfAction = null;
			if (perfProfile.NullOrEmpty() == false)
				perfAction = ZombieSymbiant.SetDebugPerfProfile(perfProfile);
			object maxCellsOverrideAction = null;
			if (maxCellsOverride >= 0)
				maxCellsOverrideAction = ZombieSymbiant.SetDebugMaxCellsOverride(maxCellsOverride);

			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded.", perf = ZombieSymbiant.DebugPerfState(), perfAction, maxCellsOverrideAction };

			mode = (mode ?? "read").Trim();
			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			object action = null;

			if (mode.Equals("profile", StringComparison.OrdinalIgnoreCase))
				action = ZombieSymbiant.DebugPerfState();
			else if (mode.Equals("spawn", StringComparison.OrdinalIgnoreCase))
			{
				if (symbiant == null)
				{
					if (x >= 0 && z >= 0)
						ZombieSymbiant.Spawn(map, new IntVec3(x, 0, z));
					else if (ZombieSymbiant.TrySpawnInBestRoom(map) == false)
					{
						var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
						if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
							return error;
						ZombieSymbiant.Spawn(map, cell);
					}
					symbiant = ZombieSymbiant.ActiveSymbiant(map);
				}
				action = new { spawned = symbiant?.Spawned == true };
			}
			else if (mode.Equals("expand", StringComparison.OrdinalIgnoreCase))
			{
				var before = symbiant?.CellCount ?? 0;
				var pulses = 0;
				for (var i = 0; i < Math.Max(1, count); i++)
					if (symbiant?.TryExpansionPulse() == true)
						pulses++;
				action = new { before, pulses, after = symbiant?.CellCount ?? 0 };
			}
			else if (mode.Equals("feedCoagulant", StringComparison.OrdinalIgnoreCase))
			{
				var before = symbiant?.CellCount ?? 0;
				var reserveBefore = symbiant?.DecouplingReserve ?? 0f;
				var pack = ThingMaker.MakeThing(CustomDefs.SymbiantCoagulantPack);
				var fed = symbiant?.TryFeed(pack) == true;
				action = new
				{
					before,
					fed,
					reserveBefore,
					reserveAfter = symbiant?.DecouplingReserve ?? 0f,
					recessionPulseCells = symbiant?.LastRecessionPulseCells ?? 0,
					after = symbiant?.Destroyed == true ? 0 : symbiant?.CellCount ?? 0,
					feedPulsesToday = symbiant?.DecouplingFeedPulsesToday ?? 0,
					feedPulsesPerDay = symbiant?.DecouplingFeedPulsesPerDay ?? 0,
					feedPulsesRemaining = symbiant?.FeedPulsesRemaining ?? 0
				};
			}
			else if (mode.Equals("removeHostHediff", StringComparison.OrdinalIgnoreCase))
			{
				var linkedHost = symbiant?.LinkedHost;
				var hediffs = linkedHost?.health?.hediffSet?.hediffs?
					.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis)
					.ToArray() ?? Array.Empty<Hediff>();
				foreach (var hediff in hediffs)
					linkedHost.health.RemoveHediff(hediff);
				action = new
				{
					host = linkedHost == null ? null : ZombieRuntimeActions.StableThingId(linkedHost),
					removed = hediffs.Length
				};
			}
			else if (mode.Equals("stress", StringComparison.OrdinalIgnoreCase))
			{
				if (symbiant == null)
				{
					var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
						return error;
					ZombieSymbiant.Spawn(map, cell);
					symbiant = ZombieSymbiant.ActiveSymbiant(map);
				}
				var before = symbiant?.CellCount ?? 0;
				var requested = Math.Max(1, count);
				var targetBudget = Math.Max(requested, requested + before);
				var targetCells = new List<IntVec3>(targetBudget);
				var seen = new HashSet<IntVec3>();
				var stressRadius = Math.Max(30d, Math.Sqrt(requested / Math.PI) + 8d);
				foreach (var cell in GenRadial.RadialCellsAround(symbiant.Position, (float)stressRadius, true))
				{
					if (targetCells.Count >= targetBudget)
						break;
					if (cell.InBounds(map) && cell.Walkable(map) && seen.Add(cell))
						targetCells.Add(cell);
				}
				var radialCells = targetCells.Count;
				var squareRadius = Math.Max((int)Math.Ceiling(stressRadius), (int)Math.Ceiling(Math.Sqrt(requested)) / 2 + 8);
				if (targetCells.Count < targetBudget)
				{
					foreach (var cell in CellRect.CenteredOn(symbiant.Position, squareRadius).ClipInsideMap(map).Cells)
					{
						if (targetCells.Count >= targetBudget)
							break;
						if (cell.Walkable(map) && seen.Add(cell))
							targetCells.Add(cell);
					}
				}
				var squareCells = targetCells.Count - radialCells;
				var added = ZombieSymbiant.AddCells(map, targetCells);
				action = new
				{
					before,
					requested = count,
					targetBudget,
					added,
					after = symbiant?.CellCount ?? 0,
					stressRadius,
					radialCells,
					squareRadius,
					squareCells,
					targetCells = targetCells.Count,
					shape = radialCells >= targetBudget ? "circle" : "squareFill"
				};
			}

			symbiant = ZombieSymbiant.ActiveSymbiant(map);
			var host = symbiant?.LinkedHost;
			var hostSymbiosisHediff = host?.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			var room = symbiant?.Position.GetRoom(map);
			var roomDisruption = room == null ? null : new
			{
				role = room.Role?.defName,
				cellCount = ZombieSymbiant.CountCellsInRoom(room),
				beauty = room.GetStat(RoomStatDefOf.Beauty),
				impressiveness = room.GetStat(RoomStatDefOf.Impressiveness)
			};
			var playerFaction = Find.FactionManager?.AllFactionsListForReading?.FirstOrDefault(faction => faction?.def?.isPlayer == true);
			var symbiantHostileToPlayer = symbiant != null && playerFaction != null && symbiant.HostileTo(playerFaction);
			var symbiantActiveThreatToPlayer = symbiant != null && playerFaction != null && GenHostility.IsActiveThreatTo(symbiant, playerFaction, false, false);
			return new
			{
				success = true,
				mode,
				action,
				perf = ZombieSymbiant.DebugPerfState(),
				perfAction,
				maxCellsOverrideAction,
				symbiant = symbiant == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					position = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					drawSize = new { x = symbiant.DrawSize.x, z = symbiant.DrawSize.y },
					occupiedDrawRect = ZombieRuntimeActions.DescribeCellRect(symbiant.OccupiedDrawRect()),
					renderWorldSize = new { x = symbiant.RenderWorldSize.x, z = symbiant.RenderWorldSize.y },
					renderTextureSize = new { x = symbiant.RenderTextureWidth, y = symbiant.RenderTextureHeight },
					renderShader = symbiant.RenderShaderName,
					renderUsesSymbiantShader = symbiant.RenderUsesSymbiantShader,
					renderOpacity = new
					{
						min = ZombieSymbiant.RenderOpacityMin,
						max = ZombieSymbiant.RenderOpacityMax,
						noiseScale = ZombieSymbiant.RenderNoiseScale,
						wavePhaseSpeed = ZombieSymbiant.RenderWavePhaseSpeed,
						waveShadeStrength = ZombieSymbiant.RenderWaveShadeStrength,
						edgeContrast = ZombieSymbiant.RenderEdgeContrast,
						noiseTimeSeconds = ZombieSymbiant.RenderNoiseTimeSeconds
					},
					pawnSystems = new
					{
						registeredInMapPawnLists = symbiant.RegisteredInMapPawnLists,
						hostileToPlayer = symbiantHostileToPlayer,
						activeThreatToPlayer = symbiantActiveThreatToPlayer,
						faction = symbiant.Faction?.def?.defName,
						kindIsFighter = symbiant.kindDef?.isFighter ?? false,
						combatPower = symbiant.kindDef?.combatPower ?? 0f
					},
					cellCount = symbiant.CellCount,
					maxCells = ZombieSymbiant.MaxCells,
					technicalMaxCells = ZombieSymbiant.MAX_METABALLS,
					debugMaxCellsOverride = ZombieSymbiant.DebugMaxCellsOverride,
					capped = symbiant.CellCount >= ZombieSymbiant.MaxCells,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						infectionState = host.InfectionState().ToString(),
						hasSymbiosisHediff = hostSymbiosisHediff != null,
						symbiosisHediffSeverity = hostSymbiosisHediff?.Severity ?? 0f
					},
					hostThingId = symbiant.HostThingId,
					eligibleColonyRoomCells = symbiant.EligibleColonyRoomCells,
					fullBenefitCells = symbiant.FullBenefitCells,
					integratedVisibleCells = symbiant.IntegratedVisibleCells,
					peakVisibleCells = symbiant.PeakVisibleCells,
					peakIntegratedVisibleCells = symbiant.PeakIntegratedVisibleCells,
					peakBenefitFactor = symbiant.PeakBenefitFactor,
					benefitFactor = symbiant.BenefitFactor,
					zombieIgnoreMinBenefit = ZombieSymbiant.ZombieIgnoreMinBenefit,
					hasZombieTargetingProtection = ZombieSymbiant.HasZombieTargetingProtection(host),
					severanceMaturityCells = symbiant.SeveranceMaturityCells,
					hasMaturedForSeverance = symbiant.HasMaturedForSeverance,
					decouplingReserve = symbiant.DecouplingReserve,
					decouplingReserveMax = symbiant.DecouplingReserveMax,
					severanceReserveRequired = symbiant.SeveranceReserveRequired,
					reserveMaturityFactor = symbiant.ReserveMaturityFactor,
					effectiveDecouplingReserve = symbiant.EffectiveDecouplingReserve,
					safeVisibleMinimum = symbiant.SafeVisibleMinimum,
					canSafelySever = symbiant.CanSafelySever,
					feedPulsesToday = symbiant.DecouplingFeedPulsesToday,
					feedPulsesPerDay = symbiant.DecouplingFeedPulsesPerDay,
					feedPulsesRemaining = symbiant.FeedPulsesRemaining,
					feedRequested = symbiant.FeedRequested,
					nextExpansionTick = symbiant.NextExpansionTick,
					feedPausedUntilTick = symbiant.FeedPausedUntilTick,
					lastRecessionPulseCells = symbiant.LastRecessionPulseCells,
					cancelNextBreach = symbiant.CancelNextBreach,
					roomDisruption,
					sampleCells = symbiant.AbsoluteCells.Take(24).Select(ZombieRuntimeActions.DescribeCell).ToArray()
				},
				settings = new
				{
					ZombieSettings.Values.symbiantEnabled,
					ZombieSettings.Values.symbiantExpansionIntervalHours,
					ZombieSettings.Values.symbiantPostFeedPauseHours,
					ZombieSettings.Values.symbiantMaxCells,
					ZombieSettings.Values.symbiantFullBenefitRoomCoverage,
					ZombieSettings.Values.symbiantSeveranceMaturityCoverage,
					ZombieSettings.Values.symbiantSeveranceMaturityMinCells,
					ZombieSettings.Values.symbiantSeveranceMaturityMaxCells,
					ZombieSettings.Values.symbiantSeveranceReserveCoverage,
					ZombieSettings.Values.symbiantSeveranceReserveMin,
					ZombieSettings.Values.symbiantSeveranceReserveMax,
					ZombieSettings.Values.symbiantZombieIgnoreMinBenefit,
					ZombieSettings.Values.symbiantDecouplingFeedPulsesPerDay,
					ZombieSettings.Values.symbiantMaxSkillBonus,
					ZombieSettings.Values.symbiantPathCost,
					ZombieSettings.Values.symbiantCanBreakConstructedWalls,
					symbiantCoagulantPotency = ZombieSettings.Values.symbiantCoagulantPotency.ToString()
				}
			};
		}

		static object CleanupTemporarySymbiant(Map map, ZombieSymbiant symbiant, bool cleanup)
		{
			if (symbiant == null)
				return new { requested = cleanup, cleaned = false, reason = "No temporary symbiant was spawned." };
			if (cleanup == false)
				return new { requested = false, cleaned = false, reason = "Cleanup disabled.", symbiant = ZombieRuntimeActions.StableThingId(symbiant) };
			if (symbiant.Destroyed)
				return new { requested = true, cleaned = false, reason = "Temporary symbiant was already destroyed.", symbiant = ZombieRuntimeActions.StableThingId(symbiant) };

			var id = ZombieRuntimeActions.StableThingId(symbiant);
			symbiant.DebugDestroyWithoutHostTrauma();
			_ = ZombieSymbiant.ActiveSymbiant(map);
			return new { requested = true, cleaned = symbiant.Destroyed, symbiant = id };
		}

		static object CleanupTemporaryLetters(Letter[] letters, bool cleanup)
		{
			if (cleanup == false || letters == null || letters.Length == 0 || Find.LetterStack == null)
				return new { removed = 0, skipped = cleanup == false };

			var removed = 0;
			foreach (var letter in letters)
			{
				if (letter == null)
					continue;
				Find.LetterStack.RemoveLetter(letter);
				removed++;
			}

			return new { removed, skipped = false };
		}

		static object DescribeSymbiantDiscoveryLetter(Letter letter)
		{
			if (letter == null)
				return null;

			var choice = letter as ChoiceLetter;
			return new
			{
				label = letter.Label.ToString(),
				text = choice?.Text.ToString(),
				defName = letter.def?.defName,
				arriveSound = letter.def?.arriveSound?.defName,
				color = letter.def == null ? null : DescribeColor(letter.def.color),
				letter.arrivalTick,
				lookTargetCount = letter.lookTargets?.targets?.Count ?? 0,
				lookTargets = letter.lookTargets?.targets?
					.Select(target => new
					{
						valid = target.IsValid,
						label = target.Label,
						hasThing = target.HasThing,
						thing = ZombieRuntimeActions.StableThingId(target.Thing),
						cell = target.IsMapTarget ? ZombieRuntimeActions.DescribeCell(target.Cell) : null,
						mapId = target.Map?.uniqueID
					})
					.ToArray()
			};
		}
	}
}
