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
		[Tool("zombieland/symbiant_infestation_state", Description = "Inspect or exercise the zombie symbiant state with spawn, expand, feedCoagulant, and stress modes.")]
		public static object SymbiantInfestationState(
			[ToolParameter(Description = "Mode: read, spawn, expand, feedCoagulant, stress.", Required = false, DefaultValue = "read")] string mode = "read",
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
						hasSymbiosisHediff = host.health?.hediffSet?.HasHediff(CustomDefs.SymbiantSymbiosis) ?? false
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
	}
}
