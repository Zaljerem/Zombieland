using System;
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
            var pawn = (Pawn)AccessTools.Field(typeof(PawnRenderer), "pawn").GetValue(__instance);
            if (pawn is ZombieBlob zombieBlob)
            {
                zombieBlob.DrawBlob();
            }
            else if (pawn is ZombieSpitter zombieSpitter)
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

    	[HarmonyPatch(typeof(PawnRenderer))]
    	[HarmonyPatch(MethodType.Constructor)]
    	[HarmonyPatch(new Type[] { typeof(Pawn) })]    public static class PawnRenderer_Constructor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PawnRenderer __instance)
        {
            var pawn = (Pawn)AccessTools.Field(typeof(PawnRenderer), "pawn").GetValue(__instance);
            if (__instance.renderTree == null)
            {
                __instance.renderTree = new PawnRenderTree(pawn);
            }
        }
    }
}