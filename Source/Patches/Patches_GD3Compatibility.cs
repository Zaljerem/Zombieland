using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;

namespace ZombieLand
{
    [StaticConstructorOnStartup]
    public static class GD3Compatibility_Patches
    {
        static GD3Compatibility_Patches()
        {
            var harmony = new Harmony("Zombieland.GD3Compatibility");

            MethodInfo targetMethod = AccessTools.Method("GD3.GDUtility:IsFlyingMech", new[] { typeof(Pawn) });

            if (targetMethod != null)
            {
                var prefixMethod = new HarmonyMethod(typeof(GD3Compatibility_Patches), nameof(IsFlyingMech_Prefix));

                harmony.Patch(targetMethod, prefix: prefixMethod);
                Log.Message("Zombieland: Successfully patched GD3.GDUtility.IsFlyingMech for compatibility.");
            }
            else
            {
                Log.Warning("Zombieland: Could not find GD3.GDUtility.IsFlyingMech. GD3 compatibility patch not applied.");
            }
        }

        public static bool IsFlyingMech_Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn != null && pawn.def.defName.Contains("Zombie"))
            {
                __result = false; 
                return false; 
            }
            return true;
        }
    }
}
