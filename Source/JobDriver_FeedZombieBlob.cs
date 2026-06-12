using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_FeedZombieBlob : JobDriver
	{
		ZombieBlob Blob => job.GetTarget(TargetIndex.A).Thing as ZombieBlob;
		Thing Feed => job.GetTarget(TargetIndex.B).Thing;

		public override string GetReport()
		{
			return "FeedingZombieBlob".Translate();
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			var blob = Blob;
			var feed = Feed;
			return blob != null
				&& feed != null
				&& pawn.Reserve(feed, job, 1, 1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			_ = this.FailOnDestroyedOrNull(TargetIndex.B);

			yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
				.FailOnDespawnedNullOrForbidden(TargetIndex.B)
				.FailOnSomeonePhysicallyInteracting(TargetIndex.B);
			yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
				.FailOnDespawnedOrNull(TargetIndex.A);

			var feed = Toils_General.Wait(90, TargetIndex.A);
			_ = feed.FailOnDespawnedOrNull(TargetIndex.A);
			_ = feed.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			_ = feed.WithProgressBarToilDelay(TargetIndex.A);
			yield return feed;

			var finish = ToilMaker.MakeToil("FeedZombieBlob");
			finish.initAction = delegate ()
			{
				var blob = Blob;
				var carried = pawn.carryTracker?.CarriedThing;
				if (blob == null || carried == null || blob.TryFeed(carried) == false)
				{
					pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
					return;
				}
				if (blob.DestroyedOrNull() || blob.CellCount == 0)
					blob?.RequestFeed(false);
				pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
			};
			yield return finish;
		}
	}
}
