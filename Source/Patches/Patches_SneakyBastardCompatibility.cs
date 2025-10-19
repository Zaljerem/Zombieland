
using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;

namespace ZombieLand
{
    [StaticConstructorOnStartup]
    public static class SneakyBastardCompatibility_Patches
    {
        static SneakyBastardCompatibility_Patches()
        {
            var harmony = new Harmony("Zombieland.SneakyBastardCompatibility");

            var original = AccessTools.Method("SneakyBastardForCE.StatPart_SEX_Dodge:TransformValue");
            var prefix = new HarmonyMethod(typeof(SneakyBastardCompatibility_Patches), nameof(TransformValue_Prefix));
            harmony.Patch(original, prefix: prefix);

            original = AccessTools.Method("SneakyBastardForCE.StatPart_SEX_AnimalDodge:TransformValue");
            harmony.Patch(original, prefix: prefix);
        }

        public static bool TransformValue_Prefix(StatRequest req, ref float val)
        {
            if (req.HasThing && req.Thing is Pawn pawn && pawn.IsZombie())
            {
                val = 0f;
                return false;
            }
            return true;
        }

        private static bool IsZombie(this Pawn pawn)
        {
            return pawn.def.defName.Contains("Zombie");
        }
    }
}
