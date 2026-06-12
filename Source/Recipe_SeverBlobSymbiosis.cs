using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class Recipe_SeverBlobSymbiosis : Recipe_Surgery
	{
		public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
		{
			if (ZombieBlob.CanSeverSymbiosis(pawn) == false)
				yield break;
			var torso = pawn.health?.hediffSet?.GetNotMissingParts().FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
			if (torso != null)
				yield return torso;
		}

		public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
		{
			if (pawn.DestroyedOrNull() || pawn.Dead || pawn.Map != billDoer.Map || pawn.IsInAnyStorage())
				return;
			var blob = ZombieBlob.LinkedBlobFor(pawn);
			if (blob == null)
				return;

			var success = blob.TrySeverSymbiosis(pawn, billDoer);
			if (success)
			{
				_ = TaleRecorder.RecordTale(TaleDefOf.DidSurgery, new object[] { billDoer, pawn });
				return;
			}

			HealthUtility.GiveRandomSurgeryInjuries(pawn, 45, part);
			pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.BotchedMySurgery, billDoer);
			Messages.Message("MessageMedicalOperationFailureMinor".Translate(billDoer.LabelShort, pawn.LabelShort, billDoer.Named("SURGEON"), pawn.Named("PATIENT"), recipe.Named("RECIPE")), pawn, MessageTypeDefOf.NegativeHealthEvent, true);
		}

		public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part)
		{
			return "SeverBlobSymbiosis".Translate();
		}
	}
}
