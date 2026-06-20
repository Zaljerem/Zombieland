namespace ZombieLand
{
	public static class ContaminationThresholds
	{
		public const float MinimumForCalculations = 0.0001f;
		public const float MinimumForVisuals = 0.001f;

		public static bool IsVisible(float contamination)
			=> contamination >= MinimumForVisuals;
	}
}
