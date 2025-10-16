using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    public class JobDriver_ClearContamination : JobDriver
    {
        private float workDone;
        private float totalWorkRequired;

        private float ContaminationPresent => ContaminationManager.Instance.grounds[pawn.Map.Index][TargetA.Cell];

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workDone, "workDone", 0f);
            Scribe_Values.Look(ref totalWorkRequired, "totalWorkRequired", 0f);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetA, job, 1, -1, null, false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var targetCell = TargetA.Cell;

            this.FailOn(delegate
            {
                return ContaminationPresent == 0f;
            });

            var gotoCell = Toils_Goto.GotoCell(targetCell, PathEndMode.Touch);
            yield return gotoCell;

            var clean = new Toil();
            clean.initAction = delegate
            {
                totalWorkRequired = 250f; // Time to clean a tile, can be adjusted
                workDone = 0f;
            };
            clean.tickAction = delegate
            {
                workDone += pawn.GetStatValue(StatDefOf.CleaningSpeed);
                if (workDone >= totalWorkRequired)
                {
                    ContaminationManager.Instance.grounds[pawn.Map.Index][targetCell] = 0f;
                    ReadyForNextToil();
                }
            };
            clean.defaultCompleteMode = ToilCompleteMode.Never;
            clean.WithEffect(EffecterDefOf.Clean, TargetIndex.A);
            clean.WithProgressBar(TargetIndex.A, () => workDone / totalWorkRequired, true);
            clean.PlaySustainerOrSound(() => SoundDefOf.Interact_CleanFilth);
            yield return clean;
        }
    }
}
