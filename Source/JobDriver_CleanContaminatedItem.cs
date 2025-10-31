using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    public class JobDriver_CleanContaminatedItem : JobDriver
    {
        private float workDone;
        private float totalWorkRequired;
        private float initialContamination;

        private Thing Item => TargetA.Thing;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workDone, "workDone", 0f);
            Scribe_Values.Look(ref totalWorkRequired, "totalWorkRequired", 0f);
            Scribe_Values.Look(ref initialContamination, "initialContamination", 0f);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Item, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Item.GetContamination() == 0f);

            var goToItem = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return goToItem;

            var clean = new Toil();
            clean.initAction = delegate
            {
                initialContamination = Item.GetContamination();
                totalWorkRequired = initialContamination * 250f; // Adjust this value for cleaning time
                workDone = 0f;
            };
            clean.tickAction = delegate
            {
                workDone += pawn.GetStatValue(StatDefOf.CleaningSpeed);
                if (workDone >= totalWorkRequired)
                {
                    Item.SetContamination(0f);
                    ReadyForNextToil();
                }
                else
                {
                    var progress = workDone / totalWorkRequired;
                    Item.SetContamination(initialContamination * (1 - progress));
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