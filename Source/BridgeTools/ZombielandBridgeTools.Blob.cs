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
		[Tool("zombieland/blob_infestation_state", Description = "Inspect or exercise the zombie blob symbiote state with spawn, expand, feedCoagulant, and stress modes.")]
		public static object BlobInfestationState(
			[ToolParameter(Description = "Mode: read, spawn, expand, feedCoagulant, stress.", Required = false, DefaultValue = "read")] string mode = "read",
			[ToolParameter(Description = "Target x coordinate for spawn/stress. Use -1 with z -1 for automatic placement.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate for spawn/stress. Use -1 with x -1 for automatic placement.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Number of expansion pulses or stress cells.", Required = false, DefaultValue = 1)] int count = 1,
			[ToolParameter(Description = "Bridge-only debug performance profile: default, inert, renderOnly, pathOnly, symbiosisOnly, noRender, noPath, noCellStats, or noTick.", Required = false, DefaultValue = "")] string perfProfile = "",
			[ToolParameter(Description = "Bridge-only max-cell override for stress testing. Use 0 to keep normal settings.", Required = false, DefaultValue = 0)] int maxCellsOverride = 0)
		{
			object perfAction = null;
			if (perfProfile.NullOrEmpty() == false)
				perfAction = ZombieBlob.SetDebugPerfProfile(perfProfile);
			object maxCellsOverrideAction = null;
			if (maxCellsOverride >= 0)
				maxCellsOverrideAction = ZombieBlob.SetDebugMaxCellsOverride(maxCellsOverride);

			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded.", perf = ZombieBlob.DebugPerfState(), perfAction, maxCellsOverrideAction };

			mode = (mode ?? "read").Trim();
			var blob = ZombieBlob.ActiveBlob(map);
			object action = null;

			if (mode.Equals("profile", StringComparison.OrdinalIgnoreCase))
				action = ZombieBlob.DebugPerfState();
			else if (mode.Equals("spawn", StringComparison.OrdinalIgnoreCase))
			{
				if (blob == null)
				{
					if (x >= 0 && z >= 0)
						ZombieBlob.Spawn(map, new IntVec3(x, 0, z));
					else if (ZombieBlob.TrySpawnInBestRoom(map) == false)
					{
						var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
						if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
							return error;
						ZombieBlob.Spawn(map, cell);
					}
					blob = ZombieBlob.ActiveBlob(map);
				}
				action = new { spawned = blob?.Spawned == true };
			}
			else if (mode.Equals("expand", StringComparison.OrdinalIgnoreCase))
			{
				var before = blob?.CellCount ?? 0;
				var pulses = 0;
				for (var i = 0; i < Math.Max(1, count); i++)
					if (blob?.TryExpansionPulse() == true)
						pulses++;
				action = new { before, pulses, after = blob?.CellCount ?? 0 };
			}
			else if (mode.Equals("feedCoagulant", StringComparison.OrdinalIgnoreCase))
			{
				var before = blob?.CellCount ?? 0;
				var reserveBefore = blob?.DecouplingReserve ?? 0f;
				var pack = ThingMaker.MakeThing(CustomDefs.BlobCoagulantPack);
				var fed = blob?.TryFeed(pack) == true;
				action = new
				{
					before,
					fed,
					reserveBefore,
					reserveAfter = blob?.DecouplingReserve ?? 0f,
					recessionPulseCells = blob?.LastRecessionPulseCells ?? 0,
					after = blob?.Destroyed == true ? 0 : blob?.CellCount ?? 0,
					feedPulsesToday = blob?.DecouplingFeedPulsesToday ?? 0,
					feedPulsesPerDay = blob?.DecouplingFeedPulsesPerDay ?? 0,
					feedPulsesRemaining = blob?.FeedPulsesRemaining ?? 0
				};
			}
			else if (mode.Equals("stress", StringComparison.OrdinalIgnoreCase))
			{
				if (blob == null)
				{
					var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
						return error;
					ZombieBlob.Spawn(map, cell);
					blob = ZombieBlob.ActiveBlob(map);
				}
				var before = blob?.CellCount ?? 0;
				var requested = Math.Max(1, count);
				var targetBudget = Math.Max(requested, requested + before);
				var targetCells = new List<IntVec3>(targetBudget);
				var seen = new HashSet<IntVec3>();
				var stressRadius = Math.Max(30d, Math.Sqrt(requested / Math.PI) + 8d);
				foreach (var cell in GenRadial.RadialCellsAround(blob.Position, (float)stressRadius, true))
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
					foreach (var cell in CellRect.CenteredOn(blob.Position, squareRadius).ClipInsideMap(map).Cells)
					{
						if (targetCells.Count >= targetBudget)
							break;
						if (cell.Walkable(map) && seen.Add(cell))
							targetCells.Add(cell);
					}
				}
				var squareCells = targetCells.Count - radialCells;
				var added = ZombieBlob.AddCells(map, targetCells);
				action = new
				{
					before,
					requested = count,
					targetBudget,
					added,
					after = blob?.CellCount ?? 0,
					stressRadius,
					radialCells,
					squareRadius,
					squareCells,
					targetCells = targetCells.Count,
					shape = radialCells >= targetBudget ? "circle" : "squareFill"
				};
			}

			blob = ZombieBlob.ActiveBlob(map);
			var host = blob?.LinkedHost;
			var room = blob?.Position.GetRoom(map);
			var roomDisruption = room == null ? null : new
			{
				role = room.Role?.defName,
				cellCount = ZombieBlob.CountCellsInRoom(room),
				beauty = room.GetStat(RoomStatDefOf.Beauty),
				impressiveness = room.GetStat(RoomStatDefOf.Impressiveness)
			};
			return new
			{
				success = true,
				mode,
				action,
				perf = ZombieBlob.DebugPerfState(),
				perfAction,
				maxCellsOverrideAction,
				blob = blob == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(blob),
					position = ZombieRuntimeActions.DescribeCell(blob.Position),
					drawSize = new { x = blob.DrawSize.x, z = blob.DrawSize.y },
					occupiedDrawRect = ZombieRuntimeActions.DescribeCellRect(blob.OccupiedDrawRect()),
					renderWorldSize = new { x = blob.RenderWorldSize.x, z = blob.RenderWorldSize.y },
					renderTextureSize = new { x = blob.RenderTextureWidth, y = blob.RenderTextureHeight },
					renderShader = blob.RenderShaderName,
					renderUsesBlobShader = blob.RenderUsesBlobShader,
					renderOpacity = new
					{
						min = ZombieBlob.RenderOpacityMin,
						max = ZombieBlob.RenderOpacityMax,
						noiseScale = ZombieBlob.RenderNoiseScale,
						noiseDrift = ZombieBlob.RenderNoiseDrift,
						noiseTickScale = ZombieBlob.RenderNoiseTickScale
					},
					cellCount = blob.CellCount,
					maxCells = ZombieBlob.MaxCells,
					technicalMaxCells = ZombieBlob.MAX_METABALLS,
					debugMaxCellsOverride = ZombieBlob.DebugMaxCellsOverride,
					capped = blob.CellCount >= ZombieBlob.MaxCells,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						infectionState = host.InfectionState().ToString(),
						hasSymbiosisHediff = host.health?.hediffSet?.HasHediff(CustomDefs.BlobSymbiosis) ?? false
					},
					hostThingId = blob.HostThingId,
					eligibleColonyRoomCells = blob.EligibleColonyRoomCells,
					fullBenefitCells = blob.FullBenefitCells,
					integratedVisibleCells = blob.IntegratedVisibleCells,
					peakVisibleCells = blob.PeakVisibleCells,
					peakIntegratedVisibleCells = blob.PeakIntegratedVisibleCells,
					peakBenefitFactor = blob.PeakBenefitFactor,
					benefitFactor = blob.BenefitFactor,
					zombieIgnoreMinBenefit = ZombieBlob.ZombieIgnoreMinBenefit,
					hasZombieTargetingProtection = ZombieBlob.HasZombieTargetingProtection(host),
					severanceMaturityCells = blob.SeveranceMaturityCells,
					hasMaturedForSeverance = blob.HasMaturedForSeverance,
					decouplingReserve = blob.DecouplingReserve,
					decouplingReserveMax = blob.DecouplingReserveMax,
					severanceReserveRequired = blob.SeveranceReserveRequired,
					reserveMaturityFactor = blob.ReserveMaturityFactor,
					effectiveDecouplingReserve = blob.EffectiveDecouplingReserve,
					safeVisibleMinimum = blob.SafeVisibleMinimum,
					canSafelySever = blob.CanSafelySever,
					feedPulsesToday = blob.DecouplingFeedPulsesToday,
					feedPulsesPerDay = blob.DecouplingFeedPulsesPerDay,
					feedPulsesRemaining = blob.FeedPulsesRemaining,
					feedRequested = blob.FeedRequested,
					nextExpansionTick = blob.NextExpansionTick,
					feedPausedUntilTick = blob.FeedPausedUntilTick,
					lastRecessionPulseCells = blob.LastRecessionPulseCells,
					cancelNextBreach = blob.CancelNextBreach,
					roomDisruption,
					sampleCells = blob.AbsoluteCells.Take(24).Select(ZombieRuntimeActions.DescribeCell).ToArray()
				},
				settings = new
				{
					ZombieSettings.Values.blobEnabled,
					ZombieSettings.Values.blobExpansionIntervalHours,
					ZombieSettings.Values.blobPostFeedPauseHours,
					ZombieSettings.Values.blobMaxCells,
					ZombieSettings.Values.blobFullBenefitRoomCoverage,
					ZombieSettings.Values.blobSeveranceMaturityCoverage,
					ZombieSettings.Values.blobSeveranceMaturityMinCells,
					ZombieSettings.Values.blobSeveranceMaturityMaxCells,
					ZombieSettings.Values.blobSeveranceReserveCoverage,
					ZombieSettings.Values.blobSeveranceReserveMin,
					ZombieSettings.Values.blobSeveranceReserveMax,
					ZombieSettings.Values.blobZombieIgnoreMinBenefit,
					ZombieSettings.Values.blobDecouplingFeedPulsesPerDay,
					ZombieSettings.Values.blobSymbioteMaxSkillBonus,
					ZombieSettings.Values.blobPathCost,
					ZombieSettings.Values.blobCanBreakConstructedWalls,
					blobCoagulantPotency = ZombieSettings.Values.blobCoagulantPotency.ToString()
				}
			};
		}
	}
}
