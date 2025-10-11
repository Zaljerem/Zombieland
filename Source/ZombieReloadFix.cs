using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
    [StaticConstructorOnStartup]
    public class ZombieReloadFix : GameComponent
    {
        public ZombieReloadFix(Game game) { }

        public override void FinalizeInit()
        {
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawns == null) continue;

                var pawnsToDestroy = new List<Pawn>();
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    if (pawn == null || pawn.health == null || (pawn.Dead && pawn.Corpse == null) || pawn.Destroyed)
                    {
                        pawnsToDestroy.Add(pawn);
                    }
                }

                foreach (var pawn in pawnsToDestroy)
                {
                    Log.Warning($"[ZombieLand] Destroying invalid pawn {pawn?.LabelCap ?? "NULL"} during PostLoadInit cleanup.");
                    pawn?.Destroy(DestroyMode.Vanish);
                }
            }
        }
    }
}