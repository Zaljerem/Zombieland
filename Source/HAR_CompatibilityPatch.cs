using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
    /// <summary>
    /// Compatibility patch for Humanoid Alien Races (HAR) to fix NullReferenceException in StumpGraphicHelper
    /// The issue occurs when pawnRenderResolveData is null in the StumpGraphicHelper method.
    /// This patch ensures proper initialization before accessing the data.
    /// Uses reflection to avoid compilation dependencies on HAR.
    /// </summary>
    public static class HAR_CompatibilityPatch
    {
        private static readonly Type alienRenderTreePatchesType;
        private static readonly FieldInfo pawnRenderResolveDataField;
        private static readonly MethodInfo regenerateResolveDataMethod;
        private static readonly Type pawnRenderResolveDataType;
        private static readonly ConstructorInfo pawnRenderResolveDataConstructor;
        private static readonly FieldInfo pawnField;
        private static readonly FieldInfo alienPropsField;
        private static readonly FieldInfo alienCompField;
        private static readonly FieldInfo lsaaField;
        private static readonly FieldInfo sharedIndexField;

        static HAR_CompatibilityPatch()
        {
            // Try to find HAR types via reflection
            alienRenderTreePatchesType = AccessTools.TypeByName("AlienRace.AlienRenderTreePatches");
            if (alienRenderTreePatchesType == null)
            {
                Log.Warning("HAR_CompatibilityPatch: Could not find AlienRace.AlienRenderTreePatches type. HAR may not be installed or loaded.");
                return;
            }

            pawnRenderResolveDataField = AccessTools.Field(alienRenderTreePatchesType, "pawnRenderResolveData");
            regenerateResolveDataMethod = AccessTools.Method(alienRenderTreePatchesType, "RegenerateResolveData", new Type[] { typeof(Pawn) });
            pawnRenderResolveDataType = AccessTools.TypeByName("AlienRace.AlienRenderTreePatches+PawnRenderResolveData");
            
            if (pawnRenderResolveDataType != null)
            {
                pawnRenderResolveDataConstructor = pawnRenderResolveDataType.GetConstructor(Type.EmptyTypes);
                pawnField = AccessTools.Field(pawnRenderResolveDataType, "pawn");
                alienPropsField = AccessTools.Field(pawnRenderResolveDataType, "alienProps");
                alienCompField = AccessTools.Field(pawnRenderResolveDataType, "alienComp");
                lsaaField = AccessTools.Field(pawnRenderResolveDataType, "lsaa");
                sharedIndexField = AccessTools.Field(pawnRenderResolveDataType, "sharedIndex");
            }
        }

        public static void Patch(Harmony harmony)
        {
            // Only patch if HAR is available
            if (alienRenderTreePatchesType == null || pawnRenderResolveDataField == null || regenerateResolveDataMethod == null)
            {
                Log.Message("HAR_CompatibilityPatch: Skipping patch - HAR not found or incomplete");
                return;
            }

            // Use a prefix to ensure pawnRenderResolveData is initialized before the original method runs
            harmony.Patch(
                AccessTools.Method(alienRenderTreePatchesType, "StumpGraphicHelper", new Type[] { typeof(Verse.PawnRenderNode_Stump), typeof(Verse.Pawn) }),
                new HarmonyMethod(typeof(HAR_CompatibilityPatch), nameof(StumpGraphicHelperPrefix)),
                null,
                null
            );
        }

        /// <summary>
        /// Prefix method that ensures pawnRenderResolveData is initialized before the original method runs
        /// This is the most reliable approach - it ensures the data is always initialized before the original method executes
        /// Uses reflection to access HAR types safely
        /// </summary>
        public static bool StumpGraphicHelperPrefix(PawnRenderNode_Stump node, Pawn pawn)
        {
            try
            {
                // Skip if HAR is not available
                if (alienRenderTreePatchesType == null || pawnRenderResolveDataField == null || regenerateResolveDataMethod == null)
                {
                    return true; // Continue with original method
                }

                // Ensure pawnRenderResolveData is initialized for this pawn
                // Just call RegenerateResolveData - it handles all the initialization logic
                regenerateResolveDataMethod.Invoke(null, new object[] { pawn });
            }
            catch (Exception ex)
            {
                Log.Error($"HAR Compatibility Patch - Error in StumpGraphicHelperPrefix: {ex}");
                return false; // Don't run original method if we can't initialize safely
            }
            
            return true; // Continue with original method
        }
    }
}