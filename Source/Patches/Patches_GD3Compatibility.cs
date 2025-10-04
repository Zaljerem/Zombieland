using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;

namespace ZombieLand.Patches
{
    [StaticConstructorOnStartup]
    public static class Patches_GD3Compatibility
    {
        static Patches_GD3Compatibility()
        {
            var harmony = new Harmony("Zombieland.GD3Compatibility");

            // Get the IsFlyingMech method from GD3.GDUtility using reflection
            MethodInfo targetMethod = AccessTools.Method("GD3.GDUtility:IsFlyingMech", new[] { typeof(Pawn) });

            if (targetMethod != null)
            {
                // Create a Prefix method for our patch
                var prefixMethod = new HarmonyMethod(typeof(Patches_GD3Compatibility), nameof(IsFlyingMech_Prefix));

                // Apply the patch
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
            // Check if the pawn is a zombie (using Zombieland's internal logic or defName)
            // For simplicity, we'll use a defName check here. A more robust check might use Zombieland's internal zombie identification.
            if (pawn != null && pawn.def.defName.Contains("Zombie"))
            {
                __result = false; // Force IsFlyingMech to return false for zombies
                return false; // Skip the original method
            }
            return true; // Continue with the original method for non-zombie pawns
        }
    }
}
