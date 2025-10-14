
using HarmonyLib;
using System.Reflection;
using Verse;

namespace ZombieLand
{
    [HarmonyPatch]
            static class WASDCompatibility
            {
                private static System.Type _wasdGameComponentType;
                private static FieldInfo _instanceField;
                private static FieldInfo _wasdedPawnField;
    
                static bool Prepare()
                {
                    _wasdGameComponentType = AccessTools.TypeByName("wasdedPawn.WASDGameComponent");
                    if (_wasdGameComponentType == null)
                        return false;
    
                    _instanceField = AccessTools.Field(_wasdGameComponentType, "Instance");
                    if (_instanceField == null)
                        return false;
    
                    _wasdedPawnField = AccessTools.Field(_wasdGameComponentType, "wasdedPawn");
                    return _wasdedPawnField != null;
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
                // Use cached values
                var wasdGameComponent = _instanceField.GetValue(null);
                if (wasdGameComponent != null)
                {
                    var wasdedPawn = _wasdedPawnField.GetValue(wasdGameComponent) as Pawn;
                    if (pawn == wasdedPawn)
                    {
                        __result = true;
                    }
                }
            }
        }
    }
}
