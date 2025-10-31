
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    [StaticConstructorOnStartup]
    static class DubsBadHygiene_Patch
    {
        static DubsBadHygiene_Patch()
        {
            var harmony = new Harmony("net.pardeike.zombieland.dubsbadhygiene");
            var washJobs = new[]
            {
                "JobDriver_takeShower",
                "JobDriver_takeBath",
                "JobDriver_washAtCell",
                "JobDriver_washHands",
                "JobDriver_useWashBucket"
            };

            foreach (var jobName in washJobs)
            {
                var jobType = AccessTools.TypeByName($"DubsBadHygiene.{jobName}");
                if (jobType != null)
                {
                    var makeNewToils = AccessTools.Method(jobType, "MakeNewToils");
                    if (makeNewToils != null)
                    {
                        harmony.Patch(makeNewToils, postfix: new HarmonyMethod(typeof(DubsBadHygiene_Patch), nameof(MakeNewToils_Postfix)));
                    }
                }
            }
        }

        static IEnumerable<Toil> MakeNewToils_Postfix(IEnumerable<Toil> toils, JobDriver __instance)
        {
            foreach (var toil in toils)
            {
                if (toil.defaultCompleteMode == ToilCompleteMode.Delay)
                {
                    toil.AddFinishAction(() =>
                    {
                        if (ZombieSettings.Values.contamination.disableWashingContamination) return;

                        var pawn = __instance.pawn;
                        if (pawn == null) return;

                        var contamination = pawn.GetContamination();
                        if (contamination > 0)
                        {
                            pawn.SetContamination(0);

                            var map = pawn.Map;
                            if (map == null) return;

                            var fixture = __instance.job.targetA.Thing;
                            if (fixture != null)
                            {
                                var contaminationToSpread = contamination * 0.5f;

                                var adjacentOffsets = GenAdj.AdjacentCells;
                                foreach (var offset in adjacentOffsets)
                                {
                                    var cell = fixture.Position + offset;
                                    if (cell.InBounds(map))
                                    {
                                        map.AddContamination(cell, contaminationToSpread / 8f);
                                    }
                                }
                            }
                        }
                    });
                }
                yield return toil;
            }
        }
    }
}
