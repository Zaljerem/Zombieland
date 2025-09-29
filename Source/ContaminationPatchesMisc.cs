using System.Reflection.Emit;
using HarmonyLib;
using Mono.Security;
using RimWorld;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
	static class Pawn_Kill_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn __instance)
		{
			if (__instance is Zombie zombie)
				zombie.Map?.AddContamination(zombie.Position, ZombieSettings.Values.contamination.zombieDeathAdd);
		}
	}

	[HarmonyPatch(typeof(Fire), "DoComplexCalcs", MethodType.Normal)]
	static class Fire_DoComplexCalcs_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Fire __instance)
		{
			var map = __instance.Map;
			if (map == null)
				return;
			var cell = __instance.Position;
			var instance = ContaminationManager.Instance;
			map.thingGrid.ThingsListAtFast(cell).Do(thing => instance.Subtract(thing, ZombieSettings.Values.contamination.fireReduction));
			var grid = map.GetContamination();
			var oldValue = grid[cell];
			if (oldValue > 0)
				grid[cell] = oldValue - ZombieSettings.Values.contamination.fireReduction;
		}
	}

	[HarmonyPatch]
	static class Verb_MeleeAttack_ApplyMeleeDamageToTarget_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Verb_MeleeApplyHediff), "ApplyMeleeDamageToTarget");
			yield return AccessTools.Method(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget");
		}

		static void Postfix(Verb_MeleeAttack __instance, LocalTargetInfo target, DamageWorker.DamageResult __result)
		{
			if (__result.totalDamageDealt <= 0f)
				return;
			var pawn = __instance.Caster;
			var thing = target.Thing;
			ZombieSettings.Values.contamination.meleeEqualize.Equalize(pawn, thing);
		}
	}

	[HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
	static class GenReciepe_MakeRecipeProducts_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, Pawn worker, IBillGiver billGiver, List<Thing> ingredients)
		{
			var results = things.ToArray();
			if (billGiver is not Thing bench)
				return things;

			var manager = ContaminationManager.Instance;
			var transfer = ingredients.Sum(i => manager.Get(i));
			ingredients.TransferContamination(ZombieSettings.Values.contamination.receipeTransfer, results);
			foreach (var result in results)
				transfer += Mathf.Abs(manager.Equalize(result, bench, ZombieSettings.Values.contamination.produceEqualize));
			transfer += Mathf.Abs(manager.Equalize(bench, worker, ZombieSettings.Values.contamination.benchEqualize));
			worker.TransferContamination(ZombieSettings.Values.contamination.workerTransfer, results);
			//if (transfer > 0)
			//	Log.Warning($"{worker} produces {results.Join(t => $"{t}")} from {ingredients.Join(t => $"{t}")}{(bench != null ? $" on {bench}" : "")}");
			return results;
		}
	}

	[HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryTransferToContainer))]
	[HarmonyPatch([typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing), typeof(bool)], [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal])]
	static class ThingOwner_TryTransferToContainer_Patch
	{
		public static sbyte activeThingOwnerMapIndex;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(ThingOwner __instance, Thing item, ThingOwner otherContainer)
		{
			activeThingOwnerMapIndex = (sbyte)(ThingOwnerUtility.GetRootMap(__instance.Owner)?.Index ?? -1);
			if (otherContainer.Owner is Frame frame && (sbyte)Tools.f_mapIndexOrState.GetValue(frame) >= 0)
			{
				var savedMapIndex = (sbyte)Tools.f_mapIndexOrState.GetValue(item);
				Tools.f_mapIndexOrState.SetValue(item, (sbyte)Tools.f_mapIndexOrState.GetValue(frame));
				frame.SetContamination(item.GetContamination());
				Tools.f_mapIndexOrState.SetValue(item, savedMapIndex);
			}
		}

		static void Postfix()
		{
			activeThingOwnerMapIndex = -1;
		}
	}

	[HarmonyPatch]
	static class Pawn_CarryTracker_TryStartCarry_Patch_Patch
	{
		public static sbyte pawnMapIndex;



		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			var methods = AccessTools.GetDeclaredMethods(typeof(Pawn_CarryTracker))
				.Where(method => method.Name == nameof(Pawn_CarryTracker.TryStartCarry));
			foreach (var method in methods)
				yield return method;
		}

		static void Prefix(Pawn_CarryTracker __instance)
		{
			pawnMapIndex = (sbyte)Tools.f_mapIndexOrState.GetValue(__instance.pawn);
		}

		static void Postfix()
		{
			pawnMapIndex = -1;
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
	static class Thing_TryAbsorbStack_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing other, out (int, float) __state)
		{
			__state = other == null || Tools.IsPlaying() == false ? (0, 0f) : (other.stackCount, other.GetContamination(includeHoldings: true));
		}

		static void Postfix(bool __result, Thing __instance, Thing other, (int, float) __state)
		{
			if (Tools.IsPlaying() == false)
				return;

			var (otherOldStackSize, otherContamination) = __state;
			var otherNewStackSize = other.stackCount;
			var otherCount = otherOldStackSize - otherNewStackSize;
			var thisCount = __instance.stackCount - otherCount;

			var thisContamination = __instance.GetContamination(includeHoldings: true);
			var newContamination = (otherCount * otherContamination + thisCount * thisContamination) / (thisCount + otherCount);
			var transfer = newContamination - thisContamination;

						var savedMapIndex = (sbyte)Tools.f_mapIndexOrState.GetValue(__instance);
						Tools.f_mapIndexOrState.SetValue(__instance, Pawn_CarryTracker_TryStartCarry_Patch_Patch.pawnMapIndex);
						if (transfer > 0)
							__instance.AddContamination(transfer);
						if (transfer < 0)
							__instance.SubtractContamination(-transfer);
						Tools.f_mapIndexOrState.SetValue(__instance, savedMapIndex);
			
						if (__result == false && other != null)
						{
							var factor = otherNewStackSize / (float)otherOldStackSize;
							savedMapIndex = (sbyte)Tools.f_mapIndexOrState.GetValue(other);
							Tools.f_mapIndexOrState.SetValue(other, Pawn_CarryTracker_TryStartCarry_Patch_Patch.pawnMapIndex);
							other.SubtractContamination(otherContamination * factor);
							Tools.f_mapIndexOrState.SetValue(other, savedMapIndex);
						}		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
	static class Thing_SplitOff_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Thing __result, Thing __instance)
		{
			if (__result == null || __result == __instance)
				return;
			if (Tools.IsPlaying() == false)
				return;

			var contamination = __instance.GetContamination();
			if (contamination == 0)
				return;

			var savedMapIndex1 = (sbyte)Tools.f_mapIndexOrState.GetValue(__instance);
			var savedMapIndex2 = (sbyte)Tools.f_mapIndexOrState.GetValue(__result);
			var ownerMapIndex = ThingOwner_TryTransferToContainer_Patch.activeThingOwnerMapIndex;
			if ((sbyte)Tools.f_mapIndexOrState.GetValue(__instance) < 0 && ownerMapIndex >= 0)
			{
				Tools.f_mapIndexOrState.SetValue(__instance, ownerMapIndex);
				Tools.f_mapIndexOrState.SetValue(__result, ownerMapIndex);
				__result.AddContamination(contamination);
				Tools.f_mapIndexOrState.SetValue(__instance, savedMapIndex1);
				Tools.f_mapIndexOrState.SetValue(__result, savedMapIndex2);
			}
			else
			{
				var savedMapIndex = (sbyte)Tools.f_mapIndexOrState.GetValue(__result);
				Tools.f_mapIndexOrState.SetValue(__result, (sbyte)Tools.f_mapIndexOrState.GetValue(__instance));
				__result.AddContamination(contamination);
				Tools.f_mapIndexOrState.SetValue(__result, savedMapIndex);
			}
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
	static class Thing_Ingested_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static readonly MethodInfo m_IngestedCalculateAmounts = AccessTools.Method(typeof(Thing), "IngestedCalculateAmounts", new[] { typeof(Pawn), typeof(float), typeof(int).MakeByRefType(), typeof(float).MakeByRefType() });

		static void IngestedCalculateAmounts(Thing self, Pawn ingester, float nutritionWanted, out int numTaken, out float nutritionIngested)
		{
			var oldStackCount = self.stackCount;

			float totalNutrition = 0f;
			if (self is Plant plant)
				totalNutrition = plant.GetStatValue(StatDefOf.Nutrition);
			if (self is Pawn pawn)
				totalNutrition = FoodUtility.NutritionForEater(ingester, pawn);
			if (self is Corpse corpse)
				totalNutrition = FoodUtility.NutritionForEater(corpse.InnerPawn, self);

			var parameters = new object[] { ingester, nutritionWanted, 0, 0f };
			m_IngestedCalculateAmounts.Invoke(self, parameters);
			numTaken = (int)parameters[2];
			nutritionIngested = (float)parameters[3];

			var factor = numTaken == 0 ? (totalNutrition == 0 ? 1 : nutritionIngested / totalNutrition) : (oldStackCount == 0 ? 1 : numTaken / (float)oldStackCount);
			self.TransferContamination(ZombieSettings.Values.contamination.ingestTransfer * factor, ingester);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = AccessTools.Method(typeof(Thing), "IngestedCalculateAmounts", new[] { typeof(Pawn), typeof(float), typeof(int).MakeByRefType(), typeof(float).MakeByRefType() });
			var to = SymbolExtensions.GetMethodInfo((int numToken, float nutritionIngested) => IngestedCalculateAmounts(default, default, default, out numToken, out nutritionIngested));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.SplitOff))]
	static class MinifiedThing_SplitOff_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(MinifiedThing __instance, out int __state) => __state = __instance.stackCount;

		static void Postfix(Thing __result, MinifiedThing __instance, int __state)
		{
			if (__result == __instance)
				return;
			if (Tools.IsPlaying() == false)
				return;

			var remaining = __instance.Spawned == false ? 0 : __instance.stackCount;
			var factor = __state == 0 ? 1f : 1f - remaining / (float)__state;
			__instance.TransferContamination(factor, __result);
		}
	}

	[HarmonyPatch]
	static class ThingComp_MakeThing_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((CompChangeableProjectile comp) => comp.RemoveShell());
			yield return SymbolExtensions.GetMethodInfo((CompEggLayer comp) => comp.ProduceEgg());
			yield return SymbolExtensions.GetMethodInfo((CompHasGatherableBodyResource comp) => comp.Gathered(default));
			yield return AccessTools.Method(typeof(CompMechCarrier), nameof(CompMechCarrier.PostSpawnSetup));
			yield return SymbolExtensions.GetMethodInfo((CompPlantable comp) => comp.DoPlant(default, default, default));
			yield return AccessTools.Method(typeof(CompPollutionPump), "Pump");
			yield return AccessTools.Method(typeof(CompRefuelable), nameof(CompRefuelable.PostDestroy));
			yield return AccessTools.Method(typeof(CompSpawnerItems), "SpawnItems");
			yield return SymbolExtensions.GetMethodInfo((CompSpawner comp) => comp.TryDoSpawn());
			yield return AccessTools.Method(typeof(CompTreeConnection), nameof(CompTreeConnection.CompTick));
			yield return SymbolExtensions.GetMethodInfo((CompWasteProducer comp) => comp.ProduceWaste(0));
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, ThingComp thingComp)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			if (thingComp?.parent is Thing thing)
			{
				thing.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			}
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default(ThingDef), default(ThingDef)));
			var to = SymbolExtensions.GetMethodInfo(() => MakeThing(default(ThingDef), default(ThingDef), default));

			var codes = new List<CodeInstruction>(instructions);
			for (int i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == System.Reflection.Emit.OpCodes.Call && codes[i].operand is MethodInfo method && method == from)
				{
					// The two arguments for ThingMaker.MakeThing (def, stuff) are already on the stack.
					// We need to insert Ldarg_0 (this, which is the ThingComp) before the call, but after the arguments have been pushed.
					// So, we insert Ldarg_0 at the current index, and then update the operand of the original call.

					codes.Insert(i, new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0)); // Push 'this' (ThingComp)
					i++; // Adjust index for the newly inserted instruction

					codes[i].operand = to; // Change the operand of the original call to our MakeThing
					break; // Assuming only one such call needs patching
				}
			}
			return codes;
		}
	}


	[HarmonyPatch(typeof(ExecutionUtility), "ExecutionInt")]
	static class ExecutionContamination
	{
		[ThreadStatic]
		public static Pawn currentExecutionVictim;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn victim)
		{
			currentExecutionVictim = victim;
		}

		static void Postfix()
		{
			currentExecutionVictim = null;
		}
	}

	[HarmonyPatch(typeof(FilthMaker), "TryMakeFilth", new Type[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(Filth), typeof(string), typeof(FilthSourceFlags), typeof(bool) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
	static class FilthMaker_TryMakeFilth_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(bool __result, Filth outFilth)
		{
			if (__result && outFilth != null && ExecutionContamination.currentExecutionVictim != null)
			{
				ExecutionContamination.currentExecutionVictim.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, outFilth);
			}
		}
	}

	[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
	static class TendUtility_DoTend_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn doctor, Pawn patient, Medicine medicine)
		{
			if (doctor == null || patient == null)
				return;
			var manager = ContaminationManager.Instance;
			if (medicine != null)
			{
				manager.Transfer(medicine, ZombieSettings.Values.contamination.medicineTransfer, new[] { patient });
				if (doctor != patient)
					manager.Transfer(medicine, ZombieSettings.Values.contamination.medicineTransfer, new[] { doctor });
			}
			if (doctor != patient)
			{
				var medicineSkill = doctor.skills.GetSkill(SkillDefOf.Medicine).Level;
				var weight = GenMath.LerpDoubleClamped(0, 20, ZombieSettings.Values.contamination.tendEqualizeWorst, ZombieSettings.Values.contamination.tendEqualizeBest, medicineSkill);
				manager.Equalize(doctor, patient, weight);
			}
		}
	}

	[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GainComfortFromCellIfPossible))]
	[HarmonyPatch(new[] { typeof(Pawn), typeof(int), typeof(bool) })]
	static class PawnUtility_GainComfortFromCellIfPossible_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn p)
		{
			var tick = p.thingIDNumber % 1000;
			if (Find.TickManager.TicksGame % 1000 != tick)
				return;

			var cell = p.Position;
			ZombieSettings.Values.contamination.restEqualize.Equalize(p, cell);
			var edifice = cell.GetEdifice(p.Map);
			if (edifice != null)
				ZombieSettings.Values.contamination.restEqualize.Equalize(p, edifice);
		}
	}

	[HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.CarryHandsTickInterval))]
	static class Pawn_CarryTracker_CarryHandsTick_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn_CarryTracker __instance, int delta)
		{
			var pawn = __instance.pawn;

			var tick = pawn.thingIDNumber % 900;
			if (Find.TickManager.TicksGame % 900 != tick)
				return;

			var thing = __instance.CarriedThing;
			if (thing == null)
				return;
			ZombieSettings.Values.contamination.carryEqualize.Equalize(pawn, thing, false, true);
		}
	}

	[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.Mated))]
	static class PawnUtility_Mated_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn male, Pawn female)
		{
			0.5f.Equalize(male, female);
		}
	}

	[HarmonyPatch(typeof(JobDriver_Lovin), "MakeNewToils")]
	static class JobDriver_Lovin_MakeNewToils_Patch
	{
		static readonly string layDownToilName = Toils_LayDown.LayDown(default, default, default).debugName;

		static bool Prepare() => Constants.CONTAMINATION;

		static readonly FieldInfo f_ticksLeft = AccessTools.Field(typeof(JobDriver_Lovin), "ticksLeft");
		static readonly FieldInfo f_Partner = AccessTools.Field(typeof(JobDriver_Lovin), "Partner");

		static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_Lovin __instance)
		{
			foreach (var toil in toils)
			{
				if (toil.debugName == layDownToilName && toil.initAction != null)
				{
					var action = toil.initAction;
					toil.initAction = () =>
					{
						if ((int)f_ticksLeft.GetValue(__instance) <= 25000)
						{
							var p1 = __instance.pawn;
							var p2 = (Pawn)f_Partner.GetValue(__instance);
							0.1f.Equalize(p1, p2);
						}
						action();
					};
				}
				yield return toil;
			}
		}
	}

	[HarmonyPatch(typeof(Corpse), nameof(Corpse.InnerPawn), MethodType.Setter)]
	static class Corpse_InnerPawn_Setter_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;
		static void Postfix(Corpse __instance, Pawn value)
		{
			if (Current.Game.World != null && value != null)
				__instance.SetContamination(value.GetContamination());
		}
	}

	[HarmonyPatch]
	static class Jobdriver_ClearPollution_Spawn_Patch
	{
		static readonly MethodInfo m_Spawn = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn((ThingDef)default, default, default, default));

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			return AccessTools.FirstMethod(typeof(JobDriver_ClearPollution), method => method.CallsMethod(m_Spawn));
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, ZombieSettings.Values.contamination.wastePackAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = m_Spawn;
			var to = SymbolExtensions.GetMethodInfo(() => Spawn(default, default, default, default));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch]
	static class MedicalRecipesUtility_GenSpawn_Spawn_Patches
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo(() => MedicalRecipesUtility.SpawnNaturalPartIfClean(default, default, default, default));
			yield return SymbolExtensions.GetMethodInfo(() => MedicalRecipesUtility.SpawnThingsFromHediffs(default, default, default, default));
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Pawn pawn)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			pawn.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Recipe_RemoveImplant), nameof(Recipe_RemoveImplant.ApplyOnPawn))]
	static class Recipe_RemoveImplant_ApplyOnPawn_Patches
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Pawn pawn)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			pawn.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(CompLifespan), "Expire")]
	static class CompLifespan_Expire_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, CompLifespan comp)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			comp.parent.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(RoofCollapserImmediate), "DropRoofInCellPhaseOne")]
	static class RoofCollapserImmediate_DropRoofInCellPhaseOne_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, IntVec3 c)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			var contamination = map.GetContamination(c);
			result.AddContamination(contamination);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_AffectFloor_MakeNewToils_Patch
	{
		static readonly MethodInfo m_DoEffect = AccessTools.Method(typeof(JobDriver_AffectFloor), "DoEffect", new[] { typeof(IntVec3) });
		static readonly FieldInfo f_Map = AccessTools.Field(typeof(JobDriver), "Map");

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var type = AccessTools.FirstInner(typeof(JobDriver_AffectFloor), type => type.Name.Contains("DisplayClass"));
			return AccessTools.FirstMethod(type, method => method.CallsMethod(m_DoEffect));
		}

		static void DoEffect(JobDriver_AffectFloor self, IntVec3 c)
		{
			var map = (Map)f_Map.GetValue(self);
			var contamination = map.GetContamination(c);
			self.pawn.AddContamination(contamination, ZombieSettings.Values.contamination.floorAdd);
			m_DoEffect.Invoke(self, new object[] { c });
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = SymbolExtensions.GetMethodInfo(() => DoEffect(default, default));
			return instructions.MethodReplacer(m_DoEffect, replacement);
		}
	}

	[HarmonyPatch]
	static class JobDriver_DisassembleMech_MakeNewToils_Patch
	{
		static readonly MethodInfo m_TryPlaceThing = AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) });
		static readonly FieldInfo f_Mech = AccessTools.Field(typeof(JobDriver_DisassembleMech), "Mech");

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			return AccessTools.FirstMethod(typeof(JobDriver_DisassembleMech), method => method.CallsMethod(m_TryPlaceThing));
		}

		static bool TryPlaceThing(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode, JobDriver_DisassembleMech driver)
		{
			var pawn = driver.pawn;
			var mech = (Pawn)f_Mech.GetValue(driver);
			mech.TransferContamination(ZombieSettings.Values.contamination.disassembleTransfer, pawn, thing);
			return GenPlace.TryPlaceThing(thing, center, map, mode);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var findMethod = AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode) });
			var replaceMethod = AccessTools.Method(typeof(JobDriver_DisassembleMech_MakeNewToils_Patch), nameof(TryPlaceThing), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(JobDriver_DisassembleMech) });

			for (int i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo method && method == findMethod)
				{
					// Found the call to GenPlace.TryPlaceThing
					// Inject Ldarg_0 (this of the anonymous method's generated class)
					codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
					i++; // Move to the next instruction

					// Change the operand to our helper method
					codes[i].operand = replaceMethod;
					break; // Assuming only one such call needs patching
				}
			}
			return codes;
		}
	}
}