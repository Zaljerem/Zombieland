using HarmonyLib;
using System.Reflection;
using Verse;
using static HarmonyLib.AccessTools;

namespace ZombieLand
{
	public class DubsTools
	{
		private static Pawn GetPawn(PawnUIOverlay overlay)
		{
			var field = typeof(PawnUIOverlay).GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);
			return (Pawn)field.GetValue(overlay);
		}

		public static void Init()
		{
			var harmony = new Harmony("net.pardeike.zombieland.dubs");
			var method = Method("Analyzer.Fixes.H_DrawNamesFix:Prefix");
			if (method != null)
			{
				var prefix = SymbolExtensions.GetMethodInfo((bool b) => Prefix(default, ref b));
				harmony.Patch(method, prefix: new HarmonyMethod(prefix));
			}
		}

		static bool Prefix([HarmonyArgument("__instance")] PawnUIOverlay instance, ref bool __result)
		{
			if (GetPawn(instance) is not Zombie)
				return true;

			__result = true;
			return false;
		}
	}
}