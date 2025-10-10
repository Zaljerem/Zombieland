using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    public class WorkGiver_ClearContamination : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Filth);

        public override int MaxRegionsToScanBeforeGlobalSearch => 4;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
			if (ZombieSettings.Values.disableCleanContamination)
				return true;

            var grid = ContaminationManager.Instance.grounds[pawn.Map.Index];
            if (grid == null)
                return true;
            return !grid.cells.Any(c => c > 0);
        }

        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            if (ContaminationManager.Instance.grounds[pawn.Map.Index][c] > 0f)
                return JobMaker.MakeJob(CustomDefs.ClearContamination, c);
            return null;
        }
    }
}
