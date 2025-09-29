using Verse.AI;

namespace ZombieLand
{
	public class MentalState_Contamination : MentalState
	{
		public MentalState_Contamination()
		{
		}

		protected override bool CanEndBeforeMaxDurationNow => false;
	}
}