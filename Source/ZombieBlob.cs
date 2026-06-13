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
	public class ZombieBlob : Pawn
	{
		public const int MAX_METABALLS = 4000;
		static readonly Color color = new(0, 0.8f, 0);
		static readonly float elementPower = 1f;
		static readonly float elementRadius = 0.011f;
		static readonly float[] elementSizes = [2.5f, 2.4f, 1.6f, 1.2f, 1f, 0.9f, 0.9f, 1f, 1f];
		static readonly HashSet<ZombieBlob> renderResourceOwners = [];
		static readonly Dictionary<Map, ZombieBlob> activeBlobByMap = [];
		static readonly HashSet<Map> mapsWithoutActiveBlob = [];
		internal static string DebugPerfProfile { get; private set; } = "default";
		internal static bool DebugDisableRendering { get; private set; }
		internal static bool DebugDisableBlobTick { get; private set; }
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
		const float BlobOpacityMin = 0.42f;
		const float BlobOpacityMax = 0.76f;
		const float BlobNoiseScale = 2.00f;
		const float BlobWavePhaseSpeed = 0.45f;
		const float BlobWaveShadeStrength = 0.68f;
		const float BlobEdgeContrast = 0.95f;
		const float BlobNormalTicksPerSecond = 60f;
		const float BlobRenderAltitudeOffset = -0.25f;
		const int SymbiosisMetricRefreshInterval = 250;
		static readonly int BlobOpacityMinId = Shader.PropertyToID("_BlobOpacityMin");
		static readonly int BlobOpacityMaxId = Shader.PropertyToID("_BlobOpacityMax");
		static readonly int BlobNoiseScaleId = Shader.PropertyToID("_BlobNoiseScale");
		static readonly int BlobWavePhaseSpeedId = Shader.PropertyToID("_BlobFlowSpeed");
		static readonly int BlobWaveShadeStrengthId = Shader.PropertyToID("_BlobWaveShadeStrength");
		static readonly int BlobEdgeContrastId = Shader.PropertyToID("_BlobEdgeContrast");
		static readonly int BlobNoiseTimeId = Shader.PropertyToID("_BlobNoiseTime");
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
		int lastRecessionPulseCells;
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
		public bool CancelNextBreach => cancelNextBreach;
		public bool FeedRequested => feedRequested;
		public IEnumerable<IntVec3> AbsoluteCells => orderedCells.Select(cell => Position + cell);
		CellRect AbsoluteCellBounds => relativeCellBounds.MovedBy(Position);
		public override Vector2 DrawSize => hasCellBounds ? drawCullSize : base.DrawSize;
		public int RenderTextureWidth => metaballTexture?.width ?? 0;
		public int RenderTextureHeight => metaballTexture?.height ?? 0;
		public Vector2 RenderWorldSize => new(renderWidth, renderHeight);
		public string RenderShaderName => metaballMaterial?.shader?.name;
		public bool RenderUsesBlobShader => Assets.ZombieBlobShader != null && metaballMaterial?.shader == Assets.ZombieBlobShader;
		public bool RegisteredInMapPawnLists => (MapHeld?.mapPawns?.AllPawnsSpawned?.Contains(this) ?? false);
		public static float RenderOpacityMin => BlobOpacityMin;
		public static float RenderOpacityMax => BlobOpacityMax;
		public static float RenderNoiseScale => BlobNoiseScale;
		public static float RenderWavePhaseSpeed => BlobWavePhaseSpeed;
		public static float RenderWaveShadeStrength => BlobWaveShadeStrength;
		public static float RenderEdgeContrast => BlobEdgeContrast;
		public static float RenderNoiseTimeSeconds => GenTicks.TicksGame / BlobNormalTicksPerSecond;
		public static int MaxCells => Mathf.Clamp(DebugMaxCellsOverride > 0 ? DebugMaxCellsOverride : (ZombieSettings.Values?.blobMaxCells ?? 400), 1, MAX_METABALLS);
		Map BlobMap => Spawned ? Map : host?.MapHeld ?? MapHeld;
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
		public int DecouplingFeedPulsesPerDay => Mathf.Max(1, ZombieSettings.Values?.blobDecouplingFeedPulsesPerDay ?? 2);
		public int FeedPulsesRemaining => Mathf.Max(0, DecouplingFeedPulsesPerDay - DecouplingFeedPulsesToday);
		public bool CanSafelySever => LinkedHost != null && HasMaturedForSeverance && decouplingReserve >= DecouplingReserveMax - 0.01f && CellCount <= 3;
		public static float ZombieIgnoreMinBenefit => Mathf.Clamp(ZombieSettings.Values?.blobZombieIgnoreMinBenefit ?? 0.5f, 0f, 1f);

		internal static object SetDebugPerfProfile(string profile)
		{
			var normalized = (profile ?? "default").Trim().ToLowerInvariant();
			DebugDisableRendering = false;
			DebugDisableBlobTick = false;
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
					DebugDisableBlobTick = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "renderonly":
				case "render-only":
					normalized = "renderOnly";
					DebugDisableBlobTick = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "pathonly":
				case "path-only":
					normalized = "pathOnly";
					DebugDisableRendering = true;
					DebugDisableBlobTick = true;
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
					DebugDisableBlobTick = true;
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
				blobTick = DebugDisableBlobTick == false,
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
			var blob = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieBlob, null) as ZombieBlob;
			blob.Position = cell;
			blob.AddRelativeCell(IntVec3.Zero);
			blob.ResetExpansionClock();
			blob.EnsureRenderResources();
			blob.UpdateAll();

			blob.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(blob, cell, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveBlob(blob, map);

			blob.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Blob));
			_ = blob.TryAssignRandomHost();
			blob.UpdateSymbiosisState();

			if (ZombieSettings.Values.showZombieEventLetters)
			{
				var headline = "LetterLabelZombieBlob".Translate();
				var linkedHost = blob.LinkedHost;
				var text = linkedHost == null ? "ZombieBlobNoHost".Translate() : "ZombieBlob".Translate(linkedHost.LabelShortCap);
				Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, new GlobalTargetInfo(cell, map));
			}

			if (Constants.USE_SOUND && ZombieSettings.Values.playSpecialZombieAmbientSounds && Prefs.VolumeAmbient > 0f)
				CustomDefs.ZombiesRising.PlayOneShotOnCamera(null);
		}

		public static bool TrySpawnInBestRoom(Map map)
		{
			if (map == null || ZombieSettings.Values.blobEnabled == false)
				return false;
			if (ActiveBlob(map) != null)
				return false;
			if (EligibleHosts(map, null).Any() == false)
				return false;

			var room = BestSpawnRoom(map);
			if (room == null)
				return false;

			var cells = room.Cells
				.Where(cell => CanOccupyOpenCell(map, cell))
				.OrderByDescending(cell => ScoreTraffic(map, cell) + ScoreColonyUse(map, cell))
				.ToArray();
			if (cells.Length == 0)
				return false;

			Spawn(map, cells.First());
			return true;
		}

		static Room BestSpawnRoom(Map map)
		{
			return CandidateRooms(map)
				.Select(room => new
				{
					room,
					score = room.Cells.Take(120).Sum(cell => ScoreTraffic(map, cell)) + room.ContainedAndAdjacentThings.Sum(ScoreRoomThing)
				})
				.Where(entry => entry.score > 0f)
				.OrderByDescending(entry => entry.score)
				.FirstOrDefault()?.room;
		}

		static IEnumerable<Room> CandidateRooms(Map map)
		{
			var home = map.areaManager.Home;
			return map.regionGrid.allRooms.Where(room =>
				IsEligibleIndoorRoom(room)
				&& room.Cells.Any(cell => home.TrueCount == 0 || home[cell]));
		}

		static bool CanEverBeLinkedHostFast(Pawn pawn, bool allowDead = false)
		{
			if (pawn == null || pawn.Destroyed)
				return false;
			if (allowDead == false && pawn.Dead)
				return false;
			if (allowDead == false && (pawn.Spawned == false || pawn.Map == null))
				return false;
			if (pawn is Zombie || pawn is ZombieBlob || pawn is ZombieSpitter)
				return false;
			if (pawn.RaceProps?.Humanlike != true || pawn.RaceProps.IsFlesh == false)
				return false;
			if (pawn.Faction?.IsPlayer != true || pawn.IsColonistPlayerControlled == false || pawn.IsPrisoner)
				return false;
			if (pawn.DevelopmentalStage == DevelopmentalStage.Newborn || pawn.DevelopmentalStage == DevelopmentalStage.Baby || pawn.DevelopmentalStage == DevelopmentalStage.Child)
				return false;
			return true;
		}

		internal static bool CanBeAffectedByBlobCellFast(Pawn pawn)
		{
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& pawn.Spawned
				&& pawn.Map != null
				&& pawn is not Zombie
				&& pawn is not ZombieBlob
				&& pawn is not ZombieSpitter
				&& pawn.RaceProps?.Humanlike == true
				&& pawn.Faction?.IsPlayer == true
				&& pawn.IsColonistPlayerControlled
				&& IsLinkedHostOnCurrentMapFast(pawn) == false;
		}

		internal static bool CanBeSlowedByBlobCellFast(Pawn pawn)
		{
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& pawn.Spawned
				&& pawn.Map != null
				&& pawn.Flying == false
				&& pawn.RaceProps?.doesntMove != true
				&& pawn is not Zombie
				&& pawn is not ZombieBlob
				&& pawn is not ZombieSpitter
				&& IsLinkedHostOnCurrentMapFast(pawn) == false;
		}

		static bool IsLinkedHostOnCurrentMapFast(Pawn pawn)
		{
			if (CanEverBeLinkedHostFast(pawn) == false)
				return false;
			return ActiveBlob(pawn.Map)?.IsLinkedTo(pawn) == true;
		}

		static bool IsActiveBlobOnMap(ZombieBlob blob, Map map)
		{
			return blob != null && blob.Destroyed == false && blob.Spawned && blob.Dead == false && blob.Map == map;
		}

		static void RegisterActiveBlob(ZombieBlob blob, Map map)
		{
			if (blob == null || map == null)
				return;
			activeBlobByMap[map] = blob;
			mapsWithoutActiveBlob.Remove(map);
		}

		static void ForgetActiveBlob(ZombieBlob blob)
		{
			foreach (var map in activeBlobByMap
				.Where(pair => ReferenceEquals(pair.Value, blob))
				.Select(pair => pair.Key)
				.ToArray())
				activeBlobByMap.Remove(map);
		}

		public static ZombieBlob ActiveBlob(Map map)
		{
			if (map == null)
				return null;
			if (activeBlobByMap.TryGetValue(map, out var cached))
			{
				if (IsActiveBlobOnMap(cached, map))
					return cached;
				activeBlobByMap.Remove(map);
			}
			if (mapsWithoutActiveBlob.Contains(map))
				return null;

			foreach (var blob in SpawnedBlobThings(map))
			{
				if (IsActiveBlobOnMap(blob, map))
				{
					blob.EnsureHiddenFromPawnSystems(map);
					RegisterActiveBlob(blob, map);
					return blob;
				}
			}
			mapsWithoutActiveBlob.Add(map);
			return null;
		}

		static IEnumerable<ZombieBlob> SpawnedBlobThings(Map map)
		{
			var lister = map?.listerThings;
			if (lister == null)
				yield break;

			var def = CustomDefs.ZombieBlob;
			if (def != null)
			{
				var things = lister.ThingsOfDef(def);
				if (things != null)
					for (var i = 0; i < things.Count; i++)
						if (things[i] is ZombieBlob blob)
							yield return blob;
				yield break;
			}

			foreach (var thing in lister.AllThings)
				if (thing is ZombieBlob blob)
					yield return blob;
		}

		static IEnumerable<ZombieBlob> ActiveBlobs()
		{
			if (Find.Maps == null)
				yield break;
			foreach (var map in Find.Maps)
			{
				var blob = ActiveBlob(map);
				if (blob != null)
					yield return blob;
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

		public static ZombieBlob LinkedBlobFor(Pawn pawn)
		{
			return LinkedBlobFor(pawn, false);
		}

		static ZombieBlob LinkedBlobFor(Pawn pawn, bool allowDead)
		{
			if (CanEverBeLinkedHostFast(pawn, allowDead) == false)
				return null;
			if (pawn.Spawned)
			{
				var mapBlob = ActiveBlob(pawn.Map);
				if (mapBlob != null && mapBlob.IsLinkedTo(pawn))
					return mapBlob;
			}
			return ActiveBlobs().FirstOrDefault(blob => blob.IsLinkedTo(pawn) || blob.ResolveHost() == pawn);
		}

		public static bool HasZombieTargetingProtection(Pawn pawn)
		{
			if (pawn?.Spawned != true || pawn.Map == null)
				return false;
			if (DebugDisableSymbiosisBenefits || CanEverBeLinkedHostFast(pawn) == false)
				return false;
			return SymbioteBenefitFactor(pawn) >= ZombieIgnoreMinBenefit;
		}

		public static float SymbioteBenefitFactor(Pawn pawn)
		{
			if (pawn?.Spawned != true || pawn.Map == null)
				return 0f;
			if (DebugDisableSymbiosisBenefits || CanEverBeLinkedHostFast(pawn) == false)
				return 0f;
			return LinkedBlobFor(pawn)?.BenefitFactor ?? 0f;
		}

		public static void ApplySymbioteSkillBonus(SkillRecord skill, ref int level)
		{
			var pawn = skill?.Pawn;
			var factor = SymbioteBenefitFactor(pawn);
			if (factor <= 0f)
				return;
			var bonus = Mathf.RoundToInt(Mathf.Max(0, ZombieSettings.Values.blobSymbioteMaxSkillBonus) * factor);
			if (bonus > 0)
				level = Mathf.Clamp(level + bonus, 0, SkillRecord.MaxLevel);
		}

		public static bool CanSeverSymbiosis(Pawn pawn)
		{
			if (CanEverBeLinkedHostFast(pawn) == false)
				return false;
			return LinkedBlobFor(pawn)?.CanSafelySever == true;
		}

		public static void NotifyHostKilled(Pawn pawn)
		{
			if (CanEverBeLinkedHostFast(pawn, true) == false)
				return;
			var blob = LinkedBlobFor(pawn, true);
			if (blob == null)
				return;
			blob.CollapseFromHostDeath();
		}

		public static bool IsBlobCell(Map map, IntVec3 cell, out ZombieBlob blob)
		{
			blob = null;
			if (map == null || cell.InBounds(map) == false)
				return false;
			blob = ActiveBlob(map);
			return blob != null && blob.ContainsCell(cell);
		}

		public static bool IsBlobCellForAffectedPawn(Pawn pawn, IntVec3 cell, out ZombieBlob blob)
		{
			blob = null;
			if (CanBeAffectedByBlobCellFast(pawn) == false)
				return false;
			var map = pawn.Map;
			if (cell.InBounds(map) == false)
				return false;
			blob = ActiveBlob(map);
			if (blob == null)
				return false;
			return blob.ContainsCell(cell);
		}

		public static bool IsBlobCellForSlowedPawn(Pawn pawn, IntVec3 cell, out ZombieBlob blob)
		{
			blob = null;
			if (CanBeSlowedByBlobCellFast(pawn) == false)
				return false;
			var map = pawn.Map;
			if (cell.InBounds(map) == false)
				return false;
			blob = ActiveBlob(map);
			if (blob == null)
				return false;
			return blob.ContainsCell(cell);
		}

		public static int CountCellsInRoom(Room room)
		{
			var map = room?.Map;
			if (map == null)
				return 0;
			var blob = ActiveBlob(map);
			if (blob == null)
				return 0;
			return blob.CountCellsInRoomInternal(room);
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
			return CandidateRooms(map).Sum(room => room.CellCount);
		}

		static int CalculateFullBenefitCells(Map map)
		{
			return CalculateFullBenefitCells(EligibleColonyRoomCellCount(map));
		}

		static int CalculateFullBenefitCells(int eligibleCells)
		{
			var maxCells = Mathf.Max(1, MaxCells);
			var coverage = Mathf.Clamp(ZombieSettings.Values?.blobFullBenefitRoomCoverage ?? 0.20f, 0.01f, 1f);
			var target = Mathf.Max(20, Mathf.CeilToInt(eligibleCells * coverage));
			return Mathf.Clamp(target, 1, maxCells);
		}

		int CalculateSeveranceMaturityCells(Map map)
		{
			return CalculateSeveranceMaturityCells(CalculateFullBenefitCells(map));
		}

		int CalculateSeveranceMaturityCells(int fullBenefitCells)
		{
			var settings = ZombieSettings.Values;
			var coverage = Mathf.Clamp(settings?.blobSeveranceMaturityCoverage ?? 0.50f, 0.01f, 1f);
			var min = Mathf.Max(1, settings?.blobSeveranceMaturityMinCells ?? 10);
			var max = Mathf.Max(min, settings?.blobSeveranceMaturityMaxCells ?? 80);
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
			var coverage = Mathf.Clamp(settings?.blobSeveranceReserveCoverage ?? 0.25f, 0.01f, 1f);
			var min = Mathf.Max(1, settings?.blobSeveranceReserveMin ?? 12);
			var max = Mathf.Max(min, settings?.blobSeveranceReserveMax ?? 60);
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
			return 0.25f;
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
			var map = BlobMap;
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
			var map = BlobMap;
			if (map == null)
				return false;
			var candidates = EligibleHosts(map, this).ToArray();
			if (candidates.Length == 0)
				return false;
			AssignHost(candidates.RandomElement());
			return true;
		}

		static IEnumerable<Pawn> EligibleHosts(Map map, ZombieBlob blob)
		{
			if (map?.mapPawns?.FreeColonistsSpawned == null)
				return Enumerable.Empty<Pawn>();
			return map.mapPawns.FreeColonistsSpawned.Where(pawn => IsEligibleHost(pawn, blob));
		}

		static bool IsEligibleHost(Pawn pawn, ZombieBlob blob)
		{
			if (pawn?.Spawned != true)
				return false;
			if (CanEverBeLinkedHostFast(pawn) == false)
				return false;
			if (AlienTools.IsFleshPawn(pawn) == false || SoSTools.IsHologram(pawn))
				return false;
			if (pawn.InfectionState() >= InfectionState.Infecting)
				return false;
			var linkedBlob = LinkedBlobFor(pawn);
			return linkedBlob == null || linkedBlob == blob;
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
			EnsureHostHediff();
		}

		void EnsureHostHediff()
		{
			if (DebugDisableHostHediffSync)
				return;
			var pawn = ResolveHost();
			if (pawn?.health?.hediffSet == null || CustomDefs.BlobSymbiosis == null)
				return;
			var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(CustomDefs.BlobSymbiosis) as Hediff_BlobSymbiosis;
			if (hediff == null)
			{
				hediff = HediffMaker.MakeHediff(CustomDefs.BlobSymbiosis, pawn) as Hediff_BlobSymbiosis;
				if (hediff != null)
					pawn.health.AddHediff(hediff);
			}
			if (hediff != null)
			{
				hediff.blobThingId = ThingID;
				hediff.Severity = BenefitFactor;
			}
		}

		static void RemoveHostHediff(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null || CustomDefs.BlobSymbiosis == null)
				return;
			foreach (var hediff in pawn.health.hediffSet.hediffs
				.Where(hediff => hediff.def == CustomDefs.BlobSymbiosis)
				.ToArray())
				pawn.health.RemoveHediff(hediff);
		}

		public static void AddCell(Map map, IntVec3 cell)
		{
			ActiveBlob(map)?.AddCell(cell);
		}

		public static int AddCells(Map map, IEnumerable<IntVec3> newCells)
		{
			if (map == null)
				return 0;
			var newCellArray = newCells?.ToArray() ?? Array.Empty<IntVec3>();
			if (newCellArray.Length == 0)
				return 0;
			return ActiveBlob(map)?.AddCells(newCellArray) ?? 0;
		}

		internal static void ReleaseAllRenderResources()
		{
			foreach (var blob in renderResourceOwners.ToArray())
				blob.ReleaseRenderResources(false);
			renderResourceOwners.Clear();
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
			EnsureBlobDefaults();
			RegisterActiveBlob(this, map);
			EnsureHiddenFromPawnSystems(map);
		}

		void EnsureHiddenFromPawnSystems(Map map = null)
		{
			map ??= MapHeld;
			map?.mapPawns?.DeRegisterPawn(this);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			ForgetActiveBlob(this);
			ReleaseRenderResources();
			base.DeSpawn(mode);
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			ForgetActiveBlob(this);
			if (safeSeveranceInProgress == false && hostCollapseInProgress == false)
				HandleUncontrolledDestroy();
			ReleaseRenderResources();
			base.Destroy(mode);
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

		void HandleUncontrolledDestroy()
		{
			if (uncontrolledDestroyHandled)
				return;
			uncontrolledDestroyHandled = true;

			var pawn = ResolveHost();
			if (pawn == null || pawn.Destroyed || pawn.Dead)
				return;

			var requiredReserve = DecouplingReserveMax;
			var effectiveReserve = EffectiveDecouplingReserve;
			if (effectiveReserve >= requiredReserve - 0.01f)
			{
				decouplingReserve = 0f;
				ApplyHostTrauma(Mathf.Max(12f, requiredReserve * 0.4f), false);
			}
			else
			{
				hostCollapseInProgress = true;
				pawn.Kill(null);
				hostCollapseInProgress = false;
			}

			RemoveHostHediff(pawn);
			host = null;
			hostThingId = null;
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
			RemoveHostHediff(pawn);
			host = null;
			hostThingId = null;
			Destroy(DestroyMode.Vanish);
			hostCollapseInProgress = false;
		}

		void ApplyHostTrauma(float amount, bool killIfOverwhelmed)
		{
			var pawn = ResolveHost();
			if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.health == null)
				return;
			if (killIfOverwhelmed)
			{
				hostCollapseInProgress = true;
				pawn.Kill(null);
				hostCollapseInProgress = false;
				return;
			}
			var damage = Mathf.Clamp(amount, 1f, 80f);
			_ = pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, 0f, -1f, this));
		}

		public void PreApplyLinkedDamage(ref DamageInfo dinfo)
		{
			if (safeSeveranceInProgress || hostCollapseInProgress || ResolveHost() == null)
				return;
			var amount = dinfo.Amount;
			if (amount <= 0f)
				return;
			var effectiveReserve = EffectiveDecouplingReserve;
			if (effectiveReserve > 0f)
			{
				var absorbed = Mathf.Min(effectiveReserve, amount);
				decouplingReserve -= absorbed;
				amount -= absorbed;
			}
			if (amount <= 0f)
			{
				dinfo.SetAmount(0f);
				return;
			}
			ApplyHostTrauma(amount * 0.5f, false);
			dinfo.SetAmount(amount);
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
			RemoveHostHediff(pawn);
			host = null;
			hostThingId = null;
			Destroy(DestroyMode.Vanish);
			return true;
		}

		public void BlobTick()
		{
			if (DebugDisableBlobTick)
				return;
			var ticks = GenTicks.TicksGame;
			if (ticks % SymbiosisMetricRefreshInterval == Mathf.Abs(thingIDNumber % SymbiosisMetricRefreshInterval))
			{
				EnsureHostLink();
				UpdateSymbiosisState(false);
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
			var hours = Mathf.Max(1, ZombieSettings.Values.blobExpansionIntervalHours);
			nextExpansionTick = GenTicks.TicksGame + hours * GenDate.TicksPerHour;
		}

		public bool TryExpansionPulse()
		{
			if (CanExpand() == false)
				return false;

			var target = FindExpansionTarget();
			if (target == null)
				return false;

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			AddCell(target.cell);
			return true;
		}

		ExpansionTarget FindExpansionTarget()
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

					if (ZombieSettings.Values.blobCanBreakConstructedWalls == false)
						continue;

					var wall = BreakableConstructedWall(map, candidate);
					if (wall == null)
						continue;

					var beyond = candidate + direction;
					var adjacentOpen = GenAdj.CardinalDirections
						.Select(dir => candidate + dir)
						.Any(adjacent => ContainsCell(adjacent) == false && (CanOccupyOpenCell(map, adjacent) || IsDoorCell(map, adjacent)));
					if ((beyond.InBounds(map) && (CanOccupyOpenCell(map, beyond) || IsDoorCell(map, beyond))) || adjacentOpen)
						targets.Add(new ExpansionTarget(candidate, wall, ScoreExpansionCell(map, beyond.InBounds(map) ? beyond : candidate) + 150f));
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
				return false;

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
			feedPausedUntilTick = GenTicks.TicksGame + Mathf.Max(0, ZombieSettings.Values.blobPostFeedPauseHours) * GenDate.TicksPerHour;
			if (Spawned)
			{
				CustomDefs.ZombieEating.PlayOneShot(SoundInfo.InMap(this));
				MoteMaker.ThrowText(DrawPos, Map, "BlobFedMote".Translate(pulseSize, lastRecessionPulseCells), 3.65f);
			}
			return true;
		}

		int RecessionPulseSize(Thing feed)
		{
			var baseSize = 1;
			if (feed.def == CustomDefs.BlobCoagulantPack)
			{
				baseSize = ZombieSettings.Values.blobCoagulantPotency switch
				{
					BlobCoagulantPotency.Cheap => 2,
					BlobCoagulantPotency.Expensive => 5,
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
			if (feed.def == CustomDefs.BlobCoagulantPack)
				return true;
			if (feed is Corpse corpse)
			{
				var pawn = corpse.InnerPawn;
				if (pawn == null || pawn.RaceProps?.Humanlike != true)
					return false;
				return pawn is not Zombie && pawn is not ZombieBlob && pawn is not ZombieSpitter;
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
			EnsureBlobDefaults();
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
				name = $"ZombieBlobMetaballs_{textureWidth}x{textureHeight}",
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

		void EnsureBlobDefaults()
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
		}

		void EnsureRenderResources()
		{
			EnsureBlobDefaults();
			if (metaballTexture == null)
			{
				metaballTexture = new Texture2D(MetaballTextureMinSize, MetaballTextureMinSize, TextureFormat.RGBA32, false, true)
				{
					name = $"ZombieBlobMetaballs_{MetaballTextureMinSize}x{MetaballTextureMinSize}",
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
			var shader = Assets.ZombieBlobShader ?? ShaderDatabase.Transparent;
			if (metaballMaterial == null || metaballMaterial.shader != shader)
			{
				if (metaballMaterial != null)
					UnityEngine.Object.Destroy(metaballMaterial);
				metaballMaterial = new Material(shader)
				{
					name = "ZombieBlobMetaballs",
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
			metaballMaterial.name = "ZombieBlobMetaballs";
			metaballMaterial.color = Color.white;
			metaballMaterial.mainTexture = metaballTexture;
			SetMaterialFloatIfPresent(metaballMaterial, BlobOpacityMinId, BlobOpacityMin);
			SetMaterialFloatIfPresent(metaballMaterial, BlobOpacityMaxId, BlobOpacityMax);
			SetMaterialFloatIfPresent(metaballMaterial, BlobNoiseScaleId, BlobNoiseScale);
			SetMaterialFloatIfPresent(metaballMaterial, BlobWavePhaseSpeedId, BlobWavePhaseSpeed);
			SetMaterialFloatIfPresent(metaballMaterial, BlobWaveShadeStrengthId, BlobWaveShadeStrength);
			SetMaterialFloatIfPresent(metaballMaterial, BlobEdgeContrastId, BlobEdgeContrast);
		}

		static void SetMaterialFloatIfPresent(Material material, int propertyId, float value)
		{
			if (material.HasProperty(propertyId))
				material.SetFloat(propertyId, value);
		}

		void UpdateMetaballMaterialTime()
		{
			if (metaballMaterial != null && metaballMaterial.HasProperty(BlobNoiseTimeId))
				metaballMaterial.SetFloat(BlobNoiseTimeId, RenderNoiseTimeSeconds);
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
			position.y = AltitudeLayer.MoteLow.AltitudeFor(BlobRenderAltitudeOffset);
			UpdateMetaballMaterialTime();
			Graphics.DrawMesh(mesh, position, Quaternion.identity, metaballMaterial, 0);
		}

		public override string GetInspectString()
		{
			var linkedHost = LinkedHost;
			var hostLabel = linkedHost == null ? "none" : linkedHost.LabelShortCap;
			var benefitPercent = Mathf.RoundToInt(BenefitFactor * 100f);
			return "ZombieBlobInspect".Translate(CellCount, MaxCells, hostLabel, benefitPercent, Mathf.FloorToInt(decouplingReserve), DecouplingReserveMax, FeedPulsesRemaining);
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (var gizmo in base.GetGizmos())
				yield return gizmo;

			yield return new Command_Action
			{
				defaultLabel = (feedRequested ? "CancelFeedZombieBlob" : "FeedZombieBlob").Translate(),
				defaultDesc = "FeedZombieBlobDesc".Translate(),
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
				EnsureBlobDefaults();
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
