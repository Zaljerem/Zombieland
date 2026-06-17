using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class ZombieSymbiant : Pawn
	{
		public const int MAX_METABALLS = 4000;
		static readonly Color color = new(0, 0.8f, 0);
		static readonly float elementPower = 1f;
		static readonly float elementRadius = 0.011f;
		static readonly float[] elementSizes = [2.5f, 2.4f, 1.6f, 1.2f, 1f, 0.9f, 0.9f, 1f, 1f];
		static readonly HashSet<ZombieSymbiant> renderResourceOwners = [];
		static readonly Dictionary<Map, ZombieSymbiant> activeSymbiantByMap = [];
		static readonly HashSet<Map> mapsWithoutActiveSymbiant = [];
		internal static string DebugPerfProfile { get; private set; } = "default";
		internal static bool DebugDisableRendering { get; private set; }
		internal static bool DebugDisableSymbiantTick { get; private set; }
		internal static bool DebugDisablePathCost { get; private set; }
		internal static bool DebugDisableCellStatEffects { get; private set; }
		internal static bool DebugDisableHostHediffSync { get; private set; }
		internal static bool DebugDisableSymbiosisBenefits { get; private set; }
		internal static int DebugMaxCellsOverride { get; private set; }
		const int MetaballTextureMinSize = 256;
		const int MetaballTextureMaxSize = 1024;
		const float MetaballTexturePixelsPerCell = 6f;
		const float MetaballInfluenceRadiusCells = 4.5f;
		const float MetaballCellRadiusFactor = 0.45f;
		const float MetaballCellRadiusMin = 0.55f;
		const float MetaballCellRadiusMax = 0.95f;
		const float MetaballAlphaStart = 0.45f;
		const float MetaballAlphaFull = 1.20f;
		const float MetaballMaxAlpha = 0.40f;
		const float MetaballEdgeStart = 0.45f;
		const float MetaballEdgeFull = 1.80f;
		const float SymbiantOpacityMin = 0.42f;
		const float SymbiantOpacityMax = 0.76f;
		const float SymbiantNoiseScale = 2.00f;
		const float SymbiantWavePhaseSpeed = 0.45f;
		const float SymbiantWaveShadeStrength = 0.68f;
		const float SymbiantEdgeContrast = 0.95f;
		const float SymbiantNormalTicksPerSecond = 60f;
		const float SymbiantRenderAltitudeOffset = -0.25f;
		const int SymbiosisMetricRefreshInterval = 250;
		static int UprootedRelocationGraceTicks => GenDate.TicksPerHour * 4;
		const float UprootedIntegratedCellThreshold = 0.01f;
		static readonly int SymbiantOpacityMinId = Shader.PropertyToID("_SymbiantOpacityMin");
		static readonly int SymbiantOpacityMaxId = Shader.PropertyToID("_SymbiantOpacityMax");
		static readonly int SymbiantNoiseScaleId = Shader.PropertyToID("_SymbiantNoiseScale");
		static readonly int SymbiantWavePhaseSpeedId = Shader.PropertyToID("_SymbiantFlowSpeed");
		static readonly int SymbiantWaveShadeStrengthId = Shader.PropertyToID("_SymbiantWaveShadeStrength");
		static readonly int SymbiantEdgeContrastId = Shader.PropertyToID("_SymbiantEdgeContrast");
		static readonly int SymbiantNoiseTimeId = Shader.PropertyToID("_SymbiantNoiseTime");
		// static readonly float[] elementSizes = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];

		HashSet<IntVec3> cells = [];
		List<IntVec3> orderedCells = [];
		readonly Dictionary<IntVec3, float> metaballRadiusByCell = [];
		Texture2D metaballTexture;

		Mesh mesh = null;
		Material metaballMaterial;

		float radius, power, centerX, centerZ, renderMinX, renderMinZ, renderWidth = 1f, renderHeight = 1f;
		Vector2 drawCullSize = Vector2.one;
		int nextExpansionTick;
		int feedPausedUntilTick;
		int lastSymbiantTick = -1;
		int lastRecessionPulseCells;
		int relocationCellDebt;
		int nextRelocationPulseTick;
		int uprootedSinceTick = -1;
		bool cancelNextBreach;
		bool feedRequested;
		Pawn host;
		string hostThingId;
		float decouplingReserve;
		int decouplingFeedDay = -1;
		int decouplingFeedPulsesToday;
		int peakVisibleCells;
		float peakIntegratedVisibleCells;
		float peakBenefitFactor;
		bool maturedForSeverance;
		bool safeSeveranceInProgress;
		bool hostCollapseInProgress;
		bool uncontrolledDestroyHandled;
		int lastSymbiosisMetricTick = int.MinValue;
		int lastRejectedDamageMessageTick = int.MinValue;
		int cachedEligibleColonyRoomCells;
		int cachedFullBenefitCells = 20;
		float cachedIntegratedVisibleCells;
		float cachedBenefitFactor;
		int cachedSeveranceMaturityCells = 10;
		int cachedSeveranceReserveRequired = 12;
		CellRect relativeCellBounds;
		bool hasCellBounds;

		public int CellCount => cells?.Count ?? 0;
		public int NextExpansionTick => nextExpansionTick;
		public int FeedPausedUntilTick => feedPausedUntilTick;
		public int LastRecessionPulseCells => lastRecessionPulseCells;
		public int RelocationCellDebt => relocationCellDebt;
		public int NextRelocationPulseTick => nextRelocationPulseTick;
		public int UprootedSinceTick => uprootedSinceTick;
		public bool CancelNextBreach => cancelNextBreach;
		public bool FeedRequested => feedRequested;
		public IEnumerable<IntVec3> AbsoluteCells => orderedCells.Select(cell => Position + cell);
		CellRect AbsoluteCellBounds => relativeCellBounds.MovedBy(Position);
		public override Vector2 DrawSize => hasCellBounds ? drawCullSize : base.DrawSize;
		public int RenderTextureWidth => metaballTexture?.width ?? 0;
		public int RenderTextureHeight => metaballTexture?.height ?? 0;
		public Vector2 RenderWorldSize => new(renderWidth, renderHeight);
		public string RenderShaderName => metaballMaterial?.shader?.name;
		public bool RenderUsesSymbiantShader => Assets.ZombieSymbiantShader != null && metaballMaterial?.shader == Assets.ZombieSymbiantShader;
		public bool RegisteredInMapPawnLists => (MapHeld?.mapPawns?.AllPawnsSpawned?.Contains(this) ?? false);
		public static float RenderOpacityMin => SymbiantOpacityMin;
		public static float RenderOpacityMax => SymbiantOpacityMax;
		public static float RenderNoiseScale => SymbiantNoiseScale;
		public static float RenderWavePhaseSpeed => SymbiantWavePhaseSpeed;
		public static float RenderWaveShadeStrength => SymbiantWaveShadeStrength;
		public static float RenderEdgeContrast => SymbiantEdgeContrast;
		public static float RenderNoiseTimeSeconds => GenTicks.TicksGame / SymbiantNormalTicksPerSecond;
		public static int MaxCells => Mathf.Clamp(DebugMaxCellsOverride > 0 ? DebugMaxCellsOverride : (ZombieSettings.Values?.symbiantMaxCells ?? 400), 1, MAX_METABALLS);
		Map SymbiantMap => Spawned ? Map : host?.MapHeld ?? MapHeld;
		public Pawn LinkedHost => ResolveHost();
		public string HostThingId => hostThingId;
		public int EligibleColonyRoomCells { get { RefreshSymbiosisMetrics(); return cachedEligibleColonyRoomCells; } }
		public int FullBenefitCells { get { RefreshSymbiosisMetrics(); return cachedFullBenefitCells; } }
		public float IntegratedVisibleCells { get { RefreshSymbiosisMetrics(); return cachedIntegratedVisibleCells; } }
		public int PeakVisibleCells => peakVisibleCells;
		public float PeakIntegratedVisibleCells => peakIntegratedVisibleCells;
		public float PeakBenefitFactor => peakBenefitFactor;
		public int SeveranceMaturityCells { get { RefreshSymbiosisMetrics(); return cachedSeveranceMaturityCells; } }
		public bool HasMaturedForSeverance => maturedForSeverance || PeakMeetsSeveranceMaturity();
		public int SeveranceReserveRequired { get { RefreshSymbiosisMetrics(); return cachedSeveranceReserveRequired; } }
		public float ReserveMaturityFactor => HasMaturedForSeverance ? 1f : Mathf.Clamp01(peakIntegratedVisibleCells / Mathf.Max(1f, SeveranceMaturityCells));
		public float EffectiveDecouplingReserve => Mathf.Min(decouplingReserve, DecouplingReserveMax * ReserveMaturityFactor);
		public float BenefitFactor { get { RefreshSymbiosisMetrics(); return cachedBenefitFactor; } }
		public float DecouplingReserve => decouplingReserve;
		public int DecouplingReserveMax => SeveranceReserveRequired;
		public string GrowthState
		{
			get
			{
				if (Spawned == false || Destroyed || Dead)
					return "inactive";
				RefreshSymbiosisMetrics();
				var linkedHost = ResolveHost();
				if (linkedHost != null && cachedIntegratedVisibleCells <= UprootedIntegratedCellThreshold)
				{
					if (TryFindReseedPlan(linkedHost, out _, out _))
						return "uprooted";
					return HasReseedCandidateRoom(linkedHost) ? "contained" : "dormantNoRoom";
				}
				if (relocationCellDebt > 0 || HasMovableUnintegratedCells())
					return FindExpansionTarget(false) == null ? "contained" : "relocating";
				if (CellCount >= MaxCells)
					return "capped";
				var ticks = GenTicks.TicksGame;
				if (ticks < feedPausedUntilTick)
					return "pausedAfterFeeding";
				if (ticks < nextExpansionTick)
					return "waiting";
				return FindExpansionTarget(false) == null ? "contained" : "growing";
			}
		}
		public int SafeVisibleMinimum
		{
			get
			{
				var requiredReserve = Mathf.Max(1, DecouplingReserveMax);
				var maturityCells = Mathf.Max(1, SeveranceMaturityCells);
				var minimum = Mathf.CeilToInt(maturityCells * (1f - EffectiveDecouplingReserve / requiredReserve));
				return Mathf.Clamp(minimum, 1, maturityCells);
			}
		}
		public int DecouplingFeedPulsesToday
		{
			get
			{
				ResetDailyFeedCounter();
				return decouplingFeedPulsesToday;
			}
		}
		public int DecouplingFeedPulsesPerDay => Mathf.Max(1, ZombieSettings.Values?.symbiantDecouplingFeedPulsesPerDay ?? 2);
		public int FeedPulsesRemaining => Mathf.Max(0, DecouplingFeedPulsesPerDay - DecouplingFeedPulsesToday);
		public bool CanSafelySever => LinkedHost != null && HasMaturedForSeverance && decouplingReserve >= DecouplingReserveMax - 0.01f && CellCount <= 3;
		public static float ZombieIgnoreMinBenefit => Mathf.Clamp(ZombieSettings.Values?.symbiantZombieIgnoreMinBenefit ?? 0.5f, 0f, 1f);
		public static float HostHediffSeverity(float benefitFactor) => Mathf.Max(0.001f, Mathf.Clamp01(benefitFactor));

		internal static float NaturalSpawnPressure(Map map, bool ignoreActive = false)
		{
			if (map == null || ZombieSettings.Values?.symbiantEnabled != true)
				return 0f;
			if (ignoreActive == false && ActiveSymbiant(map) != null)
				return 0f;

			var hostCount = EligibleHosts(map, null).Count();
			if (hostCount == 0)
				return 0f;

			var rooms = CandidateRooms(map).ToArray();
			if (rooms.Length == 0)
				return 0f;

			var eligibleCells = rooms.Sum(room => room.CellCount);
			if (eligibleCells < MinimumNaturalSpawnEligibleCells())
				return 0f;

			var bestRoomScore = rooms.Select(room => ScoreSpawnRoom(map, room)).DefaultIfEmpty(0f).Max();
			var footprintPressure = GenMath.LerpDoubleClamped(20f, 260f, 0.35f, 1.15f, eligibleCells);
			var hostPressure = GenMath.LerpDoubleClamped(1f, 8f, 0.65f, 1.15f, hostCount);
			var usePressure = GenMath.LerpDoubleClamped(80f, 900f, 0.55f, 1.15f, bestRoomScore);
			return Mathf.Clamp(footprintPressure * hostPressure * usePressure, 0.15f, 1.6f);
		}

		internal static object SetDebugPerfProfile(string profile)
		{
			var normalized = (profile ?? "default").Trim().ToLowerInvariant();
			DebugDisableRendering = false;
			DebugDisableSymbiantTick = false;
			DebugDisablePathCost = false;
			DebugDisableCellStatEffects = false;
			DebugDisableHostHediffSync = false;
			DebugDisableSymbiosisBenefits = false;

			switch (normalized)
			{
				case "":
				case "default":
				case "all":
					normalized = "default";
					break;
				case "inert":
					DebugDisableRendering = true;
					DebugDisableSymbiantTick = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "renderonly":
				case "render-only":
					normalized = "renderOnly";
					DebugDisableSymbiantTick = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "pathonly":
				case "path-only":
					normalized = "pathOnly";
					DebugDisableRendering = true;
					DebugDisableSymbiantTick = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "symbiosisonly":
				case "symbiosis-only":
					normalized = "symbiosisOnly";
					DebugDisableRendering = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					break;
				case "norender":
				case "no-render":
					normalized = "noRender";
					DebugDisableRendering = true;
					break;
				case "nopath":
				case "no-path":
					normalized = "noPath";
					DebugDisablePathCost = true;
					break;
				case "nocellstats":
				case "no-cell-stats":
					normalized = "noCellStats";
					DebugDisableCellStatEffects = true;
					break;
				case "notick":
				case "no-tick":
					normalized = "noTick";
					DebugDisableSymbiantTick = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				default:
					normalized = "default";
					break;
			}

			DebugPerfProfile = normalized;
			return DebugPerfState();
		}

		internal static object DebugPerfState()
		{
			return new
			{
				profile = DebugPerfProfile,
				rendering = DebugDisableRendering == false,
				symbiantTick = DebugDisableSymbiantTick == false,
				pathCost = DebugDisablePathCost == false,
				cellStatEffects = DebugDisableCellStatEffects == false,
				hostHediffSync = DebugDisableHostHediffSync == false,
				symbiosisBenefits = DebugDisableSymbiosisBenefits == false,
				maxCellsOverride = DebugMaxCellsOverride,
				effectiveMaxCells = MaxCells,
				technicalMaxCells = MAX_METABALLS
			};
		}

		internal static object SetDebugMaxCellsOverride(int maxCells)
		{
			DebugMaxCellsOverride = Mathf.Clamp(maxCells, 0, MAX_METABALLS);
			return DebugPerfState();
		}

		public static void Spawn(Map map, IntVec3 cell)
		{
			var symbiant = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSymbiant, null) as ZombieSymbiant;
			symbiant.Position = cell;
			symbiant.AddRelativeCell(IntVec3.Zero);
			symbiant.ResetExpansionClock();
			symbiant.EnsureRenderResources();
			symbiant.UpdateAll();

			symbiant.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(symbiant, cell, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(symbiant, map);

			symbiant.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			_ = symbiant.TryAssignRandomHost();
			symbiant.UpdateSymbiosisState();

			var sentLetter = false;
			var linkedHost = symbiant.LinkedHost;
			if (ZombieAwarenessCues.ShouldShowZombieEventLetter())
			{
				var roomLabel = SpawnRoomLabel(map, cell);
				var headline = linkedHost == null ? "LetterLabelZombieSymbiantNoHost".Translate() : "LetterLabelZombieSymbiant".Translate(linkedHost.LabelShortCap);
				var text = linkedHost == null ? "ZombieSymbiantNoHost".Translate(roomLabel) : "ZombieSymbiant".Translate(roomLabel, linkedHost.LabelShortCap);
				Find.LetterStack.ReceiveLetter(headline, text, CustomDefs.SymbiantConnection ?? LetterDefOf.NeutralEvent, SpawnLookTargets(symbiant, linkedHost, map, cell));
				sentLetter = true;
			}

			if (sentLetter == false && ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound())
				CustomDefs.SymbiantConnected?.PlayOneShotOnCamera(null);
		}

		static TaggedString SpawnRoomLabel(Map map, IntVec3 cell)
		{
			var role = cell.GetRoom(map)?.Role;
			return role == null || role.defName == "None" || role.label.NullOrEmpty() ? "ZombieSymbiantUnknownRoom".Translate() : role.LabelCap;
		}

		static LookTargets SpawnLookTargets(ZombieSymbiant symbiant, Pawn linkedHost, Map map, IntVec3 cell)
		{
			var targets = new List<GlobalTargetInfo> { new(cell, map) };
			if (linkedHost != null && linkedHost.Destroyed == false)
				targets.Add(new GlobalTargetInfo(linkedHost));
			return new LookTargets(targets);
		}

		public static bool TrySpawnInBestRoom(Map map)
		{
			if (map == null || ZombieSettings.Values.symbiantEnabled == false)
				return false;
			if (ActiveSymbiant(map) != null)
				return false;
			if (EligibleHosts(map, null).Any() == false)
				return false;
			if (NaturalSpawnPressure(map) <= 0f)
				return false;

			var room = BestSpawnRoom(map);
			if (room == null)
				return false;

			if (TryFindBestSpawnCell(map, room, out var cell, out _) == false)
				return false;

			Spawn(map, cell);
			return true;
		}

		internal static bool CanNaturalSpawnNow(Map map)
		{
			return map != null
				&& ZombieSettings.Values.symbiantEnabled
				&& ActiveSymbiant(map) == null
				&& EligibleHosts(map, null).Any()
				&& BestSpawnRoom(map) != null
				&& NaturalSpawnPressure(map) > 0f;
		}

		internal static object DebugNaturalSpawnPlan(Map map, int limit = 8)
		{
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var active = ActiveSymbiant(map);
			var hosts = EligibleHosts(map, null).ToArray();
			var candidateRooms = CandidateRooms(map).ToArray();
			var eligibleRoomCells = candidateRooms.Sum(room => room.CellCount);
			var naturalSpawnPressure = NaturalSpawnPressure(map);
			var scoredRooms = candidateRooms
				.Select(room =>
				{
					var hasSpawnCell = TryFindBestSpawnCell(map, room, out var bestCell, out var bestCellScore);
					return new
					{
						role = room.Role?.defName,
						roleLabel = room.Role?.LabelCap.ToString(),
						cellCount = room.CellCount,
						extents = DescribeDebugCellRect(room.ExtentsClose),
						score = ScoreSpawnRoom(map, room),
						bestCell = hasSpawnCell ? DescribeDebugCell(bestCell) : null,
						bestCellScore = hasSpawnCell ? bestCellScore : 0f,
						valuableThingCount = room.ContainedAndAdjacentThings.Count(thing => ScoreRoomThing(thing) > 0f)
					};
				})
				.Where(room => room.score > 0f && room.bestCell != null)
				.OrderByDescending(room => room.score)
				.ToArray();
			var rooms = scoredRooms
				.Take(Mathf.Max(1, limit))
				.ToArray();

			return new
			{
				success = true,
				enabled = ZombieSettings.Values.symbiantEnabled,
				activeSymbiant = active?.ThingID,
				eligibleHostCount = hosts.Length,
				eligibleRoomCells,
				minimumNaturalSpawnEligibleCells = MinimumNaturalSpawnEligibleCells(),
				naturalSpawnPressure,
				eligibleHosts = hosts.Take(16).Select(host => new
				{
					id = host.ThingID,
					label = host.LabelShortCap,
					cell = host.Spawned ? DescribeDebugCell(host.Position) : null
				}).ToArray(),
				candidateRoomCount = scoredRooms.Length,
				returnedRoomCount = rooms.Length,
				canSpawnNow = ZombieSettings.Values.symbiantEnabled && active == null && hosts.Length > 0 && scoredRooms.Length > 0 && naturalSpawnPressure > 0f,
				bestRoom = rooms.FirstOrDefault(),
				rooms
			};
		}

		static Room BestSpawnRoom(Map map)
		{
			return CandidateRooms(map)
				.Select(room => new
				{
					room,
					score = ScoreSpawnRoom(map, room),
					hasSpawnCell = TryFindBestSpawnCell(map, room, out _, out _)
				})
				.Where(entry => entry.score > 0f && entry.hasSpawnCell)
				.OrderByDescending(entry => entry.score)
				.FirstOrDefault()?.room;
		}

		static float ScoreSpawnRoom(Map map, Room room)
		{
			if (map == null || room == null)
				return 0f;
			return room.Cells.Take(120).Sum(cell => ScoreTraffic(map, cell)) + room.ContainedAndAdjacentThings.Sum(ScoreRoomThing);
		}

		static bool TryFindBestSpawnCell(Map map, Room room, out IntVec3 cell, out float score)
		{
			cell = IntVec3.Invalid;
			score = 0f;
			if (map == null || room == null)
				return false;

			var best = room.Cells
				.Where(candidate => CanOccupyInitialSpawnCell(map, candidate))
				.Select(candidate => new { cell = candidate, score = ScoreTraffic(map, candidate) + ScoreColonyUse(map, candidate) })
				.OrderByDescending(candidate => candidate.score)
				.FirstOrDefault();
			if (best == null)
				return false;

			cell = best.cell;
			score = best.score;
			return true;
		}

		static bool CanOccupyInitialSpawnCell(Map map, IntVec3 cell)
		{
			return CanOccupyOpenCell(map, cell)
				&& cell.GetEdifice(map) == null
				&& cell.GetThingList(map).Any(thing => thing is Pawn || thing.def.category == ThingCategory.Building) == false;
		}

		static IEnumerable<Room> CandidateRooms(Map map)
		{
			var home = map.areaManager.Home;
			var hasHomeArea = home.TrueCount > 0;
			return map.regionGrid.allRooms.Where(room =>
				IsEligibleIndoorRoom(room)
				&& (hasHomeArea == false || RoomHasHomeAreaCell(home, room) || RoomHasColonyUseSignal(map, room)));
		}

		static bool RoomHasHomeAreaCell(Area home, Room room)
		{
			return home != null && room != null && room.Cells.Any(cell => home[cell]);
		}

		static bool RoomHasColonyUseSignal(Map map, Room room)
		{
			if (map == null || room == null)
				return false;
			if (room.ContainedAndAdjacentThings.Any(thing => ScoreRoomThing(thing) > 0f))
				return true;
			return room.Cells.Take(120).Any(cell => ScoreTraffic(map, cell) > 0f || ScoreColonyUse(map, cell) > 0f);
		}

		static object DescribeDebugCell(IntVec3 cell)
		{
			return cell.IsValid ? new { x = cell.x, z = cell.z } : null;
		}

		static object DescribeDebugCellRect(CellRect rect)
		{
			return new { rect.minX, rect.maxX, rect.minZ, rect.maxZ };
		}

		static bool CanBeLinkedHostIdentityFast(Pawn pawn, bool allowDead = false)
		{
			if (pawn == null || pawn.Destroyed)
				return false;
			if (allowDead == false && pawn.Dead)
				return false;
			if (pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
				return false;
			if (pawn.RaceProps?.Humanlike != true || pawn.RaceProps.IsFlesh == false)
				return false;
			return true;
		}

		static bool CanEverBeLinkedHostFast(Pawn pawn, bool allowDead = false)
		{
			if (CanBeLinkedHostIdentityFast(pawn, allowDead) == false)
				return false;
			if (allowDead == false && (pawn.Spawned == false || pawn.Map == null))
				return false;
			if (pawn.Faction?.IsPlayer != true || pawn.IsColonistPlayerControlled == false || pawn.IsPrisoner)
				return false;
			if (pawn.IsSlave || pawn.HostFaction != null || pawn.IsQuestLodger())
				return false;
			if (pawn.DevelopmentalStage == DevelopmentalStage.Newborn || pawn.DevelopmentalStage == DevelopmentalStage.Baby || pawn.DevelopmentalStage == DevelopmentalStage.Child)
				return false;
			return true;
		}

		static bool CanBeAffectedBySymbiantCellCandidateFast(Pawn pawn)
		{
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& pawn.Spawned
				&& pawn.Map != null
				&& pawn is not Zombie
				&& pawn is not ZombieSymbiant
				&& pawn is not ZombieSpitter
				&& pawn.RaceProps?.Humanlike == true
				&& pawn.Faction?.IsPlayer == true
				&& pawn.IsColonistPlayerControlled;
		}

		internal static bool CanBeAffectedBySymbiantCellFast(Pawn pawn)
		{
			if (CanBeAffectedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			return IsLinkedHostOnCurrentMapFast(pawn) == false;
		}

		static bool CanBeSlowedBySymbiantCellCandidateFast(Pawn pawn)
		{
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& pawn.Spawned
				&& pawn.Map != null
				&& pawn.Flying == false
				&& pawn.RaceProps?.doesntMove != true
				&& pawn is not Zombie
				&& pawn is not ZombieSymbiant
				&& pawn is not ZombieSpitter;
		}

		internal static bool CanBeSlowedBySymbiantCellFast(Pawn pawn)
		{
			if (CanBeSlowedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			return IsLinkedHostOnCurrentMapFast(pawn) == false;
		}

		static bool IsLinkedHostOnCurrentMapFast(Pawn pawn)
		{
			if (CanBeLinkedHostIdentityFast(pawn) == false || pawn.Spawned == false || pawn.Map == null)
				return false;
			return ActiveSymbiant(pawn.Map)?.IsLinkedTo(pawn) == true;
		}

		static bool IsActiveSymbiantOnMap(ZombieSymbiant symbiant, Map map)
		{
			return symbiant != null && symbiant.Destroyed == false && symbiant.Spawned && symbiant.Dead == false && symbiant.Map == map;
		}

		static void RegisterActiveSymbiant(ZombieSymbiant symbiant, Map map)
		{
			if (symbiant == null || map == null)
				return;
			activeSymbiantByMap[map] = symbiant;
			mapsWithoutActiveSymbiant.Remove(map);
		}

		static void ForgetActiveSymbiant(ZombieSymbiant symbiant)
		{
			foreach (var map in activeSymbiantByMap
				.Where(pair => ReferenceEquals(pair.Value, symbiant))
				.Select(pair => pair.Key)
				.ToArray())
				activeSymbiantByMap.Remove(map);
		}

		public static ZombieSymbiant ActiveSymbiant(Map map)
		{
			if (map == null)
				return null;
			if (activeSymbiantByMap.TryGetValue(map, out var cached))
			{
				if (IsActiveSymbiantOnMap(cached, map))
					return cached;
				activeSymbiantByMap.Remove(map);
			}
			if (mapsWithoutActiveSymbiant.Contains(map))
				return null;

			foreach (var symbiant in SpawnedSymbiantThings(map))
			{
				if (IsActiveSymbiantOnMap(symbiant, map))
				{
					symbiant.EnsureHiddenFromPawnSystems(map);
					RegisterActiveSymbiant(symbiant, map);
					return symbiant;
				}
			}
			mapsWithoutActiveSymbiant.Add(map);
			return null;
		}

		static IEnumerable<ZombieSymbiant> SpawnedSymbiantThings(Map map)
		{
			var lister = map?.listerThings;
			if (lister == null)
				yield break;

			var def = CustomDefs.ZombieSymbiant;
			if (def != null)
			{
				var things = lister.ThingsOfDef(def);
				if (things != null)
					for (var i = 0; i < things.Count; i++)
						if (things[i] is ZombieSymbiant symbiant)
							yield return symbiant;
				yield break;
			}

			foreach (var thing in lister.AllThings)
				if (thing is ZombieSymbiant symbiant)
					yield return symbiant;
		}

		static IEnumerable<ZombieSymbiant> ActiveSymbiants()
		{
			if (Find.Maps == null)
				yield break;
			foreach (var map in Find.Maps)
			{
				var symbiant = ActiveSymbiant(map);
				if (symbiant != null)
					yield return symbiant;
			}
		}

		bool IsLinkedTo(Pawn pawn)
		{
			if (pawn == null)
				return false;
			if (ReferenceEquals(host, pawn))
			{
				hostThingId ??= pawn.ThingID;
				return true;
			}
			return hostThingId.NullOrEmpty() == false && hostThingId == pawn.ThingID;
		}

		public static ZombieSymbiant LinkedSymbiantFor(Pawn pawn)
		{
			return LinkedSymbiantFor(pawn, false);
		}

		static ZombieSymbiant LinkedSymbiantFor(Pawn pawn, bool allowDead)
		{
			if (CanBeLinkedHostIdentityFast(pawn, allowDead) == false)
				return null;
			if (pawn.Spawned && pawn.Map != null)
			{
				var mapSymbiant = ActiveSymbiant(pawn.Map);
				if (mapSymbiant != null && mapSymbiant.IsLinkedTo(pawn))
					return mapSymbiant;
			}
			return ActiveSymbiants().FirstOrDefault(symbiant => symbiant.IsLinkedTo(pawn) || symbiant.ResolveHost() == pawn);
		}

		static bool TryGetSameMapLinkedSymbiant(Pawn pawn, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (pawn?.Spawned != true || pawn.Map == null)
				return false;
			if (CanBeLinkedHostIdentityFast(pawn) == false)
				return false;
			symbiant = LinkedSymbiantFor(pawn);
			return symbiant != null
				&& symbiant.Destroyed == false
				&& symbiant.Spawned
				&& symbiant.Map == pawn.Map
				&& symbiant.IsLinkedTo(pawn);
		}

		public static bool HasZombieTargetingProtection(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return false;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) && symbiant.BenefitFactor >= ZombieIgnoreMinBenefit;
		}

		public static float SymbiantBenefitFactor(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return 0f;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) ? symbiant.BenefitFactor : 0f;
		}

		public static bool TryGetHostAuraFactor(Pawn pawn, out float factor)
		{
			factor = 0f;
			if (DebugDisableSymbiosisBenefits)
				return false;
			if (CanBeLinkedHostIdentityFast(pawn) == false || pawn.Spawned == false || pawn.Map == null)
				return false;
			var symbiant = ActiveSymbiant(pawn.Map);
			if (symbiant == null || symbiant.IsLinkedTo(pawn) == false)
				return false;
			factor = symbiant.BenefitFactor;
			return factor > 0.01f;
		}

		public static void ApplySymbiantSkillBonus(SkillRecord skill, ref int level)
		{
			var pawn = skill?.Pawn;
			var factor = SymbiantBenefitFactor(pawn);
			if (factor <= 0f)
				return;
			var bonus = Mathf.RoundToInt(Mathf.Max(0, ZombieSettings.Values.symbiantMaxSkillBonus) * factor);
			if (bonus > 0)
				level = Mathf.Clamp(level + bonus, 0, SkillRecord.MaxLevel);
		}

		public static bool CanSeverSymbiosis(Pawn pawn)
		{
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) && symbiant.CanSafelySever;
		}

		public static void NotifyHostKilled(Pawn pawn)
		{
			if (CanBeLinkedHostIdentityFast(pawn, true) == false)
				return;
			var symbiant = LinkedSymbiantFor(pawn, true);
			if (symbiant == null)
				return;
			symbiant.CollapseFromHostDeath();
		}

		public static bool IsSymbiantCell(Map map, IntVec3 cell, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (map == null || cell.InBounds(map) == false)
				return false;
			symbiant = ActiveSymbiant(map);
			return symbiant != null && symbiant.ContainsCell(cell);
		}

		public static bool IsSymbiantCellForAffectedPawn(Pawn pawn, IntVec3 cell, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (CanBeAffectedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			var map = pawn.Map;
			if (cell.InBounds(map) == false)
				return false;
			symbiant = ActiveSymbiant(map);
			if (symbiant == null)
				return false;
			if (symbiant.ContainsCell(cell) == false)
				return false;
			return symbiant.IsLinkedTo(pawn) == false;
		}

		public static bool IsSymbiantCellForSlowedPawn(Pawn pawn, IntVec3 cell, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (CanBeSlowedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			var map = pawn.Map;
			if (cell.InBounds(map) == false)
				return false;
			symbiant = ActiveSymbiant(map);
			if (symbiant == null)
				return false;
			if (symbiant.ContainsCell(cell) == false)
				return false;
			return symbiant.IsLinkedTo(pawn) == false;
		}

		public static int CountCellsInRoom(Room room)
		{
			var map = room?.Map;
			if (map == null)
				return 0;
			var symbiant = ActiveSymbiant(map);
			if (symbiant == null)
				return 0;
			return symbiant.CountCellsInRoomInternal(room);
		}

		int CountCellsInRoomInternal(Room room)
		{
			if (room == null || hasCellBounds == false || AbsoluteCellBounds.Overlaps(room.ExtentsClose) == false)
				return 0;
			var map = room.Map;
			var count = 0;
			for (var i = 0; i < orderedCells.Count; i++)
			{
				var cell = Position + orderedCells[i];
				if (cell.InBounds(map) && cell.GetRoom(map) == room)
					count++;
			}
			return count;
		}

		static int EligibleColonyRoomCellCount(Map map)
		{
			if (map == null)
				return 0;
			return CandidateRooms(map).ToArray().Sum(room => room.CellCount);
		}

		static int CalculateFullBenefitCells(Map map)
		{
			return CalculateFullBenefitCells(EligibleColonyRoomCellCount(map));
		}

		static int CalculateFullBenefitCells(int eligibleCells)
		{
			var maxCells = Mathf.Max(1, MaxCells);
			var coverage = Mathf.Clamp(ZombieSettings.Values?.symbiantFullBenefitRoomCoverage ?? 0.20f, 0.01f, 1f);
			var target = Mathf.Max(20, Mathf.CeilToInt(eligibleCells * coverage));
			return Mathf.Clamp(target, 1, maxCells);
		}

		static int MinimumNaturalSpawnEligibleCells()
		{
			var maturityMin = Mathf.Max(1, ZombieSettings.Values?.symbiantSeveranceMaturityMinCells ?? 10);
			return Mathf.Min(maturityMin, MaxCells);
		}

		int CalculateSeveranceMaturityCells(Map map)
		{
			return CalculateSeveranceMaturityCells(CalculateFullBenefitCells(map));
		}

		int CalculateSeveranceMaturityCells(int fullBenefitCells)
		{
			var settings = ZombieSettings.Values;
			var coverage = Mathf.Clamp(settings?.symbiantSeveranceMaturityCoverage ?? 0.50f, 0.01f, 1f);
			var min = Mathf.Max(1, settings?.symbiantSeveranceMaturityMinCells ?? 10);
			var max = Mathf.Max(min, settings?.symbiantSeveranceMaturityMaxCells ?? 80);
			var target = Mathf.CeilToInt(fullBenefitCells * coverage);
			var upper = Mathf.Max(1, Mathf.Min(max, MaxCells));
			var lower = Mathf.Min(min, upper);
			return Mathf.Clamp(target, lower, upper);
		}

		int CalculateSeveranceReserveRequired(Map map)
		{
			return CalculateSeveranceReserveRequired(CalculateFullBenefitCells(map));
		}

		int CalculateSeveranceReserveRequired(int fullBenefitCells)
		{
			var settings = ZombieSettings.Values;
			var coverage = Mathf.Clamp(settings?.symbiantSeveranceReserveCoverage ?? 0.25f, 0.01f, 1f);
			var min = Mathf.Max(1, settings?.symbiantSeveranceReserveMin ?? 12);
			var max = Mathf.Max(min, settings?.symbiantSeveranceReserveMax ?? 60);
			var target = Mathf.CeilToInt(fullBenefitCells * coverage);
			var upper = Mathf.Max(1, Mathf.Min(max, MaxCells));
			var lower = Mathf.Min(min, upper);
			return Mathf.Clamp(target, lower, upper);
		}

		float CalculateBenefitFactor(Map map)
		{
			return Mathf.Clamp01(CalculateIntegratedVisibleCells(map) / Mathf.Max(1f, CalculateFullBenefitCells(map)));
		}

		float CalculateIntegratedVisibleCells(Map map)
		{
			if (map == null)
				return 0f;
			return AbsoluteCells.Sum(cell => IntegratedCellWeight(map, cell));
		}

		static bool IsEligibleIndoorRoom(Room room)
		{
			return room != null
				&& room.IsDoorway == false
				&& room.Fogged == false
				&& room.IsHuge == false
				&& room.UsesOutdoorTemperature == false
				&& room.ProperRoom;
		}

		static float IntegratedCellWeight(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false || cell.Fogged(map) || cell.Roofed(map) == false)
				return 0f;
			var room = cell.GetRoom(map);
			if (IsEligibleIndoorRoom(room) == false)
				return 0f;

			var home = map.areaManager.Home;
			var inHome = home.TrueCount == 0 || home[cell];
			var traffic = ScoreTraffic(map, cell) > 0f;
			var colonyUse = ScoreColonyUse(map, cell) > (inHome ? 40f : 0f);
			if (inHome && (traffic || colonyUse))
				return 1f;
			if (inHome || traffic || colonyUse)
				return 0.5f;
			return 0.10f;
		}

		bool PeakMeetsSeveranceMaturity()
		{
			return peakIntegratedVisibleCells >= SeveranceMaturityCells || peakBenefitFactor >= ZombieIgnoreMinBenefit;
		}

		void RefreshSymbiosisMetrics(bool force = false)
		{
			var ticks = GenTicks.TicksGame;
			if (force == false && lastSymbiosisMetricTick != int.MinValue && ticks - lastSymbiosisMetricTick < SymbiosisMetricRefreshInterval)
				return;
			var map = SymbiantMap;
			cachedEligibleColonyRoomCells = EligibleColonyRoomCellCount(map);
			cachedFullBenefitCells = CalculateFullBenefitCells(cachedEligibleColonyRoomCells);
			cachedIntegratedVisibleCells = CalculateIntegratedVisibleCells(map);
			cachedBenefitFactor = Mathf.Clamp01(cachedIntegratedVisibleCells / Mathf.Max(1f, cachedFullBenefitCells));
			cachedSeveranceMaturityCells = CalculateSeveranceMaturityCells(cachedFullBenefitCells);
			cachedSeveranceReserveRequired = CalculateSeveranceReserveRequired(cachedFullBenefitCells);
			lastSymbiosisMetricTick = ticks;
		}

		void UpdateSymbiosisState(bool forceMetricRefresh = true)
		{
			if (Destroyed)
				return;
			RefreshSymbiosisMetrics(forceMetricRefresh);
			if (cachedIntegratedVisibleCells > UprootedIntegratedCellThreshold)
				uprootedSinceTick = -1;
			peakVisibleCells = Mathf.Max(peakVisibleCells, CellCount);
			var integratedVisibleCells = cachedIntegratedVisibleCells;
			peakIntegratedVisibleCells = Mathf.Max(peakIntegratedVisibleCells, integratedVisibleCells);
			peakBenefitFactor = Mathf.Max(peakBenefitFactor, cachedBenefitFactor);
			if (PeakMeetsSeveranceMaturity())
				maturedForSeverance = true;
			decouplingReserve = Mathf.Min(decouplingReserve, cachedSeveranceReserveRequired);
		}

		static Pawn ResolvePawnByThingId(string thingId)
		{
			if (thingId.NullOrEmpty() || Find.Maps == null)
				return null;
			foreach (var map in Find.Maps)
			{
				var pawn = map?.mapPawns?.AllPawns?.FirstOrDefault(candidate => candidate?.ThingID == thingId);
				if (pawn != null)
					return pawn;
			}
			return null;
		}

		Pawn ResolveHost()
		{
			if (host != null && host.Destroyed == false)
			{
				hostThingId ??= host.ThingID;
				return host;
			}
			host = ResolvePawnByThingId(hostThingId);
			if (host != null)
				hostThingId = host.ThingID;
			return host;
		}

		public bool TryAssignRandomHost()
		{
			if (ResolveHost() != null)
				return true;
			var map = SymbiantMap;
			if (map == null)
				return false;
			var candidates = EligibleHosts(map, this).ToArray();
			if (candidates.Length == 0)
				return false;
			AssignHost(candidates.RandomElement());
			return true;
		}

		static IEnumerable<Pawn> EligibleHosts(Map map, ZombieSymbiant symbiant)
		{
			if (map?.mapPawns?.FreeColonistsSpawned == null)
				return Enumerable.Empty<Pawn>();
			return map.mapPawns.FreeColonistsSpawned.Where(pawn => IsEligibleHost(pawn, symbiant));
		}

		static bool IsEligibleHost(Pawn pawn, ZombieSymbiant symbiant)
		{
			if (pawn?.Spawned != true)
				return false;
			if (CanEverBeLinkedHostFast(pawn) == false)
				return false;
			if (AlienTools.IsFleshPawn(pawn) == false || SoSTools.IsHologram(pawn))
				return false;
			if (pawn.InfectionState() >= InfectionState.Infecting)
				return false;
			var linkedSymbiant = LinkedSymbiantFor(pawn);
			return linkedSymbiant == null || linkedSymbiant == symbiant;
		}

		void AssignHost(Pawn pawn)
		{
			host = pawn;
			hostThingId = pawn?.ThingID;
			EnsureHostHediff();
		}

		void EnsureHostLink()
		{
			var linkedHost = ResolveHost();
			if (linkedHost == null)
				return;
			if (linkedHost.Dead || linkedHost.Destroyed)
			{
				CollapseFromHostDeath();
				return;
			}
			if (linkedHost.Spawned && linkedHost.Map != null && Spawned && linkedHost.Map != Map)
			{
				ReleaseHostLink(linkedHost, "SymbiantHostRelocatedMessage");
				return;
			}
			EnsureHostHediff();
		}

		void EnsureHostHediff()
		{
			if (DebugDisableHostHediffSync)
				return;
			var pawn = ResolveHost();
			if (pawn?.health?.hediffSet == null || CustomDefs.SymbiantSymbiosis == null)
				return;
			var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			var severity = HostHediffSeverity(SymbiantBenefitFactor(pawn));
			if (hediff == null)
			{
				hediff = HediffMaker.MakeHediff(CustomDefs.SymbiantSymbiosis, pawn) as Hediff_SymbiantSymbiosis;
				if (hediff != null)
				{
					hediff.symbiantThingId = ThingID;
					hediff.Severity = severity;
					pawn.health.AddHediff(hediff);
				}
			}
			if (hediff != null)
			{
				hediff.symbiantThingId = ThingID;
				hediff.Severity = severity;
			}
		}

		static void RemoveHostHediff(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null || CustomDefs.SymbiantSymbiosis == null)
				return;
			foreach (var hediff in pawn.health.hediffSet.hediffs
				.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis)
				.ToArray())
				pawn.health.RemoveHediff(hediff);
		}

		public static void AddCell(Map map, IntVec3 cell)
		{
			ActiveSymbiant(map)?.AddCell(cell);
		}

		public static int AddCells(Map map, IEnumerable<IntVec3> newCells)
		{
			if (map == null)
				return 0;
			var newCellArray = newCells?.ToArray() ?? Array.Empty<IntVec3>();
			if (newCellArray.Length == 0)
				return 0;
			return ActiveSymbiant(map)?.AddCells(newCellArray) ?? 0;
		}

		internal static void ReleaseAllRenderResources()
		{
			foreach (var symbiant in renderResourceOwners.ToArray())
				symbiant.ReleaseRenderResources(false);
			renderResourceOwners.Clear();
		}

		internal static void ClearActiveSymbiantCaches()
		{
			activeSymbiantByMap.Clear();
			mapsWithoutActiveSymbiant.Clear();
		}

		internal static void ResetTransientStaticState()
		{
			ReleaseAllRenderResources();
			ClearActiveSymbiantCaches();
		}

		internal static object DebugCacheState(Map map = null)
		{
			return new
			{
				activeCacheCount = activeSymbiantByMap.Count,
				emptyCacheCount = mapsWithoutActiveSymbiant.Count,
				currentMapActiveCached = map != null && activeSymbiantByMap.TryGetValue(map, out var cached) && IsActiveSymbiantOnMap(cached, map),
				currentMapCachedSymbiant = map != null && activeSymbiantByMap.TryGetValue(map, out var cachedSymbiant) ? cachedSymbiant.ThingID : null,
				currentMapMarkedEmpty = map != null && mapsWithoutActiveSymbiant.Contains(map)
			};
		}

		void ReleaseRenderResources(bool unregister = true)
		{
			if (metaballMaterial != null)
			{
				UnityEngine.Object.Destroy(metaballMaterial);
				metaballMaterial = null;
			}
			if (metaballTexture != null)
			{
				UnityEngine.Object.Destroy(metaballTexture);
				metaballTexture = null;
			}
			if (mesh != null)
			{
				UnityEngine.Object.Destroy(mesh);
				mesh = null;
			}
			if (unregister)
				renderResourceOwners.Remove(this);
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			EnsureSymbiantDefaults();
			RegisterActiveSymbiant(this, map);
			EnsureHiddenFromPawnSystems(map);
		}

		void EnsureHiddenFromPawnSystems(Map map = null)
		{
			map ??= MapHeld;
			map?.mapPawns?.DeRegisterPawn(this);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			ForgetActiveSymbiant(this);
			ReleaseRenderResources();
			base.DeSpawn(mode);
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			ForgetActiveSymbiant(this);
			if (safeSeveranceInProgress == false && hostCollapseInProgress == false)
				HandleUncontrolledDestroy();
			ReleaseRenderResources();
			base.Destroy(mode);
		}

		internal void DebugDestroyWithoutHostTrauma()
		{
			if (Destroyed)
				return;

			safeSeveranceInProgress = true;
			try
			{
				var pawn = ResolveHost();
				RemoveHostHediff(pawn);
				host = null;
				hostThingId = null;
				Destroy(DestroyMode.Vanish);
			}
			finally
			{
				safeSeveranceInProgress = false;
			}
		}

		bool AddRelativeCell(IntVec3 relative)
		{
			if (cells.Add(relative) == false)
				return false;
			orderedCells.Add(relative);
			ExpandCellBounds(relative);
			return true;
		}

		void ExpandCellBounds(IntVec3 relative)
		{
			if (hasCellBounds)
				relativeCellBounds = relativeCellBounds.Encapsulate(relative);
			else
			{
				relativeCellBounds = CellRect.SingleCell(relative);
				hasCellBounds = true;
			}
		}

		void RebuildCellBounds()
		{
			hasCellBounds = false;
			if (cells == null)
				return;
			foreach (var cell in cells)
				ExpandCellBounds(cell);
			UpdateDrawCullSize();
		}

		void UpdateDrawCullSize()
		{
			if (hasCellBounds == false)
			{
				drawCullSize = Vector2.one;
				return;
			}

			var minX = relativeCellBounds.minX - 1f;
			var maxX = relativeCellBounds.maxX + 1f;
			var minZ = relativeCellBounds.minZ - 1f;
			var maxZ = relativeCellBounds.maxZ + 1f;
			var width = Mathf.Max(Mathf.Abs(minX), Mathf.Abs(maxX)) * 2f + 1f;
			var height = Mathf.Max(Mathf.Abs(minZ), Mathf.Abs(maxZ)) * 2f + 1f;
			drawCullSize = new Vector2(Mathf.Max(1f, width), Mathf.Max(1f, height));
		}

		int AddRelativeCells(IEnumerable<IntVec3> relatives)
		{
			var added = 0;
			foreach (var relative in relatives)
			{
				if (CellCount >= MaxCells)
					break;
				if (AddRelativeCell(relative))
					added++;
			}
			return added;
		}

		int AddCells(IEnumerable<IntVec3> newCells)
		{
			var added = AddRelativeCells(newCells.Select(cell => cell - Position));
			if (added > 0)
			{
				UpdateAll();
				UpdateSymbiosisState();
			}
			return added;
		}

		void AddCell(IntVec3 newCell)
		{
			if (CellCount >= MaxCells)
				return;
			if (AddRelativeCell(newCell - Position))
			{
				UpdateAll();
				UpdateSymbiosisState();
			}
		}

		public bool ContainsCell(IntVec3 absoluteCell)
		{
			if (hasCellBounds == false)
				return false;
			var relative = absoluteCell - Position;
			return relativeCellBounds.Contains(relative) && cells?.Contains(relative) == true;
		}

		public bool CanExpand()
		{
			return Spawned && Destroyed == false && Dead == false && CellCount < MaxCells;
		}

		bool TryReseedIfUprooted()
		{
			if (Spawned == false || Destroyed || Dead)
				return false;
			var linkedHost = ResolveHost();
			if (linkedHost == null)
			{
				uprootedSinceTick = -1;
				return false;
			}

			RefreshSymbiosisMetrics(true);
			if (cachedIntegratedVisibleCells > UprootedIntegratedCellThreshold)
			{
				uprootedSinceTick = -1;
				return false;
			}

			var ticks = GenTicks.TicksGame;
			if (uprootedSinceTick < 0)
			{
				uprootedSinceTick = ticks;
				return false;
			}
			if (ticks - uprootedSinceTick < UprootedRelocationGraceTicks)
				return false;

			if (TryFindReseedPlan(linkedHost, out var anchor, out var reseedCells) == false)
				return false;

			ReseedAt(anchor, reseedCells, linkedHost, Mathf.Max(0, CellCount - 1));
			return true;
		}

		bool TryFindReseedPlan(Pawn linkedHost, out IntVec3 anchor, out List<IntVec3> reseedCells)
		{
			anchor = IntVec3.Invalid;
			reseedCells = null;
			var map = Map;
			if (map == null)
				return false;

			var wantedCells = 1;
			foreach (var room in ReseedCandidateRooms(map, linkedHost))
			{
				if (TryBuildReseedCells(map, room, wantedCells, out anchor, out reseedCells))
					return true;
			}
			return false;
		}

		static IEnumerable<Room> ReseedCandidateRooms(Map map, Pawn linkedHost)
		{
			var hostRoom = linkedHost?.Spawned == true && linkedHost.Map == map ? linkedHost.Position.GetRoom(map) : null;
			if (IsEligibleIndoorRoom(hostRoom))
				yield return hostRoom;

			foreach (var room in CandidateRooms(map)
				.Where(room => room != hostRoom)
				.Select(room => new { room, score = ScoreSpawnRoom(map, room) })
				.Where(entry => entry.score > 0f)
				.OrderByDescending(entry => entry.score)
				.Select(entry => entry.room))
				yield return room;
		}

		bool HasReseedCandidateRoom(Pawn linkedHost)
		{
			var map = Map;
			if (map == null)
				return false;
			var hostRoom = linkedHost?.Spawned == true && linkedHost.Map == map ? linkedHost.Position.GetRoom(map) : null;
			return IsEligibleIndoorRoom(hostRoom) || CandidateRooms(map).Any();
		}

		static bool TryBuildReseedCells(Map map, Room room, int wantedCells, out IntVec3 anchor, out List<IntVec3> reseedCells)
		{
			anchor = IntVec3.Invalid;
			reseedCells = null;
			if (TryFindBestSpawnCell(map, room, out anchor, out _) == false)
				return false;

			var targetCount = Mathf.Clamp(wantedCells, 1, MaxCells);
			var root = anchor;
			var cellsInOrder = room.Cells
				.Where(cell => cell != root && CanOccupyOpenCell(map, cell))
				.OrderBy(cell => cell.DistanceToSquared(root))
				.ThenByDescending(cell => ScoreExpansionCell(map, cell));
			reseedCells = [anchor];
			foreach (var cell in cellsInOrder)
			{
				if (reseedCells.Count >= targetCount)
					break;
				reseedCells.Add(cell);
			}
			return true;
		}

		void ReseedAt(IntVec3 anchor, List<IntVec3> reseedCells, Pawn linkedHost, int relocationDebt)
		{
			var map = Map;
			if (map == null || anchor.IsValid == false || reseedCells == null || reseedCells.Count == 0)
				return;

			var targetCells = reseedCells.Distinct().Take(MaxCells).ToArray();
			DeSpawn(DestroyMode.Vanish);
			Position = anchor;
			cells = [];
			orderedCells = [];
			hasCellBounds = false;
			AddRelativeCells(targetCells.Select(cell => cell - anchor));
			RebuildCellBounds();
			relocationCellDebt = Mathf.Clamp(relocationCellDebt + relocationDebt, 0, Mathf.Max(0, MaxCells - CellCount));
			nextRelocationPulseTick = GenTicks.TicksGame + RelocationPulseIntervalTicks();
			uprootedSinceTick = -1;
			lastSymbiosisMetricTick = int.MinValue;

			GenSpawn.Spawn(this, anchor, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(this, map);
			EnsureHiddenFromPawnSystems(map);
			jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			ResetExpansionClock();
			UpdateAll();
			UpdateSymbiosisState();

			PlayConnectedSound();
			if (linkedHost != null)
				Messages.Message("SymbiantReseededMessage".Translate(linkedHost.LabelShortCap), new TargetInfo(anchor, map), MessageTypeDefOf.NeutralEvent, false);
		}

		void HandleUncontrolledDestroy()
		{
			if (uncontrolledDestroyHandled)
				return;
			uncontrolledDestroyHandled = true;

			var pawn = ResolveHost();
			if (pawn == null || pawn.Destroyed || pawn.Dead)
				return;
			ReleaseHostLink(pawn);
		}

		void ReleaseHostLink(Pawn pawn, string messageKey = null)
		{
			PlayDisconnectedSound();
			RemoveHostHediff(pawn);
			host = null;
			hostThingId = null;
			if (messageKey.NullOrEmpty() == false && pawn != null)
				Messages.Message(messageKey.Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.NeutralEvent, false);
		}

		void CollapseFromHostDeath()
		{
			if (Destroyed || hostCollapseInProgress || safeSeveranceInProgress)
				return;
			if (uncontrolledDestroyHandled)
			{
				var currentHost = ResolveHost();
				RemoveHostHediff(currentHost);
				host = null;
				hostThingId = null;
				return;
			}
			hostCollapseInProgress = true;
			var pawn = ResolveHost();
			PlayDisconnectedSound();
			RemoveHostHediff(pawn);
			host = null;
			hostThingId = null;
			Destroy(DestroyMode.Vanish);
			hostCollapseInProgress = false;
		}

		void PlayConnectedSound()
		{
			if (ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound())
				CustomDefs.SymbiantConnected?.PlayOneShotOnCamera(null);
		}

		void PlayDisconnectedSound()
		{
			if (ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound())
				CustomDefs.SymbiantDisconnected?.PlayOneShotOnCamera(null);
		}

		void NotifyOrdinaryDamageRejected()
		{
			if (Spawned == false || Map == null)
				return;
			var ticks = GenTicks.TicksGame;
			if (ticks - lastRejectedDamageMessageTick < 600)
				return;
			lastRejectedDamageMessageTick = ticks;
			Messages.Message("SymbiantWeaponRejectedMessage".Translate(), this, MessageTypeDefOf.RejectInput, false);
			MoteMaker.ThrowText(DrawPos, Map, "SymbiantWeaponRejectedMote".Translate(), 3.65f);
		}

		public void PreApplyLinkedDamage(ref DamageInfo dinfo, ref bool absorbed)
		{
			if (safeSeveranceInProgress || hostCollapseInProgress || ResolveHost() == null)
				return;
			if (dinfo.Amount <= 0f)
				return;
			dinfo.SetAmount(0f);
			absorbed = true;
			NotifyOrdinaryDamageRejected();
		}

		public bool TrySeverSymbiosis(Pawn pawn, Pawn doctor)
		{
			if (pawn == null || pawn != LinkedHost || CanSafelySever == false)
				return false;

			var medicine = doctor?.skills?.GetSkill(SkillDefOf.Medicine);
			var chance = Mathf.Clamp(0.55f + (medicine?.Level ?? 0) * 0.03f, 0.55f, 0.95f);
			if (Rand.Chance(chance) == false)
			{
				decouplingReserve = Mathf.Max(0f, decouplingReserve - Mathf.Max(1f, DecouplingReserveMax * 0.25f));
				return false;
			}

			safeSeveranceInProgress = true;
			PlayDisconnectedSound();
			Messages.Message("SymbiantSeveredMessage".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent, false);
			RemoveHostHediff(pawn);
			host = null;
			hostThingId = null;
			Destroy(DestroyMode.Vanish);
			return true;
		}

		public void SymbiantTick()
		{
			if (DebugDisableSymbiantTick)
				return;
			var ticks = GenTicks.TicksGame;
			if (lastSymbiantTick == ticks)
				return;
			lastSymbiantTick = ticks;
			if (ticks % SymbiosisMetricRefreshInterval == Mathf.Abs(thingIDNumber % SymbiosisMetricRefreshInterval))
			{
				EnsureHostLink();
				if (TryReseedIfUprooted())
					return;
				if (uprootedSinceTick < 0)
				{
					UpdateSymbiosisState(false);
					if (relocationCellDebt <= 0 && nextRelocationPulseTick <= 0 && HasMovableUnintegratedCells())
						nextRelocationPulseTick = ticks;
				}
			}
			if (uprootedSinceTick >= 0)
				return;
			if (relocationCellDebt > 0 || (nextRelocationPulseTick > 0 && ticks >= nextRelocationPulseTick))
			{
				_ = TryRelocationPulse();
				return;
			}
			if (CanExpand() == false)
				return;
			if (ticks < nextExpansionTick || ticks < feedPausedUntilTick)
				return;
			_ = TryExpansionPulse();
			ResetExpansionClock();
		}

		void ResetExpansionClock()
		{
			nextExpansionTick = GenTicks.TicksGame + AutomaticExpansionIntervalTicks();
		}

		int AutomaticExpansionIntervalTicks()
		{
			var difficulty = Mathf.Clamp(ZombieLand.Tools.Difficulty(), 0f, 5f);
			var maxCells = Mathf.Max(1, MaxCells);
			var growthFactor = Mathf.Clamp01(CellCount / (float)maxCells);
			var baseHours = GenMath.LerpDoubleClamped(0f, 5f, 22f, 7f, difficulty);
			var sizeFactor = Mathf.Lerp(0.85f, 1.35f, growthFactor);
			var benefitFactor = Mathf.Lerp(0.95f, 1.20f, Mathf.Clamp01(cachedBenefitFactor));
			var randomFactor = Rand.Range(0.85f, 1.20f);
			var hours = baseHours * sizeFactor * benefitFactor * randomFactor;
			return Mathf.Max(GenDate.TicksPerHour, Mathf.RoundToInt(hours * GenDate.TicksPerHour));
		}

		int RelocationPulseIntervalTicks()
		{
			return Mathf.Max(GenDate.TicksPerHour / 2, AutomaticExpansionIntervalTicks() / 2);
		}

		public bool TryExpansionPulse()
		{
			if (CanExpand() == false)
				return false;

			var target = FindExpansionTarget(true);
			if (target == null)
				return false;

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			AddCell(target.cell);
			return true;
		}

		bool HasMovableUnintegratedCells()
		{
			var map = Map;
			return map != null
				&& CellCount > 1
				&& orderedCells.Any(relative => relative != IntVec3.Zero && IntegratedCellWeight(map, Position + relative) <= UprootedIntegratedCellThreshold);
		}

		bool TryMoveUnintegratedCell(Map map, ExpansionTarget target)
		{
			if (map == null || target == null || CellCount <= 1)
				return false;

			var relative = orderedCells
				.LastOrDefault(cell => cell != IntVec3.Zero && IntegratedCellWeight(map, Position + cell) <= UprootedIntegratedCellThreshold);
			if (relative == IntVec3.Zero || relative.IsValid == false || cells.Contains(relative) == false)
				return false;

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			orderedCells.Remove(relative);
			cells.Remove(relative);
			AddRelativeCell(target.cell - Position);
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			return true;
		}

		bool TryRelocationPulse()
		{
			var ticks = GenTicks.TicksGame;
			if (ticks < nextRelocationPulseTick)
				return false;

			var map = Map;
			var target = FindExpansionTarget(true);
			if (target == null)
			{
				nextRelocationPulseTick = ticks + RelocationPulseIntervalTicks();
				return false;
			}

			if (TryMoveUnintegratedCell(map, target))
			{
				nextRelocationPulseTick = relocationCellDebt > 0 || HasMovableUnintegratedCells() ? ticks + RelocationPulseIntervalTicks() : 0;
				return true;
			}

			if (relocationCellDebt <= 0 || CanExpand() == false)
			{
				nextRelocationPulseTick = 0;
				return false;
			}

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			AddCell(target.cell);
			relocationCellDebt = Mathf.Max(0, relocationCellDebt - 1);
			nextRelocationPulseTick = relocationCellDebt > 0 || HasMovableUnintegratedCells() ? ticks + RelocationPulseIntervalTicks() : 0;
			if (relocationCellDebt == 0)
				ResetExpansionClock();
			return true;
		}

		ExpansionTarget FindExpansionTarget(bool consumeCancelNextBreach)
		{
			var map = Map;
			var targets = new List<ExpansionTarget>();
			foreach (var cell in orderedCells.Select(relative => Position + relative).ToArray())
			{
				for (var i = 0; i < 4; i++)
				{
					var direction = GenAdj.CardinalDirections[i];
					var candidate = cell + direction;
					if (candidate.InBounds(map) == false || ContainsCell(candidate))
						continue;

					if (CanOccupyOpenCell(map, candidate) || IsDoorCell(map, candidate))
					{
						targets.Add(new ExpansionTarget(candidate, null, ScoreExpansionCell(map, candidate)));
						continue;
					}

					if (ZombieSettings.Values.symbiantCanBreakConstructedWalls == false)
						continue;

					var wall = BreakableConstructedWall(map, candidate);
					if (wall == null)
						continue;

					var beyond = candidate + direction;
					if (beyond.InBounds(map) && ContainsCell(beyond) == false && (CanOccupyOpenCell(map, beyond) || IsDoorCell(map, beyond)))
						targets.Add(new ExpansionTarget(candidate, wall, ScoreExpansionCell(map, beyond) + 150f));
				}
			}
			var openTarget = targets
				.Where(target => target.wall == null)
				.OrderByDescending(target => target.score)
				.FirstOrDefault();
			if (openTarget != null)
				return openTarget;

			var wallTarget = targets
				.Where(target => target.wall != null)
				.OrderByDescending(target => target.score)
				.FirstOrDefault();
			if (wallTarget != null && cancelNextBreach)
			{
				if (consumeCancelNextBreach)
					cancelNextBreach = false;
				return null;
			}
			return wallTarget;
		}

		static Building BreakableConstructedWall(Map map, IntVec3 cell)
		{
			var edifice = cell.GetEdifice(map);
			if (edifice == null || edifice is Building_Door)
				return null;
			if (edifice.def == null || edifice.def.building == null || edifice.def.useHitPoints == false)
				return null;
			if (edifice.def.building.isNaturalRock || edifice.def.mineable)
				return null;
			if (edifice.Faction != Faction.OfPlayer)
				return null;
			return edifice;
		}

		static bool CanOccupyOpenCell(Map map, IntVec3 cell)
		{
			if (cell.InBounds(map) == false || cell.Fogged(map))
				return false;
			if (cell.Walkable(map) == false)
				return false;
			var room = cell.GetRoom(map);
			return IsEligibleIndoorRoom(room);
		}

		static bool IsDoorCell(Map map, IntVec3 cell)
		{
			var door = cell.GetEdifice(map) as Building_Door;
			if (door == null)
				return false;
			return GenAdj.CardinalDirections
				.Select(dir => cell + dir)
				.Where(adjacent => adjacent.InBounds(map))
				.Select(adjacent => adjacent.GetRoom(map))
				.Any(IsEligibleIndoorRoom);
		}

		static float ScoreExpansionCell(Map map, IntVec3 cell)
		{
			if (cell.InBounds(map) == false)
				return 0f;
			var room = cell.GetRoom(map);
			var roomScore = room == null ? 0f : room.ContainedAndAdjacentThings.Sum(ScoreRoomThing) + Mathf.Min(100f, room.CellCount / 4f);
			return ScoreTraffic(map, cell) + ScoreColonyUse(map, cell) + roomScore;
		}

		static float ScoreTraffic(Map map, IntVec3 cell)
		{
			var pheromone = map.GetGrid()?.GetPheromone(cell, false);
			if (pheromone == null || pheromone.timestamp <= 0)
				return 0f;
			var age = Mathf.Max(0f, ZombieLand.Tools.Ticks() - pheromone.timestamp);
			return Mathf.Max(0f, 300f - age / 200f);
		}

		static float ScoreColonyUse(Map map, IntVec3 cell)
		{
			var home = map.areaManager.Home;
			var score = home.TrueCount == 0 || home[cell] ? 40f : 0f;
			score += cell.GetThingList(map).Sum(ScoreRoomThing);
			return score;
		}

		static float ScoreRoomThing(Thing thing)
		{
			if (thing is Building_Bed bed)
				return bed.OwnersForReading?.Count > 0 ? 180f : 80f;
			if (thing is Building_NutrientPasteDispenser)
				return 140f;
			if (thing is Building_WorkTable)
				return 120f;
			if (thing is Building_Storage)
				return 90f;
			if (thing is Building_PowerSwitch || thing is Building_Battery || thing is Building_TempControl || thing is Building_Cooler || thing is Building_Heater)
				return 70f;
			return 0f;
		}

		public void RequestFeed(bool requested)
		{
			feedRequested = requested;
		}

		void ResetDailyFeedCounter()
		{
			var day = GenTicks.TicksGame / GenDate.TicksPerDay;
			if (decouplingFeedDay == day)
				return;
			decouplingFeedDay = day;
			decouplingFeedPulsesToday = 0;
		}

		public bool TryFeed(Thing feed)
		{
			if (IsValidFeed(feed) == false)
				return false;
			ResetDailyFeedCounter();
			if (decouplingFeedPulsesToday >= DecouplingFeedPulsesPerDay)
			{
				lastRecessionPulseCells = 0;
				return false;
			}

			UpdateSymbiosisState();
			var pulseSize = RecessionPulseSize(feed);
			var consumed = feed.stackCount > 1 ? feed.SplitOff(1) : feed;
			consumed.Destroy(DestroyMode.Vanish);
			decouplingFeedPulsesToday++;
			decouplingReserve = Mathf.Min(DecouplingReserveMax, decouplingReserve + pulseSize);
			var minRemainingCells = LinkedHost == null && hostThingId.NullOrEmpty() ? 0 : SafeVisibleMinimum;
			lastRecessionPulseCells = ShrinkCells(pulseSize, minRemainingCells);
			UpdateSymbiosisState();
			cancelNextBreach = true;
			feedPausedUntilTick = GenTicks.TicksGame + Mathf.Max(0, ZombieSettings.Values.symbiantPostFeedPauseHours) * GenDate.TicksPerHour;
			if (Spawned)
			{
				CustomDefs.ZombieEating.PlayOneShot(SoundInfo.InMap(this));
				MoteMaker.ThrowText(DrawPos, Map, "SymbiantFedMote".Translate(pulseSize, lastRecessionPulseCells), 3.65f);
			}
			return true;
		}

		int RecessionPulseSize(Thing feed)
		{
			var baseSize = 1;
			if (feed.def == CustomDefs.SymbiantCoagulantPack)
			{
				baseSize = ZombieSettings.Values.symbiantCoagulantPotency switch
				{
					SymbiantCoagulantPotency.Cheap => 2,
					SymbiantCoagulantPotency.Expensive => 5,
					_ => 3
				};
			}
			else if (feed is Corpse corpse)
			{
				var pawn = corpse.InnerPawn;
				var fresh = corpse.GetRotStage() == RotStage.Fresh;
				if (fresh)
					baseSize = Mathf.Max(baseSize, 2);
				if (pawn?.BodySize >= 1.5f)
					baseSize = Mathf.Max(baseSize, 2);
				if (pawn?.BodySize >= 2.5f)
					baseSize = Mathf.Max(baseSize, 3);
			}

			return baseSize + RecessionSizeBonus();
		}

		int RecessionSizeBonus()
		{
			if (CellCount >= 300)
				return 3;
			if (CellCount >= 200)
				return 2;
			if (CellCount >= 100)
				return 1;
			return 0;
		}

		public static bool IsValidFeed(Thing feed)
		{
			if (feed == null || feed.Destroyed)
				return false;
			if (feed.def == CustomDefs.SymbiantCoagulantPack)
				return true;
			if (feed is Corpse corpse)
			{
				var pawn = corpse.InnerPawn;
				if (pawn == null || pawn.RaceProps?.Humanlike != true)
					return false;
				return pawn is not Zombie && pawn is not ZombieSymbiant && pawn is not ZombieSpitter;
			}
			return false;
		}

		public bool ShrinkOneCell()
		{
			return ShrinkCells(1, SafeVisibleMinimum) > 0;
		}

		public int ShrinkCells(int count)
		{
			return ShrinkCells(count, 0);
		}

		int ShrinkCells(int count, int minRemainingCells)
		{
			EnsureSymbiantDefaults();
			var removed = 0;
			var minRemaining = Mathf.Clamp(minRemainingCells, 0, Mathf.Max(0, orderedCells.Count));
			while (removed < count && orderedCells.Count > minRemaining)
			{
				var cell = orderedCells.Count > 1 ? orderedCells[^1] : orderedCells[0];
				orderedCells.Remove(cell);
				if (cells.Remove(cell))
					removed++;
			}
			if (cells.Count == 0)
			{
				Destroy(DestroyMode.Vanish);
				return removed;
			}
			if (removed > 0)
			{
				RebuildCellBounds();
				UpdateAll();
			}
			return removed;
		}

		void UpdateAll()
		{
			EnsureRenderResources();
			if (hasCellBounds == false)
				return;

			var min_x = relativeCellBounds.minX - 1f;
			var min_z = relativeCellBounds.minZ - 1f;
			var max_x = relativeCellBounds.maxX + 1f;
			var max_z = relativeCellBounds.maxZ + 1f;

			centerX = (min_x + max_x) / 2;
			centerZ = (min_z + max_z) / 2;
			UpdateDrawCullSize();

			var dx = max_x - min_x;
			var dz = max_z - min_z;
			renderMinX = min_x;
			renderMinZ = min_z;
			renderWidth = Mathf.Max(1f, dx);
			renderHeight = Mathf.Max(1f, dz);
			EnsureMetaballTextureResolution(renderWidth, renderHeight);

			var size2 = new Vector2(renderWidth, renderHeight);

			if (mesh != null)
				UnityEngine.Object.Destroy(mesh);
			mesh = MeshMakerPlanes.NewPlaneMesh(size2, false, false, false);

			var allCells = cells.ToArray();
			var cellCount = Mathf.Min(allCells.Length, MAX_METABALLS);
			metaballRadiusByCell.Clear();

			for (var i = 0; i < cellCount; i++)
			{
				var cell = allCells[i];
				var cellRadius = Mathf.Clamp(GetSize(cell) * MetaballCellRadiusFactor, MetaballCellRadiusMin, MetaballCellRadiusMax);
				metaballRadiusByCell[cell] = cellRadius;
			}

			UpdateMetaballTexture();
		}

		void UpdateMetaballTexture()
		{
			if (metaballTexture == null)
				return;

			var textureWidth = metaballTexture.width;
			var textureHeight = metaballTexture.height;
			var pixels = new Color[textureWidth * textureHeight];
			var influenceRadius = MetaballInfluenceRadiusCells;
			for (var y = 0; y < textureHeight; y++)
			{
				var worldZ = renderMinZ + (y + 0.5f) / textureHeight * renderHeight;
				var minCellZ = Mathf.FloorToInt(worldZ - influenceRadius);
				var maxCellZ = Mathf.CeilToInt(worldZ + influenceRadius);
				for (var x = 0; x < textureWidth; x++)
				{
					var worldX = renderMinX + (x + 0.5f) / textureWidth * renderWidth;
					var minCellX = Mathf.FloorToInt(worldX - influenceRadius);
					var maxCellX = Mathf.CeilToInt(worldX + influenceRadius);
					var field = 0f;
					var r = 0f;
					var g = 0f;
					var b = 0f;

					for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
					{
						for (var cellX = minCellX; cellX <= maxCellX; cellX++)
						{
							var cell = new IntVec3(cellX, 0, cellZ);
							if (metaballRadiusByCell.TryGetValue(cell, out var cellRadius) == false || cellRadius <= 0.0001f)
								continue;
							var dx = worldX - cellX;
							var dy = worldZ - cellZ;
							var distanceSq = Mathf.Max(dx * dx + dy * dy, 0.0001f);
							var contribution = cellRadius * cellRadius / distanceSq;
							contribution *= contribution;
							contribution *= Mathf.Max(power, 0f);
							field += contribution;
							r += color.r * contribution;
							g += color.g * contribution;
							b += color.b * contribution;
						}
					}

					var alpha = SmoothStep(MetaballAlphaStart, MetaballAlphaFull, field) * MetaballMaxAlpha;
					if (alpha <= 0.001f || field <= 0.0001f)
					{
						pixels[y * textureWidth + x] = Color.clear;
						continue;
					}

					var edge = SmoothStep(MetaballEdgeStart, MetaballEdgeFull, field);
					pixels[y * textureWidth + x] = new Color(
						Mathf.Clamp01(r / field * edge),
						Mathf.Clamp01(g / field * edge),
						Mathf.Clamp01(b / field * edge),
						alpha);
				}
			}

			metaballTexture.SetPixels(pixels);
			metaballTexture.Apply(false, false);
		}

		static int DesiredMetaballTextureSize(float worldSize)
		{
			var desired = Mathf.CeilToInt(Mathf.Max(1f, worldSize) * MetaballTexturePixelsPerCell);
			return Mathf.Clamp(Mathf.NextPowerOfTwo(desired), MetaballTextureMinSize, MetaballTextureMaxSize);
		}

		void EnsureMetaballTextureResolution(float worldWidth, float worldHeight)
		{
			var textureWidth = DesiredMetaballTextureSize(worldWidth);
			var textureHeight = DesiredMetaballTextureSize(worldHeight);
			if (metaballTexture != null && metaballTexture.width == textureWidth && metaballTexture.height == textureHeight)
				return;

			if (metaballTexture != null)
				UnityEngine.Object.Destroy(metaballTexture);
			metaballTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false, true)
			{
				name = $"ZombieSymbiantMetaballs_{textureWidth}x{textureHeight}",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear
			};
			if (metaballMaterial != null)
				ConfigureMetaballMaterial();
			renderResourceOwners.Add(this);
		}

		static float SmoothStep(float edge0, float edge1, float x)
		{
			var t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.0001f, edge1 - edge0));
			return t * t * (3f - 2f * t);
		}

		void EnsureSymbiantDefaults()
		{
			cells ??= [];
			if (cells.Count == 0)
				cells.Add(IntVec3.Zero);
			orderedCells ??= [];
			if (orderedCells.Count == 0)
				orderedCells.AddRange(cells);
			else
			{
				foreach (var cell in cells)
				{
					if (orderedCells.Contains(cell) == false)
						orderedCells.Add(cell);
				}
				orderedCells.RemoveAll(cell => cells.Contains(cell) == false);
			}
			while (orderedCells.Count > MAX_METABALLS)
			{
				var cell = orderedCells[^1];
				orderedCells.RemoveAt(orderedCells.Count - 1);
				cells.Remove(cell);
			}
			RebuildCellBounds();
			if (radius <= 0f)
				radius = elementRadius * 9f;
			if (power <= 0f)
				power = elementPower;
			if (nextExpansionTick <= 0)
				ResetExpansionClock();
			if (uprootedSinceTick < -1)
				uprootedSinceTick = -1;
			relocationCellDebt = Mathf.Clamp(relocationCellDebt, 0, Mathf.Max(0, MaxCells - CellCount));
			if (relocationCellDebt > 0 && nextRelocationPulseTick <= 0)
				nextRelocationPulseTick = GenTicks.TicksGame + RelocationPulseIntervalTicks();
		}

		void EnsureRenderResources()
		{
			EnsureSymbiantDefaults();
			if (metaballTexture == null)
			{
				metaballTexture = new Texture2D(MetaballTextureMinSize, MetaballTextureMinSize, TextureFormat.RGBA32, false, true)
				{
					name = $"ZombieSymbiantMetaballs_{MetaballTextureMinSize}x{MetaballTextureMinSize}",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear
				};
			}
			EnsureMetaballMaterial();
			if (metaballTexture != null || metaballMaterial != null || mesh != null)
				renderResourceOwners.Add(this);
		}

		void EnsureMetaballMaterial()
		{
			var shader = Assets.ZombieSymbiantShader ?? ShaderDatabase.Transparent;
			if (metaballMaterial == null || metaballMaterial.shader != shader)
			{
				if (metaballMaterial != null)
					UnityEngine.Object.Destroy(metaballMaterial);
				metaballMaterial = new Material(shader)
				{
					name = "ZombieSymbiantMetaballs",
					color = Color.white,
					mainTexture = metaballTexture
				};
			}
			ConfigureMetaballMaterial();
		}

		void ConfigureMetaballMaterial()
		{
			if (metaballMaterial == null)
				return;
			metaballMaterial.name = "ZombieSymbiantMetaballs";
			metaballMaterial.color = Color.white;
			metaballMaterial.mainTexture = metaballTexture;
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantOpacityMinId, SymbiantOpacityMin);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantOpacityMaxId, SymbiantOpacityMax);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantNoiseScaleId, SymbiantNoiseScale);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantWavePhaseSpeedId, SymbiantWavePhaseSpeed);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantWaveShadeStrengthId, SymbiantWaveShadeStrength);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantEdgeContrastId, SymbiantEdgeContrast);
		}

		static void SetMaterialFloatIfPresent(Material material, int propertyId, float value)
		{
			if (material.HasProperty(propertyId))
				material.SetFloat(propertyId, value);
		}

		void UpdateMetaballMaterialTime()
		{
			if (metaballMaterial != null && metaballMaterial.HasProperty(SymbiantNoiseTimeId))
				metaballMaterial.SetFloat(SymbiantNoiseTimeId, RenderNoiseTimeSeconds);
		}

		float GetSize(IntVec3 cell)
		{
			var (x, y) = (cell.x, cell.z);
			var count = 0;
			for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0)
						continue;
					if (cells.Contains(new IntVec3(x + dx, 0, y + dy)))
						count++;
				}
			return elementSizes[count];
		}

		public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
		{
			if (DebugDisableRendering)
				return;
			if (phase == DrawPhase.Draw)
				DrawAt(drawLoc, flip);
		}

		public override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			if (DebugDisableRendering)
				return;
			if (mesh == null || metaballMaterial == null)
				UpdateAll();
			if (mesh == null || metaballMaterial == null)
				return;

			var offset = new Vector3(centerX, 0, centerZ);
			var position = drawLoc + offset;
			position.y = AltitudeLayer.MoteLow.AltitudeFor(SymbiantRenderAltitudeOffset);
			UpdateMetaballMaterialTime();
			Graphics.DrawMesh(mesh, position, Quaternion.identity, metaballMaterial, 0);
		}

		string MaturityInspectLabel()
		{
			if (HasMaturedForSeverance)
				return "SymbiantMaturityReady".Translate();
			return "SymbiantMaturityProgress".Translate(Mathf.FloorToInt(PeakIntegratedVisibleCells), SeveranceMaturityCells);
		}

		string SeveranceInspectLabel()
		{
			if (LinkedHost == null)
				return "SymbiantSeveranceNoHost".Translate();
			if (HasMaturedForSeverance == false)
				return "SymbiantSeveranceNeedsMaturity".Translate();
			if (decouplingReserve < DecouplingReserveMax - 0.01f)
				return "SymbiantSeveranceNeedsReserve".Translate();
			if (CellCount > 3)
				return "SymbiantSeveranceNeedsShrink".Translate();
			return "SymbiantSeveranceReady".Translate();
		}

		string GrowthInspectLabel()
		{
			return GrowthState switch
			{
				"inactive" => "SymbiantGrowthInactive".Translate(),
				"capped" => "SymbiantGrowthCapped".Translate(),
				"pausedAfterFeeding" => "SymbiantGrowthPaused".Translate(),
				"waiting" => "SymbiantGrowthWaiting".Translate(),
				"growing" => "SymbiantGrowthGrowing".Translate(),
				"uprooted" => "SymbiantGrowthUprooted".Translate(),
				"relocating" => "SymbiantGrowthRelocating".Translate(),
				"dormantNoRoom" => "SymbiantGrowthDormantNoRoom".Translate(),
				_ => "SymbiantGrowthContained".Translate()
			};
		}

		public override string GetInspectString()
		{
			var linkedHost = LinkedHost;
			var hostLabel = linkedHost == null ? "none" : linkedHost.LabelShortCap;
			var benefitPercent = Mathf.RoundToInt(BenefitFactor * 100f);
			return "ZombieSymbiantInspect".Translate(
				CellCount,
				MaxCells,
				hostLabel,
				benefitPercent,
				GrowthInspectLabel(),
				MaturityInspectLabel(),
				Mathf.FloorToInt(decouplingReserve),
				DecouplingReserveMax,
				SafeVisibleMinimum,
				FeedPulsesRemaining,
				SeveranceInspectLabel(),
				"SymbiantWeaponInspect".Translate());
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (var gizmo in base.GetGizmos())
				yield return gizmo;

			yield return new Command_Action
			{
				defaultLabel = (feedRequested ? "CancelFeedZombieSymbiant" : "FeedZombieSymbiant").Translate(),
				defaultDesc = "FeedZombieSymbiantDesc".Translate(),
				icon = TexCommand.DesirePower,
				action = () => feedRequested = !feedRequested
			};
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
			Scribe_Collections.Look(ref orderedCells, "orderedCells", LookMode.Value);
			Scribe_Values.Look(ref radius, "radius", elementRadius * 9f);
			Scribe_Values.Look(ref power, "power", elementPower);
			Scribe_Values.Look(ref nextExpansionTick, "nextExpansionTick");
			Scribe_Values.Look(ref feedPausedUntilTick, "feedPausedUntilTick");
			Scribe_Values.Look(ref lastRecessionPulseCells, "lastRecessionPulseCells");
			Scribe_Values.Look(ref relocationCellDebt, "relocationCellDebt");
			Scribe_Values.Look(ref nextRelocationPulseTick, "nextRelocationPulseTick");
			Scribe_Values.Look(ref uprootedSinceTick, "uprootedSinceTick", -1);
			Scribe_Values.Look(ref cancelNextBreach, "cancelNextBreach");
			Scribe_Values.Look(ref feedRequested, "feedRequested");
			Scribe_References.Look(ref host, "host");
			Scribe_Values.Look(ref hostThingId, "hostThingId");
			Scribe_Values.Look(ref decouplingReserve, "decouplingReserve");
			Scribe_Values.Look(ref decouplingFeedDay, "decouplingFeedDay", -1);
			Scribe_Values.Look(ref decouplingFeedPulsesToday, "decouplingFeedPulsesToday");
			Scribe_Values.Look(ref peakVisibleCells, "peakVisibleCells");
			Scribe_Values.Look(ref peakIntegratedVisibleCells, "peakIntegratedVisibleCells");
			Scribe_Values.Look(ref peakBenefitFactor, "peakBenefitFactor");
			Scribe_Values.Look(ref maturedForSeverance, "maturedForSeverance");
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				EnsureSymbiantDefaults();
				if (host != null)
					hostThingId = host.ThingID;
				UpdateSymbiosisState();
				EnsureHostHediff();
			}
		}

		sealed class ExpansionTarget
		{
			public readonly IntVec3 cell;
			public readonly Building wall;
			public readonly float score;

			public ExpansionTarget(IntVec3 cell, Building wall, float score)
			{
				this.cell = cell;
				this.wall = wall;
				this.score = score;
			}
		}
	}
}
