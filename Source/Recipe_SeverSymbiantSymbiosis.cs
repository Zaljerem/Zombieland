using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class Recipe_SeverSymbiantSymbiosis : Recipe_Surgery
	{
		public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
		{
			if (ZombieSymbiant.CanSeverSymbiosis(pawn) == false)
				yield break;
			var torso = pawn.health?.hediffSet?.GetNotMissingParts().FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
			if (torso != null)
				yield return torso;
		}

		public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
		{
			if (pawn.DestroyedOrNull() || pawn.Dead || pawn.Map != billDoer?.Map || pawn.IsInAnyStorage())
				return;
			var symbiant = ZombieSymbiant.LinkedSymbiantFor(pawn);
			if (symbiant == null)
				return;

			var success = symbiant.TrySeverSymbiosis(pawn, billDoer);
			if (success)
			{
				_ = TaleRecorder.RecordTale(TaleDefOf.DidSurgery, new object[] { billDoer, pawn });
				return;
			}
		}

		public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part)
		{
			return "SeverSymbiantSymbiosisWithCost".Translate(ZombieSymbiant.SeveranceExtractCost());
		}

		public override float GetIngredientCount(IngredientCount ing, Bill bill)
		{
			if (ing?.filter != null && CustomDefs.ZombieExtract != null && ing.filter.Allows(CustomDefs.ZombieExtract))
				return ZombieSymbiant.SeveranceExtractCost();
			return base.GetIngredientCount(ing, bill);
		}
	}
}
