using Verse;

namespace ZombieLand
{
	public class Hediff_BlobSymbiosis : HediffWithComps
	{
		const int SyncInterval = 250;

		public string blobThingId;

		public override bool ShouldRemove
		{
			get
			{
				if (ZombieBlob.DebugDisableHostHediffSync)
					return false;
				if (base.ShouldRemove)
					return true;
				return ZombieBlob.LinkedBlobFor(pawn) == null;
			}
		}

		public override void Tick()
		{
			if (ZombieBlob.DebugDisableHostHediffSync)
				return;
			base.Tick();
			if (pawn.IsHashIntervalTick(SyncInterval) == false)
				return;
			var blob = ZombieBlob.LinkedBlobFor(pawn);
			if (blob == null)
			{
				pawn.health.RemoveHediff(this);
				return;
			}
			blobThingId = blob.ThingID;
			Severity = blob.BenefitFactor;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref blobThingId, "blobThingId");
		}
	}
}
