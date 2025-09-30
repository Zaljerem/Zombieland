using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System;
using HarmonyLib;
using RimWorld.Planet;
// for accessing map.Tile.Layer
namespace ZombieLand
{
    [StaticConstructorOnStartup]
    public static class OdysseyTools
    {
        public static bool IsActive { get; private set; }
        public static List<Def> AllLayers { get; private set; }

        private static Type planetLayerDefType;

        static OdysseyTools()
        {
            
            IsActive = ModLister.GetActiveModWithIdentifier("MrHydralisk.LayeredAtmosphereOrbit") != null;
            if (!IsActive)
            {
                AllLayers = new List<Def>();
                return;
            }

           
            planetLayerDefType = AccessTools.TypeByName("RimWorld.PlanetLayerDef");
            if (planetLayerDefType == null)
            {
                Log.Error("[Zombieland] Layered Atmosphere and Orbit is active, but its PlanetLayerDef type was not found. Disabling integration.");
                IsActive = false; 
                AllLayers = new List<Def>();
                return;
            }
            

            AllLayers = GenDefDatabase.GetAllDefsInDatabaseForDef(planetLayerDefType).ToList();
            Log.Message($"[Zombieland] Found {AllLayers.Count} Odyssey layers for settings.");
        }

        public static Def GetMapLayer(Map map)
        {
            if (!IsActive || map == null)
                return null;

            return map.Tile.Layer?.Def;
        }
    }
}
