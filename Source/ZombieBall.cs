using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombieBall : Projectile
	{
		public float rotation;

		public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
		{
			rotation = Rand.Range(2f, 5f) * (Rand.Bool ? 1 : -1);
			base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref rotation, "rotation", 0);
		}

		public override Quaternion ExactRotation => Quaternion.Euler(0, GenTicks.TicksGame * rotation, 0);

		protected override void Impact(Thing hitThing, bool blockedByShield = false)
		{
			var map = Map;
			Destroy(DestroyMode.Vanish);

			if (def.projectile.explosionEffect != null)
			{
				var effecter = def.projectile.explosionEffect.Spawn();
				effecter.Trigger(new TargetInfo(Position, map, false), new TargetInfo(Position, map, false), -1);
				effecter.Cleanup();
			}

			GenExplosion.DoExplosion(
				Position,
				map,
				def.projectile.explosionRadius,
				def.projectile.damageDef,
				launcher,
				DamageAmount,
				ArmorPenetration,
				def.projectile.soundExplode,
				equipmentDef,
				def,
				intendedTarget.Thing,
				def.projectile.postExplosionSpawnThingDef,
				def.projectile.postExplosionSpawnChance,
				def.projectile.postExplosionSpawnThingCount,
				def.projectile.postExplosionGasType,
				null, // postExplosionGasRadiusOverride
				255, // postExplosionGasAmount
				def.projectile.applyDamageToExplosionCellsNeighbors,
				def.projectile.preExplosionSpawnThingDef,
				def.projectile.preExplosionSpawnChance,
				def.projectile.preExplosionSpawnThingCount,
				def.projectile.explosionChanceToStartFire,
				def.projectile.explosionDamageFalloff,
				new float?(origin.AngleToFlat(destination)),
				null, // ignoredThings
				null, // affectedAngle
				true, // doVisualEffects
				def.projectile.damageDef.expolosionPropagationSpeed,
				0f, // excludeRadius
				true, // doSoundEffects
				def.projectile.postExplosionSpawnThingDefWater,
				1f, // screenShakeFactor (default)
				null, // flammabilityChanceCurve
				null, // overrideCells
				null, // postExplosionSpawnSingleThingDef
				null // preExplosionSpawnSingleThingDef
			);

			landed = true;

			var zombie = ZombieGenerator.SpawnZombie(Position, map, ZombieType.Random);
			zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
			zombie.state = ZombieState.Wandering;
			zombie.Rotation = Rot4.Random;
			var tickManager = map.GetComponent<TickManager>();
			_ = tickManager?.allZombiesCached.Add(zombie);
		}
	}
}
