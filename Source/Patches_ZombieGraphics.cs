using HarmonyLib;
using Verse;
using UnityEngine;

namespace ZombieLand
{
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    public static class PawnRenderer_RenderPawnAt_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PawnRenderer __instance, Vector3 drawLoc)
        {
            if (__instance.pawn is ZombieBlob zombieBlob)
            {
                zombieBlob.DrawBlob();
            }
            else if (__instance.pawn is ZombieSpitter zombieSpitter)
            {
                zombieSpitter.DrawSpitter();
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode_Body), nameof(PawnRenderNode_Body.GraphicFor))]
    public static class PawnRenderNode_Body_GraphicFor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Graphic __result)
        {
            if (pawn is Zombie zombie && zombie.customBodyGraphic != null)
            {
                __result = zombie.customBodyGraphic;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode_Head), nameof(PawnRenderNode_Head.GraphicFor))]
    public static class PawnRenderNode_Head_GraphicFor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Graphic __result)
        {
            if (pawn is Zombie zombie && zombie.customHeadGraphic != null)
            {
                __result = zombie.customHeadGraphic;
            }
        }
    }
}