using Verse;

namespace ZombieLand
{
	public class ContaminationWorkFixer : GameComponent
	{
		public ContaminationWorkFixer(Game game) : base()
		{
		}

		public override void FinalizeInit()
		{
			base.FinalizeInit();
			ContaminationManager.Instance.FixGrounds();
			foreach (var map in Find.Maps)
				map.mapPawns.AllPawns.ForEach(pawn => pawn.workSettings?.Notify_DisabledWorkTypesChanged());
		}
	}
}