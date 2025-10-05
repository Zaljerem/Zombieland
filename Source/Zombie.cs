﻿using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public enum NthTick
	{
		// never use one value more than once per tick cycle and zombie!
		Every2,
		Every10,
		Every12,
		Every45,
		Every50,
		Every60,
		Every480,
		Every960
	}

	public class Verb_Shock : Verb
	{
		        protected override bool TryCastShot()		{
			return true;
		}
	}

	public class ZombieSerum : ThingWithComps
	{
	}

	public class ZombieExtract : ThingWithComps
	{
	}

	public class HealerInfo : IExposable
	{
		public int step;
		public Pawn pawn;

		public void ExposeData()
		{
			Scribe_Values.Look(ref step, "step");
			Scribe_References.Look(ref pawn, "pawn");
		}

		public HealerInfo(Pawn pawn)
		{
			step = 0;
			this.pawn = pawn;
		}
	}

	[StaticConstructorOnStartup]
	public class Zombie : Pawn, IDisposable
	{
		public ZombieState state = ZombieState.Emerging;
		public int raging;
		public IntVec3 wanderDestination = IntVec3.Invalid;
		public static Color[] zombieColors;

		int rubbleTicks = Rand.Range(0, 60);
		public int rubbleCounter;
		List<Rubble> rubbles = new();

		public IntVec2 sideEyeOffset;
		public bool wasMapPawnBefore;
		public IntVec3 lastGotoPosition = IntVec3.Invalid;
		public bool isHealing = false;
		public float consciousness = 1f;
		public int paralyzedUntil = 0;
		public Pawn ropedBy;
		public bool IsConfused => Downed == false && ropedBy == null && (paralyzedUntil > 0 || consciousness <= Constants.MIN_CONSCIOUSNESS);
		public bool IsRopedOrConfused => Downed == false && (paralyzedUntil > 0 || consciousness <= Constants.MIN_CONSCIOUSNESS || ropedBy != null);

		// being pushed over walls
		public float wallPushProgress = -1f;
		public Vector3 wallPushStart;
		public Vector3 wallPushDestination;
		public int wallPushCooldown = 0;

		// suicide bomber
		public float bombTickingInterval = -1f;
		public bool bombWillGoOff;
		public int lastBombTick;
		public bool IsSuicideBomber => bombTickingInterval != -1;

		// toxic splasher
		public bool isToxicSplasher = false;

		// tanky operator
		public float hasTankyShield = -1f;
		public float hasTankyHelmet = -1f;
		public float hasTankySuit = -1f;
		public IntVec3 tankDestination = IntVec3.Invalid;
		public bool IsTanky => hasTankyHelmet > 0f || hasTankySuit > 0f;

		// miner
		public bool isMiner = false;
		public int miningCounter = 0;

		// electrifier
		public bool isElectrifier = false;
		public int electricDisabledUntil = 0;
		public bool IsActiveElectric => isElectrifier && Downed == false && GenTicks.TicksGame > electricDisabledUntil && this.InWater() == false;
		public void DisableElectric(int ticks) { electricDisabledUntil = GenTicks.TicksGame + ticks; }
		public int electricCounter = -1000;
		public float electricAngle = 0;
		public List<KeyValuePair<float, int>> absorbAttack = new();

		// albino
		public bool isAlbino = false;
		public int scream = -1;

		// dark slimer
		public bool isDarkSlimer = false;

		// healer
		public bool isHealer = false;
		public List<HealerInfo> healInfo = new();

		// transient vars
		public bool needsGraphics = false;
		public bool isOnFire = false;
		public bool checkSmashable = true;
		public float currentDownedAngle = 0f;
		public VariableGraphic customBodyGraphic;
		public VariableGraphic customHeadGraphic;
		bool disposed = false;

		public ZombieStateHandler.TrackMove[] topTrackingMoves = new ZombieStateHandler.TrackMove[Constants.NUMBER_OF_TOP_MOVEMENT_PICKS];
		public readonly int[] adjIndex8 = { 0, 1, 2, 3, 4, 5, 6, 7 };
		public int prevIndex8;

		static readonly int totalNthTicks;
		static public int[] nthTickValues;

		static Zombie()
		{
			var nths = Enum.GetNames(typeof(NthTick));
			totalNthTicks = nths.Length;
			nthTickValues = new int[totalNthTicks];
			for (var n = 0; n < totalNthTicks; n++)
			{
				var vstr = nths[n].ReplaceFirst("Every", "");
				nthTickValues[n] = int.Parse(vstr);
			}

			zombieColors = new Color[]
			{
				"442a0a".HexColor(),
				"615951".HexColor(),
				"1f4960".HexColor(),
				"182a64".HexColor(),
				"73000d".HexColor(),
				"2c422a".HexColor(),
				"332341".HexColor()
			};
			(zombieColors.Clone() as Color[]).Do(c =>
			{
				c.r *= Rand.Range(0.2f, 1f);
				c.g *= Rand.Range(0.2f, 1f);
				c.b *= Rand.Range(0.2f, 1f);
				_ = zombieColors.AddItem(c);
			});
			_ = zombieColors.AddItem("000000".HexColor());
		}

		public void UpgradeOldZombieData()
		{
			// fix leaner
			if ((Drawer.leaner is ZombieLeaner) == false)
				Drawer.leaner = new ZombieLeaner(this);

			// define suicide bombers
			if (bombTickingInterval == 0f)
				bombTickingInterval = -1f;

			// define tanky operators
			if (hasTankyShield == 0f)
				hasTankyShield = -1f;
			if (hasTankyHelmet == 0f)
				hasTankyHelmet = -1f;
			if (hasTankySuit == 0f)
				hasTankySuit = -1f;

			if (wallPushProgress == 0 && wallPushStart == Vector3.zero && wallPushDestination == Vector3.zero)
				wallPushProgress = -1f;
		}

		public override void ExposeData()
		{
			base.ExposeData();

			var wasColonist = wasMapPawnBefore;
			Scribe_Values.Look(ref state, "zstate");
			Scribe_Values.Look(ref raging, "raging");
			Scribe_Values.Look(ref wanderDestination, "wanderDestination");
			Scribe_Values.Look(ref rubbleTicks, "rubbleTicks");
			Scribe_Values.Look(ref rubbleCounter, "rubbleCounter");
			Scribe_Collections.Look(ref rubbles, "rubbles", LookMode.Deep);
			Scribe_Values.Look(ref wasColonist, "wasColonist");
			Scribe_Values.Look(ref wasMapPawnBefore, "wasMapPawnBefore");
			Scribe_Values.Look(ref bombWillGoOff, "bombWillGoOff");
			Scribe_Values.Look(ref bombTickingInterval, "bombTickingInterval");
			Scribe_Values.Look(ref isToxicSplasher, "toxicSplasher");
			Scribe_Values.Look(ref isMiner, "isMiner");
			Scribe_Values.Look(ref isElectrifier, "isElectrifier");
			Scribe_Values.Look(ref electricDisabledUntil, "electricDisabledUntil");
			Scribe_Values.Look(ref isAlbino, "isAlbino");
			Scribe_Values.Look(ref isDarkSlimer, "isDarkSlimer");
			Scribe_Values.Look(ref isHealer, "isHealer");
			Scribe_Values.Look(ref scream, "scream");
			Scribe_Values.Look(ref hasTankyShield, "tankyShield");
			Scribe_Values.Look(ref hasTankyHelmet, "tankyHelmet");
			Scribe_Values.Look(ref hasTankySuit, "tankySuit");
			Scribe_Values.Look(ref tankDestination, "tankDestination", IntVec3.Invalid);
			Scribe_Values.Look(ref isHealing, "isHealing");
			Scribe_Values.Look(ref consciousness, "consciousness", 1f);
			Scribe_Values.Look(ref paralyzedUntil, "paralyzedUntil");
			Scribe_References.Look(ref ropedBy, "ropedBy");
			Scribe_Values.Look(ref wallPushProgress, "wallPushProgress", -1f);
			Scribe_Values.Look(ref wallPushStart, "wallPushStart", Vector3.zero);
			Scribe_Values.Look(ref wallPushDestination, "wallPushDestination", Vector3.zero);
			Scribe_Values.Look(ref wallPushCooldown, "wallPushCooldown", 0);
			wasMapPawnBefore |= wasColonist;

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				UpgradeOldZombieData();

				_ = ZombieGenerator.FixGlowingEyeOffset(this);

				if (ZombieSettings.Values.useCustomTextures)
					needsGraphics = true; // make custom textures in renderer

				isOnFire = this.HasAttachment(ThingDefOf.Fire);
				checkSmashable = true;

				if (consciousness == 0)
					consciousness = 1;
			}

			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
				_ = ageTracker.CurLifeStageIndex; // trigger calculations
		}

		~Zombie()
		{
			Dispose(false);
		}

		protected virtual void Dispose(bool disposing)
		{
			_ = disposing;
			if (disposed)
				return;
			disposed = true;
			CleanupZombie();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		string ZombieType
		{
			get
			{
				if (IsSuicideBomber)
					return "ZombieSuicideBomber".Translate();
				if (isToxicSplasher)
					return "ZombieToxicSplasher".Translate();
				if (IsTanky)
					return "ZombieTanky".Translate();
				if (isMiner)
					return "ZombieMiner".Translate();
				if (isElectrifier)
					return "ZombieElectrifier".Translate();
				if (isAlbino)
					return "ZombieAlbino".Translate();
				if (isDarkSlimer)
					return "ZombieDarkSlimer".Translate();
				if (isHealer)
					return "ZombieHealer".Translate();
				if (story.bodyType == BodyTypeDefOf.Child)
					return "ZombieChild".Translate();
				if (story.bodyType == BodyTypeDefOf.Thin)
					return "ZombieWeak".Translate();
				if (story.bodyType == BodyTypeDefOf.Fat)
					return "ZombieStrong".Translate();
				return "ZombieSimple".Translate();
			}
		}

		public override string LabelMouseover
		{
			get
			{
				if (Name is NameTriple nameTriple)
				{
					var last = nameTriple.Last;
					if (last.StartsWith("#"))
						return $"{ZombieType} {last.Substring(1)}";
					return $"{ZombieType} {last}";
				}
				return ZombieType;
			}
		}

		public void Randomize8()
		{
			var nextIndex = Constants.random.Next(8);
			(adjIndex8[nextIndex], adjIndex8[prevIndex8]) = (adjIndex8[prevIndex8], adjIndex8[nextIndex]);
			prevIndex8 = nextIndex;
		}

		void CleanupZombie()
		{
			// log
			Find.BattleLog.Battles.Do(battle => battle.Entries.RemoveAll(entry => entry.Concerns(this)));

			// tales
			_ = Find.TaleManager.AllTalesListForReading.RemoveAll(tale =>
			{
				var singlePawnTale = tale as Tale_SinglePawn;
				if (singlePawnTale?.pawnData?.pawn == this)
					return true;
				var doublePawnTale = tale as Tale_DoublePawn;
				if (doublePawnTale?.firstPawnData?.pawn == this)
					return true;
				if (doublePawnTale?.secondPawnData?.pawn == this)
					return true;
				return false;
			});

			// worldpawns
			var worldPawns = Find.WorldPawns;
			if (worldPawns.Contains(this))
				worldPawns.RemovePawn(this);

			// our graphics
			if (Drawer?.renderer?.renderTree != null)
			{
				var head = Drawer.renderer.renderTree.HeadGraphic as VariableGraphic;
				head?.Dispose();
				// Drawer.renderer.renderTree.HeadGraphic = null; // HeadGraphic is a property, not a field, cannot be set to null directly

				var naked = Drawer.renderer.renderTree.BodyGraphic as VariableGraphic; // Assuming BodyGraphic is the equivalent for nakedGraphic
				naked?.Dispose();
				// Drawer.renderer.renderTree.BodyGraphic = null; // BodyGraphic is a property, not a field, cannot be set to null directly
			}

			// vanilla graphics
			// Drawer.renderer.graphics.furCoveredGraphic.UnCache();
			// Drawer.renderer.graphics.faceTattooGraphic.UnCache();
			// Drawer.renderer.graphics.bodyTattooGraphic.UnCache();
			// Drawer.renderer.graphics.swaddledBabyGraphic.UnCache();
			// Drawer.renderer.graphics.hairGraphic.UnCache();
			// Drawer.renderer.graphics.nakedGraphic.UnCache();
			// Drawer.renderer.graphics.rottingGraphic.UnCache();
			// Drawer.renderer.graphics.dessicatedGraphic.UnCache();
			// Drawer.renderer.graphics.packGraphic.UnCache();
			// Drawer.renderer.graphics.corpseGraphic.UnCache();
			// Drawer.renderer.graphics.desiccatedHeadGraphic.UnCache();
			// Drawer.renderer.graphics.skullGraphic.UnCache();
			// Drawer.renderer.graphics.headStumpGraphic.UnCache();
			// Drawer.renderer.graphics.desiccatedHeadStumpGraphic.UnCache();
			// Drawer.renderer.graphics.headGraphic.UnCache();
			// Drawer.renderer.graphics.beardGraphic.UnCache();

			GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(this);
		}

		public void Unrope()
		{
			ropedBy = null;
			paralyzedUntil = GenTicks.TicksAbs + GenDate.TicksPerHour / 2;
		}

		        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
				{
					if (Destroyed)
						return;
		
					if (IsSuicideBomber)
					{
						bombTickingInterval = -1f;
						bombWillGoOff = false;
						hasTankyShield = -1f;
						// _ = Drawer.renderer.graphics.apparelGraphics.RemoveAll(record => record.sourceApparel?.def == CustomDefs.Apparel_BombVest);
						Map.GetComponent<TickManager>()?.AddExplosion(Position);
					}
		
					if (isToxicSplasher)
						DropStickyGoo();
		
					base.Kill(dinfo, exactCulprit);
		
					if (Corpse == null && Find.WorldPawns.Contains(this) == false)
						Find.WorldPawns.PassToWorld(this, PawnDiscardDecideMode.Discard);
				}
		// public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		// {
		// 	base.Destroy(mode);
		// 	Dispose(false);
		// }

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			var map = Map;
			if (map != null)
			{
				var tm = map.GetComponent<TickManager>();
				_ = tm?.hummingZombies.Remove(this);
				_ = tm?.tankZombies.Remove(this);

				var grid = map.GetGrid();
				grid.ChangeZombieCount(lastGotoPosition, -1);
			}
			base.DeSpawn(mode);
		}

		public override Vector3 DrawPos
		{
			get
			{
				if (wallPushProgress >= 0)
				{
					var sqt = wallPushProgress * wallPushProgress;
					var f = sqt / (2.0f * (sqt - wallPushProgress) + 1.0f);
					if (GenTicks.TicksGame % 10 == 0 && Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
						Rotation = new Rot4((Rotation.AsInt + 1) % 4);
					var vec = wallPushStart + (wallPushDestination - wallPushStart) * f;
					vec.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
					return vec;
				}
				return base.DrawPos;
			}
		}

		public float RopingFactorTo(Pawn pawn)
		{
			var delta = pawn.DrawPos - DrawPos;
			return (delta.x * delta.x + delta.z * delta.z) / Constants.MAX_ROPING_DISTANCE_SQUARED;
		}

		void DropStickyGoo()
		{
			var pos = Position;
			var map = Map;
			if (map == null)
				return;

			var amount = 1 + (int)(ZombieLand.Tools.Difficulty() + 0.5f);
			if (story.bodyType == BodyTypeDefOf.Thin)
				amount -= 1;
			if (story.bodyType == BodyTypeDefOf.Fat)
				amount += 1;
			if (story.bodyType == BodyTypeDefOf.Hulk)
				amount += 2;

			var maxRadius = 0f;
			var count = (int)GenMath.LerpDouble(0, 10, 2, 30, amount);
			var hasFilth = 0;

			for (var i = 0; i < count; i++)
			{
				var n = (int)GenMath.LerpDouble(0, 10, 1, 4, amount);
				var vec = new IntVec3(Rand.Range(-n, n), 0, Rand.Range(-n, n));
				var r = vec.LengthHorizontalSquared;
				if (r > maxRadius)
					maxRadius = r;
				var cell = pos + vec;
				if (GenSight.LineOfSight(pos, cell, map, true, null, 0, 0) && cell.Walkable(map))
					if (FilthMaker.TryMakeFilth(cell, map, CustomDefs.StickyGoo, Name.ToStringShort, 1))
						hasFilth++;
			}
			if (hasFilth >= 6)
			{
				GenExplosion.DoExplosion(pos, map, Mathf.Max(0.5f, Mathf.Sqrt(maxRadius) - 1), CustomDefs.ToxicSplatter, null, 0, 0);
				if (Constants.USE_SOUND)
					CustomDefs.ToxicSplash.PlayOneShot(SoundInfo.InMap(new TargetInfo(pos, map)));
			}
		}

		public void ElectrifyAnimation()
		{
			electricCounter = 1;
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
			absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
		}

		void HandleRubble()
		{
			if (rubbleCounter == 0 && Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var map = Map;
				if (map != null)
				{
					var info = SoundInfo.InMap(new TargetInfo(Position, map));
					CustomDefs.ZombieDigOut.PlayOneShot(info);
				}
			}

			if (rubbleCounter == Constants.RUBBLE_AMOUNT)
			{
				state = ZombieState.Wandering;
				rubbles = new List<Rubble>();
			}
			else if (rubbleCounter < Constants.RUBBLE_AMOUNT && rubbleTicks-- < 0)
			{
				var idx = Rand.Range(rubbleCounter * 4 / 5, rubbleCounter);
				rubbles.Insert(idx, Rubble.Create(rubbleCounter / (float)Constants.RUBBLE_AMOUNT));

				var deltaTicks = Constants.RUBBLE_MIN_DELTA_TICKS + (float)(Constants.RUBBLE_MAX_DELTA_TICKS - Constants.RUBBLE_MIN_DELTA_TICKS) / Math.Min(1, rubbleCounter * 2 - Constants.RUBBLE_AMOUNT);
				rubbleTicks = (int)deltaTicks;

				rubbleCounter++;
			}

			foreach (var r in rubbles)
			{
				var dx = Mathf.Sign(r.pX) / 2f - r.pX;
				r.pX += (r.destX - r.pX) * 0.5f;
				var dy = r.destY - r.pY;
				r.pY += dy * 0.5f + Mathf.Abs(0.5f - dx) / 10f;
				r.rot = r.rot * 0.95f - (r.destX - r.pX) / 2f;

				if (dy < 0.1f)
				{
					r.dropSpeed += 0.01f;
					if (r.drop < 0.3f)
						r.drop += r.dropSpeed;
				}
			}
		}

		void RenderRubble(Vector3 drawLoc)
		{
			foreach (var r in rubbles)
			{
				var scale = Constants.RUBBLE_MIN_SCALE + (Constants.RUBBLE_MAX_SCALE - Constants.RUBBLE_MIN_SCALE) * r.scale;
				var x = 0f + r.pX / 2f;
				var bottomExtend = Mathf.Abs(r.pX) / 6f;
				var y = -0.5f + Mathf.Max(bottomExtend, r.pY - r.drop) * (Constants.RUBBLE_MAX_HEIGHT - scale / 2f) + (scale - Constants.RUBBLE_MAX_SCALE) / 2f;
				var pos = drawLoc + new Vector3(x, 0, y);
				pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn + 1);
				var rot = Quaternion.Euler(0f, r.rot * 360f, 0f);
				GraphicToolbox.DrawScaledMesh(MeshPool.plane10, Constants.RUBBLE, pos, rot, scale, scale);
			}
		}

		static readonly Color[] severity = new[]
		{
			new Color(0.9f, 0, 0),
			new Color(1f, 0.5f, 0),
			new Color(1f, 1f, 0),
		};

		public override void DrawGUIOverlay()
		{
			const float width = 60;

			base.DrawGUIOverlay();
			if (ZombieSettings.Values.showHealthBar == false)
				return;

			if (UI.MapToUIPosition(Vector3.one).x - UI.MapToUIPosition(Vector3.zero).x < width / 2)
				return;

			var pos = DrawPos;
			if ((UI.MouseMapPosition() - pos).MagnitudeHorizontalSquared() > 0.64f)
				return;

			pos.z -= 0.65f;
			Vector2 vec = Find.Camera.WorldToScreenPoint(pos) / Prefs.UIScale;
			vec.y = UI.screenHeight - vec.y;

			var barRect = new Rect(vec - new Vector2(width / 2, 0), new Vector2(width, width / 5));
			Widgets.DrawBoxSolid(barRect, Constants.healthBarBG);
			var barInnerRect = barRect;
			var percentHealth = health.summaryHealth.SummaryHealthPercent;
			barInnerRect.width *= percentHealth;
			Widgets.DrawBoxSolid(barInnerRect, new Color(1 - percentHealth, 0, percentHealth));
			var barInnerLowerRect = barRect;
			var percentConsciousness = health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
			barInnerLowerRect.yMin += barInnerLowerRect.height * 4 / 5;
			barInnerLowerRect.width *= percentConsciousness;
			Widgets.DrawBoxSolid(barInnerLowerRect, Color.white);
			Widgets.DrawBox(barRect, 1, Constants.healthBarFrame);

			int num = HealthUtility.TicksUntilDeathDueToBloodLoss(this);
			if (num < 60000)
			{
				var text = "TimeToDeath".Translate(num.ToStringTicksToPeriod(true, true, true, true));
				var color = num <= GenDate.TicksPerHour ? severity[0] : (num < GenDate.TicksPerHour * 4 ? severity[1] : severity[2]);

				Text.Font = GameFont.Tiny;
				var textWidth = Text.CalcSize(text).x;
				vec.y -= 16;
				GUI.DrawTexture(new Rect(vec.x - textWidth / 2f - 4f, vec.y, textWidth + 8f, 12f), TexUI.GrayTextBG);
				GUI.color = color;
				Text.Anchor = TextAnchor.UpperCenter;
				Widgets.Label(new Rect(vec.x - textWidth / 2f, vec.y - 3f, textWidth, 999f), text.RawText);
				GUI.color = Color.white;
				Text.Anchor = TextAnchor.UpperLeft;
				Text.Font = GameFont.Small;
			}
		}

		readonly int[] nextNthTick = new int[totalNthTicks];
		public bool EveryNTick(NthTick interval)
		{
			var n = (int)interval;
			var t = GenTicks.TicksAbs;
			if (t > nextNthTick[n])
			{
				var d = nthTickValues[n];
				nextNthTick[n] = t + d;
				return true;
			}
			return false;
		}

		        protected override void Tick()		{
			var comps = AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTick();
		}

		static DamageInfo damageInfo = new(DamageDefOf.Crush, 20f, 20f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null);
		public void CustomTick(float threatLevel)
		{
			var map = Map;
			if (map == null)
				return;

			if (!ThingOwnerUtility.ContentsSuspended(ParentHolder))
			{
				if (Spawned)
				{
					pather?.PatherTick();
					stances?.StanceTrackerTick();
					verbTracker?.VerbsTick();
					roping?.RopingTick();
					natives?.NativeVerbsTick();
					jobs?.JobTrackerTick();
					health?.HealthTick();
				}
			}

			if (state == ZombieState.Emerging)
				HandleRubble();

			if (threatLevel <= 0.002f && ZombieSettings.Values.zombiesDieOnZeroThreat && Rand.Chance(0.002f))
				_ = TakeDamage(damageInfo);

			if (state != ZombieState.Emerging && EveryNTick(NthTick.Every12))
			{
				if (isToxicSplasher)
				{
					var gasAmount = Mathf.CeilToInt(BodySize * 1.15f * ZombieLand.Tools.Difficulty());
					if (gasAmount > 0)
						GasUtility.AddGas(Position, map, GasType.ToxGas, gasAmount);
				}

				//new
                if (isHealer)
                {
					int radius = (int)(4 + ZombieLand.Tools.Difficulty() * 2);
                    // gather nearby zombie candidates once
                    var candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn)
                        .OfType<Zombie>();

                    // For better locality, use GenRadial radius enumeration to limit scanning
                    var cells = GenRadial.RadialCellsAround(Position, radius, true);
                    // select up to M best by hediff count (M=8)
                    const int M = 8;
                    Zombie[] best = new Zombie[M];
                    int bestCount = 0;

                    foreach (var cell in cells)
                    {
                        if (!cell.InBounds(map)) continue;
                        var things = map.thingGrid.ThingsListAt(cell);
                        if (things == null) continue;
                        for (int t = 0; t < things.Count; t++)
                        {
                            if (things[t] is Zombie z && z.health?.hediffSet?.hediffs != null && z.health.hediffSet.hediffs.Count > 0)
                            {
                                int score = z.health.hediffSet.hediffs.Count;
                                // insert into best[] if score is high enough (simple insertion into fixed-size top-N)
                                int insertIndex = bestCount;
                                if (bestCount < M)
                                {
                                    best[bestCount++] = z;
                                    insertIndex = bestCount - 1;
                                }
                                else
                                {
                                    // find min in best to replace if current score higher
                                    int minIdx = 0, minScore = int.MaxValue;
                                    for (int i = 0; i < M; i++)
                                    {
                                        int s = best[i].health.hediffSet.hediffs.Count;
                                        if (s < minScore) { minScore = s; minIdx = i; }
                                    }
                                    if (score > minScore) best[minIdx] = z;
                                }
                            }
                        }
                    }

                    // Now process up to M best (avoid calling Clear on entire hediffSet maybe heavy)
                    for (int i = 0; i < bestCount; i++)
                    {
                        var z = best[i];
                        if (z == null) continue;

                        // Instead of Clear(), consider tending/healing specific hediffs selectively,
                        // or clearing only non-permanent ones. If Clear() is required, keep it but keep M small.
                        z.health.hediffSet.Clear();
                        healInfo.Add(new HealerInfo(z));
                    }
                }
            }
		}

		public static Quaternion ZombieAngleAxis(float angle, Vector3 axis, Pawn pawn)
		{
			var result = Quaternion.AngleAxis(angle, axis);

			if (pawn is not Zombie zombie)
				return result;

			var progress = zombie.rubbleCounter / (float)Constants.RUBBLE_AMOUNT;
			if (progress >= Constants.RUBBLE_EMERGE_DELAY)
			{
				var bodyRot = GenMath.LerpDouble(Constants.RUBBLE_EMERGE_DELAY, 1, 90, 0, progress);
				result *= Quaternion.Euler(Vector3.right * bodyRot);
			}
			return result;
		}

		public void Render(PawnRenderer renderer, Vector3 drawLoc)
		{
			drawLoc.x = (int)(drawLoc.x) + 0.5f;

			var progress = rubbleCounter / (float)Constants.RUBBLE_AMOUNT;
			if (progress >= Constants.RUBBLE_EMERGE_DELAY)
			{
				var bodyOffset = GenMath.LerpDouble(Constants.RUBBLE_EMERGE_DELAY, 1, -0.45f, 0, progress);
				var bodyRot = GenMath.LerpDouble(Constants.RUBBLE_EMERGE_DELAY, 1, 90, 0, progress);

				Rot4 rot = Rot4.South;

				// Draw body
				Graphic bodyGraphic = customBodyGraphic ?? Graphic;
				Mesh bodyMesh = bodyGraphic.MeshAt(rot);
				Material bodyMat = bodyGraphic.MatAt(rot);
				Vector3 bodyDrawPos = drawLoc + new Vector3(0, 0, bodyOffset);
				Graphics.DrawMesh(bodyMesh, bodyDrawPos, Quaternion.Euler(Vector3.right * bodyRot), bodyMat, 0);

				// Draw head
				Graphic headGraphic = customHeadGraphic ?? Graphic;
				Mesh headMesh = headGraphic.MeshAt(rot);
				Material headMat = headGraphic.MatAt(rot);
				Vector3 headOffset = renderer.BaseHeadOffsetAt(rot);
				Vector3 headDrawPos = bodyDrawPos + headOffset;
				Graphics.DrawMesh(headMesh, headDrawPos, Quaternion.Euler(Vector3.right * bodyRot), headMat, 0);
			}

			RenderRubble(drawLoc);
		}
	}
}
