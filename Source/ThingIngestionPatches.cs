using HarmonyLib;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
	static class Thing_Ingested_ZeroStackGuard_Patch
	{
		[HarmonyPriority(Priority.First)]
		static bool Prefix(Thing __instance, ref float __result)
		{
			if (__instance == null || __instance.Destroyed || __instance.stackCount > 0)
				return true;

			// Vanilla clamps numTaken to at least one, so zero-stack food logs and
			// then tries to SplitOff(1). The invalid transient cannot provide nutrition.
			__result = 0f;
			RemoveInvalidIngestible(__instance);
			return false;
		}

		static void RemoveInvalidIngestible(Thing thing)
		{
			_ = thing.holdingOwner?.Remove(thing);
			if (thing.Destroyed == false && thing.def?.destroyable != false)
				thing.Destroy(DestroyMode.Vanish);
		}
	}
}
