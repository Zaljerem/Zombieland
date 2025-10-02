using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    [HarmonyPatch(typeof(Pawn_HealthTracker))]
    [HarmonyPatch("MakeDowned")]
    public static class Patch_Pawn_HealthTracker_MakeDowned
    {
        // Signature: private void MakeDowned(DamageInfo? dinfo, Hediff hediff)
        public static void Prefix(Pawn_HealthTracker __instance, DamageInfo? dinfo, Hediff hediff, Pawn ___pawn)
        {
            if (___pawn is Zombie zombie)
            {
                // Cancel any current job to avoid pathing errors
                zombie.jobs?.EndCurrentJob(JobCondition.Incompletable);
                                
                //Log.Message($"[ZombieLand] Preventing downed pathing error: {zombie} job ended.");
            }
        }
    }
}
