using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    public class WorkGiver_ClearContamination : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;



        public override int MaxRegionsToScanBeforeGlobalSearch => 4;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
			if (ZombieSettings.Values.disableCleanContamination)
				return true;

            if (ContaminationManager.Instance.grounds.TryGetValue(pawn.Map.Index, out var grid) == false || grid == null)
                return true;

            return !grid.cells.Any(c => c > 0);
        }

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            if (ZombieSettings.Values.disableCleanContamination)
                return Enumerable.Empty<IntVec3>();

            if (ContaminationManager.Instance.grounds.TryGetValue(pawn.Map.Index, out var grid) == false || grid == null)
                return Enumerable.Empty<IntVec3>();

            return grid.cells.Select((value, index) => new { value, index })
                .Where(cell => cell.value > 0)
                .Select(cell => pawn.Map.cellIndices.IndexToCell(cell.index));
        }

        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            if (ZombieSettings.Values.disableCleanContamination)
                return null;

            if (ContaminationManager.Instance.grounds.TryGetValue(pawn.Map.Index, out var grid) == false || grid == null)
                return null;

            var cellContamination = grid[c];
            if (cellContamination >= 0.1f)
            {
                if (pawn.CanReserve(c, 1, -1, null, forced))
                {
                    return JobMaker.MakeJob(CustomDefs.ClearContamination, c);
                }
            }
            return null;
        }
    }
}
