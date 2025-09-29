using System;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	[HarmonyPatch(typeof(JobDriver_ClearPollution), "MakeNewToils")]
	static class JobDriver_ClearPollution_ClearPollutionAt_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_ClearPollution __instance)
		{
			foreach (var toil in toils)
			{
				GridUtility_Unpollute_Patch.subject = __instance.pawn;
				yield return toil;
			}
			GridUtility_Unpollute_Patch.subject = null;
		}
	}

	[HarmonyPatch(typeof(GridsUtility), nameof(GridsUtility.Unpollute))]
	static class GridUtility_Unpollute_Patch
	{
		public static Thing subject = null;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(IntVec3 c, Map map)
		{
			var contamination = map.GetContamination(c);
			subject?.AddContamination(contamination, ZombieSettings.Values.contamination.pollutionAdd);
		}
	}
	[HarmonyPatch(typeof(JobDriver_ClearSnowAndSand), "MakeNewToils")]
	static class ClearSnowAndSandContamination
	{
		[ThreadStatic]
		public static Pawn currentClearingPawn;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(JobDriver_ClearSnowAndSand __instance)
		{
			currentClearingPawn = __instance.pawn;
		}

		static void Postfix()
		{
			currentClearingPawn = null;
		}
	}

	[HarmonyPatch(typeof(SnowGrid), nameof(SnowGrid.SetDepth))]
	static class SnowGrid_SetDepth_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(SnowGrid __instance, IntVec3 c, float newDepth)
		{
			if (newDepth == 0f && ClearSnowAndSandContamination.currentClearingPawn != null)
			{
				var map = (Map)AccessTools.Field(typeof(SnowGrid), "map").GetValue(__instance);
				var contamination = map.GetContamination(c);
				ClearSnowAndSandContamination.currentClearingPawn.AddContamination(contamination, ZombieSettings.Values.contamination.snowAdd);
			}
		}
	}
}