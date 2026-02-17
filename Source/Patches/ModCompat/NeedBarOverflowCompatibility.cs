using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace ZombieLand.ModCompat
{
    /// <summary>
    /// Compatibility layer for Need Bar Overflow mod (Steam ID: 2566316158)
    /// Detects if NBO is loaded and provides compatibility hooks
    /// </summary>
    [StaticConstructorOnStartup]
    public static class NeedBarOverflowCompatibility
    {
        public static bool IsActive { get; private set; }
        
        static NeedBarOverflowCompatibility()
        {
            // Check if Need Bar Overflow assembly is loaded by checking loaded assemblies
            IsActive = false;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "NeedBarOverflow")
                {
                    IsActive = true;
                    break;
                }
            }
            
            if (IsActive)
                Log.Message("[Zombieland] Need Bar Overflow detected - enabling compatibility mode");
        }
    }
}
