using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn_PathFollower))]
	public static class Zombie_PathFollower_Patches
	{
		[HarmonyPatch("StartPath")]
		[HarmonyPostfix]
		public static void StartPath_Patch(Pawn_PathFollower __instance)
		{
			var pawn = (Pawn)AccessTools.Field(typeof(Pawn_PathFollower), "pawn").GetValue(__instance);
			if (pawn is Zombie)
			{
				if (pawn.CurJob != null)
					pawn.CurJob.locomotionUrgency = LocomotionUrgency.Sprint;
			}
		}

		[HarmonyPatch("PatherTick")]
		[HarmonyPrefix]
		public static bool PatherTick_Patch(Pawn_PathFollower __instance) 
		{
			var pawn = (Pawn)AccessTools.Field(typeof(Pawn_PathFollower), "pawn").GetValue(__instance);
			if (pawn is Zombie zombie)
			{
				var building = (Building)AccessTools.Method(typeof(Pawn_PathFollower), "BuildingBlockingNextPathCell").Invoke(__instance, null);
				                if (building is Building_Door door && !door.FreePassage)
				                {
				                    if (pawn.CurJob == null || pawn.CurJob.def != JobDefOf.AttackMelee)
				                    {
				                        var job = JobMaker.MakeJob(JobDefOf.AttackMelee, building);
				                        pawn.jobs.StartJob(job, JobCondition.Incompletable);
				                    }
				                    return true;
				                }
				var blockingPawn = PawnUtility.PawnBlockingPathAt(__instance.nextCell, pawn);
				if (blockingPawn != null && blockingPawn != pawn)
				{
					if (pawn.CurJob == null || pawn.CurJob.def != JobDefOf.AttackMelee)
					{
						var job = JobMaker.MakeJob(JobDefOf.AttackMelee, blockingPawn);
						pawn.jobs.StartJob(job, JobCondition.Incompletable);
					}
					return true;
				}
			}

			return true;
		}
	}
}
