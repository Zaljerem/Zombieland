using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class ZombieBlob : Pawn
	{
		public const int MAX_METABALLS = 800;
		static readonly Color color = new(0, 0.8f, 0);
		static readonly float elementPower = 1f;
		static readonly float elementRadius = 0.011f;
		static readonly float[] elementSizes = [2.5f, 2.4f, 1.6f, 1.2f, 1f, 0.9f, 0.9f, 1f, 1f];
		static readonly HashSet<ZombieBlob> renderResourceOwners = [];
		// static readonly float[] elementSizes = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];

		struct Metaball
		{
			public float radius;
			public float size;
			public float power;
			public Vector2 position;
			public Vector2 direction;
			public Vector4 color;
		}

		HashSet<IntVec3> cells = [];
		List<IntVec3> orderedCells = [];
		readonly List<Metaball> metaballs = [];
		ComputeBuffer metaballBuffer;

		Mesh mesh = null;
		Material metaballMaterial;

		float radius, power, centerX, centerZ;
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

		public int CellCount => cells?.Count ?? 0;
		public int NextExpansionTick => nextExpansionTick;
		public int FeedPausedUntilTick => feedPausedUntilTick;
		public int LastRecessionPulseCells => lastRecessionPulseCells;
		public bool CancelNextBreach => cancelNextBreach;
		public bool FeedRequested => feedRequested;
		public IEnumerable<IntVec3> AbsoluteCells => orderedCells.Select(cell => Position + cell);
		public static int MaxCells => Mathf.Clamp(ZombieSettings.Values?.blobMaxCells ?? 400, 1, MAX_METABALLS);
		Map BlobMap => Spawned ? Map : host?.MapHeld ?? MapHeld;
		public Pawn LinkedHost => ResolveHost();
		public string HostThingId => hostThingId;
		public int EligibleColonyRoomCells => EligibleColonyRoomCellCount(BlobMap);
		public int FullBenefitCells => CalculateFullBenefitCells(BlobMap);
		public float IntegratedVisibleCells => CalculateIntegratedVisibleCells(BlobMap);
		public int PeakVisibleCells => peakVisibleCells;
		public float PeakIntegratedVisibleCells => peakIntegratedVisibleCells;
		public float PeakBenefitFactor => peakBenefitFactor;
		public int SeveranceMaturityCells => CalculateSeveranceMaturityCells(BlobMap);
		public bool HasMaturedForSeverance => maturedForSeverance || PeakMeetsSeveranceMaturity();
		public int SeveranceReserveRequired => CalculateSeveranceReserveRequired(BlobMap);
		public float ReserveMaturityFactor => HasMaturedForSeverance ? 1f : Mathf.Clamp01(peakIntegratedVisibleCells / Mathf.Max(1f, SeveranceMaturityCells));
		public float EffectiveDecouplingReserve => Mathf.Min(decouplingReserve, DecouplingReserveMax * ReserveMaturityFactor);
		public float BenefitFactor => CalculateBenefitFactor(BlobMap);
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

		public static ZombieBlob ActiveBlob(Map map)
		{
			return map?.mapPawns?.AllPawns?.OfType<ZombieBlob>().FirstOrDefault(blob => blob.Destroyed == false && blob.Spawned && blob.Dead == false);
		}

		static IEnumerable<ZombieBlob> ActiveBlobs()
		{
			if (Find.Maps == null)
				yield break;
			foreach (var map in Find.Maps)
			{
				var pawns = map?.mapPawns?.AllPawns;
				if (pawns == null)
					continue;
				foreach (var blob in pawns.OfType<ZombieBlob>())
					if (blob.Destroyed == false && blob.Spawned && blob.Dead == false)
						yield return blob;
			}
		}

		public static ZombieBlob LinkedBlobFor(Pawn pawn)
		{
			if (pawn == null)
				return null;
			return ActiveBlobs()
				.FirstOrDefault(blob => blob.ResolveHost() == pawn || (blob.hostThingId.NullOrEmpty() == false && blob.hostThingId == pawn.ThingID));
		}

		public static bool HasZombieTargetingProtection(Pawn pawn)
		{
			return SymbioteBenefitFactor(pawn) >= ZombieIgnoreMinBenefit;
		}

		public static float SymbioteBenefitFactor(Pawn pawn)
		{
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
			return LinkedBlobFor(pawn)?.CanSafelySever == true;
		}

		public static void NotifyHostKilled(Pawn pawn)
		{
			var blob = LinkedBlobFor(pawn);
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

		public static int CountCellsInRoom(Room room)
		{
			var map = room?.Map;
			if (map == null)
				return 0;
			var blob = ActiveBlob(map);
			if (blob == null)
				return 0;
			return blob.AbsoluteCells.Count(cell => cell.InBounds(map) && cell.GetRoom(map) == room);
		}

		static int EligibleColonyRoomCellCount(Map map)
		{
			if (map == null)
				return 0;
			return CandidateRooms(map).Sum(room => room.CellCount);
		}

		static int CalculateFullBenefitCells(Map map)
		{
			var maxCells = Mathf.Max(1, MaxCells);
			var coverage = Mathf.Clamp(ZombieSettings.Values?.blobFullBenefitRoomCoverage ?? 0.20f, 0.01f, 1f);
			var eligibleCells = EligibleColonyRoomCellCount(map);
			var target = Mathf.Max(20, Mathf.CeilToInt(eligibleCells * coverage));
			return Mathf.Clamp(target, 1, maxCells);
		}

		int CalculateSeveranceMaturityCells(Map map)
		{
			var settings = ZombieSettings.Values;
			var coverage = Mathf.Clamp(settings?.blobSeveranceMaturityCoverage ?? 0.50f, 0.01f, 1f);
			var min = Mathf.Max(1, settings?.blobSeveranceMaturityMinCells ?? 10);
			var max = Mathf.Max(min, settings?.blobSeveranceMaturityMaxCells ?? 80);
			var target = Mathf.CeilToInt(CalculateFullBenefitCells(map) * coverage);
			var upper = Mathf.Max(1, Mathf.Min(max, MaxCells));
			var lower = Mathf.Min(min, upper);
			return Mathf.Clamp(target, lower, upper);
		}

		int CalculateSeveranceReserveRequired(Map map)
		{
			var settings = ZombieSettings.Values;
			var coverage = Mathf.Clamp(settings?.blobSeveranceReserveCoverage ?? 0.25f, 0.01f, 1f);
			var min = Mathf.Max(1, settings?.blobSeveranceReserveMin ?? 12);
			var max = Mathf.Max(min, settings?.blobSeveranceReserveMax ?? 60);
			var target = Mathf.CeilToInt(CalculateFullBenefitCells(map) * coverage);
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

		void UpdateSymbiosisState()
		{
			if (Destroyed)
				return;
			peakVisibleCells = Mathf.Max(peakVisibleCells, CellCount);
			var integratedVisibleCells = IntegratedVisibleCells;
			peakIntegratedVisibleCells = Mathf.Max(peakIntegratedVisibleCells, integratedVisibleCells);
			peakBenefitFactor = Mathf.Max(peakBenefitFactor, BenefitFactor);
			if (PeakMeetsSeveranceMaturity())
				maturedForSeverance = true;
			decouplingReserve = Mathf.Min(decouplingReserve, DecouplingReserveMax);
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
			if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Spawned == false)
				return false;
			if (pawn is Zombie || pawn is ZombieBlob || pawn is ZombieSpitter)
				return false;
			if (pawn.Faction != Faction.OfPlayer || pawn.IsColonistPlayerControlled == false || pawn.IsPrisoner)
				return false;
			if (pawn.DevelopmentalStage == DevelopmentalStage.Newborn || pawn.DevelopmentalStage == DevelopmentalStage.Baby || pawn.DevelopmentalStage == DevelopmentalStage.Child)
				return false;
			if (pawn.RaceProps?.Humanlike != true || pawn.RaceProps.IsFlesh == false)
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
			var blob = map.mapPawns.AllPawns.OfType<ZombieBlob>()
				.OrderBy(blob => blob.Position.DistanceTo(cell))
				.FirstOrDefault();
			blob?.AddCell(cell);
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
			if (metaballBuffer != null)
			{
				metaballBuffer.Dispose();
				metaballBuffer = null;
			}
			if (mesh != null)
			{
				UnityEngine.Object.Destroy(mesh);
				mesh = null;
			}
			if (unregister)
				renderResourceOwners.Remove(this);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			ReleaseRenderResources();
			base.DeSpawn(mode);
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			if (safeSeveranceInProgress == false && hostCollapseInProgress == false)
				HandleUncontrolledDestroy();
			ReleaseRenderResources();
			base.Destroy(mode);
		}

		void AddRelativeCell(IntVec3 relative)
		{
			if (cells.Add(relative))
				orderedCells.Add(relative);
		}

		void AddCell(IntVec3 newCell)
		{
			if (CellCount >= MaxCells)
				return;
			AddRelativeCell(newCell - Position);
			UpdateAll();
			UpdateSymbiosisState();
		}

		public bool ContainsCell(IntVec3 absoluteCell)
		{
			EnsureBlobDefaults();
			return cells.Contains(absoluteCell - Position);
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
			EnsureHostLink();
			UpdateSymbiosisState();
			if (CanExpand() == false)
				return;
			var ticks = GenTicks.TicksGame;
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
				UpdateAll();
			return removed;
		}

		void UpdateAll()
		{
			EnsureRenderResources();

			var min_x = cells.Min(c => c.x) - 1f;
			var min_z = cells.Min(c => c.z) - 1f;
			var max_x = cells.Max(c => c.x) + 1f;
			var max_z = cells.Max(c => c.z) + 1f;

			centerX = (min_x + max_x) / 2;
			centerZ = (min_z + max_z) / 2;

			var dx = max_x - min_x;
			var dz = max_z - min_z;
			var totalSize = Mathf.Max(dx, dz);
			if (dx < totalSize)
			{
				min_x -= (totalSize - dx) / 2;
				max_x += (totalSize - dx) / 2;
			}
			if (dz < totalSize)
			{
				min_z -= (totalSize - dz) / 2;
				max_z += (totalSize - dz) / 2;
			}

			var size2 = new Vector2(totalSize, totalSize);

			if (mesh != null)
				UnityEngine.Object.Destroy(mesh);
			mesh = MeshMakerPlanes.NewPlaneMesh(size2, false, false, false);

			var allCells = cells.ToArray();
			var cellCount = Mathf.Min(allCells.Length, MAX_METABALLS);

			while (metaballs.Count < cellCount)
			{
				var cell = allCells[metaballs.Count];
				var x = GenMath.LerpDouble(min_x, max_x, 0, 1, cell.x);
				var y = GenMath.LerpDouble(min_z, max_z, 0, 1, cell.z);
				metaballs.Add(new()
				{
					position = new Vector2(x, y),
					direction = Vector2.zero,
					color = color,
				});
			}
			while (metaballs.Count > cellCount)
				metaballs.RemoveLast();

			for (var i = 0; i < cellCount; i++)
			{
				var mb = metaballs[i];
				mb.radius = radius;
				mb.power = power;
				mb.size = GetSize(allCells[i]) / totalSize;
				metaballs[i] = mb;
			}

			metaballBuffer?.SetData(metaballs, 0, 0, metaballs.Count);
			metaballMaterial?.SetBuffer("_MetaballBuffer", metaballBuffer);
			metaballMaterial?.SetInt("_MetaballCount", metaballs.Count);
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
			metaballBuffer ??= new ComputeBuffer(MAX_METABALLS, Marshal.SizeOf(typeof(Metaball)));
			if (metaballMaterial == null && Assets.MetaballShader != null)
				metaballMaterial = new Material(Assets.MetaballShader);
			if (metaballBuffer != null || metaballMaterial != null || mesh != null)
				renderResourceOwners.Add(this);
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

		public override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			if (mesh == null || metaballMaterial == null)
				UpdateAll();
			if (mesh == null || metaballMaterial == null)
				return;

			var offset = new Vector3(centerX, 0, centerZ);
			Graphics.DrawMesh(mesh, drawLoc + offset, Quaternion.identity, metaballMaterial, 0);
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
