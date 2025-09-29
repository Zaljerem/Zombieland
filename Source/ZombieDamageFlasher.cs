using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
 

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch(nameof(DamageFlasher.Notify_DamageApplied))]
	static class DamageFlasher_Notify_DamageApplied_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(DamageFlasher __instance, DamageInfo dinfo)
		{
			if (__instance is ZombieDamageFlasher zombieDamageFlasher)
				zombieDamageFlasher.dinfoDef = dinfo.Def;
		}
	}

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch(nameof(DamageFlasher.GetDamagedMat))]
	static class DamageFlasher_GetDamagedMat_Patch
	{
		private static int GetLastDamageTick(DamageFlasher flasher)
		{
			var field = typeof(DamageFlasher).GetField("lastDamageTick", BindingFlags.NonPublic | BindingFlags.Instance);
			return (int)field.GetValue(flasher);
		}

		static readonly Color greenDamagedMatStartingColor = new(0f, 0.8f, 0f);

		private static int DamageFlashTicksLeft(DamageFlasher damageFlasher)
		{
			// copied from DamageFlasher.DamageFlashTicksLeft
			return GetLastDamageTick(damageFlasher) + 16 - GenTicks.TicksGame;
		}

		[HarmonyPriority(Priority.Last)]
		static void Postfix(DamageFlasher __instance, Material baseMat, Material __result)
		{
			if (__instance is ZombieDamageFlasher zombieDamageFlasher
				&& zombieDamageFlasher.dinfoDef == CustomDefs.ZombieBite
				&& __result != null)
			{
				var damPct = DamageFlashTicksLeft(__instance) / 16f;
				__result.color = Color.Lerp(baseMat.color, greenDamagedMatStartingColor, damPct);
			}
		}
	}

	class ZombieDamageFlasher : DamageFlasher
	{
		public DamageDef dinfoDef;

		public ZombieDamageFlasher(Pawn pawn) : base(pawn) { }
	}
}
