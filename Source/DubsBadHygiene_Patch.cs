
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
                        var pawn = __instance.pawn;
                        if (pawn == null) return;

                        var contamination = pawn.GetContamination();
                        if (contamination > 0)
                        {
                            pawn.SetContamination(0);

                            var map = pawn.Map;
                            if (map == null) return;

                            var hygieneCompType = AccessTools.TypeByName("DubsBadHygiene.MapComponent_Hygiene");
                            if (hygieneCompType == null) return;

                            var getCompMethod = typeof(Map).GetMethod("GetComponent", new Type[] { });
                            if (getCompMethod == null) return;

                            var genericGetCompMethod = getCompMethod.MakeGenericMethod(hygieneCompType);
                            if (genericGetCompMethod == null) return;

                            var hygieneComp = genericGetCompMethod.Invoke(map, null);
                            if (hygieneComp == null) return;

                            var sewageGridProperty = AccessTools.Property(hygieneCompType, "SewageGrid");
                            if (sewageGridProperty == null) return;

                            var sewageGrid = sewageGridProperty.GetValue(hygieneComp);
                            if (sewageGrid == null) return;

                            var gridLayerType = sewageGrid.GetType();
                            var addAtMethod = AccessTools.Method(gridLayerType, "AddAt", new[] { typeof(IntVec3), typeof(float), typeof(bool), typeof(bool), typeof(MapMeshFlagDef) });
                            if (addAtMethod == null) return;

                            var fixture = __instance.job.targetA.Thing;
                            if (fixture != null)
                            {
                                var contaminationToSpread = contamination;

                                var adjacentOffsets = GenAdj.AdjacentCells;
                                foreach (var offset in adjacentOffsets)
                                {
                                    var cell = fixture.Position + offset;
                                    if (cell.InBounds(map))
                                    {
                                        map.AddContamination(cell, contaminationToSpread / 8);
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
