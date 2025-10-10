
using HarmonyLib;
using System.Reflection;
using Verse;

namespace ZombieLand
{
    [HarmonyPatch]
    static class WASDCompatibility
    {
        static bool Prepare()
        {
            return AccessTools.TypeByName("wasdedPawn.WASDGameComponent") != null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Tools), nameof(Tools.Attackable));
        }

        [HarmonyPostfix]
        static void Postfix(ref bool __result, Zombie zombie, AttackMode mode, Thing thing)
        {
            if (__result == false && thing is Pawn pawn)
            {
                var wasdGameComponentType = AccessTools.TypeByName("wasdedPawn.WASDGameComponent");
                if (wasdGameComponentType != null)
                {
                    var instanceField = AccessTools.Field(wasdGameComponentType, "Instance");
                    if (instanceField != null)
                    {
                        var wasdGameComponent = instanceField.GetValue(null);
                        if (wasdGameComponent != null)
                        {
                            var wasdedPawnField = AccessTools.Field(wasdGameComponentType, "wasdedPawn");
                            if (wasdedPawnField != null)
                            {
                                var wasdedPawn = wasdedPawnField.GetValue(wasdGameComponent) as Pawn;
                                if (pawn == wasdedPawn)
                                {
                                    __result = true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
