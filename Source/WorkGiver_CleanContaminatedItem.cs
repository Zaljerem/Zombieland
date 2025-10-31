using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    public class WorkGiver_CleanContaminatedItem : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.HaulableEver);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (ZombieSettings.Values.disableCleanContamination)
                return true;

            return !ContaminationManager.Instance.contaminations.Any(c => c.Value >= ZombieSettings.Values.minContaminationToCleanItem);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (ZombieSettings.Values.disableCleanContamination)
                return Enumerable.Empty<Thing>();

            var contaminationManager = ContaminationManager.Instance;
            var contaminatedThingIDs = contaminationManager.contaminations
                .Where(c => c.Value >= ZombieSettings.Values.minContaminationToCleanItem)
                .Select(c => c.Key)
                .ToHashSet();

            return pawn.Map.listerThings.AllThings
                .Where(t => contaminatedThingIDs.Contains(t.thingIDNumber))
                .Where(t => pawn.Map.areaManager.Home[t.Position])
                .Where(t => pawn.CanReserve(t));
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.GetContamination() < ZombieSettings.Values.minContaminationToCleanItem)
                return null;

            if (!pawn.Map.areaManager.Home[t.Position])
                return null;

            if (pawn.Map.reservationManager.IsReserved(t))
                return null;

            if (!pawn.CanReserve(t, 1, -1, null, forced))
                return null;

            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("CleanContaminatedItem"), t);
        }
    }
}