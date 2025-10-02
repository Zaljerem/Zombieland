using HarmonyLib;
using Verse;

namespace ZombieLand.Supress
{
    [HarmonyPatch(typeof(Log), nameof(Log.Error))]
    static class Patch_SuppressDownedPathingError
    {
        [HarmonyPrefix]
        static bool Prefix(string text)
        {
            if (text.Contains("tried to path while downed. This should never happen."))
            {
                return false;
            }

            return true;
        }
    }
}