using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

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
			var defOk = CustomDefs.SymbiantConnection != null
				&& CustomDefs.SymbiantConnected != null
				&& CustomDefs.SymbiantDisconnected != null
				&& CustomDefs.SymbiantConnection.arriveSound == CustomDefs.SymbiantConnected;
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
