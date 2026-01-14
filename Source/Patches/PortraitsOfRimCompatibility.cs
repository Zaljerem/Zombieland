using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace ZombieLand
{
    [StaticConstructorOnStartup]
    public class PortraitsOfRimCompatibility
    {
        static PortraitsOfRimCompatibility()
        {
            // Check if Portraits of Rim is loaded before applying patches
            if (ModLister.HasActiveModWithName("Portraits of the Rim"))
            {
                var harmony = new Harmony("ZombieLand.PortraitsOfRimCompatibility");

                // Patch the ShouldRefreshPortrait method to add null checks for zombie pawns
                var shouldRefreshMethod = AccessTools.Method("PortraitsOfTheRim.Portrait:ShouldRefreshPortrait");
                if (shouldRefreshMethod != null)
                {
                    try
                    {
                        harmony.Patch(shouldRefreshMethod,
                            prefix: new HarmonyMethod(typeof(PortraitsOfRimCompatibility), nameof(ShouldRefreshPortraitPrefix)));
                        Log.Message("[ZombieLand] Successfully patched PortraitsOfTheRim.Portrait.ShouldRefreshPortrait for compatibility");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[ZombieLand] Failed to patch PortraitsOfTheRim.Portrait.ShouldRefreshPortrait: {ex.Message}");
                    }
                }

                // Also patch the PortraitTextures property getter as a backup
                var portraitTexturesMethod = AccessTools.PropertyGetter("PortraitsOfTheRim.Portrait:PortraitTextures");
                if (portraitTexturesMethod != null)
                {
                    try
                    {
                        harmony.Patch(portraitTexturesMethod,
                            prefix: new HarmonyMethod(typeof(PortraitsOfRimCompatibility), nameof(PortraitTexturesPrefix)));
                        Log.Message("[ZombieLand] Successfully patched PortraitsOfTheRim.Portrait.PortraitTextures for compatibility");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[ZombieLand] Failed to patch PortraitsOfTheRim.Portrait.PortraitTextures: {ex.Message}");
                    }
                }
            }
        }

        // Prefix method that runs before the original ShouldRefreshPortrait
        public static bool ShouldRefreshPortraitPrefix(object __instance)
        {
            try
            {
                // Get the pawn field from the Portrait instance
                FieldInfo pawnField = AccessTools.Field("PortraitsOfTheRim.Portrait:pawn");
                if (pawnField == null)
                {
                    Log.Warning("[ZombieLand] Could not find pawn field in Portrait class");
                    return true; // Let original method run
                }

                Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null)
                {
                    // If pawn is null, let original method handle it
                    return true;
                }

                // Check if this is a zombie by checking its type
                if (pawn.GetType().Name == "Zombie" || pawn is Zombie)
                {
                    // For zombie pawns, return false to indicate no refresh is needed
                    // This prevents the null reference exceptions that occur when accessing uninitialized properties
                    return false; // Skip original method and return false
                }

                // For non-zombie pawns, let the original method run normally
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[ZombieLand] Error in ShouldRefreshPortraitPrefix: {ex.Message}");
                return true; // Let original method run if there's an error
            }
        }

        // Prefix method that runs before the original PortraitTextures getter
        public static bool PortraitTexturesPrefix(object __instance, out object __result)
        {
            __result = null;

            try
            {
                // Get the pawn field from the Portrait instance
                FieldInfo pawnField = AccessTools.Field("PortraitsOfTheRim.Portrait:pawn");
                if (pawnField == null)
                {
                    Log.Warning("[ZombieLand] Could not find pawn field in Portrait class");
                    __result = null;
                    return true; // Let original method run
                }

                Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null)
                {
                    // If pawn is null, let original method handle it
                    __result = null;
                    return true;
                }

                // Check if this is a zombie by checking its type
                if (pawn.GetType().Name == "Zombie" || pawn is Zombie)
                {
                    // For zombie pawns, return an empty list to prevent null reference exceptions
                    // Using reflection to create the generic list type
                    Type tupleType = typeof(ValueTuple<,>).MakeGenericType(
                        AccessTools.TypeByName("PortraitsOfTheRim.PortraitElementDef"),
                        typeof(Texture)
                    );
                    Type listType = typeof(List<>).MakeGenericType(tupleType);
                    var emptyList = Activator.CreateInstance(listType);
                    __result = emptyList;

                    // Also set the portraitTextures field to prevent future issues
                    FieldInfo texturesField = AccessTools.Field("PortraitsOfTheRim.Portrait:portraitTextures");
                    if (texturesField != null)
                    {
                        texturesField.SetValue(__instance, emptyList);
                    }

                    return false; // Skip original method
                }

                // For non-zombie pawns, let the original method run normally
                __result = null;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[ZombieLand] Error in PortraitTexturesPrefix: {ex.Message}");
                __result = null;
                return true; // Let original method run if there's an error
            }
        }
    }
}