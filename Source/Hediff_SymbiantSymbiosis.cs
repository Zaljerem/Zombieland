using Verse;

namespace ZombieLand
{
	public class Hediff_SymbiantSymbiosis : HediffWithComps
	{
		const int SyncInterval = 250;

		public string symbiantThingId;

		public override bool ShouldRemove
		{
			get
			{
				if (ZombieSymbiant.DebugDisableHostHediffSync)
					return false;
				return ZombieSymbiant.LinkedSymbiantFor(pawn) == null;
			}
		}

		public override void Tick()
		{
			if (ZombieSymbiant.DebugDisableHostHediffSync)
				return;
			base.Tick();
			if (pawn.IsHashIntervalTick(SyncInterval) == false)
				return;
			var symbiant = ZombieSymbiant.LinkedSymbiantFor(pawn);
			if (symbiant == null)
			{
				pawn.health.RemoveHediff(this);
				return;
			}
			symbiantThingId = symbiant.ThingID;
			Severity = ZombieSymbiant.HostHediffSeverity(ZombieSymbiant.SymbiantBenefitFactor(pawn));
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref symbiantThingId, "symbiantThingId");
		}
	}
}
